using System.Diagnostics;
using System.IO;
using System.Windows;
using H2o.Core.History;
using H2o.Core.Storage;
using Microsoft.Web.WebView2.Core;

namespace H2o.App;

public partial class App : Application
{
    /// <summary>
    /// Oldest Evergreen WebView2 runtime h2o accepts. Evergreen updates itself, so anything older
    /// means the machine is missing security fixes; raise this as h2o starts relying on newer APIs.
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
                "h2o can't start", MessageBoxButton.YesNo, MessageBoxImage.Error);
            if (answer == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(RuntimeDownloadPage) { UseShellExecute = true });
            }
            Shutdown(1);
            return;
        }

        var h2oRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "h2o");
#if DEBUG
        // Debug builds only: lets test scripts use a throwaway h2o Data folder instead of the real one.
        if (Environment.GetEnvironmentVariable("H2O_DEBUG_DATA_ROOT") is { Length: > 0 } testRoot) h2oRoot = testRoot;
#endif
        Directory.CreateDirectory(h2oRoot);
        var siteData = new SiteDataStore(h2oRoot);
        siteData.WipeAll(); // leftovers from a crash or a forced shutdown

        database = H2oDatabase.Open(Path.Combine(h2oRoot, "h2o.db"));
        var history = new WatchHistory(database, TimeProvider.System);
        history.ApplyRetention();

        MainWindow = new MainWindow(siteData, database, history);
        MainWindow.Show();
    }

    H2oDatabase? database;

    protected override void OnExit(ExitEventArgs e)
    {
        database?.Dispose();
        base.OnExit(e);
    }

    static bool RuntimeIsUsable(out string problem)
    {
        string version;
        try
        {
            // null folder = the machine-wide Evergreen runtime (ADR 0001); h2o never ships its own.
            version = CoreWebView2Environment.GetAvailableBrowserVersionString(null);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            problem = "The Microsoft Edge WebView2 Runtime is not installed.";
            return false;
        }

        if (CoreWebView2Environment.CompareBrowserVersions(version, MinimumRuntimeVersion) < 0)
        {
            problem = $"The WebView2 Runtime on this PC ({version}) is too old. h2o needs {MinimumRuntimeVersion} or newer.";
            return false;
        }

        problem = "";
        return true;
    }
}
