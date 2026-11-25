using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClimaPulse.Desktop.Controls;

public partial class TierBadge : UserControl
{
    public static readonly DependencyProperty TierProperty =
        DependencyProperty.Register(
            nameof(Tier),
            typeof(string),
            typeof(TierBadge),
            new PropertyMetadata("Normal", OnTierChanged));

    public string Tier
    {
        get => (string)GetValue(TierProperty);
        set => SetValue(TierProperty, value);
    }

    public TierBadge()
    {
        InitializeComponent();
        ApplyTier();
    }

    private static void OnTierChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        ((TierBadge)dependencyObject).ApplyTier();
    }

    private void ApplyTier()
    {
        var tier = Tier?.Trim().ToLowerInvariant() ?? "normal";

        TierText.Text = tier switch
        {
            "watch" => "◉ Watch",
            "warning" => "△ Warning",
            "extreme" => "⊗ Extreme",
            _ => "✓ Normal"
        };

        (BadgeBorder.Background, TierText.Foreground) = tier switch
        {
            "watch" => (
                new SolidColorBrush(Color.FromRgb(255, 242, 204)),
                new SolidColorBrush(Color.FromRgb(138, 90, 0))
            ),
            "warning" => (
                new SolidColorBrush(Color.FromRgb(255, 232, 204)),
                new SolidColorBrush(Color.FromRgb(158, 78, 0))
            ),
            "extreme" => (
                new SolidColorBrush(Color.FromRgb(255, 225, 225)),
                new SolidColorBrush(Color.FromRgb(180, 35, 24))
            ),
            _ => (
                new SolidColorBrush(Color.FromRgb(220, 247, 230)),
                new SolidColorBrush(Color.FromRgb(18, 123, 68))
            )
        };
    }
}