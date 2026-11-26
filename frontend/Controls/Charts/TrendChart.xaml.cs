using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClimaPulse.Desktop.Controls.Charts;

public partial class TrendChart : UserControl
{
    public static readonly DependencyProperty MetricProperty = DependencyProperty.Register(
        nameof(Metric),
        typeof(string),
        typeof(TrendChart),
        new PropertyMetadata("Temperature", OnMetricChanged)
    );

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values),
        typeof(IReadOnlyList<double>),
        typeof(TrendChart),
        new PropertyMetadata(null, OnMetricChanged)
    );

    public string Metric
    {
        get => (string)GetValue(MetricProperty);
        set => SetValue(MetricProperty, value);
    }

    /// <summary>Daily values, oldest first (IMonitoringService.GetRecentAsync). Nothing is drawn until set.</summary>
    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public TrendChart()
    {
        InitializeComponent();
        SizeChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        var values = Values;

        if (width < 40 || height < 50 || values is null || values.Count < 2)
        {
            return;
        }

        var left = 14d;
        var right = width - 14d;
        var top = 10d;
        var bottom = height - 20d;
        var chartWidth = right - left;
        var chartHeight = bottom - top;

        var bandBrush = new SolidColorBrush(Color.FromRgb(233, 240, 243));

        drawingContext.DrawRectangle(
            bandBrush,
            null,
            new Rect(left, top + chartHeight * 0.35, chartWidth, chartHeight * 0.45)
        );

        var min = values.Min();
        var max = values.Max();
        var range = Math.Max(max - min, 1d);

        var lineColor = Metric == "Rain" ? Color.FromRgb(7, 86, 107) : Color.FromRgb(7, 86, 107);

        var pen = new Pen(new SolidColorBrush(lineColor), 1.8);

        if (Metric == "Rain")
        {
            var barWidth = chartWidth / values.Count * 0.45;

            for (var index = 0; index < values.Count; index++)
            {
                var x = left + index * chartWidth / values.Count;
                var barHeight = (values[index] - min) / range * chartHeight * 0.82 + 6;

                var brush =
                    index == values.Count - 1
                        ? new SolidColorBrush(Color.FromRgb(198, 42, 42))
                        : new SolidColorBrush(lineColor);

                drawingContext.DrawRectangle(
                    brush,
                    null,
                    new Rect(x, bottom - barHeight, barWidth, barHeight)
                );
            }

            return;
        }

        var line = new StreamGeometry();

        using (var context = line.Open())
        {
            for (var index = 0; index < values.Count; index++)
            {
                var x = left + index * chartWidth / (values.Count - 1);

                var y =
                    bottom
                    - ((values[index] - min) / range) * chartHeight * 0.82
                    - chartHeight * 0.08;

                if (index == 0)
                {
                    context.BeginFigure(new Point(x, y), false, false);
                }
                else
                {
                    context.LineTo(new Point(x, y), true, false);
                }
            }
        }

        line.Freeze();
        drawingContext.DrawGeometry(null, pen, line);

        var finalX = right;
        var finalY =
            bottom - ((values[^1] - min) / range) * chartHeight * 0.82 - chartHeight * 0.08;

        drawingContext.DrawEllipse(
            new SolidColorBrush(Color.FromRgb(198, 42, 42)),
            null,
            new Point(finalX, finalY),
            3,
            3
        );
    }

    private static void OnMetricChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e
    )
    {
        ((TrendChart)dependencyObject).InvalidateVisual();
    }
}
