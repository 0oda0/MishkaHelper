using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MishkaHelper.Core;

/// <summary>Neon line chart with soft fill. Set Values then call InvalidateVisual.</summary>
public class Sparkline : FrameworkElement
{
    public IReadOnlyList<double> Values { get; set; } = Array.Empty<double>();
    public double Max { get; set; } = 100;    // <= 0 → auto
    public Brush? Stroke { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var v = Values;
        if (v.Count < 2 || w <= 0 || h <= 0) return;
        double max = Max > 0 ? Max : Math.Max(1, v.Max() * 1.15);
        var stroke = Stroke ?? (Brush)FindResource("AccentBrush");
        var color = stroke is SolidColorBrush sb ? sb.Color : Colors.White;

        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var c = line.Open())
        using (var f = fill.Open())
        {
            for (int i = 0; i < v.Count; i++)
            {
                var p = new Point(i * w / (v.Count - 1), h - Math.Clamp(v[i] / max, 0, 1) * (h - 2) - 1);
                if (i == 0) { c.BeginFigure(p, false, false); f.BeginFigure(new Point(0, h), true, true); f.LineTo(p, false, false); }
                else { c.LineTo(p, true, true); f.LineTo(p, false, false); }
            }
            f.LineTo(new Point(w, h), false, false);
        }
        var grad = new LinearGradientBrush(Color.FromArgb(0x60, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90);
        dc.DrawGeometry(grad, null, fill);
        dc.DrawGeometry(null, new Pen(stroke, 2) { LineJoin = PenLineJoin.Round }, line);
    }
}

/// <summary>Circular gauge 0..100 with a value label and caption.</summary>
public class Ring : FrameworkElement
{
    public double Value { get; set; }
    public string Label { get; set; } = "";
    public string Caption { get; set; } = "";
    public double Thickness { get; set; } = 8;

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = s / 2 - Thickness;
        var track = new Pen((Brush)FindResource("ControlBrush"), Thickness);
        dc.DrawEllipse(null, track, c, r, r);

        double frac = Math.Clamp(Value / 100, 0, 0.9999);
        var accent = Value >= 90 ? (Brush)FindResource("BadBrush") : Value >= 75 ? (Brush)FindResource("WarnBrush") : (Brush)FindResource("AccentBrush");
        if (frac > 0)
        {
            double a = frac * 2 * Math.PI;
            var start = new Point(c.X, c.Y - r);
            var end = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(start, false, false);
                ctx.ArcTo(end, new Size(r, r), 0, frac > 0.5, SweepDirection.Clockwise, true, true);
            }
            dc.DrawGeometry(null, new Pen(accent, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var txt = new FormattedText(Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, s * 0.2, (Brush)FindResource("TextBrush"), dpi);
        var cap = new FormattedText(Caption, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), Math.Max(9, s * 0.1), (Brush)FindResource("DimBrush"), dpi);
        dc.DrawText(txt, new Point(c.X - txt.Width / 2, c.Y - txt.Height / 2 - cap.Height / 3));
        dc.DrawText(cap, new Point(c.X - cap.Width / 2, c.Y + txt.Height / 2 - cap.Height / 2));
    }
}
