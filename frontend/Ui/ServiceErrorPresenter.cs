using System.Windows;
using ClimaPulse.Contracts;

namespace ClimaPulse.Desktop.Ui;

public static class ServiceErrorPresenter
{
    public static void Show(
        ClimaPulseException exception,
        string title)
    {
        MessageBox.Show(
            exception.Message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    public static void ShowUnexpected(string title)
    {
        MessageBox.Show(
            "Something went wrong. Please try again.",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}