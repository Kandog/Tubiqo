using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using H2o.Core.Navigation;
using H2o.Core.Storage;
using Microsoft.Web.WebView2.Core;

namespace H2o.App;

public partial class MainWindow : Window
{
    const string HomeUrl = "https://www.youtube.com/";

    // Context-menu entries worth keeping; everything else (Inspect, Save as, Print, Open in new window...) is removed.
    static readonly HashSet<string> AllowedMenuItems = new(StringComparer.OrdinalIgnoreCase)
    {
        "back", "forward", "reload", "copy", "cut", "paste", "pasteAndMatchStyle",
        "selectAll", "undo", "redo", "copyLinkLocation", "copyImage", "copyImageLocation", "emoji",
    };

    readonly SiteDataStore siteData;
    CoreWebView2Environment? environment;
    bool shuttingDown, readyToClose;

    public MainWindow(SiteDataStore siteData)
    {
        this.siteData = siteData;
        InitializeComponent();
        Loaded += async (_, _) => await InitializeBrowserAsync();
    }

    CoreWebView2 Core => Browser.CoreWebView2;

    async Task InitializeBrowserAsync()
    {
        var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = false };
        environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null, userDataFolder: siteData.CreateSessionFolder(), options);
        await Browser.EnsureCoreWebView2Async(environment);

        ApplyHardening(Core);

        Core.NavigationStarting += OnNavigationStarting;
        Core.FrameNavigationStarting += OnFrameNavigationStarting;
        Core.NewWindowRequested += OnNewWindowRequested;
        Core.HistoryChanged += (_, _) =>
        {
            BackButton.IsEnabled = Core.CanGoBack;
            ForwardButton.IsEnabled = Core.CanGoForward;
        };

        Core.Navigate(HomeUrl);
    }

    static void ApplyHardening(CoreWebView2 core)
    {
        var s = core.Settings;
#if DEBUG
        s.AreDevToolsEnabled = true;
#else
        s.AreDevToolsEnabled = false;
#endif
        s.AreBrowserAcceleratorKeysEnabled = false; // no Ctrl+S, Ctrl+P, F12...; h2o defines its own keys
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.AreHostObjectsAllowed = false;
        s.IsWebMessageEnabled = false;              // M1 enables it for the Lean Player page only
        s.IsStatusBarEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.IsReputationCheckingRequired = true;      // keep SmartScreen on

        core.Profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.Strict;
        core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Auto; // follow Windows theme

        // Every site permission (camera, mic, location, notifications, clipboard read, MIDI, USB...) is denied without a prompt.
        core.PermissionRequested += (_, e) => { e.State = CoreWebView2PermissionState.Deny; e.Handled = true; };
        core.FrameCreated += (_, e) =>
            e.Frame.PermissionRequested += (_, pe) => { pe.State = CoreWebView2PermissionState.Deny; pe.Handled = true; };

        core.DownloadStarting += (_, e) => { e.Cancel = true; e.Handled = true; };
        core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
        core.BasicAuthenticationRequested += (_, e) => e.Cancel = true;
        core.ClientCertificateRequested += (_, e) => e.Cancel = true;
        core.ServerCertificateErrorDetected += (_, e) => e.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
        core.ScreenCaptureStarting += (_, e) => e.Cancel = true;

        core.ContextMenuRequested += (_, e) =>
        {
            var items = e.MenuItems;
            for (var i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                var isSeparator = item.Kind == CoreWebView2ContextMenuItemKind.Separator;
                if (!isSeparator && !AllowedMenuItems.Contains(item.Name)) items.RemoveAt(i);
            }
            // Tidy separators left at the ends or doubled up.
            for (var i = items.Count - 1; i >= 0; i--)
            {
                var sep = items[i].Kind == CoreWebView2ContextMenuItemKind.Separator;
                var edge = i == 0 || i == items.Count - 1;
                var doubled = i > 0 && items[i - 1].Kind == CoreWebView2ContextMenuItemKind.Separator;
                if (sep && (edge || doubled)) items.RemoveAt(i);
            }
            if (items.Count == 0) e.Handled = true; // nothing useful left: show no menu
        };
    }

    void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var decision = NavigationGuard.Classify(e.Uri, NavigationTarget.TopLevel);
        if (decision.Action == NavigationAction.Allow) return;
        e.Cancel = true;
        Act(decision);
    }

    void OnFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var decision = NavigationGuard.Classify(e.Uri, NavigationTarget.Frame);
        if (decision.Action != NavigationAction.Allow) e.Cancel = true;
    }

    void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // Single Player: no pop-ups. Allowed pages open in the main view, others go to the default browser.
        e.Handled = true;
        var decision = NavigationGuard.Classify(e.Uri, NavigationTarget.TopLevel);
        if (decision.Action == NavigationAction.Allow) decision = decision with { Action = NavigationAction.UpgradeToHttps };
        Act(decision);
    }

    void Act(NavigationDecision decision)
    {
        // Never block inside a WebView2 event handler; act once it has returned.
        Dispatcher.InvokeAsync(() =>
        {
            switch (decision.Action)
            {
                case NavigationAction.UpgradeToHttps:
                    Core.Navigate(decision.Url);
                    break;
                case NavigationAction.OpenExternally:
                    ConfirmOpenExternally(decision.Url);
                    break;
            }
        });
    }

    void ConfirmOpenExternally(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return;

        var answer = MessageBox.Show(this,
            $"This link leaves YouTube. Open it in your default browser?\n\nSite: {uri.IdnHost}\n\n{uri.AbsoluteUri}",
            "Open outside h2o", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (readyToClose) { base.OnClosing(e); return; }

        // Shut the browser down first so its files are released, then wipe Site Data.
        e.Cancel = true;
        if (shuttingDown) return;
        shuttingDown = true;
        Hide();

        if (environment != null)
        {
            var exited = new TaskCompletionSource();
            environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            Browser.Dispose();
            await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        }
        siteData.WipeAll();

        readyToClose = true;
        Close();
    }

    void Back_Click(object sender, RoutedEventArgs e) { if (Core?.CanGoBack == true) Core.GoBack(); }
    void Forward_Click(object sender, RoutedEventArgs e) { if (Core?.CanGoForward == true) Core.GoForward(); }
    void Reload_Click(object sender, RoutedEventArgs e) => Core?.Reload();
    void Home_Click(object sender, RoutedEventArgs e) => Core?.Navigate(HomeUrl);
}
