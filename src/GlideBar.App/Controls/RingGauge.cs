using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Path = System.Windows.Shapes.Path;

namespace GlideBar.App.Controls;

/// <summary>
/// A compact circular progress gauge: a faint full-circle track with a coloured arc that sweeps
/// from 12 o'clock clockwise in proportion to <see cref="Value"/> (0..<see cref="Maximum"/>).
/// The centre hosts a value label (set via the template). Reusable for CPU / RAM / GPU / Temp.
/// The arc colour can follow semantic good/warn/critical thresholds automatically.
/// </summary>
[TemplatePart(Name = PartTrack, Type = typeof(Path))]
[TemplatePart(Name = PartArc, Type = typeof(Path))]
public sealed class RingGauge : Control
{
    private const string PartTrack = "PART_Track";
    private const string PartArc = "PART_Arc";

    private Path? _track;
    private Path? _arc;

    static RingGauge()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RingGauge),
            new FrameworkPropertyMetadata(typeof(RingGauge)));
    }

    // ---- Dependency properties -----------------------------------------------------------
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0.0, OnVisualChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(100.0, OnVisualChanged));

    public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
        nameof(RingThickness), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(5.0, OnVisualChanged));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.Gray, OnVisualChanged));

    public static readonly DependencyProperty ArcBrushProperty = DependencyProperty.Register(
        nameof(ArcBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, OnVisualChanged));

    /// <summary>When true, the arc brush is chosen from good/warn/critical thresholds instead of <see cref="ArcBrush"/>.</summary>
    public static readonly DependencyProperty UseSemanticColorProperty = DependencyProperty.Register(
        nameof(UseSemanticColor), typeof(bool), typeof(RingGauge),
        new FrameworkPropertyMetadata(false, OnVisualChanged));

    public static readonly DependencyProperty GoodBrushProperty = DependencyProperty.Register(
        nameof(GoodBrush), typeof(Brush), typeof(RingGauge), new PropertyMetadata(Brushes.LimeGreen));

    public static readonly DependencyProperty WarnBrushProperty = DependencyProperty.Register(
        nameof(WarnBrush), typeof(Brush), typeof(RingGauge), new PropertyMetadata(Brushes.Orange));

    public static readonly DependencyProperty CriticalBrushProperty = DependencyProperty.Register(
        nameof(CriticalBrush), typeof(Brush), typeof(RingGauge), new PropertyMetadata(Brushes.Red));

    public static readonly DependencyProperty WarnThresholdProperty = DependencyProperty.Register(
        nameof(WarnThreshold), typeof(double), typeof(RingGauge), new PropertyMetadata(70.0, OnVisualChanged));

    public static readonly DependencyProperty CriticalThresholdProperty = DependencyProperty.Register(
        nameof(CriticalThreshold), typeof(double), typeof(RingGauge), new PropertyMetadata(88.0, OnVisualChanged));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(RingGauge), new PropertyMetadata(""));

    public static readonly DependencyProperty DisplayValueProperty = DependencyProperty.Register(
        nameof(DisplayValue), typeof(string), typeof(RingGauge), new PropertyMetadata(""));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double RingThickness { get => (double)GetValue(RingThicknessProperty); set => SetValue(RingThicknessProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush ArcBrush { get => (Brush)GetValue(ArcBrushProperty); set => SetValue(ArcBrushProperty, value); }
    public bool UseSemanticColor { get => (bool)GetValue(UseSemanticColorProperty); set => SetValue(UseSemanticColorProperty, value); }
    public Brush GoodBrush { get => (Brush)GetValue(GoodBrushProperty); set => SetValue(GoodBrushProperty, value); }
    public Brush WarnBrush { get => (Brush)GetValue(WarnBrushProperty); set => SetValue(WarnBrushProperty, value); }
    public Brush CriticalBrush { get => (Brush)GetValue(CriticalBrushProperty); set => SetValue(CriticalBrushProperty, value); }
    public double WarnThreshold { get => (double)GetValue(WarnThresholdProperty); set => SetValue(WarnThresholdProperty, value); }
    public double CriticalThreshold { get => (double)GetValue(CriticalThresholdProperty); set => SetValue(CriticalThresholdProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string DisplayValue { get => (string)GetValue(DisplayValueProperty); set => SetValue(DisplayValueProperty, value); }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((RingGauge)d).Redraw();

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _track = GetTemplateChild(PartTrack) as Path;
        _arc = GetTemplateChild(PartArc) as Path;
        Redraw();
    }

    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        var s = base.ArrangeOverride(arrangeBounds);
        Redraw();
        return s;
    }

    private void Redraw()
    {
        if (_arc is null || _track is null) return;

        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0 || double.IsNaN(size)) return;

        double t = RingThickness;
        double r = (size - t) / 2.0;
        if (r <= 0) return;

        var center = new Point(size / 2.0, size / 2.0);

        // Track = full circle.
        _track.Data = FullCircle(center, r);
        _track.StrokeThickness = t;

        // Arc = proportional sweep from 12 o'clock clockwise.
        double max = Maximum <= 0 ? 100 : Maximum;
        double frac = Math.Clamp(Value / max, 0, 1);
        _arc.StrokeThickness = t;
        _arc.Data = frac <= 0 ? Geometry.Empty
                   : frac >= 1 ? FullCircle(center, r)
                   : ArcGeometry(center, r, frac);

        _arc.Stroke = ResolveArcBrush();
    }

    private Brush ResolveArcBrush()
    {
        if (!UseSemanticColor) return ArcBrush;
        double pct = Maximum <= 0 ? Value : Value / Maximum * 100.0;
        if (pct >= CriticalThreshold) return CriticalBrush;
        if (pct >= WarnThreshold) return WarnBrush;
        return GoodBrush;
    }

    private static Geometry FullCircle(Point c, double r)
    {
        var g = new EllipseGeometry(c, r, r);
        g.Freeze();
        return g;
    }

    private static Geometry ArcGeometry(Point c, double r, double frac)
    {
        // Angles measured clockwise from top (−90°).
        double startAngle = -90.0;
        double endAngle = startAngle + 360.0 * frac;
        var start = PointOnCircle(c, r, startAngle);
        var end = PointOnCircle(c, r, endAngle);
        bool isLarge = frac > 0.5;

        var fig = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, isLarge, SweepDirection.Clockwise, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }

    private static Point PointOnCircle(Point c, double r, double angleDeg)
    {
        double a = angleDeg * Math.PI / 180.0;
        return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
    }
}
