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

    string? Arg(string name) { var i = Array.IndexOf(cli, name); return i >= 0 && i + 1 < cli.Length ? cli[i + 1] : null; }
    bool Has(string name) => cli.Contains(name);
    static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    public MainWindow()
    {
        InitializeComponent();
        foreach (var c in Loc.Codes) LangBox.Items.Add(new ComboBoxItem { Content = Loc.Names[c], Tag = c });
        Loc.Set(Arg("--lang") ?? Loc.Detect());
        LangBox.SelectedIndex = Array.IndexOf(Loc.Codes, Loc.Current);
        LangBox.SelectionChanged += (_, _) =>
        {
            if (LangBox.SelectedItem is ComboBoxItem it) { Loc.Set((string)it.Tag); ApplyLanguage(); Render(); }
        };
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

    void ApplyLanguage()
    {
        Tagline.Text = Loc.T("ui.tagline");
        ScanBtn.Content = Loc.T(results.Count == 0 ? "ui.scan" : "ui.rescan");
        SpeedBox.Content = Loc.T("ui.speed");
        SaveBtn.Content = Loc.T("ui.save");
        ScreenBtn.Content = Loc.T("ui.screentest");
        AdminBtn.Content = Loc.T("ui.admin");
        ScoreLabel.Text = Loc.T("ui.score");
        PrivacyText.Text = Loc.T("ui.privacy") + (Scanner.IsAdmin ? "" : "  " + Loc.T("ui.not_admin"));
    }

    async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    async Task ScanAsync()
    {
        ScanBtn.IsEnabled = false; SaveBtn.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible; Progress.Value = 0;
        var prog = new Progress<(int i, int n, string label)>(p =>
        {
            Progress.Maximum = p.n; Progress.Value = p.i; ProgressText.Text = Loc.T(p.label);
        });
        try { results = await new Scanner { Speed = SpeedBox.IsChecked == true }.RunAsync(prog); }
        finally
        {
            ScanBtn.IsEnabled = true; SaveBtn.IsEnabled = results.Count > 0;
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
        var rtb = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(this);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var f = File.Create(path);
        enc.Save(f);
    }

    // ------------------------------------------------------------------ rendering
    static Border Card(UIElement child) => new()
    {
        Background = Brushes.White, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 14), Child = child,
        Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.08 },
    };

    static TextBlock Text(string s, double size = 13.5, FontWeight? w = null, string color = "#222", Thickness? m = null) => new()
    {
        Text = s, FontSize = size, FontWeight = w ?? FontWeights.Normal, Foreground = B(color),
        TextWrapping = TextWrapping.Wrap, Margin = m ?? new Thickness(0),
    };

    void Render()
    {
        ResultsPanel.Children.Clear();
        if (results.Count == 0)
        {
            ScoreText.Text = "–"; ScoreText.Foreground = B("#98A2B3");
            var sp = new StackPanel { Margin = new Thickness(24) };
            sp.Children.Add(Text(Loc.T("ui.welcome_title"), 18, FontWeights.SemiBold));
            sp.Children.Add(Text(Loc.T("ui.welcome"), 14, null, "#475467", new Thickness(0, 8, 0, 0)));
            ResultsPanel.Children.Add(Card(sp));
            return;
        }

        int fail = results.Count(r => r.Status == Status.Fail), warn = results.Count(r => r.Status == Status.Warn),
            ok = results.Count(r => r.Status == Status.Ok), score = Scanner.Score(results);
        ScoreText.Text = score.ToString();
        ScoreText.Foreground = B(score >= 85 ? "#2E9E5B" : score >= 60 ? "#D99A1C" : "#D0453A");

        var pills = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (n, key, col) in new[] { (fail, "ui.problems", "#D0453A"), (warn, "ui.warnings", "#D99A1C"), (ok, "ui.ok", "#2E9E5B") })
        {
            var cardInner = new StackPanel { Margin = new Thickness(18, 8, 18, 8) };
            cardInner.Children.Add(new TextBlock { Text = n.ToString(), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = B(col), HorizontalAlignment = HorizontalAlignment.Center });
            cardInner.Children.Add(Text(Loc.T(key), 12, null, "#667085"));
            var c = Card(cardInner); c.Margin = new Thickness(0, 0, 10, 0);
            pills.Children.Add(c);
        }
        ResultsPanel.Children.Add(pills);

        foreach (var g in results.GroupBy(r => r.Category))
        {
            ResultsPanel.Children.Add(Text(Loc.T(g.Key), 16, FontWeights.SemiBold, "#101828", new Thickness(2, 6, 0, 8)));
            var list = new StackPanel();
            var items = g.OrderBy(r => r.Status).ToList();
            for (int i = 0; i < items.Count; i++) list.Children.Add(Row(items[i], i == items.Count - 1));
            ResultsPanel.Children.Add(Card(list));
        }
    }

    static UIElement Row(CheckResult r, bool last)
    {
        var g = new Grid { Margin = new Thickness(14, 10, 14, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var (col, glyph) = r.Status switch
        {
            Status.Ok => ("#2E9E5B", "✓"), Status.Warn => ("#D99A1C", "!"),
            Status.Fail => ("#D0453A", "✕"), _ => ("#4A7FB5", "i"),
        };
        var badge = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = B(col),
            VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = glyph, Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var name = Text(r.Name.Text, 13.5, FontWeights.SemiBold, "#101828", new Thickness(0, 3, 12, 0));
        Grid.SetColumn(name, 1);

        var detail = new StackPanel { Margin = new Thickness(0, 3, 0, 0) };
        detail.Children.Add(Text(r.Detail.Text, 13.5, null, "#344054"));
        if (r.Fix != null) detail.Children.Add(Text("→ " + r.Fix.Text, 12.5, null, "#A15C00", new Thickness(0, 4, 0, 0)));
        Grid.SetColumn(detail, 2);

        g.Children.Add(badge); g.Children.Add(name); g.Children.Add(detail);
        return new Border { BorderBrush = B("#EEF0F3"), BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1), Child = g };
    }
}
