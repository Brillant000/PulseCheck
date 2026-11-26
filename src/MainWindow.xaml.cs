using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PulseCheck;

public partial class MainWindow : Window
{
    List<CheckResult> results = new();
    readonly string[] cli = Environment.GetCommandLineArgs();
    static readonly string Ver = typeof(MainWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

    string? Arg(string name) { var i = Array.IndexOf(cli, name); return i >= 0 && i + 1 < cli.Length ? cli[i + 1] : null; }
    bool Has(string name) => cli.Contains(name);

    public MainWindow()
    {
        InitializeComponent();
        Theme.Detect();
        if (Arg("--theme") is string th) Theme.Dark = th == "dark";
        Loc.Set(Arg("--lang") ?? Loc.Detect());
        VersionText.Text = "v" + Ver;
        BuildLangPanel();
        ApplyTheme();
        if (Scanner.IsAdmin) AdminBtn.Visibility = Visibility.Collapsed;
        ApplyLanguage();
        Render();

        Loaded += async (_, _) =>
        {
            var shot = Arg("--screenshot");
            if (shot == null) return;
            SpeedBox.IsChecked = Has("--speed");
            await ScanAsync();
            if (double.TryParse(Arg("--scroll"), out var off)) Scroller.ScrollToVerticalOffset(off);
            await Task.Delay(700);
            Shot(shot);
            Application.Current.Shutdown();
        };
    }

    void BuildLangPanel()
    {
        foreach (var code in Loc.Codes)
        {
            var rb = new RadioButton
            {
                Content = code.ToUpperInvariant(), Tag = code, GroupName = "lang", ToolTip = Loc.Names[code],
                Style = (Style)FindResource("Seg"), IsChecked = code == Loc.Current,
            };
            rb.Checked += (s, _) => { Loc.Set((string)((RadioButton)s).Tag); ApplyLanguage(); Render(); };
            LangPanel.Children.Add(rb);
        }
    }

    void ApplyTheme()
    {
        foreach (var k in Theme.Keys) Resources[k] = Theme.Br(k);
        Ring.InvalidateVisual();
    }

    void ApplyLanguage()
    {
        Tagline.Text = Loc.T("ui.tagline");
        ScanBtn.Content = Loc.T(results.Count == 0 ? "ui.scan" : "ui.rescan");
        SpeedBox.Content = Loc.T("ui.speed");
        SaveBtn.Content = Loc.T("ui.save");
        CopyBtn.Content = Loc.T("ui.copy");
        ScreenBtn.Content = Loc.T("ui.screentest");
        AdminBtn.Content = Loc.T("ui.admin");
        ScoreLabel.Text = Loc.T("ui.score");
        ThemeBtn.ToolTip = Loc.T("ui.theme");
        PrivacyText.Text = Loc.T("ui.privacy") + (Scanner.IsAdmin ? "" : "  " + Loc.T("ui.not_admin"));
    }

    void Theme_Click(object sender, RoutedEventArgs e) { Theme.Dark = !Theme.Dark; ApplyTheme(); Render(); }

    async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    async Task ScanAsync()
    {
        ScanBtn.IsEnabled = false; SaveBtn.IsEnabled = false; CopyBtn.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible; Progress.Value = 0;
        var prog = new Progress<(int i, int n, string label)>(p =>
        {
            Progress.Maximum = p.n; Progress.Value = p.i; ProgressText.Text = Loc.T(p.label);
        });
        try { results = await new Scanner { Speed = SpeedBox.IsChecked == true }.RunAsync(prog); }
        finally
        {
            ScanBtn.IsEnabled = true; SaveBtn.IsEnabled = CopyBtn.IsEnabled = results.Count > 0;
            ProgressPanel.Visibility = Visibility.Collapsed;
        }
        ApplyLanguage();
        Render();
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { FileName = $"PulseCheck-{DateTime.Now:yyyyMMdd-HHmm}.html", Filter = "HTML (*.html)|*.html" };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, Report.Build(results), new UTF8Encoding(true));
        Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
    }

    async void Copy_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PulseCheck {Ver} — {Loc.T("ui.score")}: {Scanner.Score(results)}/100");
        foreach (var r in results.Where(r => r.Status is Status.Fail or Status.Warn).OrderBy(r => r.Status))
        {
            sb.AppendLine($"[{Loc.T(r.Status == Status.Fail ? "s.fail" : "s.warn")}] {r.Name.Text}: {r.Detail.Text}" + (r.Fix != null ? $" -> {r.Fix.Text}" : ""));
        }
        try { Clipboard.SetText(sb.ToString()); CopyBtn.Content = Loc.T("ui.copied"); await Task.Delay(1600); }
        catch { /* clipboard busy */ }
        CopyBtn.Content = Loc.T("ui.copy");
    }

    void ScreenTest_Click(object sender, RoutedEventArgs e) => new ScreenTestWindow().Show();

    void Admin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch { /* UAC prompt declined */ }
    }

    void Shot(string path)
    {
        UpdateLayout();
        // Render the client area only (no title bar or window border).
        var rtb = new RenderTargetBitmap((int)Root.ActualWidth, (int)Root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(Root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var f = File.Create(path);
        enc.Save(f);
    }

    // ------------------------------------------------------------------ rendering
    static Border Card(UIElement child) => new()
    {
        Background = Theme.Br("Surface"), BorderBrush = Theme.Br("Line"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 14), Child = child,
        Effect = Theme.Dark ? null : new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.06 },
    };

    static TextBlock Text(string s, double size = 13.5, FontWeight? w = null, string color = "Ink", Thickness? m = null) => new()
    {
        Text = s, FontSize = size, FontWeight = w ?? FontWeights.Normal, Foreground = Theme.Br(color),
        TextWrapping = TextWrapping.Wrap, Margin = m ?? new Thickness(0),
    };

    void Render()
    {
        ResultsPanel.Children.Clear();
        if (results.Count == 0)
        {
            Ring.SetScore(null);
            var sp = new StackPanel { Margin = new Thickness(24) };
            sp.Children.Add(Text(Loc.T("ui.welcome_title"), 19, FontWeights.SemiBold));
            sp.Children.Add(Text(Loc.T("ui.welcome"), 14, null, "Muted", new Thickness(0, 8, 0, 0)));
            ResultsPanel.Children.Add(Card(sp));
            return;
        }

        int fail = results.Count(r => r.Status == Status.Fail), warn = results.Count(r => r.Status == Status.Warn),
            ok = results.Count(r => r.Status == Status.Ok);
        Ring.SetScore(Scanner.Score(results));

        var pills = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (n, key, col) in new[] { (fail, "ui.problems", "Bad"), (warn, "ui.warnings", "Warn"), (ok, "ui.ok", "Ok") })
        {
            var inner = new StackPanel { Margin = new Thickness(20, 9, 20, 9) };
            inner.Children.Add(new TextBlock { Text = n.ToString(), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Theme.Br(col), HorizontalAlignment = HorizontalAlignment.Center });
            inner.Children.Add(Text(Loc.T(key), 12, null, "Muted"));
            var c = Card(inner); c.Margin = new Thickness(0, 0, 10, 0);
            pills.Children.Add(c);
        }
        ResultsPanel.Children.Add(pills);

        // what needs attention, before the detail
        var attention = results.Where(r => r.Status is Status.Fail or Status.Warn).OrderBy(r => r.Status).ToList();
        ResultsPanel.Children.Add(Text(Loc.T("ui.attention"), 16, FontWeights.SemiBold, "Ink", new Thickness(2, 6, 0, 8)));
        if (attention.Count == 0)
        {
            var g = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 16, 18, 16) };
            g.Children.Add(Badge(Status.Ok));
            g.Children.Add(Text(Loc.T("ui.allgood"), 14.5, FontWeights.SemiBold, "Ink", new Thickness(14, 3, 0, 0)));
            ResultsPanel.Children.Add(Card(g));
        }
        else
        {
            var list = new StackPanel();
            for (int i = 0; i < attention.Count; i++) list.Children.Add(Row(attention[i], i == attention.Count - 1));
            ResultsPanel.Children.Add(Card(list));
        }

        foreach (var g in results.GroupBy(r => r.Category))
        {
            ResultsPanel.Children.Add(Text(Loc.T(g.Key), 16, FontWeights.SemiBold, "Ink", new Thickness(2, 6, 0, 8)));
            var list = new StackPanel();
            var items = g.OrderBy(r => r.Status).ToList();
            for (int i = 0; i < items.Count; i++) list.Children.Add(Row(items[i], i == items.Count - 1));
            ResultsPanel.Children.Add(Card(list));
        }
    }

    static Border Badge(Status s) => new()
    {
        Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Theme.ForStatus(s),
        VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left,
        Child = new TextBlock
        {
            Text = s switch { Status.Ok => "✓", Status.Warn => "!", Status.Fail => "✕", _ => "i" },
            Foreground = Theme.Br("On"), FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    static UIElement Row(CheckResult r, bool last)
    {
        var g = new Grid { Margin = new Thickness(16, 11, 16, 11) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var name = Text(r.Name.Text, 13.5, FontWeights.SemiBold, "Ink", new Thickness(0, 3, 14, 0));
        Grid.SetColumn(name, 1);

        var detail = new StackPanel { Margin = new Thickness(0, 3, 0, 0) };
        detail.Children.Add(Text(r.Detail.Text, 13.5, null, "Muted"));
        if (r.Fix != null) detail.Children.Add(Text("→ " + r.Fix.Text, 12.5, null, "Fix", new Thickness(0, 4, 0, 0)));
        Grid.SetColumn(detail, 2);

        g.Children.Add(Badge(r.Status)); g.Children.Add(name); g.Children.Add(detail);
        return new Border { BorderBrush = Theme.Br("Line"), BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1), Child = g };
    }
}
