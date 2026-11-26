using System.Windows;
using System.Windows.Controls;
using ClimaPulse.Contracts;

namespace ClimaPulse.Desktop.Controls;

public partial class BaselineImportPanel : UserControl
{
    public BaselineImportPanel()
    {
        InitializeComponent();
    }

    public async Task<bool> RunAsync(IBaselineService baselineService, CityDto city)
    {
        Visibility = Visibility.Visible;

        ErrorText.Visibility = Visibility.Collapsed;
        ImportProgress.Value = 0;

        CityText.Text = $"Preparing baseline for {city.Name}…";

        var progress = new Progress<int>(percentage => ImportProgress.Value = percentage);

        try
        {
            await baselineService.RefreshAsync(city.Id, progress);

            CityText.Text = $"Baseline ready for {city.Name}.";

            return true;
        }
        catch (ClimaPulseException ex)
        {
            CityText.Text = $"Baseline import for {city.Name} did not finish.";

            ErrorText.Text = $"{ex.Message} You can retry later from Settings.";

            ErrorText.Visibility = Visibility.Visible;

            return false;
        }
    }
}
