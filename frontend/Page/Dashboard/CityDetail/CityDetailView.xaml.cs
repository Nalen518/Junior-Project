using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ClimaPulse.Contracts;
using ClimaPulse.Desktop.Formatting;
using ClimaPulse.Desktop.Page.Dashboard.CityDetail.RemoveCity;

namespace ClimaPulse.Desktop.Page.Dashboard.CityDetail;

public partial class CityDetailView : UserControl
{
    private readonly ProfileDto _profile;
    private WatchlistEntryDto _entry;
    private readonly IWatchlistService _watchlistService;
    private readonly IEventLedgerService _eventLedgerService;
    private readonly Action _goBack;

    private ObservableCollection<EventRow> Events { get; } = [];

    public CityDetailView(
        ProfileDto profile,
        WatchlistEntryDto entry,
        IWatchlistService watchlistService,
        IEventLedgerService eventLedgerService,
        Action goBack
    )
    {
        _profile = profile;
        _entry = entry;
        _watchlistService = watchlistService;
        _eventLedgerService = eventLedgerService;
        _goBack = goBack;

        InitializeComponent();

        CityNameText.Text = _entry.City.Name;
        CityLocationText.Text = FormatLocation(_entry.City);
        EventsHeadingText.Text = $"Past events in {_entry.City.Name}";

        ThresholdTextBox.Text = _entry.AlertThresholdZScore.ToString(
            "0.0",
            CultureInfo.InvariantCulture
        );

        NotesTextBox.Text = _entry.Notes ?? string.Empty;

        EventsList.ItemsSource = Events;
        SetPreviewMetric("Temperature");

        Loaded += CityDetailView_Loaded;
    }

    private async void CityDetailView_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadEventsAsync();
    }

    private async Task LoadEventsAsync()
    {
        try
        {
            var page = await _eventLedgerService.ListAsync(
                new EventQuery(CityId: _entry.City.Id),
                page: 1,
                pageSize: 5
            );

            Events.Clear();

            foreach (var item in page.Items)
            {
                Events.Add(
                    new EventRow(
                        item.Tier.ToString(),
                        FormatDateRange(item),
                        item.Status.ToString()
                    )
                );
            }

            EventsEmptyText.Text =
                Events.Count == 0 ? "No anomaly events for this city yet." : string.Empty;
        }
        catch
        {
            EventsEmptyText.Text = "Past events could not be loaded.";
        }
    }

    private void TemperatureButton_Click(object sender, RoutedEventArgs e)
    {
        SetPreviewMetric("Temperature");
    }

    private void RainButton_Click(object sender, RoutedEventArgs e)
    {
        SetPreviewMetric("Rain");
    }

    private void WindButton_Click(object sender, RoutedEventArgs e)
    {
        SetPreviewMetric("Wind");
    }

    private void SetPreviewMetric(string metric)
    {
        BellCurve.Metric = metric;
        TrendChart.Metric = metric;

        if (metric == "Temperature")
        {
            PreviewValueText.Text = TemperatureDisplay.Format(
                36.9,
                _profile.PreferredTemperatureUnit
            );

            NormalValueText.Text = TemperatureDisplay.Format(
                32.6,
                _profile.PreferredTemperatureUnit
            );

            StandardDeviationText.Text = TemperatureDisplay.FormatDifference(
                1.0,
                _profile.PreferredTemperatureUnit
            );

            ZScoreText.Text = "+4.3";

            PreviewExplanationText.Text = "Far hotter than usual for this time of year.";

            TrendDescriptionText.Text = "Sample daily mean-temperature preview.";

            SetActiveMetricButton(TemperatureButton);

            return;
        }

        if (metric == "Rain")
        {
            PreviewValueText.Text = "41.2 mm";
            NormalValueText.Text = "9.8 mm";
            StandardDeviationText.Text = "8.7 mm";
            ZScoreText.Text = "+3.6";

            PreviewExplanationText.Text = "Much wetter than usual for this time of year.";

            TrendDescriptionText.Text = "Sample daily rainfall preview. The final bar is today.";

            SetActiveMetricButton(RainButton);

            return;
        }

        PreviewValueText.Text = "20.1 km/h";
        NormalValueText.Text = "12.4 km/h";
        StandardDeviationText.Text = "3.7 km/h";
        ZScoreText.Text = "+2.1";

        PreviewExplanationText.Text = "Wind is stronger than normal for this time of year.";

        TrendDescriptionText.Text = "Sample daily maximum wind-speed preview.";

        SetActiveMetricButton(WindButton);
    }

    private void SetActiveMetricButton(Button activeButton)
    {
        var buttons = new[] { TemperatureButton, RainButton, WindButton };

        foreach (var button in buttons)
        {
            button.Background =
                button == activeButton
                    ? System.Windows.Media.Brushes.LightCyan
                    : System.Windows.Media.Brushes.White;

            button.BorderBrush =
                button == activeButton
                    ? System.Windows.Media.Brushes.DarkCyan
                    : System.Windows.Media.Brushes.LightGray;
        }
    }

    private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (
            !double.TryParse(
                ThresholdTextBox.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var threshold
            )
            || threshold <= 0
        )
        {
            SettingsMessageText.Foreground = System.Windows.Media.Brushes.IndianRed;

            SettingsMessageText.Text = "Enter a threshold greater than 0, using a decimal point.";

            return;
        }

        try
        {
            _entry = await _watchlistService.UpdateAsync(
                _profile.Id,
                _entry.City.Id,
                new UpdateWatchlistRequest(
                    threshold,
                    NotesTextBox.Text.Trim() is { Length: > 0 } note ? note : null
                )
            );

            SettingsMessageText.Foreground = System.Windows.Media.Brushes.SeaGreen;

            SettingsMessageText.Text = "Settings saved.";
        }
        catch (ClimaPulseException ex)
        {
            SettingsMessageText.Foreground = System.Windows.Media.Brushes.IndianRed;

            SettingsMessageText.Text = ex.Message;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _goBack();
    }

    private async void RemoveCityButton_Click(object sender, RoutedEventArgs e)
    {
        var removeWindow = new RemoveCityWindow(_entry.City.Name)
        {
            Owner = Window.GetWindow(this),
        };

        if (removeWindow.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _watchlistService.RemoveAsync(_profile.Id, _entry.City.Id);

            _goBack();
        }
        catch (ClimaPulseException ex)
        {
            MessageBox.Show(
                ex.Message,
                "Could not remove city",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }
    }

    private static string FormatLocation(CityDto city)
    {
        var region = string.IsNullOrWhiteSpace(city.AdminRegion)
            ? city.CountryCode
            : $"{city.AdminRegion}, {city.CountryCode}";

        return $"{region} · {city.Latitude:F2}, {city.Longitude:F2} · {city.Timezone}";
    }

    private static string FormatDateRange(AnomalyEventDto item)
    {
        var start = item.StartedAt.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture);

        var end = item.EndedAt?.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture);

        return end is null ? $"Since {start}" : $"{start} – {end}";
    }

    private sealed record EventRow(string Tier, string DateRange, string Status);
}
