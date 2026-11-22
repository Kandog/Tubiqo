using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Tubiqo.Core.History;
using Tubiqo.Core.Navigation;
using Tubiqo.Core.Playback;
using Tubiqo.Core.Rules;
using Tubiqo.Core.Storage;
using Tubiqo.Core.Updates;
using Microsoft.Web.WebView2.Core;

namespace Tubiqo.App;

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
    readonly WatchHistory history;
    readonly WatchTracker tracker;
    readonly PlaybackSettings playbackSettings;
    readonly TrimRuleSettings trimSettings;
    readonly AppPreferences preferences;
    readonly RulePack builtInRulePack = LoadBuiltInRulePack();
    readonly RulePackStore rulePackStore;
    readonly SignatureVerifier releaseVerifier;
    RulePack rulePack;
    string? policyScriptId, trimScriptId;
    List<string> blockPatterns = [];
    CoreWebView2Environment? environment;
    bool shuttingDown, readyToClose;

    // The video currently shown on a Watch Page Fallback; its watch URL must not be rerouted to the Lean Player.
    string? fallbackVideoId;

    // Window state saved while the player is fullscreen.
    WindowState savedState;
    WindowStyle savedStyle;
    ResizeMode savedResize;

    public MainWindow(SiteDataStore siteData, TubiqoDatabase database, WatchHistory history, string dataRoot)
    {
        this.siteData = siteData;
        this.history = history;
        releaseVerifier = new SignatureVerifier(ReleaseKey.PublicKey);
        rulePackStore = new RulePackStore(dataRoot, releaseVerifier);
        rulePack = CurrentRulePack();
        tracker = new WatchTracker(history, TimeProvider.System);
        playbackSettings = new PlaybackSettings(database);
        trimSettings = new TrimRuleSettings(database);
        preferences = new AppPreferences(database);
        InitializeComponent();
        RestoreWindowPlacement();
        InitializeHistoryPanel();
        InitializeSettingsPanel();
        PreviewKeyDown += OnPreviewKeyDown;
        StateChanged += OnWindowStateChanged;
        Loaded += async (_, _) => await InitializeBrowserAsync();
    }

    CoreWebView2 Core => Browser.CoreWebView2;

    async Task InitializeBrowserAsync()
    {
        var options = new CoreWebView2EnvironmentOptions
        {
            AreBrowserExtensionsEnabled = false,
            // The Lean Player starts videos on a new page, after the user's click on the previous one.
            // Media buffers: Chromium keeps up to 150 MB of video buffered by default; 40 MB is still about a minute
            // of 1080p60 ahead and holds a long session at ~295 MB instead of ~450 MB (measured in M7).
            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required " +
                "--mse-video-buffer-size-limit-mb=40 --mse-audio-buffer-size-limit-mb=6",
        };
#if DEBUG
        // Debug builds only: keep WebView2's own override variable working (used by test scripts to attach DevTools).
        if (Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS") is { Length: > 0 } extra)
            options.AdditionalBrowserArguments += " " + extra;
#endif
        environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null, userDataFolder: siteData.CreateSessionFolder(), options);
        await Browser.EnsureCoreWebView2Async(environment);

        ApplyHardening(Core);

        // Lean Player page: served read-only from the app folder; other origins can't load its files.
        Core.SetVirtualHostNameToFolderMapping(
            AllowedDomains.PlayerHost, Path.Combine(AppContext.BaseDirectory, "Player"),
            CoreWebView2HostResourceAccessKind.Deny);
        await Core.AddScriptToExecuteOnDocumentCreatedAsync(await ReadScriptAsync("video-links.js"));
        Core.WebResourceRequested += OnBlockedRequest;
        await ApplySettingsAsync();

        Core.NavigationStarting += OnNavigationStarting;
        Core.SourceChanged += OnSourceChanged;
        Core.WebMessageReceived += OnWebMessageReceived;
        Core.ContainsFullScreenElementChanged += (_, _) => SetFullScreen(Core.ContainsFullScreenElement);
        Core.FrameNavigationStarting += OnFrameNavigationStarting;
        Core.NewWindowRequested += OnNewWindowRequested;
        Core.HistoryChanged += (_, _) =>
        {
            BackButton.IsEnabled = Core.CanGoBack;
            ForwardButton.IsEnabled = Core.CanGoForward;
        };

        Core.Navigate(HomeUrl);
    }

    static Task<string> ReadScriptAsync(string name) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Scripts", name));

    /// <summary>
    /// (Re)applies the Playback Policy and Trim Rules: page scripts take effect on the next page,
    /// request blocking at once. Called at startup and whenever a setting changes.
    /// </summary>
    async Task ApplySettingsAsync()
    {
        if (Browser.CoreWebView2 == null) return; // not started yet; startup applies the saved settings
        if (policyScriptId != null) Core.RemoveScriptToExecuteOnDocumentCreated(policyScriptId);
        if (trimScriptId != null) Core.RemoveScriptToExecuteOnDocumentCreated(trimScriptId);

        // Runs in every frame, including the embed player inside the Lean Player page.
        policyScriptId = await Core.AddScriptToExecuteOnDocumentCreatedAsync(
            CurrentPlaybackPolicy().BuildScript(await ReadScriptAsync("playback-policy.js")));

        var plan = TrimPlan.From(rulePack, trimSettings);
        trimScriptId = await Core.AddScriptToExecuteOnDocumentCreatedAsync(plan.BuildScript(await ReadScriptAsync("trim-page.js")));

        // One filter per pattern (never "*"), so ordinary requests such as video segments never come through the app.
        foreach (var p in blockPatterns)
            Core.RemoveWebResourceRequestedFilter(p, CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        blockPatterns = [.. plan.BlockPatterns];
        foreach (var p in blockPatterns)
            Core.AddWebResourceRequestedFilter(p, CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
    }

    void OnBlockedRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        // Only requests matching an enabled Trim Rule's block pattern reach this handler.
        e.Response = Core.Environment.CreateWebResourceResponse(null, 403, "Blocked by Tubiqo", "");
        BlockedRequestCount++;
    }

    public int BlockedRequestCount { get; private set; }

    PlaybackPolicy CurrentPlaybackPolicy()
    {
        var policy = playbackSettings.Policy;
#if DEBUG
        // Debug builds only: lets test scripts try other caps.
        if (int.TryParse(Environment.GetEnvironmentVariable("TUBIQO_DEBUG_QUALITY_CAP"), out var cap) &&
            PlaybackPolicy.ValidCaps.Contains(cap))
            policy = policy.WithQualityCap(cap);
#endif
        return policy;
    }

    static void ApplyHardening(CoreWebView2 core)
    {
        var s = core.Settings;
#if DEBUG
        s.AreDevToolsEnabled = true;
#else
        s.AreDevToolsEnabled = false;
#endif
        s.AreBrowserAcceleratorKeysEnabled = false; // no Ctrl+S, Ctrl+P, F12...; Tubiqo defines its own keys
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.AreHostObjectsAllowed = false;
        s.IsWebMessageEnabled = true;               // only messages from the Lean Player page are accepted (OnWebMessageReceived)
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
        if (decision.Action != NavigationAction.Allow)
        {
            e.Cancel = true;
            Act(decision);
            return;
        }

        var url = decision.Url; // a youtube.com/redirect link is judged by its real destination
        if (LeanPlayerRoute.IsFallback(url, out var fallbackId))
        {
            fallbackVideoId = fallbackId;
            return;
        }
        fallbackVideoId = null;

        if (LeanPlayerRoute.TryGetVideo(url, out var video))
        {
            e.Cancel = true;
            Dispatcher.InvokeAsync(() => Core.Navigate(LeanPlayerRoute.PlayerUrl(video)));
            return;
        }

        if (IsPlayerHost(url) && !LeanPlayerRoute.TryParsePlayerUrl(url, out _)) e.Cancel = true;
    }

    void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        var source = Core.Source;
        if (e.IsNewDocument) OnNewPage();
        if (!IsPlayerHost(source))
        {
            Title = "Tubiqo";
            tracker.Flush(); // left the Lean Player: save where the video got to
            RefreshHistory();
        }

        // Backstop for in-page navigation to a video the click hook missed: swap this entry for the Lean Player.
        if (!e.IsNewDocument && LeanPlayerRoute.TryGetVideo(source, out var video) && video.VideoId != fallbackVideoId)
        {
            _ = Core.ExecuteScriptAsync($"location.replace({JsonSerializer.Serialize(LeanPlayerRoute.PlayerUrl(video))})");
        }
    }

    void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Only Tubiqo's own player page may talk to the app, and only about the video it is showing.
        if (!LeanPlayerRoute.TryParsePlayerUrl(e.Source, out var page)) return;
        var message = PlayerMessage.Parse(e.WebMessageAsJson);

        switch (message)
        {
            case PlayerStateMessage s when s.VideoId == page.VideoId:
                Title = s.Title.Length > 0 ? $"{s.Title} – Tubiqo" : "Tubiqo";
                tracker.OnState(s);
                OnPlayerState(s);
                break;
            case PlayerErrorMessage err when err.VideoId == page.VideoId && LeanPlayerRoute.NeedsWatchPageFallback(err.Code):
                // Replace the player's history entry, so Back skips the failed player and returns to the Browse Page.
                _ = Core.ExecuteScriptAsync($"location.replace({JsonSerializer.Serialize(LeanPlayerRoute.FallbackUrl(page))})");
                break;
        }
    }

    static bool IsPlayerHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        string.Equals(u.IdnHost, AllowedDomains.PlayerHost, StringComparison.OrdinalIgnoreCase);

    void SetFullScreen(bool on)
    {
        if (on)
        {
            savedState = WindowState; savedStyle = WindowStyle; savedResize = ResizeMode;
            Toolbar.Visibility = Visibility.Collapsed;
            HistoryButton.IsChecked = false;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal; // re-maximizing without borders covers the taskbar
            WindowState = WindowState.Maximized;
        }
        else
        {
            Toolbar.Visibility = Visibility.Visible;
            WindowStyle = savedStyle;
            ResizeMode = savedResize;
            WindowState = savedState;
        }
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
            "Open outside Tubiqo", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
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
        tracker.Flush();
        SaveWindowPlacement();
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
