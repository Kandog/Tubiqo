using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using H2o.Core.Playback;
using H2o.Core.Rules;

namespace H2o.App;

public partial class MainWindow
{
    bool settingsUiReady;

    static RulePack LoadBuiltInRulePack()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("h2o.default-rulepack.json");
        if (stream == null) return new RulePack("missing", []);
        using var reader = new System.IO.StreamReader(stream);
        try { return RulePack.Parse(reader.ReadToEnd()); }
        catch (RulePackException) { return new RulePack("invalid", []); } // unit tests keep this from shipping
    }

    void InitializeSettingsPanel()
    {
        foreach (var cap in PlaybackPolicy.ValidCaps.Reverse())
            QualityCap.Items.Add(new ComboBoxItem { Content = cap + "p", Tag = cap });
        QualityCap.SelectedItem = QualityCap.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == playbackSettings.Policy.QualityCap);

        foreach (var rule in rulePack.Rules)
        {
            var box = new CheckBox { Content = rule.Title, Tag = rule.Id, IsChecked = trimSettings.IsEnabled(rule), Margin = new Thickness(0, 2, 0, 2) };
            box.Checked += TrimRule_Changed;
            box.Unchecked += TrimRule_Changed;
            TrimRuleList.Children.Add(box);
        }
        RulePackInfo.Text = $"Rule Pack: built-in, version {rulePack.Version}.";
        settingsUiReady = true;
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

    void ReloadForSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsChangedNote.Visibility = Visibility.Collapsed;
        Core?.Reload();
    }
}
