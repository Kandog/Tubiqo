using System.Diagnostics;
using System.IO;
using System.Windows;
using Tubiqo.Core.History;
using Tubiqo.Core.Storage;
using Microsoft.Web.WebView2.Core;

namespace Tubiqo.App;

public partial class App : Application
{
    /// <summary>
    /// Oldest Evergreen WebView2 runtime Tubiqo accepts. Evergreen updates itself, so anything older
    /// means the machine is missing security fixes; raise this as Tubiqo starts relying on newer APIs.
    /// </summary>
    public const string MinimumRuntimeVersion = "130.0.0.0";

    const string RuntimeDownloadPage = "https://developer.microsoft.com/microsoft-edge/webview2/";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!RuntimeIsUsable(out var problem))
        {
            var answer = MessageBox.Show(
                problem + "\n\nOpen Microsoft's WebView2 download page now?",
                "Tubiqo can't start", MessageBoxButton.YesNo, MessageBoxImage.Error);
            if (answer == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(RuntimeDownloadPage) { UseShellExecute = true });
            }
            Shutdown(1);
            return;
        }

        var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tubiqo");
#if DEBUG
        // Debug builds only: lets test scripts use a throwaway Tubiqo Data folder instead of the real one.
        if (Environment.GetEnvironmentVariable("TUBIQO_DEBUG_DATA_ROOT") is { Length: > 0 } testRoot) dataRoot = testRoot;
#endif
        Directory.CreateDirectory(dataRoot);
        var siteData = new SiteDataStore(dataRoot);
        siteData.WipeAll(); // leftovers from a crash or a forced shutdown
        DeleteOldUpdateDownloads();

        database = TubiqoDatabase.Open(Path.Combine(dataRoot, "tubiqo.db"));
        var history = new WatchHistory(database, TimeProvider.System);
        history.ApplyRetention();

        MainWindow = new MainWindow(siteData, database, history, dataRoot);
        MainWindow.Show();
    }

    TubiqoDatabase? database;

    protected override void OnExit(ExitEventArgs e)
    {
        database?.Dispose();
        base.OnExit(e);
    }

    /// <summary>An in-app update leaves its installer in %TEMP% (it's still running when Tubiqo closes); remove it next time.</summary>
    static void DeleteOldUpdateDownloads()
    {
        foreach (var dir in Directory.EnumerateDirectories(Path.GetTempPath(), "Tubiqo-update-*"))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // still in use: try next start
        }
    }

    static bool RuntimeIsUsable(out string problem)
    {
        string version;
        try
        {
            // null folder = the machine-wide Evergreen runtime (ADR 0001); Tubiqo never ships its own.
            version = CoreWebView2Environment.GetAvailableBrowserVersionString(null);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            problem = "The Microsoft Edge WebView2 Runtime is not installed.";
            return false;
        }

        if (CoreWebView2Environment.CompareBrowserVersions(version, MinimumRuntimeVersion) < 0)
        {
            problem = $"The WebView2 Runtime on this PC ({version}) is too old. Tubiqo needs {MinimumRuntimeVersion} or newer.";
            return false;
        }

        problem = "";
        return true;
    }
}
