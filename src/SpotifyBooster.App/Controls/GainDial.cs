using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace SpotifyBooster.App.Controls;

public sealed class GainDial : FrameworkElement
{
    private const double StartAngle = 135;
    private const double SweepAngle = 270;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(GainDial),
        new FrameworkPropertyMetadata(
            0d,
            FrameworkPropertyMetadataOptions.AffectsRender,
            OnDialValueChanged,
            CoerceValue));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(GainDial),
        new FrameworkPropertyMetadata(
            1d,
            FrameworkPropertyMetadataOptions.AffectsRender,
            OnDialValueChanged,
            CoerceMaximum));

    private static readonly DependencyProperty DisplayValueProperty = DependencyProperty.Register(
        nameof(DisplayValue),
        typeof(double),
        typeof(GainDial),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public GainDial()
    {
        Focusable = false;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

#if DEBUG
        Point start = PointOnCircle(new Point(), 1, StartAngle);
        Point end = PointOnCircle(new Point(), 1, StartAngle + SweepAngle);
        double diagonal = Math.Sqrt(0.5);
        System.Diagnostics.Debug.Assert(Math.Abs(start.X + diagonal) < 0.000001);
        System.Diagnostics.Debug.Assert(Math.Abs(start.Y - diagonal) < 0.000001);
        System.Diagnostics.Debug.Assert(Math.Abs(end.X - diagonal) < 0.000001);
        System.Diagnostics.Debug.Assert(Math.Abs(end.Y - diagonal) < 0.000001);
#endif
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    private double DisplayValue
    {
        get => (double)GetValue(DisplayValueProperty);
        set => SetValue(DisplayValueProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 260 : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? 260 : availableSize.Height;
        double side = Math.Min(width, height);
        return new Size(side, side);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        Point center = new(ActualWidth / 2, ActualHeight / 2);
        double outerRadius = Math.Max(0, (size / 2) - 3);
        double arcRadius = Math.Max(0, outerRadius - 24);
        double faceRadius = Math.Max(0, arcRadius - 13);
        Color accentColor = ResolveColor("Brush.Accent", Color.FromRgb(55, 239, 126));

        DrawFace(drawingContext, center, faceRadius, accentColor);
        DrawTicks(drawingContext, center, outerRadius);

        if (arcRadius <= 0)
        {
            return;
        }

        Pen trackPen = CreatePen(Color.FromArgb(210, 39, 49, 55), Math.Max(5, size * 0.026));
        drawingContext.DrawGeometry(
            null,
            trackPen,
            CreateArc(center, arcRadius, StartAngle, SweepAngle));

        double maximum = Math.Max(0.001, Maximum);
        double progress = Math.Clamp(DisplayValue / maximum, 0, 1);
        double progressSweep = SweepAngle * progress;
        if (progressSweep <= 0.01)
        {
            return;
        }

        Pen glowPen = CreatePen(Color.FromArgb(70, accentColor.R, accentColor.G, accentColor.B), Math.Max(12, size * 0.055));
        Pen progressPen = CreatePen(accentColor, Math.Max(6, size * 0.031));
        StreamGeometry progressGeometry = CreateArc(center, arcRadius, StartAngle, progressSweep);
        drawingContext.DrawGeometry(null, glowPen, progressGeometry);
        drawingContext.DrawGeometry(null, progressPen, progressGeometry);

        Point indicator = PointOnCircle(center, arcRadius, StartAngle + progressSweep);
        DrawIndicator(drawingContext, indicator, Math.Max(5, size * 0.027), accentColor);
    }

    private static void DrawFace(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Color accentColor)
    {
        if (radius <= 0)
        {
            return;
        }

        RadialGradientBrush shadow = new()
        {
            Center = new Point(0.46, 0.42),
            GradientOrigin = new Point(0.38, 0.32),
            RadiusX = 0.68,
            RadiusY = 0.68,
        };
        shadow.GradientStops.Add(new GradientStop(Color.FromRgb(31, 39, 44), 0));
        shadow.GradientStops.Add(new GradientStop(Color.FromRgb(12, 17, 20), 0.62));
        shadow.GradientStops.Add(new GradientStop(Color.FromRgb(5, 8, 10), 1));
        shadow.Freeze();

        SolidColorBrush accentGlow = new(Color.FromArgb(70, accentColor.R, accentColor.G, accentColor.B));
        accentGlow.Freeze();
        drawingContext.DrawEllipse(
            accentGlow,
            null,
            new Point(center.X, center.Y + 5),
            radius + 4,
            radius + 4);
        drawingContext.DrawEllipse(
            shadow,
            CreatePen(Color.FromRgb(28, 37, 42), 1),
            center,
            radius,
            radius);

        double highlightRadius = radius - 5;
        if (highlightRadius > 0)
        {
            drawingContext.DrawGeometry(
                null,
                CreatePen(Color.FromArgb(48, 255, 255, 255), 1.2),
                CreateArc(center, highlightRadius, 205, 118));
        }
    }

    private static void DrawTicks(
        DrawingContext drawingContext,
        Point center,
        double radius)
    {
        const int tickCount = 40;
        Pen majorPen = CreatePen(Color.FromArgb(155, 104, 122, 132), 1.6);
        Pen minorPen = CreatePen(Color.FromArgb(92, 84, 100, 109), 1);
        for (int index = 0; index <= tickCount; index++)
        {
            bool major = index % 5 == 0;
            double angle = StartAngle + (SweepAngle * index / tickCount);
            Point outer = PointOnCircle(center, radius, angle);
            Point inner = PointOnCircle(center, radius - (major ? 11 : 6), angle);
            drawingContext.DrawLine(major ? majorPen : minorPen, inner, outer);
        }
    }

    private static void DrawIndicator(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Color accentColor)
    {
        RadialGradientBrush glow = new();
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(190, accentColor.R, accentColor.G, accentColor.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, accentColor.R, accentColor.G, accentColor.B), 1));
        glow.Freeze();
        drawingContext.DrawEllipse(glow, null, center, radius * 2.2, radius * 2.2);
        drawingContext.DrawEllipse(Brushes.White, CreatePen(accentColor, 2), center, radius, radius);
    }

    private Color ResolveColor(string resourceKey, Color fallback)
    {
        return TryFindResource(resourceKey) is SolidColorBrush brush
            ? brush.Color
            : fallback;
    }

    private static Pen CreatePen(Color color, double thickness)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        Pen pen = new(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        return pen;
    }

    private static StreamGeometry CreateArc(
        Point center,
        double radius,
        double startAngle,
        double sweepAngle)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(PointOnCircle(center, radius, startAngle), false, false);
            context.ArcTo(
                PointOnCircle(center, radius, startAngle + sweepAngle),
                new Size(radius, radius),
                0,
                Math.Abs(sweepAngle) > 180,
                sweepAngle >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                true,
                false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        double radians = angle * Math.PI / 180;
        return new Point(
            center.X + (radius * Math.Cos(radians)),
            center.Y + (radius * Math.Sin(radians)));
    }

    private static void OnDialValueChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        GainDial dial = (GainDial)dependencyObject;
        dial.CoerceValue(ValueProperty);
        dial.AnimateToCurrentValue();
    }

    private void AnimateToCurrentValue()
    {
        double target = Math.Clamp(Value, 0, Maximum);
        if (!IsVisible || !SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(DisplayValueProperty, null);
            DisplayValue = target;
            return;
        }

        DoubleAnimation animation = new(DisplayValue, target, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        BeginAnimation(DisplayValueProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static object CoerceValue(DependencyObject dependencyObject, object baseValue)
    {
        double maximum = ((GainDial)dependencyObject).Maximum;
        double value = (double)baseValue;
        return double.IsFinite(value)
            ? Math.Clamp(value, 0, Math.Max(0.001, maximum))
            : 0d;
    }

    private static object CoerceMaximum(DependencyObject dependencyObject, object baseValue)
    {
        double maximum = (double)baseValue;
        return double.IsFinite(maximum) && maximum > 0 ? maximum : 1d;
    }
}
