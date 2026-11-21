using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using H2o.Core.History;
using H2o.Core.Playback;

namespace H2o.App;

/// <summary>One row in the Watch History panel.</summary>
public sealed class HistoryItem(WatchEntry entry, DateTimeOffset now)
{
    public WatchEntry Entry { get; } = entry;
    public string VideoId => Entry.VideoId;
    public string Title => Entry.Title.Length > 0 ? Entry.Title : Entry.VideoId;
    public string Channel => Entry.Channel;

    public string Details
    {
        get
        {
            var watched = "Watched " + Ago(now - Entry.LastWatchedUtc);
            if (Entry.DurationSeconds <= 0) return watched;
            var progress = Entry.StartFromSeconds == 0 && Entry.ResumeSeconds > 0
                ? "finished"
                : $"{Clock(Entry.ResumeSeconds)} / {Clock(Entry.DurationSeconds)}";
            return $"{watched} · {progress}";
        }
    }

    static string Ago(TimeSpan t) => t switch
    {
        { TotalMinutes: < 1 } => "just now",
        { TotalHours: < 1 } => $"{(int)t.TotalMinutes} min ago",
        { TotalDays: < 1 } => $"{(int)t.TotalHours} h ago",
        { TotalDays: < 2 } => "yesterday",
        { TotalDays: < 30 } => $"{(int)t.TotalDays} days ago",
        _ => "on " + DateTimeOffset.Now.Add(-t).ToString("d MMM yyyy"),
    };

    static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }
}

public partial class MainWindow
{
    static readonly (string Label, int Months)[] RetentionLabels =
        [("Forever", 0), ("1 month", 1), ("3 months", 3), ("6 months", 6), ("12 months", 12)];

    bool historyUiReady;

    void InitializeHistoryPanel()
    {
        foreach (var (label, months) in RetentionLabels)
            Retention.Items.Add(new ComboBoxItem { Content = label, Tag = months });
        Retention.SelectedIndex = Array.FindIndex(RetentionLabels, r => r.Months == history.RetentionMonths);
        PauseRecording.IsChecked = history.IsRecordingPaused;
        RangeFrom.SelectedDate = RangeTo.SelectedDate = DateTime.Today;
        historyUiReady = true;
    }

    void HistoryButton_Changed(object sender, RoutedEventArgs e)
    {
        var show = HistoryButton.IsChecked == true;
        if (show) SettingsButton.IsChecked = false;
        HistoryPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            history.ApplyRetention();
            RefreshHistory();
            HistorySearch.Focus();
        }
    }

    void RefreshHistory()
    {
        if (HistoryPanel.Visibility != Visibility.Visible) return;
        var now = DateTimeOffset.UtcNow;
        var items = history.Search(HistorySearch.Text).Select(e => new HistoryItem(e, now)).ToList();
        HistoryList.ItemsSource = items;

        var searching = HistorySearch.Text.Trim().Length > 0;
        HistoryEmpty.Text = searching ? "No videos match your search."
            : history.IsRecordingPaused ? "Watch History is empty, and recording is paused."
            : "Videos you watch for more than about 10 seconds appear here.";
        HistoryEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void HistorySearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshHistory();

    void HistoryItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) != null) return; // the ✕ button
        if ((sender as FrameworkElement)?.DataContext is HistoryItem item) OpenEntry(item.Entry);
    }

    void HistoryList_KeyDown(object sender, KeyEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryItem item) return;
        if (e.Key == Key.Enter) { OpenEntry(item.Entry); e.Handled = true; }
        else if (e.Key == Key.Delete) { history.Delete(item.VideoId); RefreshHistory(); e.Handled = true; }
    }

    void OpenEntry(WatchEntry entry) =>
        Core?.Navigate(LeanPlayerRoute.PlayerUrl(new VideoRequest(entry.VideoId, entry.StartFromSeconds)));

    void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string id) { history.Delete(id); RefreshHistory(); }
    }

    // Checked/Unchecked (not Click) so keyboard, mouse and accessibility tools all take effect.
    void PauseRecording_Changed(object sender, RoutedEventArgs e)
    {
        if (!historyUiReady) return;
        history.IsRecordingPaused = PauseRecording.IsChecked == true;
        RefreshHistory();
    }

    void Retention_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!historyUiReady || Retention.SelectedItem is not ComboBoxItem { Tag: int months }) return;
        history.RetentionMonths = months;
        var removed = history.ApplyRetention();
        if (removed > 0) RefreshHistory();
    }

    void DeleteRange_Click(object sender, RoutedEventArgs e)
    {
        if (RangeFrom.SelectedDate is not { } from || RangeTo.SelectedDate is not { } to) return;
        if (to < from) (from, to) = (to, from);
        var fromUtc = new DateTimeOffset(from.Date, TimeZoneInfo.Local.GetUtcOffset(from.Date));
        var toLocalEnd = to.Date.AddDays(1).AddTicks(-1);
        var toUtc = new DateTimeOffset(toLocalEnd, TimeZoneInfo.Local.GetUtcOffset(toLocalEnd));

        var answer = MessageBox.Show(this,
            $"Delete everything you watched from {from:d MMM yyyy} to {to:d MMM yyyy}?\n\nThis can't be undone.",
            "Delete Watch History", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        history.DeleteRange(fromUtc, toUtc);
        RefreshHistory();
    }

    void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, "Delete your whole Watch History?\n\nThis can't be undone.",
            "Clear Watch History", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        history.Clear();
        RefreshHistory();
    }

    static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        for (var cur = d; cur != null;
             cur = cur is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
            if (cur is T t) return t;
        return null;
    }
}
