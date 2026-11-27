using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClimaPulse.Desktop.Controls;

public partial class Banner : UserControl
{
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message),
            typeof(string),
            typeof(Banner),
            new PropertyMetadata(string.Empty, OnBannerChanged));

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(
            nameof(Kind),
            typeof(string),
            typeof(Banner),
            new PropertyMetadata("Info", OnBannerChanged));

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Banner()
    {
        InitializeComponent();
        ApplyStyle();
    }

    private static void OnBannerChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        ((Banner)dependencyObject).ApplyStyle();
    }

    private void ApplyStyle()
    {
        MessageText.Text = Message;

        var kind = Kind?.Trim().ToLowerInvariant() ?? "info";

        (BannerBorder.Background, BannerBorder.BorderBrush, MessageText.Foreground) =
            kind switch
            {
                "success" => (
                    new SolidColorBrush(Color.FromRgb(228, 243, 247)),
                    new SolidColorBrush(Color.FromRgb(117, 180, 200)),
                    new SolidColorBrush(Color.FromRgb(7, 86, 107))
                ),
                "error" => (
                    new SolidColorBrush(Color.FromRgb(255, 241, 241)),
                    new SolidColorBrush(Color.FromRgb(228, 90, 90)),
                    new SolidColorBrush(Color.FromRgb(180, 35, 24))
                ),
                _ => (
                    new SolidColorBrush(Color.FromRgb(228, 243, 247)),
                    new SolidColorBrush(Color.FromRgb(117, 180, 200)),
                    new SolidColorBrush(Color.FromRgb(7, 86, 107))
                )
            };
    }
}