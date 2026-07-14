using System.Windows;
using System.Windows.Media;

namespace VoiceTranslator.App.Controls;

public sealed class CircularProgressBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(double),
            typeof(CircularProgressBar),
            new FrameworkPropertyMetadata(
                0.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(
            nameof(TrackBrush),
            typeof(Brush),
            typeof(CircularProgressBar),
            new FrameworkPropertyMetadata(
                Brushes.Gainsboro,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressBrushProperty =
        DependencyProperty.Register(
            nameof(ProgressBrush),
            typeof(Brush),
            typeof(CircularProgressBar),
            new FrameworkPropertyMetadata(
                Brushes.SeaGreen,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(
            nameof(StrokeThickness),
            typeof(double),
            typeof(CircularProgressBar),
            new FrameworkPropertyMetadata(
                6.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure
                | FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush ProgressBrush
    {
        get => (Brush)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double thickness = Math.Max(1, StrokeThickness);
        double radius = Math.Max(
            0,
            (Math.Min(ActualWidth, ActualHeight) - thickness) / 2);
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        drawingContext.DrawEllipse(
            null,
            CreatePen(TrackBrush, thickness),
            center,
            radius,
            radius);

        double progress = Math.Clamp(Value, 0, 100);
        if (progress <= 0)
        {
            return;
        }

        Pen progressPen = CreatePen(ProgressBrush, thickness);
        if (progress >= 99.999)
        {
            drawingContext.DrawEllipse(
                null,
                progressPen,
                center,
                radius,
                radius);
            return;
        }

        double angle = progress / 100 * Math.PI * 2;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(
            center.X + Math.Sin(angle) * radius,
            center.Y - Math.Cos(angle) * radius);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.ArcTo(
                end,
                new Size(radius, radius),
                rotationAngle: 0,
                isLargeArc: progress > 50,
                SweepDirection.Clockwise,
                isStroked: true,
                isSmoothJoin: false);
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, progressPen, geometry);
    }

    private static Pen CreatePen(Brush brush, double thickness)
    {
        return new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
    }
}
