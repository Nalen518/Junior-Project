using System.Windows;

namespace ClimaPulse.Desktop.Page.Dashboard.CityDetail.RemoveCity;

public partial class RemoveCityWindow : Window
{
    public RemoveCityWindow(string cityName)
    {
        InitializeComponent();

        TitleText.Text = $"Remove {cityName} from your watchlist?";
        CityNameText.Text = cityName;
        RemoveButton.Content = $"Remove {cityName}";
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
