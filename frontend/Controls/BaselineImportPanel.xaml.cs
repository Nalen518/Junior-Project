using System.Windows;
using System.Windows.Controls;
using ClimaPulse.Contracts;
using ClimaPulse.Desktop.Ui;

namespace ClimaPulse.Desktop.Controls;

/// <summary>The "importing 30 years" state (FR-07 to FR-09). Shows itself while a baseline builds.</summary>
public partial class BaselineImportPanel : UserControl
{
    public BaselineImportPanel()
    {
        InitializeComponent();
    }

    /// <summary>Builds the city's baseline. Returns false and shows the error if the service refused or failed.</summary>
    public async Task<bool> RunAsync(IBaselineService baselineService, CityDto city)
    {
        Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;
        ImportProgress.Value = 0;
        CityText.Text = $"Building the baseline for {city.Name}…";

        // Created on the UI thread, so reports come back on it.
        var progress = new Progress<int>(percent => ImportProgress.Value = percent);

        try
        {
            await baselineService.RefreshAsync(city.Id, progress);

            CityText.Text = $"Baseline ready for {city.Name}.";
            return true;
        }
        catch (ClimaPulseException ex)
        {
            ErrorLog.Write(ex);
            CityText.Text = $"The baseline for {city.Name} is not ready.";
            ErrorText.Text =
                $"{ex.Message} {city.Name} stays on your watchlist. Use Build baseline on its card to try again.";
            ErrorText.Visibility = Visibility.Visible;
            return false;
        }
    }
}
