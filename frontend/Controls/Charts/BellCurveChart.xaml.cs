using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClimaPulse.Desktop.Controls.Charts;

public partial class BellCurveChart : UserControl
{
    public static readonly DependencyProperty MetricProperty =
        DependencyProperty.Register(
            nameof(Metric),
            typeof(string),
            typeof(BellCurveChart),
            new PropertyMetadata("Temperature", OnMetricChanged));

    public static readonly DependencyProperty ZScoreProperty =
        DependencyProperty.Register(
            nameof(ZScore),
            typeof(double?),
            typeof(BellCurveChart),
            new PropertyMetadata(null, OnMetricChanged));

    public string Metric
    {
        get => (string)GetValue(MetricProperty);
        set => SetValue(MetricProperty, value);
    }

    /// <summary>The backend's z-score. Null draws the curve without a marker.</summary>
    public double? ZScore
    {
        get => (double?)GetValue(ZScoreProperty);
        set => SetValue(ZScoreProperty, value);
    }

    public BellCurveChart()
    {
        InitializeComponent();
        SizeChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;

        if (width < 40 || height < 50)
        {
            return;
        }

        var left = 14d;
        var right = width - 14d;
        var top = 10d;
        var bottom = height - 24d;
        var chartWidth = right - left;
        var chartHeight = bottom - top;

        var curvePen = new Pen(
            new SolidColorBrush(Color.FromRgb(114, 137, 148)),
            1);

        var axisPen = new Pen(
            new SolidColorBrush(Color.FromRgb(170, 188, 196)),
            1);

        drawingContext.DrawLine(
            axisPen,
            new Point(left, bottom),
            new Point(right, bottom));

        var curve = new StreamGeometry();

        using (var context = curve.Open())
        {
            for (var point = 0; point <= 80; point++)
            {
                var sigma = -4d + (8d * point / 80d);
                var normalValue = Math.Exp(-0.5 * sigma * sigma);

                var x = left + ((sigma + 4d) / 8d) * chartWidth;
                var y = bottom - normalValue * chartHeight * 0.88;

                if (point == 0)
                {
                    context.BeginFigure(new Point(x, y), false, false);
                }
                else
                {
                    context.LineTo(new Point(x, y), true, false);
                }
            }
        }

        curve.Freeze();
        drawingContext.DrawGeometry(null, curvePen, curve);

        var meanX = left + chartWidth / 2d;

        drawingContext.DrawLine(
            axisPen,
            new Point(meanX, bottom),
            new Point(meanX, bottom - chartHeight * 0.58));

        // Labels so the reader knows which line is which (city detail S4).
        var grey = new SolidColorBrush(Color.FromRgb(101, 115, 126));
        DrawLabel(drawingContext, "Normal (30-year average)", meanX, bottom + 4, grey, TextAlignment.Center);
        DrawLabel(drawingContext, "← lower than usual", left, bottom + 4, grey, TextAlignment.Left);
        DrawLabel(drawingContext, "higher than usual →", right, bottom + 4, grey, TextAlignment.Right);

        if (ZScore is not { } z)
        {
            return;
        }

        // Display only: a z beyond ±4 sits at the chart edge.
        var marker = Math.Clamp(z, -4d, 4d);
        var markerColor = Color.FromRgb(198, 42, 42);

        var markerX =
            left + ((marker + 4d) / 8d) * chartWidth;

        var markerPen = new Pen(
            new SolidColorBrush(markerColor),
            2);

        drawingContext.DrawLine(
            markerPen,
            new Point(markerX, top),
            new Point(markerX, bottom));

        drawingContext.DrawEllipse(
            new SolidColorBrush(markerColor),
            null,
            new Point(markerX, bottom),
            3,
            3);

        // "Today" beside the marker, on the side with room.
        var labelOnLeft = markerX > right - 60;
        DrawLabel(
            drawingContext,
            "Today",
            labelOnLeft ? markerX - 5 : markerX + 5,
            top,
            new SolidColorBrush(markerColor),
            labelOnLeft ? TextAlignment.Right : TextAlignment.Left);
    }

    private void DrawLabel(DrawingContext drawingContext, string text, double x, double y, Brush brush, TextAlignment alignment)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            10,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var offset = alignment switch
        {
            TextAlignment.Center => formatted.Width / 2,
            TextAlignment.Right => formatted.Width,
            _ => 0d,
        };

        drawingContext.DrawText(formatted, new Point(x - offset, y));
    }

    private static void OnMetricChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        ((BellCurveChart)dependencyObject).InvalidateVisual();
    }
}