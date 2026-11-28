using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ClimaPulse.Contracts;
using ClimaPulse.Desktop.Formatting;
using ClimaPulse.Desktop.Page.Dashboard.CityDetail.RemoveCity;
using ClimaPulse.Desktop.Ui;
using ClimaPulse.Enums;

namespace ClimaPulse.Desktop.Page.Dashboard.CityDetail;

public partial class CityDetailView : UserControl
{
    private readonly ProfileDto _profile;
    private readonly CityDto _city;
    private readonly IWatchlistService _watchlistService;
    private readonly IEventLedgerService _eventLedgerService;
    private readonly IMonitoringService _monitoringService;
    private readonly IBaselineService _baselineService;
    private readonly Action _goBack;
    private readonly Action<int> _openLedger;

    private IReadOnlyList<MetricReadingDto> _metrics;
    private DateTime? _refreshedAt;
    private MetricType _metric = MetricType.Temperature;

    private ObservableCollection<EventRow> Events { get; } = [];

    public CityDetailView(
        ProfileDto profile,
        DashboardCityDto city,
        IWatchlistService watchlistService,
        IEventLedgerService eventLedgerService,
        IMonitoringService monitoringService,
        IBaselineService baselineService,
        Action goBack,
        Action<int> openLedger
    )
    {
        _profile = profile;
        _city = city.City;
        _metrics = city.Metrics;
        _refreshedAt = city.LastRefreshedAt;
        _watchlistService = watchlistService;
        _eventLedgerService = eventLedgerService;
        _monitoringService = monitoringService;
        _baselineService = baselineService;
        _goBack = goBack;
        _openLedger = openLedger;

        InitializeComponent();

        CityNameText.Text = _city.Name;
        CityLocationText.Text = FormatLocation(_city);
        EventsHeadingText.Text = $"Past events in {_city.Name}";

        ThresholdTextBox.Text = city.AlertThresholdZScore.ToString(
            "0.0",
            CultureInfo.InvariantCulture
        );

        EventsList.ItemsSource = Events;

        Loaded += CityDetailView_Loaded;
    }

    private async void CityDetailView_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadNotesAsync();
        await ShowMetricAsync(_metric);
        await LoadEventsAsync();
    }

    /// <summary>
    /// DashboardCityDto has no note, so read it from the watchlist entry. Save stays off until
    /// this succeeds: saving an unloaded note would send null and clear the stored one.
    /// </summary>
    private async Task LoadNotesAsync()
    {
        NotesTextBox.IsEnabled = false;

        try
        {
            var entries = await _watchlistService.ListAsync(_profile.Id);

            NotesTextBox.Text =
                entries.FirstOrDefault(entry => entry.City.Id == _city.Id)?.Notes ?? string.Empty;

            NotesTextBox.IsEnabled = true;
            SaveSettingsButton.IsEnabled = true;
        }
        catch (ClimaPulseException ex)
        {
            SettingsMessageText.Foreground = System.Windows.Media.Brushes.IndianRed;
            SettingsMessageText.Text = ex.Message;
        }
    }

    private async Task LoadEventsAsync()
    {
        try
        {
            var page = await _eventLedgerService.ListAsync(
                new EventQuery(CityId: _city.Id),
                page: 1,
                pageSize: 5
            );

            Events.Clear();

            foreach (var item in page.Items)
            {
                Events.Add(
                    new EventRow(
                        $"{item.Tier} · {MetricDisplay.Name(item.Metric)}",
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

    private async void TemperatureButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowMetricAsync(MetricType.Temperature);
    }

    private async void RainButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowMetricAsync(MetricType.Precipitation);
    }

    private async void WindButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowMetricAsync(MetricType.WindSpeed);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;

        try
        {
            var result = await _monitoringService.RefreshCityAsync(_city.Id);

            _metrics = result.Metrics;
            _refreshedAt = result.RefreshedAt;

            await ShowMetricAsync(_metric);
            await LoadEventsAsync();
        }
        catch (NotFoundException ex)
        {
            ServiceErrorPresenter.Show(ex, "City not found");
            _goBack();
        }
        catch (ClimaPulseException ex)
        {
            ErrorLog.Write(ex);
            // Keep the old reading on screen (contract 7).
            ServiceErrorPresenter.Show(ex, $"Could not refresh {_city.Name}");
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    /// <summary>Shows what the services returned for one metric. Never scores in the UI.</summary>
    private async Task ShowMetricAsync(MetricType metric)
    {
        _metric = metric;

        SetActiveMetricButton(
            metric switch
            {
                MetricType.Precipitation => RainButton,
                MetricType.WindSpeed => WindButton,
                _ => TemperatureButton,
            }
        );

        var unit = _profile.PreferredTemperatureUnit;
        var reading = _metrics.FirstOrDefault(item => item.Metric == metric);
        var month = MetricDisplay.CityMonth(reading?.ObservedAt ?? DateTime.UtcNow, _city);
        var monthName = MetricDisplay.MonthName(month);

        TodaySubtitleText.Text = _refreshedAt is { } time
            ? $"Refreshed {time.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture)}"
            : "Not refreshed yet";

        TodayValueText.Text = reading?.ObservedValue is { } value
            ? MetricDisplay.Value(metric, value, unit)
            : "—";

        NormalValueText.Text = reading?.BaselineMean is { } mean
            ? MetricDisplay.Value(metric, mean, unit)
            : "—";

        StandardDeviationText.Text = reading?.BaselineStdDev is { } spread
            ? MetricDisplay.Spread(metric, spread, unit)
            : "—";

        ZScoreText.Text = reading?.ZScore is { } z ? MetricDisplay.ZScore(z) : "—";

        BellCurve.Metric = MetricDisplay.Name(metric);
        BellCurve.ZScore = reading?.ZScore;

        BellCurveCaptionText.Text =
            $"How to read: the curve shows how common each {MetricDisplay.Name(metric).ToLowerInvariant()} value was "
            + $"on {monthName} days in 1995–2024. The grey middle line is the 30-year normal"
            + (reading?.ZScore is null
                ? ". Today's red line appears after a refresh."
                : "; the red line is today. The further apart they are, the more unusual today is.");

        TodayExplanationText.Text = _refreshedAt is null
            ? "Not refreshed yet. Press Refresh to compare today with the normal."
            : reading?.DataStatus switch
            {
                DataStatus.Ok when reading is { Tier: { } tier, ZScore: { } score } =>
                    MetricDisplay.Explanation(metric, tier, score, monthName),
                DataStatus.NoBaseline => "Baseline not ready. Build it from the dashboard card.",
                DataStatus.ZeroVariance => "Not enough variation to score.",
                _ => "No data for today.",
            };

        ShowAdvice(reading?.Advisories ?? []);

        await LoadBaselineNoteAsync(metric, month, monthName);
        await LoadTrendAsync(metric, reading, monthName);
    }

    /// <summary>The 14-day chart (S4 "Last 14 days"), with a caption that says what each mark means (OPEN-9).</summary>
    private async Task LoadTrendAsync(MetricType metric, MetricReadingDto? reading, string monthName)
    {
        TrendChart.Metric = MetricDisplay.Name(metric);
        TrendChart.Values = null;

        // Normal range = this month's 30-year average ± 1 std dev (rain cannot go below 0).
        double? normalLow = reading is { BaselineMean: { } mean, BaselineStdDev: { } spread }
            ? metric == MetricType.Precipitation ? Math.Max(0, mean - spread) : mean - spread
            : null;
        double? normalHigh = reading is { BaselineMean: { } m, BaselineStdDev: { } s } ? m + s : null;
        TrendChart.NormalLow = normalLow;
        TrendChart.NormalHigh = normalHigh;
        TrendSummaryText.Text = "Loading the last 14 days…";
        TrendStateMessage.Visibility = Visibility.Collapsed;

        try
        {
            var recent = await _monitoringService.GetRecentAsync(_city.Id, 14);

            if (_metric != metric)
            {
                return; // the user switched tab while this was loading
            }

            var days = recent
                .Where(item => item.Metric == metric && item.ObservedValue is not null)
                .ToList();

            if (days.Count < 2)
            {
                TrendSummaryText.Text = string.Empty;
                TrendStateMessage.Visibility = Visibility.Visible;
                return;
            }

            var unit = _profile.PreferredTemperatureUnit;
            TrendChart.Values = days.Select(item => item.ObservedValue!.Value).ToList();
            var marks = metric switch
            {
                MetricType.Precipitation => "Each bar is one day's total rain",
                MetricType.WindSpeed => "The line is each day's maximum wind speed",
                _ => "The line is each day's mean temperature",
            };

            var band = normalLow is { } bandLow && normalHigh is { } bandHigh
                ? $" The shaded band is the normal range for {monthName} (30-year average ± 1 std dev: "
                    + $"{MetricDisplay.Value(metric, bandLow, unit)} to {MetricDisplay.Value(metric, bandHigh, unit)})."
                : " Refresh the city to see the normal range.";

            TrendSummaryText.Text =
                $"{marks}; red is today.{band} "
                + $"From {MetricDisplay.Value(metric, days[0].ObservedValue!.Value, unit)} "
                + $"to {MetricDisplay.Value(metric, days[^1].ObservedValue!.Value, unit)} today "
                + $"({days.Count} days with data).";
        }
        catch (ClimaPulseException ex)
        {
            ErrorLog.Write(ex);
            if (_metric != metric)
            {
                return;
            }

            TrendSummaryText.Text = string.Empty;
            TrendStateMessage.Title = "Couldn't load the last 14 days";
            TrendStateMessage.Message = ex.Message;
            TrendStateMessage.Visibility = Visibility.Visible;
        }
    }

    private void ShowAdvice(IReadOnlyList<AdvisoryDto> advisories)
    {
        const string none = "No advice. Today is within the normal range.";

        var health = advisories.Where(item => item.Category == AdvisoryCategory.Health).ToList();
        var climate = advisories.Where(item => item.Category == AdvisoryCategory.Climate).ToList();

        HealthAdviceList.ItemsSource = health;
        ClimateAdviceList.ItemsSource = climate;
        HealthAdviceEmptyText.Text = health.Count == 0 ? none : string.Empty;
        ClimateAdviceEmptyText.Text = climate.Count == 0 ? none : string.Empty;
    }

    private async Task LoadBaselineNoteAsync(MetricType metric, int month, string monthName)
    {
        try
        {
            var baselines = await _baselineService.ListAsync(_city.Id, metric);

            if (_metric != metric)
            {
                return; // the user switched tab while this was loading
            }

            var baseline = baselines.FirstOrDefault(item => item.Month == month);

            BaselineNoteText.Text = baseline is null
                ? $"Baseline: not built yet for {monthName}."
                : $"Baseline: {monthName} daily {MetricDisplay.DailyKind(metric)}, "
                    + $"{baseline.BaselineStartYear}–{baseline.BaselineEndYear} ({baseline.SampleSize} days) · "
                    + $"computed {baseline.ComputedAt.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
        }
        catch (ClimaPulseException ex)
        {
            BaselineNoteText.Text = ex.Message;
        }
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

        var noteLength = NotesTextBox.Text.Trim().Length;

        if (noteLength > 500)
        {
            SettingsMessageText.Foreground = System.Windows.Media.Brushes.IndianRed;

            SettingsMessageText.Text = $"The note can be at most 500 characters. This one has {noteLength}.";

            return;
        }

        try
        {
            await _watchlistService.UpdateAsync(
                _profile.Id,
                _city.Id,
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

    private void OpenLedgerButton_Click(object sender, RoutedEventArgs e)
    {
        _openLedger(_city.Id);
    }

    private async void RemoveCityButton_Click(object sender, RoutedEventArgs e)
    {
        var removeWindow = new RemoveCityWindow(_city.Name)
        {
            Owner = Window.GetWindow(this),
        };

        if (removeWindow.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _watchlistService.RemoveAsync(_profile.Id, _city.Id);

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

    private static string FormatLocation(CityDto city) =>
        $"{CityFormat.Place(city.AdminRegion, city.CountryCode)} · {CityFormat.Coordinates(city.Latitude, city.Longitude)} · {city.Timezone}";

    private static string FormatDateRange(AnomalyEventDto item)
    {
        var start = item.StartedAt.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture);

        var end = item.EndedAt?.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture);

        return end is null ? $"Since {start}" : $"{start} – {end}";
    }

    private sealed record EventRow(string Tier, string DateRange, string Status);
}
