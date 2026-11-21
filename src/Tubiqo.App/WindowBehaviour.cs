using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Tubiqo.Core.Playback;
using Tubiqo.Core.Storage;

namespace Tubiqo.App;

/// <summary>Keyboard shortcuts, remembered window placement and volume, and what happens when minimized.</summary>
public partial class MainWindow
{
    bool windowFullScreen;          // entered with F11 (not by the player's own fullscreen button)
    PlayerState lastPlayerState = PlayerState.Unstarted;
    DateTime volumeAppliedAt = DateTime.MinValue;
    bool volumeSentToPage;

    void RestoreWindowPlacement()
    {
        if (preferences.WindowPlacement is not { } saved) return;
        var screen = new ScreenArea(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var p = saved.FitTo(screen, MinWidth, MinHeight);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = p.Left; Top = p.Top; Width = p.Width; Height = p.Height;
        if (p.Maximized) WindowState = WindowState.Maximized;
    }

    void SaveWindowPlacement()
    {
        if (windowFullScreen || Browser.CoreWebView2?.ContainsFullScreenElement == true) return; // keep the last normal placement
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (b.IsEmpty || b.Width <= 0) return;
        preferences.WindowPlacement = new WindowPlacement(b.Left, b.Top, b.Width, b.Height, WindowState == WindowState.Maximized);
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        var alt = Keyboard.Modifiers == ModifierKeys.Alt;
        var none = Keyboard.Modifiers == ModifierKeys.None;

        bool handled = true;
        if (ctrl && key == Key.L) ShowOpenLink();
        else if (ctrl && key == Key.H) HistoryButton.IsChecked = HistoryButton.IsChecked != true;
        else if (none && key == Key.F11) SetWindowFullScreen(!windowFullScreen);
        else if (none && key == Key.Escape && windowFullScreen) SetWindowFullScreen(false);
        else if ((alt && key == Key.Left) || key == Key.BrowserBack) Back_Click(this, new RoutedEventArgs());
        else if ((alt && key == Key.Right) || key == Key.BrowserForward) Forward_Click(this, new RoutedEventArgs());
        else if ((none && key == Key.F5) || (ctrl && key == Key.R)) Reload_Click(this, new RoutedEventArgs());
        else if (alt && key == Key.Home) Home_Click(this, new RoutedEventArgs());
        else handled = false;
        e.Handled = handled;
    }

    void ShowOpenLink()
    {
        var box = new OpenLinkWindow(this);
        if (box.ShowDialog() == true && box.Url is { } url) Core?.Navigate(url);
    }

    void SetWindowFullScreen(bool on)
    {
        if (on == windowFullScreen) return;
        windowFullScreen = on;
        SetFullScreen(on);
    }

    // --- Volume: the embed player forgets it (Site Data is wiped), so Tubiqo keeps it in Tubiqo Data.

    void OnPlayerState(PlayerStateMessage s)
    {
        lastPlayerState = s.State;
        if (!volumeSentToPage)
        {
            volumeSentToPage = true;
            volumeAppliedAt = DateTime.UtcNow;
            Core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "setVolume", volume = preferences.Volume, muted = preferences.Muted }));
        }
        else if (DateTime.UtcNow - volumeAppliedAt > TimeSpan.FromSeconds(3)) // ignore the player's own default until ours applies
        {
            if (s.Volume is { } v && v != preferences.Volume) preferences.Volume = v;
            if (s.Muted is { } m && m != preferences.Muted) preferences.Muted = m;
        }

        if (WindowState == WindowState.Minimized && s.State is PlayerState.Paused or PlayerState.Ended) _ = SuspendIfIdleAsync();
    }

    void OnNewPage()
    {
        volumeSentToPage = false;
        lastPlayerState = PlayerState.Unstarted;
    }

    // --- Minimized: keep playing (audio) unless the user chose to pause; when nothing plays, shrink to the minimum.

    async void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (Browser.CoreWebView2 == null) return;
        if (WindowState == WindowState.Minimized)
        {
            if (preferences.PauseWhenMinimized && lastPlayerState == PlayerState.Playing)
                Core.PostWebMessageAsJson("""{"type":"pause"}""");
            await SuspendIfIdleAsync();
        }
        else
        {
            Browser.Visibility = Visibility.Visible; // also resumes a suspended page
            Core.MemoryUsageTargetLevel = Microsoft.Web.WebView2.Core.CoreWebView2MemoryUsageTargetLevel.Normal;
        }
    }

    async Task SuspendIfIdleAsync()
    {
        if (WindowState != WindowState.Minimized || lastPlayerState is PlayerState.Playing or PlayerState.Buffering) return;
        // Give a requested pause a moment to land before deciding.
        await Task.Delay(1500);
        if (WindowState != WindowState.Minimized || lastPlayerState is PlayerState.Playing or PlayerState.Buffering) return;

        Core.MemoryUsageTargetLevel = Microsoft.Web.WebView2.Core.CoreWebView2MemoryUsageTargetLevel.Low;
        Browser.Visibility = Visibility.Hidden; // a hidden WebView2 can be suspended
        try { await Core.TrySuspendAsync(); }
        catch (InvalidOperationException) { } // not allowed right now (for example, still playing audio)
    }
}
