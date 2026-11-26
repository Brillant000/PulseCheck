using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PulseCheck;

/// <summary>Full-screen solid colours to spot dead or stuck pixels on a new display.</summary>
public sealed class ScreenTestWindow : Window
{
    static readonly Color[] Colors_ = { Colors.Black, Colors.White, Colors.Red, Colors.Lime, Colors.Blue };
    readonly TextBlock hint;
    int index;

    public ScreenTestWindow()
    {
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.None;

        hint = new TextBlock
        {
            Text = Loc.T("ui.screentest_hint"),
            Foreground = Brushes.White,
            FontSize = 22,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 700,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Content = new Grid { Children = { hint } };

        MouseLeftButtonDown += (_, _) => Next();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); else Next(); };
    }

    void Next()
    {
        index++;
        if (index >= Colors_.Length) { Close(); return; }
        hint.Visibility = Visibility.Collapsed;
        Background = new SolidColorBrush(Colors_[index]);
    }
}
