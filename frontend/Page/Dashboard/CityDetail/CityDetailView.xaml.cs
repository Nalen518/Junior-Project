using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ClimaPulse.Contracts;
using ClimaPulse.Desktop.Formatting;
using ClimaPulse.Desktop.Page.Dashboard.CityDetail.RemoveCity;
using ClimaPulse.Enums;

namespace ClimaPulse.Desktop.Page.Dashboard.CityDetail;

public partial class CityDetailView : UserControl
{
    private readonly ProfileDto _profile;
    private WatchlistEntryDto _entry;

    private readonly IWatchlistService _watchlistService;
    private readonly IEventLedgerService _eventLedgerService;
    private readonly IDashboardService _dashboardService;
    private readonly IMonitoringService _monitoringService;
    private readonly IBaselineService _baselineService;
    private readonly Action _goBack;
    private readonly Action<int> _openLedgerForCity;

    private readonly ObservableCollection<EventRow> _events = [];
    private IReadOnlyList<BaselineDto> _baselines = [];
    private DashboardCityDto? _dashboardCity;

    private MetricType _selectedMetric = MetricType.Temperature;

    public CityDetailView(
        ProfileDto profile,
        WatchlistEntryDto entry,
        IWatchlistService watchlistService,
        IEventLedgerService eventLedgerService,
        IDashboardService dashboardService,
        IMonitoringService monitoringService,
        IBaselineService baselineService,
        Action goBack,
        Action<int> openLedgerForCity
    )
    {
        _profile = profile;
        _entry = entry;

        _watchlistService = watchlistService;
        _eventLedgerService = eventLedgerService;
        _dashboardService = dashboardService;
        _monitoringService = monitoringService;
        _baselineService = baselineService;
        _goBack = goBack;
        _openLedgerForCity = openLedgerForCity;

        InitializeComponent();

        CityNameText.Text = _entry.City.Name;
        CityLocationText.Text = FormatLocation(_entry.City);
        EventsHeadingText.Text = $"Past events in {_entry.City.Name}";

        ThresholdTextBox.Text = _entry.AlertThresholdZScore.ToString(
            "0.0",
            CultureInfo.InvariantCulture
        );

        NotesTextBox.Text = _entry.Notes ?? string.Empty;

        EventsList.ItemsSource = _events;

        SetActiveMetricButton(TemperatureButton);

        Loaded += CityDetailView_Loaded;
    }

    private async void CityDetailView_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        RefreshButton.IsEnabled = false;
        ReadingStatusText.Text = "Loading current climate data…";

        await LoadBaselinesAsync();
        await LoadCurrentReadingAsync();
        await LoadEventsAsync();

        RefreshButton.IsEnabled = true;
    }

    private async Task LoadBaselinesAsync()
    {
        try
        {
            _baselines = await _baselineService.ListAsync(_entry.City.Id);
        }
        catch (ClimaPulseException ex)
        {
            ReadingStatusText.Text = ex.Message;
        }
        catch (Exception)
        {
            ReadingStatusText.Text = "Baseline data could not be loaded.";
        }
    }

    private async Task LoadCurrentReadingAsync()
    {
        try
        {
            var dashboard = await _dashboardService.GetAsync(_profile.Id);

            _dashboardCity = dashboard.Cities.FirstOrDefault(city =>
                city.City.Id == _entry.City.Id
            );

            RenderSelectedMetric();
        }
        catch (ClimaPulseException ex)
        {
            ReadingStatusText.Text = ex.Message;
            RenderSelectedMetric();
        }
        catch (Exception)
        {
            ReadingStatusText.Text = "Current weather could not be loaded.";
            RenderSelectedMetric();
        }
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

            _events.Clear();

            foreach (var item in page.Items)
            {
                _events.Add(
                    new EventRow(
                        item.Tier.ToString(),
                        FormatDateRange(item),
                        item.Status.ToString()
                    )
                );
            }

            EventsEmptyText.Text =
                _events.Count == 0 ? "No anomaly events for this city yet." : string.Empty;
        }
        catch
        {
            EventsEmptyText.Text = "Past events could not be loaded.";
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        RefreshButton.Content = "Refreshing…";
        ReadingStatusText.Text = "Fetching the latest weather reading…";

        try
        {
            await _monitoringService.RefreshCityAsync(_entry.City.Id);

            await LoadCurrentReadingAsync();
            await LoadEventsAsync();
        }
        catch (ClimaPulseException ex)
        {
            ReadingStatusText.Text = ex.Message;
        }
        catch (Exception)
        {
            ReadingStatusText.Text =
                "This city could not be refreshed. Check your internet connection and try again.";
        }
        finally
        {
            RefreshButton.Content = "Refresh";
            RefreshButton.IsEnabled = true;
        }
    }

    private void TemperatureButton_Click(object sender, RoutedEventArgs e)
    {
        SelectMetric(MetricType.Temperature, TemperatureButton);
    }

    private void RainButton_Click(object sender, RoutedEventArgs e)
    {
        SelectMetric(MetricType.Precipitation, RainButton);
    }

    private void WindButton_Click(object sender, RoutedEventArgs e)
    {
        SelectMetric(MetricType.WindSpeed, WindButton);
    }

    private void SelectMetric(MetricType metric, Button selectedButton)
    {
        _selectedMetric = metric;

        SetActiveMetricButton(selectedButton);
        RenderSelectedMetric();
    }

    private void RenderSelectedMetric()
    {
        var reading = _dashboardCity?.Metrics.FirstOrDefault(metric =>
            metric.Metric == _selectedMetric
        );

        var fallbackBaseline = _baselines.FirstOrDefault(baseline =>
            baseline.Metric == _selectedMetric && baseline.Month == DateTime.UtcNow.Month
        );

        BellCurve.Metric = GetMetricName(_selectedMetric);
        BellCurve.MarkerZScore = reading?.ZScore ?? double.NaN;

        if (reading is null)
        {
            PreviewValueText.Text = "No live reading";
            NormalValueText.Text = fallbackBaseline is null
                ? "—"
                : FormatValue(fallbackBaseline.Mean, _selectedMetric);

            StandardDeviationText.Text = fallbackBaseline is null
                ? "—"
                : FormatDifference(fallbackBaseline.StdDev, _selectedMetric);

            ZScoreText.Text = "—";

            PreviewExplanationText.Text = fallbackBaseline is null
                ? "Refresh this city after its historical baseline has been prepared."
                : "Use Refresh to fetch today’s weather and compare it with the baseline.";

            ReadingStatusText.Text = "No current reading has been fetched in this app session.";

            AdvisoriesList.ItemsSource = Array.Empty<AdvisoryDto>();
            AdvisoriesPanel.Visibility = Visibility.Collapsed;

            return;
        }

        PreviewValueText.Text = reading.ObservedValue is double observed
            ? FormatValue(observed, _selectedMetric)
            : "No data";

        NormalValueText.Text = reading.BaselineMean is double mean
            ? FormatValue(mean, _selectedMetric)
            : "—";

        StandardDeviationText.Text = reading.BaselineStdDev is double stdDev
            ? FormatDifference(stdDev, _selectedMetric)
            : "—";

        ZScoreText.Text = reading.ZScore is double zScore
            ? zScore.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)
            : "—";

        PreviewExplanationText.Text = GetReadingExplanation(reading);

        ReadingStatusText.Text = reading.ObservedAt is DateTime observedAt
            ? $"Live reading from {observedAt.ToLocalTime():d MMM yyyy, HH:mm}"
            : GetDataStatusText(reading.DataStatus);

        AdvisoriesList.ItemsSource = reading.Advisories;
        AdvisoriesPanel.Visibility =
            reading.Advisories.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string GetReadingExplanation(MetricReadingDto reading)
    {
        if (reading.DataStatus != DataStatus.Ok)
        {
            return GetDataStatusText(reading.DataStatus);
        }

        var tier = reading.Tier ?? SeverityTier.Normal;

        return tier switch
        {
            SeverityTier.Normal =>
                $"{GetMetricName(_selectedMetric)} is within its expected range for this time of year.",

            SeverityTier.Watch =>
                $"{GetMetricName(_selectedMetric)} is unusual compared with its 30-year normal.",

            SeverityTier.Warning =>
                $"{GetMetricName(_selectedMetric)} is significantly unusual for this time of year.",

            SeverityTier.Extreme =>
                $"{GetMetricName(_selectedMetric)} is extremely unusual for this time of year.",

            _ => "Current climate comparison is unavailable.",
        };
    }

    private static string GetDataStatusText(DataStatus status)
    {
        return status switch
        {
            DataStatus.NoBaseline =>
                "Historical baseline data is needed before an anomaly can be calculated.",

            DataStatus.ZeroVariance =>
                "This month’s baseline has zero variance, so a z-score cannot be calculated.",

            DataStatus.NoData => "Open-Meteo did not return a reading for this metric.",

            _ => "Current climate comparison is unavailable.",
        };
    }

    private string FormatValue(double value, MetricType metric)
    {
        return metric switch
        {
            MetricType.Temperature => TemperatureDisplay.Format(
                value,
                _profile.PreferredTemperatureUnit
            ),

            MetricType.Precipitation => $"{value.ToString("0.0", CultureInfo.InvariantCulture)} mm",

            MetricType.WindSpeed => $"{value.ToString("0.0", CultureInfo.InvariantCulture)} km/h",

            _ => value.ToString("0.0", CultureInfo.InvariantCulture),
        };
    }

    private string FormatDifference(double value, MetricType metric)
    {
        return metric switch
        {
            MetricType.Temperature => TemperatureDisplay.FormatDifference(
                value,
                _profile.PreferredTemperatureUnit
            ),

            MetricType.Precipitation => $"{value.ToString("0.0", CultureInfo.InvariantCulture)} mm",

            MetricType.WindSpeed => $"{value.ToString("0.0", CultureInfo.InvariantCulture)} km/h",

            _ => value.ToString("0.0", CultureInfo.InvariantCulture),
        };
    }

    private static string GetMetricName(MetricType metric)
    {
        return metric switch
        {
            MetricType.Temperature => "Temperature",
            MetricType.Precipitation => "Rain",
            MetricType.WindSpeed => "Wind",
            _ => "Weather",
        };
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

    private void OpenInLedgerButton_Click(object sender, RoutedEventArgs e)
    {
        _openLedgerForCity(_entry.City.Id);
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
