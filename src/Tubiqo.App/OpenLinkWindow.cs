using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tubiqo.Core.Navigation;

namespace Tubiqo.App;

/// <summary>The Ctrl+L box: paste a YouTube link (or video ID). Anything else is refused here, before any navigation.</summary>
internal sealed class OpenLinkWindow : Window
{
    readonly TextBox input = new() { MinWidth = 420 };
    readonly TextBlock error = new() { Foreground = System.Windows.Media.Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };

    public string? Url { get; private set; }

    public OpenLinkWindow(Window owner)
    {
        Owner = owner;
        Title = "Open a YouTube link";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var open = new Button { Content = "Open", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        open.Click += (_, _) => TryOpen();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(open);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "YouTube link or video ID", Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(input);
        panel.Children.Add(error);
        panel.Children.Add(buttons);
        Content = panel;

        try
        {
            var clip = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            if (LinkInput.LooksLikeYouTubeLink(clip)) input.Text = clip!.Trim();
        }
        catch (System.Runtime.InteropServices.COMException) { } // clipboard busy: start empty

        input.TextChanged += (_, _) => error.Visibility = Visibility.Collapsed;
        Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
    }

    void TryOpen()
    {
        var result = LinkInput.Resolve(input.Text);
        if (!result.Ok)
        {
            error.Text = result.Error;
            error.Visibility = Visibility.Visible;
            input.Focus();
            return;
        }
        Url = result.Url;
        DialogResult = true;
    }
}
