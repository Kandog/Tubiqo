using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Tubiqo.Core.Playback;
using Tubiqo.Core.Rules;
using Tubiqo.Core.Updates;

namespace Tubiqo.App;

public partial class MainWindow
{
    bool settingsUiReady;

    internal static RulePack LoadBuiltInRulePack()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("tubiqo.default-rulepack.json");
        if (stream == null) return new RulePack("0.0", []);
        using var reader = new System.IO.StreamReader(stream);
        try { return RulePack.Parse(reader.ReadToEnd()); }
        catch (RulePackException) { return new RulePack("0.0", []); } // unit tests keep this from shipping
    }

    /// <summary>The Rule Pack in use: a downloaded one if it's valid and at least as new as the built-in one.</summary>
    RulePack CurrentRulePack() => RulePackStore.Choose(builtInRulePack, rulePackStore.LoadCurrent());

    void InitializeSettingsPanel()
    {
        foreach (var cap in PlaybackPolicy.ValidCaps.Reverse())
            QualityCap.Items.Add(new ComboBoxItem { Content = cap + "p", Tag = cap });
        QualityCap.SelectedItem = QualityCap.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == playbackSettings.Policy.QualityCap);
        PauseWhenMinimized.IsChecked = preferences.PauseWhenMinimized;
        PopulateTrimRules();
        settingsUiReady = true;
    }

    void PopulateTrimRules()
    {
        var wasReady = settingsUiReady;
        settingsUiReady = false;
        TrimRuleList.Children.Clear();
        foreach (var rule in rulePack.Rules)
        {
            var box = new CheckBox { Content = rule.Title, Tag = rule.Id, IsChecked = trimSettings.IsEnabled(rule), Margin = new Thickness(0, 2, 0, 2) };
            box.Checked += TrimRule_Changed;
            box.Unchecked += TrimRule_Changed;
            TrimRuleList.Children.Add(box);
        }
        var source = ReferenceEquals(rulePack, builtInRulePack) ? "built-in" : "downloaded";
        RulePackInfo.Text = $"Rule Pack {rulePack.Version} ({source}).";
        VersionInfo.Text = $"Tubiqo {AppVersion.ToString(3)}";
        RollbackButton.IsEnabled = rulePackStore.HasPrevious;
        settingsUiReady = wasReady;
    }

    void SettingsButton_Changed(object sender, RoutedEventArgs e)
    {
        var show = SettingsButton.IsChecked == true;
        if (show) HistoryButton.IsChecked = false;
        SettingsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    async void QualityCap_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!settingsUiReady || QualityCap.SelectedItem is not ComboBoxItem { Tag: int cap }) return;
        playbackSettings.Policy = playbackSettings.Policy.WithQualityCap(cap);
        await ApplySettingsAsync();
        SettingsChangedNote.Visibility = Visibility.Visible;
    }

    async void TrimRule_Changed(object sender, RoutedEventArgs e)
    {
        if (!settingsUiReady || sender is not CheckBox { Tag: string id } box) return;
        trimSettings.SetEnabled(id, box.IsChecked == true);
        await ApplySettingsAsync();
        SettingsChangedNote.Visibility = Visibility.Visible;
    }

    void PauseWhenMinimized_Changed(object sender, RoutedEventArgs e)
    {
        if (settingsUiReady) preferences.PauseWhenMinimized = PauseWhenMinimized.IsChecked == true;
    }

    void ReloadForSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsChangedNote.Visibility = Visibility.Collapsed;
        Core?.Reload();
    }
}
