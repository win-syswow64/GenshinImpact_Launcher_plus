using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GenShin_Launcher_Plus.Controls;

/// <summary>
/// Circular progress ring control. 
/// Background ring is gray, foreground ring is AccentColor, center shows percentage text.
/// </summary>
public class ProgressRing : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(ProgressRing),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender,
                null, (_, v) => Math.Clamp((double)v, 0, 100)));

    public static readonly DependencyProperty RingThicknessProperty =
        DependencyProperty.Register(nameof(RingThickness), typeof(double), typeof(ProgressRing),
            new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BackgroundRingColorProperty =
        DependencyProperty.Register(nameof(BackgroundRingColor), typeof(Brush), typeof(ProgressRing),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundRingColorProperty =
        DependencyProperty.Register(nameof(ForegroundRingColor), typeof(Brush), typeof(ProgressRing),
            new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextColorProperty =
        DependencyProperty.Register(nameof(TextColor), typeof(Brush), typeof(ProgressRing),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(ProgressRing),
            new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public double RingThickness { get => (double)GetValue(RingThicknessProperty); set => SetValue(RingThicknessProperty, value); }
    public Brush BackgroundRingColor { get => (Brush)GetValue(BackgroundRingColorProperty); set => SetValue(BackgroundRingColorProperty, value); }
    public Brush ForegroundRingColor { get => (Brush)GetValue(ForegroundRingColorProperty); set => SetValue(ForegroundRingColorProperty, value); }
    public Brush TextColor { get => (Brush)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double size = Math.Min(w, h);
        double radius = (size - RingThickness) / 2;
        double cx = w / 2, cy = h / 2;
        var center = new Point(cx, cy);

        // Background ring (full circle)
        dc.DrawEllipse(null, new Pen(BackgroundRingColor, RingThickness), center, radius, radius);

        // Foreground arc
        if (Progress > 0)
        {
            double angle = Progress / 100.0 * 360.0;
            var arcGeo = CreateArcGeometry(cx, cy, radius, angle);
            dc.DrawGeometry(null, new Pen(ForegroundRingColor, RingThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arcGeo);
        }

        // Center text
        string text = $"{Progress:F0}%";
        var ft = new FormattedText(text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            FontSize, TextColor, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
    }

    private static Geometry CreateArcGeometry(double cx, double cy, double radius, double angleDeg)
    {
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            double startAngle = -90; // top
            double endAngle = startAngle + angleDeg;
            double startRad = startAngle * Math.PI / 180;
            double endRad = endAngle * Math.PI / 180;

            var startPt = new Point(cx + radius * Math.Cos(startRad), cy + radius * Math.Sin(startRad));
            var endPt = new Point(cx + radius * Math.Cos(endRad), cy + radius * Math.Sin(endRad));

            bool isLargeArc = angleDeg > 180;
            var size = new Size(radius, radius);

            ctx.BeginFigure(startPt, false, false);
            ctx.ArcTo(endPt, size, 0, isLargeArc, SweepDirection.Clockwise, true, true);
        }
        geom.Freeze();
        return geom;
    }
}

