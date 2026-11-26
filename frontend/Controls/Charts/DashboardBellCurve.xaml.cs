using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClimaPulse.Desktop.Controls.Charts;

public partial class DashboardBellCurve : UserControl
{
    public static readonly DependencyProperty ZScoreProperty =
        DependencyProperty.Register(
            nameof(ZScore),
            typeof(double),
            typeof(DashboardBellCurve),
            new PropertyMetadata(double.NaN, OnChartValueChanged)
        );

    public double ZScore
    {
        get => (double)GetValue(ZScoreProperty);
        set => SetValue(ZScoreProperty, value);
    }

    public DashboardBellCurve()
    {
        InitializeComponent();

        SizeChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (ActualWidth < 80 || ActualHeight < 50)
        {
            return;
        }

        var left = 4d;
        var right = ActualWidth - 4d;
        var top = 5d;
        var bottom = ActualHeight - 10d;

        var chartWidth = right - left;
        var chartHeight = bottom - top;

        var fillBrush = new SolidColorBrush(
            Color.FromRgb(238, 243, 245)
        );

        var curveBrush = new SolidColorBrush(
            Color.FromRgb(128, 145, 154)
        );

        var axisBrush = new SolidColorBrush(
            Color.FromRgb(128, 145, 154)
        );

        var curvePen = new Pen(curveBrush, 1.2);
        var axisPen = new Pen(axisBrush, 1);

        drawingContext.DrawLine(
            axisPen,
            new Point(left, bottom),
            new Point(right, bottom)
        );

        var filledCurve = new StreamGeometry();

        using (var context = filledCurve.Open())
        {
            context.BeginFigure(
                new Point(left, bottom),
                true,
                true
            );

            for (var point = 0; point <= 100; point++)
            {
                var sigma = -4d + (8d * point / 100d);
                var normalValue = Math.Exp(-0.5 * sigma * sigma);

                var x = left + ((sigma + 4d) / 8d) * chartWidth;
                var y = bottom - normalValue * chartHeight * 0.85;

                context.LineTo(new Point(x, y), true, false);
            }

            context.LineTo(new Point(right, bottom), true, false);
        }

        filledCurve.Freeze();

        drawingContext.DrawGeometry(
            fillBrush,
            curvePen,
            filledCurve
        );

        // Centre line: the expected 30-year mean, z = 0.
        var meanX = left + chartWidth / 2d;

        drawingContext.DrawLine(
            axisPen,
            new Point(meanX, bottom),
            new Point(meanX, bottom - chartHeight * 0.83)
        );

        if (double.IsNaN(ZScore))
        {
            return;
        }

        // Keep very large anomalies visible at the edge of the graph.
        var displayedZScore = Math.Clamp(ZScore, -4d, 4d);

        var markerX =
            left + ((displayedZScore + 4d) / 8d) * chartWidth;

        var markerColor = Math.Abs(ZScore) switch
        {
            >= 4d => Color.FromRgb(194, 36, 36),  // Extreme
            >= 3d => Color.FromRgb(201, 125, 0),  // Warning
            >= 2d => Color.FromRgb(175, 107, 0),  // Watch
            _ => Color.FromRgb(7, 86, 107)        // Normal
        };

        var markerBrush = new SolidColorBrush(markerColor);
        var markerPen = new Pen(markerBrush, 2.5);

        drawingContext.DrawLine(
            markerPen,
            new Point(markerX, top),
            new Point(markerX, bottom)
        );

        drawingContext.DrawEllipse(
            markerBrush,
            null,
            new Point(markerX, bottom),
            3.5,
            3.5
        );
    }

    private static void OnChartValueChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e
    )
    {
        ((DashboardBellCurve)dependencyObject).InvalidateVisual();
    }
}