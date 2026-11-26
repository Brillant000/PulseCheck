using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PulseCheck;

/// <summary>Confirmation window: shows exactly what a fix will change before anything happens.</summary>
public static class FixDialog
{
    static Window Create(Window? owner, string title, double width)
    {
        var w = new Window
        {
            Owner = owner, Title = "PulseCheck", Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Background = Theme.Br("Bg"), Foreground = Theme.Br("Ink"), ShowInTaskbar = false,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), UseLayoutRounding = true,
        };
        if (owner != null) foreach (DictionaryEntry e in owner.Resources) w.Resources[e.Key] = e.Value;
        return w;
    }

    static TextBlock T(string s, double size, string color = "Ink", FontWeight? weight = null, Thickness? margin = null) => new()
    {
        Text = s, FontSize = size, Foreground = Theme.Br(color), FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0),
    };

    static Button Btn(Window w, string text, bool primary)
    {
        var b = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(22, 9, 22, 9), MinWidth = 96 };
        if (w.Resources[primary ? "Primary" : "Btn"] is Style s) b.Style = s;
        return b;
    }

    static (Window w, FrameworkElement root, Button apply, Button cancel, TextBlock hint) Build(Window? owner, Fix fix, HardwareInfo? hw)
    {
        var w = Create(owner, fix.Title.Text, 580);
        var sp = new StackPanel { Margin = new Thickness(26, 22, 26, 20) };
        sp.Children.Add(T(fix.Title.Text, 19, "Ink", FontWeights.SemiBold));
        sp.Children.Add(T(Loc.T("fx.dlg.will"), 11.5, "Muted", FontWeights.SemiBold, new Thickness(0, 16, 0, 6)));
        foreach (var p in fix.Plan(hw)) sp.Children.Add(T("•  " + p.Text, 14, "Ink", null, new Thickness(0, 0, 0, 5)));

        var extra = fix.ExtraUi(hw);
        if (extra != null) sp.Children.Add(extra);

        sp.Children.Add(T(Loc.T(fix.Reversible ? "fx.dlg.rev" : "fx.dlg.irrev"), 13, fix.Reversible ? "Ok" : "Warn", FontWeights.SemiBold, new Thickness(0, 14, 0, 4)));
        sp.Children.Add(T(Loc.T("fx.log", FixLog.FilePath), 11.5, "Muted"));

        var hint = T(Loc.T("fx.pick"), 12.5, "Bad", null, new Thickness(0, 10, 0, 0));
        hint.Visibility = Visibility.Collapsed;
        sp.Children.Add(hint);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = Btn(w, Loc.T("fx.cancel"), false);
        var apply = Btn(w, Loc.T("fx.apply"), true);
        row.Children.Add(cancel); row.Children.Add(apply);
        sp.Children.Add(row);

        var root = new Border { Background = Theme.Br("Bg"), Child = sp };
        w.Content = root;
        return (w, root, apply, cancel, hint);
    }

    /// <summary>Returns true only if the user pressed Apply.</summary>
    public static bool Confirm(Window owner, Fix fix, HardwareInfo? hw)
    {
        var (w, _, apply, cancel, hint) = Build(owner, fix, hw);
        cancel.IsCancel = true; apply.IsDefault = false;
        cancel.Click += (_, _) => w.DialogResult = false;
        apply.Click += (_, _) =>
        {
            if (!fix.Ready) { hint.Visibility = Visibility.Visible; return; }
            w.DialogResult = true;
        };
        return w.ShowDialog() == true;
    }

    /// <summary>Plain message (errors, results) with a single OK button.</summary>
    public static void Info(Window owner, string title, string message)
    {
        var w = Create(owner, title, 500);
        var sp = new StackPanel { Margin = new Thickness(26, 22, 26, 20) };
        sp.Children.Add(T(title, 18, "Ink", FontWeights.SemiBold));
        sp.Children.Add(T(message, 14, "Muted", null, new Thickness(0, 10, 0, 0)));
        var ok = Btn(w, "OK", true); ok.HorizontalAlignment = HorizontalAlignment.Right; ok.Margin = new Thickness(0, 18, 0, 0);
        ok.IsDefault = true; ok.IsCancel = true;
        ok.Click += (_, _) => w.Close();
        sp.Children.Add(ok);
        w.Content = new Border { Background = Theme.Br("Bg"), Child = sp };
        w.ShowDialog();
    }

    /// <summary>Renders the confirmation window to a PNG (used for documentation and checks).</summary>
    public static void Preview(Window owner, Fix fix, HardwareInfo? hw, string path)
    {
        var (w, root, _, _, _) = Build(owner, fix, hw);
        w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = 40; w.Top = 40;
        w.Show();
        root.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        using var f = File.Create(path); enc.Save(f);
        w.Close();
    }
}
