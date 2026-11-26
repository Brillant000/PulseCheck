using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PulseCheck;

public partial class MainWindow : Window
{
    List<CheckResult> results = new();
    HardwareInfo? hw;
    Usage usage = Usage.Gaming;
    bool solutions;
    readonly string[] cli = Environment.GetCommandLineArgs();
    static readonly string Ver = typeof(MainWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

    string? Arg(string name) { var i = Array.IndexOf(cli, name); return i >= 0 && i + 1 < cli.Length ? cli[i + 1] : null; }
    bool Has(string name) => cli.Contains(name);

    RadioButton? viewScan, viewSol;
    readonly Dictionary<Usage, RadioButton> profileButtons = new();

    public MainWindow()
    {
        InitializeComponent();
        Theme.Detect();
        if (Arg("--theme") is string th) Theme.Dark = th == "dark";
        Loc.Set(Arg("--lang") ?? Loc.Detect());
        solutions = Arg("--view") == "solutions";
        if (Enum.TryParse<Usage>(Arg("--profile"), true, out var up)) usage = up;
        VersionText.Text = "v" + Ver;
        BuildLangPanel();
        BuildViewPanel();
        BuildProfilePanel();
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
            if (Arg("--profile") != null) { usage = Enum.Parse<Usage>(Arg("--profile")!, true); SelectProfile(); Render(); }
            if (double.TryParse(Arg("--scroll"), out var off)) Scroller.ScrollToVerticalOffset(off);
            await Task.Delay(700);
            Shot(shot);
            Application.Current.Shutdown();
        };
    }

    // ------------------------------------------------------------------ chrome
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

    RadioButton Seg(string group, bool on, Action onChecked)
    {
        var rb = new RadioButton { GroupName = group, Style = (Style)FindResource("Seg"), IsChecked = on };
        rb.Checked += (_, _) => onChecked();
        return rb;
    }

    void BuildViewPanel()
    {
        viewScan = Seg("view", !solutions, () => { solutions = false; ApplyLanguage(); Render(); });
        viewSol = Seg("view", solutions, () => { solutions = true; ApplyLanguage(); Render(); });
        ViewPanel.Children.Add(viewScan); ViewPanel.Children.Add(viewSol);
    }

    void BuildProfilePanel()
    {
        foreach (var u in new[] { Usage.Gaming, Usage.Creator, Usage.Work, Usage.Dev })
        {
            var rb = Seg("profile", u == usage, () => { }); // handler replaced below
            var uu = u;
            rb.Checked += (_, _) => { usage = uu; Render(); };
            profileButtons[u] = rb;
            ProfilePanel.Children.Add(rb);
        }
    }

    void SelectProfile() { if (profileButtons.TryGetValue(usage, out var rb)) rb.IsChecked = true; }

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
        if (viewScan != null) viewScan.Content = Loc.T("a.ui.tab_scan");
        if (viewSol != null) viewSol.Content = Loc.T("a.ui.tab_adv");
        ProfileLabel.Text = Loc.T("a.ui.profile");
        foreach (var (u, rb) in profileButtons) rb.Content = Loc.T(Advisor.ProfileKey(u));
        ProfileBar.Visibility = solutions ? Visibility.Visible : Visibility.Collapsed;
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
        var scanner = new Scanner { Speed = SpeedBox.IsChecked == true };
        try
        {
            results = await scanner.RunAsync(prog);
            hw = scanner.Hardware;
            if (Arg("--profile") == null) { usage = HardwareProbe.GuessUsage(hw); SelectProfile(); }
        }
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
        File.WriteAllText(dlg.FileName, Report.Build(results, hw, usage), new UTF8Encoding(true));
        Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
    }

    async void Copy_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PulseCheck {Ver} — {Loc.T("ui.score")}: {Scanner.Score(results)}/100");
        foreach (var r in results.Where(r => r.Status is Status.Fail or Status.Warn).OrderBy(r => r.Status))
            sb.AppendLine($"[{Loc.T(r.Status == Status.Fail ? "s.fail" : "s.warn")}] {r.Name.Text}: {r.Detail.Text}" + (r.Fix != null ? $" -> {r.Fix.Text}" : ""));
        if (hw != null)
            foreach (var s in Advisor.Sorted(Advisor.Build(hw, usage)).Take(5))
                sb.AppendLine($"* {s.Title.Text}: {s.Action.Text}");
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
        var rtb = new RenderTargetBitmap((int)Root.ActualWidth, (int)Root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(Root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var f = File.Create(path);
        enc.Save(f);
    }

    // ------------------------------------------------------------------ rendering helpers
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

    void Render()
    {
        ResultsPanel.Children.Clear();
        Ring.SetScore(results.Count > 0 ? Scanner.Score(results) : null);
        if (solutions) RenderSolutions(); else RenderDiagnostic();
    }

    // ------------------------------------------------------------------ diagnostic view
    void RenderDiagnostic()
    {
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

    // ------------------------------------------------------------------ solutions view
    void RenderSolutions()
    {
        if (hw == null)
        {
            var sp = new StackPanel { Margin = new Thickness(24) };
            sp.Children.Add(Text(Loc.T("a.ui.scanfirst_title"), 19, FontWeights.SemiBold));
            sp.Children.Add(Text(Loc.T("a.ui.scanfirst"), 14, null, "Muted", new Thickness(0, 8, 0, 0)));
            ResultsPanel.Children.Add(Card(sp));
            return;
        }

        ResultsPanel.Children.Add(Text(Loc.T("a.ui.hint"), 13, null, "Muted", new Thickness(2, 0, 0, 14)));
        var all = Advisor.Sorted(Advisor.Build(hw, usage));
        if (all.Count == 0)
        {
            var g = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 16, 18, 16) };
            g.Children.Add(Badge(Status.Ok));
            g.Children.Add(Text(Loc.T("a.ui.none"), 14.5, FontWeights.SemiBold, "Ink", new Thickness(14, 3, 0, 0)));
            ResultsPanel.Children.Add(Card(g));
        }

        foreach (var (kind, key) in new[] { (SugKind.Quick, "a.sec.quick"), (SugKind.Settings, "a.sec.settings"), (SugKind.Upgrade, "a.sec.upgrade") })
        {
            var group = all.Where(s => s.Kind == kind).ToList();
            if (group.Count == 0) continue;
            ResultsPanel.Children.Add(Text(Loc.T(key), 16, FontWeights.SemiBold, "Ink", new Thickness(2, 6, 0, 8)));
            foreach (var s in group) ResultsPanel.Children.Add(SuggestionCard(s));
        }

        ResultsPanel.Children.Add(Text(Loc.T("a.ui.estimate", GpuDb.Instance.AsOf), 11.5, null, "Muted", new Thickness(2, 4, 0, 10)));
    }

    static Border Chip(string text, string color)
    {
        return new Border
        {
            Background = Theme.Br("Soft"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0),
            Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.Br(color) },
        };
    }

    UIElement SuggestionCard(Suggestion s)
    {
        var sp = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };

        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
        var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(chips, Dock.Right);
        if (s.Gain != null) chips.Children.Add(Chip(Loc.T(s.Gain), "Ok"));
        chips.Children.Add(s.Cost == 0 ? Chip(Loc.T("a.cost.0"), "Ok") : Chip(new string('$', s.Cost), "Muted"));
        head.Children.Add(chips);
        head.Children.Add(Text(s.Title.Text, 15.5, FontWeights.SemiBold));
        sp.Children.Add(head);

        for (int i = 0; i < s.Because.Count; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var letter = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Theme.Br("Soft"),
                VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 0),
                Child = new TextBlock { Text = ((char)('A' + i)).ToString(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Theme.Br("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var t = Text(s.Because[i].Text, 13.5, null, "Muted"); Grid.SetColumn(t, 1);
            row.Children.Add(letter); row.Children.Add(t);
            sp.Children.Add(row);
        }

        var action = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontSize = 14 };
        action.Inlines.Add(new Run("→  ") { Foreground = Theme.Br("Accent"), FontWeight = FontWeights.Bold });
        action.Inlines.Add(new Run(s.Action.Text) { Foreground = Theme.Br("Ink"), FontWeight = FontWeights.SemiBold });
        sp.Children.Add(action);

        if (s.Notes.Count > 0)
        {
            sp.Children.Add(Text(Loc.T("a.ui.check"), 11.5, FontWeights.SemiBold, "Muted", new Thickness(0, 10, 0, 2)));
            foreach (var n in s.Notes) sp.Children.Add(Text("• " + n.Text, 12.5, null, "Muted", new Thickness(0, 0, 0, 1)));
        }

        if (s.OpenUri != null)
        {
            var b = new Button { Content = Loc.T("a.ui.open"), Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 6, 14, 6) };
            var uri = s.OpenUri;
            b.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { } };
            sp.Children.Add(b);
        }
        return Card(sp);
    }
}
