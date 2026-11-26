using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace PulseCheck;

/// <summary>Light/dark palette shared by every view. Mirrors the website tokens.</summary>
public static class Theme
{
    public static bool Dark { get; set; }

    static readonly Dictionary<string, (string light, string dark)> Tokens = new()
    {
        ["Bg"] = ("#F5F8F7", "#0A1317"),
        ["Surface"] = ("#FFFFFF", "#101D23"),
        ["Soft"] = ("#E9F0EE", "#14242B"),
        ["Ink"] = ("#0F1B2D", "#E6EEEC"),
        ["Muted"] = ("#51616F", "#9BAEB5"),
        ["Line"] = ("#D8E2DF", "#21343B"),
        ["Accent"] = ("#1F4FE0", "#7F9DFF"),
        ["AccentInk"] = ("#FFFFFF", "#0A1317"),
        ["Ok"] = ("#0B8A5F", "#34D399"),
        ["Warn"] = ("#965F00", "#FBBF24"),
        ["Bad"] = ("#BE301B", "#F87171"),
        ["Info"] = ("#3B6FB6", "#7FA8E8"),
        ["On"] = ("#FFFFFF", "#04130E"),
        ["Fix"] = ("#8A5600", "#FBBF24"),
    };

    public static IEnumerable<string> Keys => Tokens.Keys;

    public static void Detect()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            Dark = k?.GetValue("AppsUseLightTheme") is int i && i == 0;
        }
        catch { Dark = false; }
    }

    public static SolidColorBrush Br(string key)
    {
        var (l, d) = Tokens[key];
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Dark ? d : l));
        b.Freeze();
        return b;
    }

    public static Brush ForStatus(Status s) => Br(s switch { Status.Ok => "Ok", Status.Warn => "Warn", Status.Fail => "Bad", _ => "Info" });
}

/// <summary>Circular health score gauge.</summary>
public sealed class ScoreRing : FrameworkElement
{
    int? score;

    public void SetScore(int? s) { score = s; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = size / 2 - 7;
        dc.DrawEllipse(null, new Pen(Theme.Br("Soft"), 9), c, r, r);

        if (score is int s)
        {
            var col = Theme.Br(s >= 85 ? "Ok" : s >= 60 ? "Warn" : "Bad");
            var pen = new Pen(col, 9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (s >= 100) dc.DrawEllipse(null, pen, c, r, r);
            else if (s > 0)
            {
                double ang = 360.0 * s / 100.0, rad = (-90 + ang) * Math.PI / 180;
                var start = new Point(c.X, c.Y - r);
                var end = new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
                var fig = new PathFigure { StartPoint = start, IsClosed = false };
                fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, ang > 180, SweepDirection.Clockwise, true));
                var geo = new PathGeometry(); geo.Figures.Add(fig);
                dc.DrawGeometry(null, pen, geo);
            }
        }

        var ft = new FormattedText(score?.ToString() ?? "–", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            size * 0.32, Theme.Br("Ink"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
    }
}
