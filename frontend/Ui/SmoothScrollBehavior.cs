using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ClimaPulse.Desktop.Ui;

public static class SmoothScrollBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(SmoothScrollBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static readonly DependencyProperty AnimatedOffsetProperty =
        DependencyProperty.RegisterAttached("AnimatedOffset", typeof(double), typeof(SmoothScrollBehavior),
            new PropertyMetadata(0.0, (d, e) =>
            {
                if (d is ScrollViewer sv) sv.ScrollToVerticalOffset((double)e.NewValue);
            }));

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;

        if ((bool)e.NewValue)
            element.PreviewMouseWheel += OnPreviewMouseWheel;
        else
            element.PreviewMouseWheel -= OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = FindScrollViewer(sender as DependencyObject);
        if (sv is null) return;

        e.Handled = true;
        double target = Math.Clamp(sv.VerticalOffset - (e.Delta * 0.8), 0, sv.ScrollableHeight);

        var anim = new DoubleAnimation
        {
            From = sv.VerticalOffset,
            To = target,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        sv.BeginAnimation(AnimatedOffsetProperty, anim);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? root)
    {
        if (root is ScrollViewer sv) return sv;
        if (root is null) return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var result = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (result is not null) return result;
        }
        return null;
    }
}