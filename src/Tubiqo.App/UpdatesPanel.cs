using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using Tubiqo.Core.Updates;

namespace Tubiqo.App;

/// <summary>"Check for updates": manual only (ADR 0003). Rule Packs apply at once; app updates ask first.</summary>
public partial class MainWindow
{
    const string ReleaseRepo = "Kandog/Tubiqo";

    static Version AppVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    UpdatePlan? pendingAppUpdate;
    LatestRelease? pendingRelease;

    static GitHubReleaseClient CreateReleaseClient()
    {
        // No automatic redirects (each hop is checked) and no cookie store.
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
        var apiBase = GitHubReleaseClient.DefaultApiBase;
        var allowLocalhost = false;
#if DEBUG
        // Debug builds only: lets test scripts point updates at a fake GitHub on localhost.
        if (Uri.TryCreate(Environment.GetEnvironmentVariable("TUBIQO_DEBUG_UPDATE_API"), UriKind.Absolute, out var fake) && fake.IsLoopback)
        {
            apiBase = fake;
            allowLocalhost = true;
        }
#endif
        return new GitHubReleaseClient(handler, apiBase, ReleaseRepo, allowLocalhost);
    }

    async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        pendingAppUpdate = null;
        UpdateStatus.Text = "Checking GitHub for updates…";
        try
        {
            var client = CreateReleaseClient();
            var latest = await client.FetchLatestAsync(CancellationToken.None);
            if (latest == null) { UpdateStatus.Text = "No Tubiqo release has been published yet."; return; }

            // Nothing in the release is trusted until its manifest's signature checks out.
            if (!releaseVerifier.Verify(latest.Manifest, latest.Signature))
            {
                UpdateStatus.Text = $"Release {latest.Tag} is NOT signed by the Tubiqo maintainer key, so it was ignored.";
                return;
            }
            var manifest = ReleaseManifest.Parse(System.Text.Encoding.UTF8.GetString(latest.Manifest));
            var plan = UpdatePlanner.Plan(manifest, AppVersion, Version.TryParse(rulePack.Version, out var rv) ? rv : new Version(0, 0));

            var messages = new List<string>();
            if (plan.RulePackUpdate)
            {
                var asset = manifest.RulePack!;
                if (!latest.Assets.TryGetValue(asset.File, out var url)) throw new UpdateException($"The release is missing {asset.File}.");
                var bytes = await client.DownloadAsync(url, asset, CancellationToken.None);
                rulePackStore.Install(latest.Manifest, latest.Signature, bytes);
                rulePack = CurrentRulePack();
                PopulateTrimRules();
                await ApplySettingsAsync();
                SettingsChangedNote.Visibility = Visibility.Visible;
                messages.Add($"Rule Pack updated to {asset.Version}.");
            }
            if (plan.AppUpdate)
            {
                pendingAppUpdate = plan;
                pendingRelease = latest;
                InstallUpdateButton.Content = $"Install Tubiqo {manifest.App!.Version.ToString(3)}";
                InstallUpdateButton.Visibility = Visibility.Visible;
                messages.Add($"Tubiqo {manifest.App.Version.ToString(3)} is available.");
            }
            UpdateStatus.Text = messages.Count > 0 ? string.Join(" ", messages) : "You're up to date.";
        }
        catch (UpdateException ex) { UpdateStatus.Text = "Update check failed: " + ex.Message; }
        finally { CheckUpdatesButton.IsEnabled = true; }
    }

    async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (pendingAppUpdate?.Manifest.App is not { } asset || pendingRelease == null) return;
        var answer = MessageBox.Show(this,
            $"Download and install Tubiqo {asset.Version.ToString(3)} ({asset.Size / (1024 * 1024)} MB)?\n\nTubiqo will close while it installs.",
            "Update Tubiqo", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes) return;

        InstallUpdateButton.IsEnabled = CheckUpdatesButton.IsEnabled = false;
        UpdateStatus.Text = "Downloading the installer…";
        var folder = Path.Combine(Path.GetTempPath(), "Tubiqo-update-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (!pendingRelease.Assets.TryGetValue(asset.File, out var url)) throw new UpdateException($"The release is missing {asset.File}.");
            Directory.CreateDirectory(folder);
            var installer = Path.Combine(folder, asset.File);
            await CreateReleaseClient().DownloadToFileAsync(url, asset, installer, CancellationToken.None);

            UpdateStatus.Text = "Starting the installer…";
            Process.Start(new ProcessStartInfo(installer, "/SILENT /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = false });
            Close();
        }
        catch (Exception ex) when (ex is UpdateException or IOException or System.ComponentModel.Win32Exception)
        {
            UpdateStatus.Text = "Update failed: " + ex.Message;
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
            InstallUpdateButton.IsEnabled = CheckUpdatesButton.IsEnabled = true;
        }
    }

    async void RollbackRulePack_Click(object sender, RoutedEventArgs e)
    {
        if (!rulePackStore.Rollback()) { UpdateStatus.Text = "There's no previous Rule Pack to go back to."; return; }
        rulePack = CurrentRulePack();
        PopulateTrimRules();
        await ApplySettingsAsync();
        SettingsChangedNote.Visibility = Visibility.Visible;
        UpdateStatus.Text = $"Rolled back. Now using Rule Pack {rulePack.Version}.";
    }
}
