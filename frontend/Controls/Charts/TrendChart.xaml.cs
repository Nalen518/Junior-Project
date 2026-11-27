using System.Globalization;
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

    public static readonly DependencyProperty NormalLowProperty = DependencyProperty.Register(
        nameof(NormalLow),
        typeof(double?),
        typeof(TrendChart),
        new PropertyMetadata(null, OnMetricChanged)
    );

    public static readonly DependencyProperty NormalHighProperty = DependencyProperty.Register(
        nameof(NormalHigh),
        typeof(double?),
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

    /// <summary>Bottom of the shaded normal range (baseline mean - 1 std dev), in the same unit as Values. Null hides the band.</summary>
    public double? NormalLow
    {
        get => (double?)GetValue(NormalLowProperty);
        set => SetValue(NormalLowProperty, value);
    }

    /// <summary>Top of the shaded normal range (baseline mean + 1 std dev).</summary>
    public double? NormalHigh
    {
        get => (double?)GetValue(NormalHighProperty);
        set => SetValue(NormalHighProperty, value);
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
        var isRain = Metric == "Rain";

        var hasBand = NormalLow is not null && NormalHigh is not null;

        // One scale for the days and the band, so the band sits at its real values. Rain bars start at 0.
        var low = isRain ? 0d : values.Min();
        var high = values.Max();

        if (hasBand)
        {
            low = Math.Min(low, NormalLow!.Value);
            high = Math.Max(high, NormalHigh!.Value);
        }

        var padding = Math.Max((high - low) * 0.08, 0.5);
        if (!isRain)
        {
            low -= padding;
        }

        high += padding;

        double Y(double value) => bottom - (value - low) / (high - low) * chartHeight;

        var grey = new SolidColorBrush(Color.FromRgb(101, 115, 126));
        var lineBrush = new SolidColorBrush(Color.FromRgb(7, 86, 107));
        var todayBrush = new SolidColorBrush(Color.FromRgb(198, 42, 42));

        if (hasBand)
        {
            var bandTop = Y(NormalHigh!.Value);
            var bandBottom = Y(NormalLow!.Value);

            drawingContext.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(233, 240, 243)),
                null,
                new Rect(left, bandTop, chartWidth, Math.Max(bandBottom - bandTop, 1))
            );

            DrawLabel(drawingContext, "Normal range", left + 4, bandTop + 2, grey, TextAlignment.Left);
        }

        DrawLabel(drawingContext, $"{values.Count - 1} days ago", left, bottom + 4, grey, TextAlignment.Left);
        DrawLabel(drawingContext, "Today", right, bottom + 4, todayBrush, TextAlignment.Right);

        if (isRain)
        {
            var slot = chartWidth / values.Count;
            var barWidth = slot * 0.45;

            for (var index = 0; index < values.Count; index++)
            {
                // Last bar ends at the right edge, under the "Today" label.
                var x = left + index * slot + (slot - barWidth);
                var barTop = Math.Min(Y(values[index]), bottom - 2);

                drawingContext.DrawRectangle(
                    index == values.Count - 1 ? todayBrush : lineBrush,
                    null,
                    new Rect(x, barTop, barWidth, bottom - barTop)
                );
            }

            return;
        }

        var line = new StreamGeometry();

        using (var context = line.Open())
        {
            for (var index = 0; index < values.Count; index++)
            {
                var point = new Point(left + index * chartWidth / (values.Count - 1), Y(values[index]));

                if (index == 0)
                {
                    context.BeginFigure(point, false, false);
                }
                else
                {
                    context.LineTo(point, true, false);
                }
            }
        }

        line.Freeze();
        drawingContext.DrawGeometry(null, new Pen(lineBrush, 1.8), line);

        drawingContext.DrawEllipse(todayBrush, null, new Point(right, Y(values[^1])), 3.5, 3.5);
    }

    private void DrawLabel(DrawingContext drawingContext, string text, double x, double y, Brush brush, TextAlignment alignment)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            10,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip
        );

        var offset = alignment == TextAlignment.Right ? formatted.Width : 0d;

        drawingContext.DrawText(formatted, new Point(x - offset, y));
    }

    private static void OnMetricChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e
    )
    {
        ((TrendChart)dependencyObject).InvalidateVisual();
    }
}
