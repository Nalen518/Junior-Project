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

    public string Metric
    {
        get => (string)GetValue(MetricProperty);
        set => SetValue(MetricProperty, value);
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

        var marker = Metric switch
        {
            "Rain" => 3.6d,
            "Wind" => 2.1d,
            _ => 4.0d
        };

        var markerColor = Metric switch
        {
            "Rain" => Color.FromRgb(198, 42, 42),
            "Wind" => Color.FromRgb(176, 112, 0),
            _ => Color.FromRgb(198, 42, 42)
        };

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

        var meanX = left + chartWidth / 2d;

        drawingContext.DrawLine(
            axisPen,
            new Point(meanX, bottom),
            new Point(meanX, bottom - chartHeight * 0.58));
    }

    private static void OnMetricChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        ((BellCurveChart)dependencyObject).InvalidateVisual();
    }
}