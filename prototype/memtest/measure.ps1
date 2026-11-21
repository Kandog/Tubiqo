# PROTOTYPE — throwaway. Launches one target on a fresh profile, plays the same video,
# and samples CPU and memory across its whole process tree.
#   .\measure.ps1 -Target h2o        # WebView2, AV1 excluded
#   .\measure.ps1 -Target h2o-av1    # WebView2, AV1 allowed (isolates the codec effect)
#   .\measure.ps1 -Target chrome     # Chrome, fresh profile, same autoplay flag
param(
    [ValidateSet('h2o', 'h2o-av1', 'h2o-1080', 'embed', 'embed-1080', 'chrome')] [string]$Target = 'h2o',
    [int]$Warmup = 30,
    [int]$Seconds = 120,
    [int]$Interval = 5,
    [string]$Url = 'https://www.youtube.com/watch?v=aqz-KE-bpKQ'
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$results = Join-Path $here 'results'
New-Item -ItemType Directory -Force $results | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$probeLog = Join-Path $results "$Target-$stamp-probe.log"
$cores = [Environment]::ProcessorCount
$tmpProfile = $null

switch ($Target) {
    'chrome' {
        $tmpProfile = Join-Path $env:TEMP "h2o-PROTOTYPE-chrome-wipe-me-$stamp"
        $exe = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
        $argList = @("--user-data-dir=`"$tmpProfile`"", '--no-first-run', '--no-default-browser-check',
            '--autoplay-policy=no-user-gesture-required', '--start-maximized', $Url)
    }
    default {
        $exe = Join-Path $here 'bin\Release\net10.0-windows\h2o-memtest.exe'
        $argList = @('--log', "`"$probeLog`"", $Url)
        if ($Target -eq 'h2o-av1') { $argList += '--allow-av1' }
        if ($Target -in 'h2o-1080', 'embed-1080') { $argList += '--force-1080' }
        if ($Target -like 'embed*') { $argList += '--embed' }
    }
}

$root = Start-Process -FilePath $exe -ArgumentList $argList -PassThru
Write-Host "[$Target] started PID $($root.Id); warming up $Warmup s..."
Start-Sleep -Seconds $Warmup

function Get-Tree([int]$rootId) {
    $all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId
    $ids = [System.Collections.Generic.HashSet[int]]::new(); [void]$ids.Add($rootId)
    do {
        $added = $false
        foreach ($p in $all) {
            if ($ids.Contains([int]$p.ParentProcessId) -and $ids.Add([int]$p.ProcessId)) { $added = $true }
        }
    } while ($added)
    $ids
}

$prevCpu = @{}; $prevT = $null; $rows = @()
$end = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $end) {
    $ids = Get-Tree $root.Id
    $now = Get-Date
    $cpuNow = @{}; $commit = 0
    foreach ($id in $ids) {
        try { $p = Get-Process -Id $id; $cpuNow[$id] = $p.TotalProcessorTime.TotalSeconds; $commit += $p.PrivateMemorySize64 } catch {}
    }
    $pws = (Get-CimInstance Win32_PerfFormattedData_PerfProc_Process |
        Where-Object { $ids.Contains([int]$_.IDProcess) } | Measure-Object WorkingSetPrivate -Sum).Sum
    if ($prevT) {
        $dt = ($now - $prevT).TotalSeconds; $d = 0
        foreach ($k in $cpuNow.Keys) { if ($prevCpu.ContainsKey($k)) { $d += $cpuNow[$k] - $prevCpu[$k] } }
        $row = [pscustomobject]@{
            Time = $now.ToString('HH:mm:ss'); Procs = $ids.Count
            CpuPct = [math]::Round($d / ($dt * $cores) * 100, 1)
            PrivateWsMB = [math]::Round($pws / 1MB); CommitMB = [math]::Round($commit / 1MB)
        }
        $rows += $row; $row | Format-Table -HideTableHeaders | Out-String | ForEach-Object { Write-Host $_.Trim() }
    }
    $prevCpu = $cpuNow; $prevT = $now
    Start-Sleep -Seconds $Interval
}

$rows | Export-Csv (Join-Path $results "$Target-$stamp.csv") -NoTypeInformation
$sum = [pscustomobject]@{
    Target = $Target
    AvgCpuPct = [math]::Round(($rows | Measure-Object CpuPct -Average).Average, 1)
    MaxCpuPct = ($rows | Measure-Object CpuPct -Maximum).Maximum
    AvgPrivateWsMB = [math]::Round(($rows | Measure-Object PrivateWsMB -Average).Average)
    MaxPrivateWsMB = ($rows | Measure-Object PrivateWsMB -Maximum).Maximum
    AvgCommitMB = [math]::Round(($rows | Measure-Object CommitMB -Average).Average)
    Procs = ($rows | Select-Object -Last 1).Procs
}
$sum | Format-List
$sum | Export-Csv (Join-Path $results 'summary.csv') -NoTypeInformation -Append
if (Test-Path $probeLog) { Write-Host "Last probe state:"; Get-Content $probeLog -Tail 1 }

# Close the whole tree and wipe throwaway profiles.
foreach ($id in (Get-Tree $root.Id)) { try { Stop-Process -Id $id -Force } catch {} }
Start-Sleep -Seconds 2
if ($tmpProfile) { Remove-Item -Recurse -Force $tmpProfile -ErrorAction SilentlyContinue }
