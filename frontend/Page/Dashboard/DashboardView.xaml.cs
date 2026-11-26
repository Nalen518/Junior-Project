using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ClimaPulse.Contracts;
using ClimaPulse.Desktop.Controls;
using ClimaPulse.Desktop.Formatting;
using ClimaPulse.Desktop.Page.Dashboard.AddCity;
using ClimaPulse.Enums;

namespace ClimaPulse.Desktop.Page;

public partial class DashboardView : UserControl
{
    private readonly ProfileDto _profile;
    private readonly ICityService _cityService;
    private readonly IWatchlistService _watchlistService;
    private readonly IDashboardService _dashboardService;
    private readonly IMonitoringService _monitoringService;
    private readonly Action<WatchlistEntryDto> _openCityDetail;
    private readonly IBaselineService _baselineService;
    private ObservableCollection<DashboardCityCard> Cards { get; } = [];
    private MetricType? _selectedMetric;
    private DashboardDto? _latestDashboard;
    private IReadOnlyList<WatchlistEntryDto> _latestWatchlist = [];

    public DashboardView(
        ProfileDto profile,
        ICityService cityService,
        IWatchlistService watchlistService,
        IDashboardService dashboardService,
        IMonitoringService monitoringService,
        IBaselineService baselineService,
        Action<WatchlistEntryDto> openCityDetail
    )
    {
        _profile = profile;
        _cityService = cityService;
        _watchlistService = watchlistService;
        _dashboardService = dashboardService;
        _monitoringService = monitoringService;
        _openCityDetail = openCityDetail;
        _baselineService = baselineService;

        InitializeComponent();

        CardsList.ItemsSource = Cards;

        Loaded += DashboardView_Loaded;
    }

    private async void DashboardView_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDashboardAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllCitiesAsync();
    }

    private async void AddCityButton_Click(object sender, RoutedEventArgs e)
    {
        var addCityWindow = new AddCityWindow(
            _profile,
            _cityService,
            _watchlistService,
            _baselineService
        )
        {
            Owner = Window.GetWindow(this),
        };

        if (addCityWindow.ShowDialog() == true)
        {
            await LoadDashboardAsync();
        }
    }

    private async Task LoadDashboardAsync()
    {
        SetLoading(true);
        ErrorBanner.Visibility = Visibility.Collapsed;

        try
        {
            var dashboard = await _dashboardService.GetAsync(_profile.Id);
            var watchlist = await _watchlistService.ListAsync(_profile.Id);

            _latestDashboard = dashboard;
            _latestWatchlist = watchlist;

            RenderDashboard();
        }
        catch (ClimaPulseException ex)
        {
            ShowDashboardError(ex.Message);
        }
        catch (Exception)
        {
            ShowDashboardError(
                "Check that PostgreSQL is running and the connection setting is correct."
            );
        }
        finally
        {
            SetLoading(false);
        }
    }

    private async Task RefreshAllCitiesAsync()
    {
        SetLoading(true);
        ErrorBanner.Visibility = Visibility.Collapsed;

        RefreshButton.Content = "Refreshing…";

        try
        {
            var watchlist = await _watchlistService.ListAsync(_profile.Id);

            var failedCityNames = new List<string>();

            foreach (var entry in watchlist)
            {
                try
                {
                    await _monitoringService.RefreshCityAsync(entry.City.Id);
                }
                catch (ClimaPulseException)
                {
                    failedCityNames.Add(entry.City.Name);
                }
                catch (Exception)
                {
                    failedCityNames.Add(entry.City.Name);
                }
            }

            var dashboard = await _dashboardService.GetAsync(_profile.Id);

            _latestDashboard = dashboard;
            _latestWatchlist = watchlist;

            RenderDashboard();

            if (failedCityNames.Count > 0)
            {
                var cityList = string.Join(", ", failedCityNames);

                ShowDashboardError(
                    $"Weather could not be refreshed for: {cityList}. "
                        + "Other cities were refreshed successfully."
                );
            }
        }
        catch (ClimaPulseException ex)
        {
            ShowDashboardError(ex.Message);
        }
        catch (Exception)
        {
            ShowDashboardError(
                "Weather could not be refreshed. Check your internet and database connection."
            );
        }
        finally
        {
            RefreshButton.Content = "Refresh all";
            SetLoading(false);
        }
    }

    private void RenderDashboard()
    {
        if (_latestDashboard is null)
        {
            return;
        }

        var watchlistByCityId = _latestWatchlist.ToDictionary(entry => entry.City.Id);

        Cards.Clear();

        foreach (var city in _latestDashboard.Cities)
        {
            if (!watchlistByCityId.TryGetValue(city.City.Id, out var entry))
            {
                continue;
            }

            Cards.Add(CreateCard(city, entry));
        }

        var refreshedCityCount = _latestDashboard.Cities.Count(city =>
            city.LastRefreshedAt is not null
        );

        var metricName = _selectedMetric is null
            ? "all metrics"
            : GetMetricLabel(_selectedMetric.Value);

        CitySummaryText.Text = _latestDashboard.Cities.Count switch
        {
            0 => "No cities on your watchlist yet.",

            1 => $"1 city · showing {metricName}.",

            _ => $"{_latestDashboard.Cities.Count} cities · showing {metricName}.",
        };

        var newestRefresh = _latestDashboard
            .Cities.Where(city => city.LastRefreshedAt is not null)
            .Select(city => city.LastRefreshedAt!.Value)
            .OrderByDescending(value => value)
            .FirstOrDefault();

        UpdatedText.Text =
            newestRefresh == default
                ? "Not refreshed yet"
                : $"Last refresh {newestRefresh.ToLocalTime():HH:mm}";

        EmptyPanel.Visibility = Cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        CardsScrollViewer.Visibility = Cards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private DashboardCityCard CreateCard(DashboardCityDto city, WatchlistEntryDto entry)
    {
        var selectedReading = SelectReading(city);

        if (selectedReading is null)
        {
            return new DashboardCityCard(
                entry,
                city.City.Name,
                FormatLocation(city.City),
                "Normal",
                "Weather",
                "No live reading",
                "—",
                "No live reading has been fetched yet.",
                "Use Refresh all to retrieve today’s weather.",
                "Not refreshed yet",
                double.NaN
            );
        }

        if (selectedReading.DataStatus != DataStatus.Ok)
        {
            return new DashboardCityCard(
                entry,
                city.City.Name,
                FormatLocation(city.City),
                "Normal",
                GetMetricLabel(selectedReading.Metric),
                GetDataStatusTitle(selectedReading.DataStatus),
                "—",
                GetDataStatusMessage(selectedReading.DataStatus),
                "Refresh the city after its baseline is ready.",
                FormatCardTime(city.LastRefreshedAt),
                double.NaN
            );
        }

        var tier = selectedReading.Tier ?? SeverityTier.Normal;

        return new DashboardCityCard(
            entry,
            city.City.Name,
            FormatLocation(city.City),
            tier.ToString(),
            $"{GetMetricLabel(selectedReading.Metric)} · today",
            FormatReading(selectedReading, _profile.PreferredTemperatureUnit),
            $"z = {FormatZScore(selectedReading.ZScore)}",
            BuildSecondaryText(selectedReading),
            BuildFooterText(city, selectedReading),
            FormatCardTime(city.LastRefreshedAt),
            selectedReading.ZScore ?? double.NaN
        );
    }

    private MetricReadingDto? SelectReading(DashboardCityDto city)
    {
        if (_selectedMetric is MetricType metric)
        {
            return city.Metrics.FirstOrDefault(reading => reading.Metric == metric);
        }

        return city
            .Metrics.OrderByDescending(reading => reading.Tier ?? SeverityTier.Normal)
            .ThenBy(reading => reading.DataStatus == DataStatus.Ok ? 0 : 1)
            .FirstOrDefault();
    }

    private static string GetMetricLabel(MetricType metric)
    {
        return metric switch
        {
            MetricType.Temperature => "Temperature",
            MetricType.Precipitation => "Rain",
            MetricType.WindSpeed => "Wind",
            _ => "Weather",
        };
    }

    private string BuildSecondaryText(MetricReadingDto reading)
    {
        if (reading.BaselineMean is not double mean)
        {
            return "Baseline data is unavailable.";
        }

        return $"Normal for {DateTime.Now:MMM}: " + $"{FormatMetricValue(mean, reading.Metric)}";
    }

    private string BuildFooterText(DashboardCityDto city, MetricReadingDto selectedReading)
    {
        var otherMetrics = city
            .Metrics.Where(metric => metric.Metric != selectedReading.Metric)
            .OrderBy(metric => metric.Metric)
            .Select(metric => $"{GetMetricLabel(metric.Metric)}: {GetMetricStatus(metric)}");

        return string.Join(" · ", otherMetrics);
    }

    private static string GetMetricStatus(MetricReadingDto reading)
    {
        if (reading.DataStatus != DataStatus.Ok)
        {
            return reading.DataStatus switch
            {
                DataStatus.NoBaseline => "Baseline needed",
                DataStatus.NoData => "No data",
                DataStatus.ZeroVariance => "Unavailable",
                _ => "Unavailable",
            };
        }

        return reading.Tier?.ToString() ?? "Normal";
    }

    private string FormatMetricValue(double value, MetricType metric)
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

    private static string FormatCardTime(DateTime? refreshedAt)
    {
        return refreshedAt is null
            ? string.Empty
            : refreshedAt.Value.ToLocalTime().ToString("HH:mm");
    }

    private static string GetDataStatusTitle(DataStatus status)
    {
        return status switch
        {
            DataStatus.NoBaseline => "Baseline needed",
            DataStatus.ZeroVariance => "Baseline variance is zero",
            DataStatus.NoData => "No weather data",
            _ => "Weather data unavailable",
        };
    }

    private static string GetDataStatusMessage(DataStatus status)
    {
        return status switch
        {
            DataStatus.NoBaseline =>
                "Historical data must be imported before ClimaPulse can calculate an anomaly.",
            DataStatus.ZeroVariance => "This month’s historical values have no usable variation.",
            DataStatus.NoData => "Open-Meteo did not return a reading for this metric.",
            _ => "The current weather reading could not be calculated.",
        };
    }

    private static string FormatReading(MetricReadingDto reading, TemperatureUnit temperatureUnit)
    {
        if (reading.ObservedValue is not double value)
        {
            return "No value";
        }

        return reading.Metric switch
        {
            MetricType.Temperature =>
                $"Temperature {TemperatureDisplay.Format(value, temperatureUnit)}",

            MetricType.Precipitation =>
                $"Rain {value.ToString("0.0", CultureInfo.InvariantCulture)} mm",

            MetricType.WindSpeed =>
                $"Wind {value.ToString("0.0", CultureInfo.InvariantCulture)} km/h",

            _ => value.ToString("0.0", CultureInfo.InvariantCulture),
        };
    }

    private static string FormatZScore(double? zScore)
    {
        return zScore is double value
            ? value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)
            : "—";
    }

    private static string FormatRefreshTime(DateTime? refreshedAt)
    {
        return refreshedAt is null
            ? "Not refreshed yet"
            : $"Updated {refreshedAt.Value.ToLocalTime():d MMM, HH:mm}";
    }

    private static string FormatLocation(CityDto city)
    {
        return string.IsNullOrWhiteSpace(city.AdminRegion)
            ? city.CountryCode
            : $"{city.AdminRegion}, {city.CountryCode}";
    }

    private void SetLoading(bool isLoading)
    {
        LoadingPanel.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;

        RefreshButton.IsEnabled = !isLoading;
        AddCityButton.IsEnabled = !isLoading;

        if (isLoading)
        {
            EmptyPanel.Visibility = Visibility.Collapsed;
            CardsScrollViewer.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowDashboardError(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;

        if (Cards.Count == 0)
        {
            CitySummaryText.Text = "Dashboard data could not be loaded.";
        }
    }

    private void AllMetricsButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedMetric = null;

        SetActiveMetricFilterButton(AllMetricsButton);

        RenderDashboard();
    }

    private void TemperatureFilterButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedMetric = MetricType.Temperature;

        SetActiveMetricFilterButton(TemperatureFilterButton);

        RenderDashboard();
    }

    private void RainFilterButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedMetric = MetricType.Precipitation;

        SetActiveMetricFilterButton(RainFilterButton);

        RenderDashboard();
    }

    private void WindFilterButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedMetric = MetricType.WindSpeed;

        SetActiveMetricFilterButton(WindFilterButton);

        RenderDashboard();
    }

    private void SetActiveMetricFilterButton(Button activeButton)
    {
        var buttons = new[]
        {
            AllMetricsButton,
            TemperatureFilterButton,
            RainFilterButton,
            WindFilterButton,
        };

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

            button.Foreground =
                button == activeButton
                    ? System.Windows.Media.Brushes.DarkCyan
                    : System.Windows.Media.Brushes.Black;
        }
    }

    private void CityCard_Click(object? sender, EventArgs e)
    {
        if (sender is CityCard { Entry: WatchlistEntryDto entry })
        {
            _openCityDetail(entry);
        }
    }

    private sealed record DashboardCityCard(
        WatchlistEntryDto Entry,
        string CityName,
        string Location,
        string Tier,
        string MetricLabel,
        string PrimaryText,
        string ZScoreText,
        string SecondaryText,
        string FooterText,
        string UpdatedText,
        double BellCurveZScore
    );
}
