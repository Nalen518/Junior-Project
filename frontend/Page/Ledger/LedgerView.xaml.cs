using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ClimaPulse.Contracts;
using ClimaPulse.Enums;
using Microsoft.Win32;

namespace ClimaPulse.Desktop.Page;

public partial class LedgerView : UserControl
{
    private const int PageSize = 10;

    private readonly IEventLedgerService _eventLedgerService;
    private readonly ICityService _cityService;
    private readonly int? _initialCityId;

    private readonly ObservableCollection<EventRow> _events = [];

    private int _page = 1;
    private int _totalPages = 1;
    private bool _loaded;

    public LedgerView(
        IEventLedgerService eventLedgerService,
        ICityService cityService,
        int? initialCityId = null
    )
    {
        _eventLedgerService = eventLedgerService;
        _cityService = cityService;
        _initialCityId = initialCityId;

        InitializeComponent();

        EventsGrid.ItemsSource = _events;

        Loaded += LedgerView_Loaded;
    }

    private async void LedgerView_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadCitiesAsync();
        _loaded = true;
        await LoadEventsAsync();
    }

    private async Task LoadCitiesAsync()
    {
        CityFilter.Items.Clear();
        CityFilter.Items.Add(new CityOption(0, "All cities"));

        try
        {
            var cities = await _cityService.ListStoredAsync();

            foreach (var city in cities)
            {
                CityFilter.Items.Add(new CityOption(city.Id, city.Name));
            }
        }
        catch
        {
            // Ledger filtering still works without a populated city list.
        }

        CityFilter.SelectedItem = _initialCityId is int cityId
            ? CityFilter.Items.OfType<CityOption>().FirstOrDefault(city => city.Id == cityId)
            : null;

        CityFilter.SelectedItem ??= CityFilter.Items[0];
    }

    private async Task LoadEventsAsync()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        EventsGrid.Visibility = Visibility.Collapsed;

        try
        {
            var result = await _eventLedgerService.ListAsync(CreateQuery(), _page, PageSize);

            _events.Clear();

            foreach (var item in result.Items)
            {
                _events.Add(ToRow(item));
            }

            _totalPages = Math.Max(result.TotalPages, 1);

            PagingSummaryText.Text =
                result.TotalItems == 0
                    ? "Showing 0 events"
                    : $"Showing {((_page - 1) * PageSize) + 1}–{((_page - 1) * PageSize) + _events.Count} of {result.TotalItems} events";

            PageText.Text = $"Page {_page} of {_totalPages}";
            PreviousButton.IsEnabled = _page > 1;
            NextButton.IsEnabled = _page < _totalPages;

            if (_events.Count == 0)
            {
                EmptyPanel.Visibility = Visibility.Visible;
            }
            else
            {
                EventsGrid.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            LedgerErrorText.Text = "Check that PostgreSQL is running, then try again.";

            ErrorPanel.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private EventQuery CreateQuery()
    {
        int? cityId = CityFilter.SelectedItem is CityOption { Id: > 0 } city ? city.Id : null;

        DateTime? from =
            SelectedText(DateFilter) == "Last 90 days" ? DateTime.UtcNow.AddDays(-90) : null;

        return new EventQuery(
            CityId: cityId,
            Metric: ParseMetric(),
            Tier: ParseTier(),
            Status: ParseStatus(),
            From: from
        );
    }

    private MetricType? ParseMetric() =>
        SelectedText(MetricFilter) switch
        {
            "Temperature" => MetricType.Temperature,
            "Precipitation" => MetricType.Precipitation,
            "Wind speed" => MetricType.WindSpeed,
            _ => null,
        };

    private SeverityTier? ParseTier() =>
        SelectedText(TierFilter) switch
        {
            "Watch" => SeverityTier.Watch,
            "Warning" => SeverityTier.Warning,
            "Extreme" => SeverityTier.Extreme,
            _ => null,
        };

    private EventStatus? ParseStatus() =>
        SelectedText(StatusFilter) switch
        {
            "Active" => EventStatus.Active,
            "Closed" => EventStatus.Closed,
            _ => null,
        };

    private static string SelectedText(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        _page = 1;
        await LoadEventsAsync();
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_page > 1)
        {
            _page--;
            await LoadEventsAsync();
        }
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_page < _totalPages)
        {
            _page++;
            await LoadEventsAsync();
        }
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadEventsAsync();
    }

    private async void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        CityFilter.SelectedIndex = 0;
        MetricFilter.SelectedIndex = 0;
        TierFilter.SelectedIndex = 0;
        StatusFilter.SelectedIndex = 0;
        DateFilter.SelectedIndex = 1;

        _page = 1;
        await LoadEventsAsync();
    }

    private async void ExportCsvButton_Click(object sender, RoutedEventArgs e)
    {
        await ExportAsync(ExportFormat.Csv, "CSV files (*.csv)|*.csv");
    }

    private async void ExportJsonButton_Click(object sender, RoutedEventArgs e)
    {
        await ExportAsync(ExportFormat.Json, "JSON files (*.json)|*.json");
    }

    private async Task ExportAsync(ExportFormat format, string filter)
    {
        var saveDialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = $"climapulse-ledger-{DateTime.Now:yyyy-MM-dd}",
        };

        if (saveDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await using var file = File.Create(saveDialog.FileName);

            await _eventLedgerService.ExportAsync(CreateQuery(), format, file);

            ShowBanner(
                $"Ledger exported successfully: {Path.GetFileName(saveDialog.FileName)}",
                false
            );
        }
        catch
        {
            ShowBanner(
                "Couldn't save the export. Close the file if it is open and try again.",
                true
            );
        }
    }

    private void EventsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EventsGrid.SelectedItem is not EventRow row)
        {
            return;
        }

        DrawerTierText.Text = $"{row.Tier} · {row.Status}";
        DrawerCityMetricText.Text = $"{row.City} · {row.Metric}";

        DrawerDetailsText.Text =
            $"Started: {row.Started}\n\n"
            + $"Ended: {row.Ended}\n\n"
            + $"Peak value: {row.Peak}\n\n"
            + $"Peak z-score: {row.PeakZ}";

        DrawerColumn.Width = new GridLength(295);
        DetailsDrawer.Visibility = Visibility.Visible;
    }

    private void CloseDrawerButton_Click(object sender, RoutedEventArgs e)
    {
        DetailsDrawer.Visibility = Visibility.Collapsed;
        DrawerColumn.Width = new GridLength(0);
        EventsGrid.SelectedItem = null;
    }

    private void ShowBanner(string message, bool isError)
    {
        MessageBanner.Message = message;
        MessageBanner.Kind = isError ? "Error" : "Success";
        MessageBanner.Visibility = Visibility.Visible;
    }

    private static EventRow ToRow(AnomalyEventDto item) =>
        new(
            item.Tier.ToString(),
            item.CityName,
            item.Metric.ToString(),
            item.StartedAt.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.InvariantCulture),
            item.EndedAt?.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.InvariantCulture)
                ?? "Active",
            item.PeakObservedValue.ToString("0.##", CultureInfo.InvariantCulture),
            item.PeakZScore.ToString("+0.0;-0.0", CultureInfo.InvariantCulture),
            item.Status.ToString()
        );

    private sealed record CityOption(int Id, string Name);

    private sealed record EventRow(
        string Tier,
        string City,
        string Metric,
        string Started,
        string Ended,
        string Peak,
        string PeakZ,
        string Status
    );
}
