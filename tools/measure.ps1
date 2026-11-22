# Measures Tubiqo's CPU and memory (its whole process tree, WebView2 included) in one scenario and checks the targets.
#
#   .\tools\measure.ps1 -Scenario lean              # Lean Player, 1080p video
#   .\tools\measure.ps1 -Scenario fallback          # Watch Page Fallback
#   .\tools\measure.ps1 -Scenario search            # Browse Page: search results
#   .\tools\measure.ps1 -Scenario home              # Browse Page: home
#   .\tools\measure.ps1 -Scenario min-paused        # minimized, nothing playing
#   .\tools\measure.ps1 -Scenario min-playing       # minimized, audio keeps playing
#   .\tools\measure.ps1 -Scenario lean -Seconds 3600 -Loop   # long session: memory growth check
#
# Uses the Debug build: it honours a throwaway data folder (TUBIQO_DEBUG_DATA_ROOT) and the DevTools port that
# the script drives the page through. Release builds ignore both. Results go to artifacts\measure\.
param(
    [ValidateSet('lean', 'fallback', 'search', 'home', 'min-paused', 'min-playing')] [string]$Scenario = 'lean',
    [string]$VideoId = 'aqz-KE-bpKQ',
    [string]$Url,                    # overrides the scenario's page
    [int]$Warmup = 25,
    [int]$Seconds = 90,
    [int]$Interval = 5,
    [switch]$Loop,                   # keep the video looping (for long sessions)
    [int]$Port = 9350,
    [string]$BrowserArgs = '',       # extra Chromium switches to try (Debug builds append them)
    [string]$Exe = (Join-Path $PSScriptRoot '..\src\Tubiqo.App\bin\Debug\net10.0-windows\Tubiqo.exe')
)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class TubiqoMeasureWin { [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd); }
'@

$results = Join-Path $PSScriptRoot '..\artifacts\measure'
New-Item -ItemType Directory -Force $results | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$cores = [Environment]::ProcessorCount
$playing = $Scenario -in 'lean', 'fallback', 'min-playing'

if (-not $Url) {
    $Url = switch ($Scenario) {
        'fallback' { "https://www.youtube.com/watch?v=$VideoId&tubiqo=fallback" }
        'search'   { 'https://www.youtube.com/results?search_query=blender+open+movie' }
        'home'     { 'https://www.youtube.com/' }
        default    { "https://www.youtube.com/watch?v=$VideoId" }
    }
}

# --- DevTools helpers
$script:msgId = 0
function Get-Targets { (Invoke-RestMethod "http://127.0.0.1:$Port/json") | ForEach-Object { $_ } } # PS 5.1 passes the array as one object
function Invoke-Page([string]$expr, [scriptblock]$where) {
    $t = Get-Targets | Where-Object $where | Select-Object -First 1
    if (-not $t) { return $null }
    $ws = [System.Net.WebSockets.ClientWebSocket]::new()
    try {
        $ws.ConnectAsync([Uri]$t.webSocketDebuggerUrl, [Threading.CancellationToken]::None).Wait()
        $script:msgId++
        $msg = @{ id = $script:msgId; method = 'Runtime.evaluate'; params = @{ expression = $expr; returnByValue = $true } } | ConvertTo-Json -Compress -Depth 5
        $b = [Text.Encoding]::UTF8.GetBytes($msg)
        $ws.SendAsync([ArraySegment[byte]]::new($b), 'Text', $true, [Threading.CancellationToken]::None).Wait()
        $cts = [Threading.CancellationTokenSource]::new(5000)
        while ($true) {
            $buf = New-Object byte[] 262144; $sb = [Text.StringBuilder]::new()
            do { $r = $ws.ReceiveAsync([ArraySegment[byte]]::new($buf), $cts.Token).Result; [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count)) } while (-not $r.EndOfMessage)
            $o = $sb.ToString() | ConvertFrom-Json
            if ($o.id -eq $script:msgId) { return $o.result.result.value }
        }
    } catch { return $null } finally { $ws.Dispose() }
}
# The frame that holds the <video>: the embed iframe in the Lean Player, the page itself otherwise.
$videoFrame = { ($_.type -eq 'iframe' -and $_.url -like '*youtube-nocookie.com/embed/*') -or ($_.type -eq 'page' -and $_.url -like '*youtube.com/watch*') }
$videoInfo = @'
(() => { const v = document.querySelector('video'); if (!v) return null;
  let codec = ''; try { const s = document.querySelector('#movie_player').getStatsForNerds(); codec = (s.codecs || '').replace(/\s+/g, ' '); } catch (e) {}
  const q = v.getVideoPlaybackQuality ? v.getVideoPlaybackQuality() : {};
  return { t: Math.round(v.currentTime), paused: v.paused, w: v.videoWidth, h: v.videoHeight, codec, dropped: q.droppedVideoFrames || 0, frames: q.totalVideoFrames || 0 }; })()
'@

function Get-Tree([int]$rootId) {
    $all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId
    $ids = [System.Collections.Generic.HashSet[int]]::new(); [void]$ids.Add($rootId)
    do {
        $added = $false
        foreach ($p in $all) { if ($ids.Contains([int]$p.ParentProcessId) -and $ids.Add([int]$p.ProcessId)) { $added = $true } }
    } while ($added)
    , $ids
}

# --- Launch on a throwaway data folder
$dataRoot = Join-Path $env:TEMP "tubiqo-measure-$stamp"
$env:TUBIQO_DEBUG_DATA_ROOT = $dataRoot
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$Port $BrowserArgs".Trim()
$root = Start-Process -FilePath (Resolve-Path $Exe) -PassThru
Remove-Item Env:\TUBIQO_DEBUG_DATA_ROOT, Env:\WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS
try {
    Start-Sleep 8; $root.Refresh(); $hwnd = $root.MainWindowHandle
    [void][TubiqoMeasureWin]::ShowWindow($hwnd, 3) # maximized: YouTube picks quality by player size, so this is the 1080p case
    [void](Invoke-Page "location.href = '$Url'" { $_.type -eq 'page' })
    Write-Host "[$Scenario] $Url  (PID $($root.Id)); warming up $Warmup s..."
    Start-Sleep -Seconds $Warmup

    if ($Scenario -eq 'min-paused') { [void](Invoke-Page "document.querySelector('video').pause()" $videoFrame); Start-Sleep 2 }
    if ($Scenario -like 'min-*') { [void][TubiqoMeasureWin]::ShowWindow($hwnd, 6); Start-Sleep 10 }

    $prevCpu = @{}; $prevT = $null; $rows = [System.Collections.Generic.List[object]]::new()
    $end = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $end) {
        $ids = Get-Tree $root.Id
        $now = Get-Date; $cpuNow = @{}
        foreach ($id in $ids) { try { $cpuNow[$id] = (Get-Process -Id $id).TotalProcessorTime.TotalSeconds } catch {} }
        $pws = (Get-CimInstance Win32_PerfFormattedData_PerfProc_Process | Where-Object { $ids.Contains([int]$_.IDProcess) } |
            Measure-Object WorkingSetPrivate -Sum).Sum
        if ($prevT) {
            $dt = ($now - $prevT).TotalSeconds; $d = 0
            foreach ($k in $cpuNow.Keys) { if ($prevCpu.ContainsKey($k)) { $d += $cpuNow[$k] - $prevCpu[$k] } }
            # A minimized, suspended page doesn't answer; don't wake it by asking.
            $v = if ($Scenario -ne 'min-paused') { Invoke-Page $videoInfo $videoFrame } else { $null }
            if ($Loop -and $v -and $v.paused) { [void](Invoke-Page "(() => { const v = document.querySelector('video'); v.currentTime = 0; v.play(); })()" $videoFrame) }
            $row = [pscustomobject]@{
                Time = $now.ToString('HH:mm:ss'); Procs = $ids.Count
                CpuPct = [math]::Round($d / ($dt * $cores) * 100, 1)
                PrivateWsMB = [math]::Round($pws / 1MB)
                VideoT = $v.t; Height = $v.h; Paused = $v.paused; Dropped = $v.dropped; Codec = $v.codec
            }
            $rows.Add($row)
            Write-Host ("{0}  cpu {1,5}%  mem {2,4} MB  procs {3}  {4}" -f $row.Time, $row.CpuPct, $row.PrivateWsMB, $row.Procs,
                $(if ($v) { "t=$($v.t)s $($v.w)x$($v.h) paused=$($v.paused) dropped=$($v.dropped)/$($v.frames)" } else { '' }))
        }
        $prevCpu = $cpuNow; $prevT = $now
        Start-Sleep -Seconds $Interval
    }
} finally {
    foreach ($id in (Get-Tree $root.Id)) { try { Stop-Process -Id $id -Force } catch {} }
    Start-Sleep 2
    Remove-Item -Recurse -Force $dataRoot -ErrorAction SilentlyContinue
}

# --- Summary and targets (i3-8100 / UHD 630): playing < 10% CPU and < 400 MB; minimized idle ~0% CPU and < 250 MB.
$rows | Export-Csv (Join-Path $results "$Scenario-$stamp.csv") -NoTypeInformation
$n = $rows.Count; $k = [math]::Max(1, [math]::Floor($n / 5))
$first = ($rows | Select-Object -First $k | Measure-Object PrivateWsMB -Average).Average
$last = ($rows | Select-Object -Last $k | Measure-Object PrivateWsMB -Average).Average
$codecs = ($rows | Where-Object Codec | Select-Object -ExpandProperty Codec -Unique) -join '; '
$sum = [pscustomobject]@{
    Scenario = $Scenario; Seconds = $Seconds; Samples = $n
    AvgCpuPct = [math]::Round(($rows | Measure-Object CpuPct -Average).Average, 1)
    MaxCpuPct = ($rows | Measure-Object CpuPct -Maximum).Maximum
    AvgMemMB = [math]::Round(($rows | Measure-Object PrivateWsMB -Average).Average)
    MaxMemMB = ($rows | Measure-Object PrivateWsMB -Maximum).Maximum
    GrowthMB = [math]::Round($last - $first)   # last fifth vs first fifth of the run
    MaxHeight = ($rows | Measure-Object Height -Maximum).Maximum
    DroppedFrames = ($rows | Measure-Object Dropped -Maximum).Maximum
    Codecs = $codecs
}
$cpuGate, $memGate = if ($Scenario -eq 'min-paused') { 1, 250 } elseif ($playing) { 10, 400 } else { 10, 500 }
$sum | Add-Member Pass ([bool]($sum.AvgCpuPct -lt $cpuGate -and $sum.AvgMemMB -lt $memGate))
$sum | Add-Member Gate "cpu < $cpuGate%, mem < $memGate MB"
$sum | Format-List
$sum | Export-Csv (Join-Path $results 'summary.csv') -NoTypeInformation -Append
