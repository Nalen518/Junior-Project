using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClimaPulse.Contracts;

namespace ClimaPulse.Desktop.Page.Dashboard.AddCity;

public partial class AddCityWindow : Window
{
    private readonly ProfileDto _profile;
    private readonly ICityService _cityService;
    private readonly IWatchlistService _watchlistService;
    private readonly IBaselineService _baselineService;

    private bool _isImportingBaseline;

    private ObservableCollection<CitySearchResult> Results { get; } = [];

    public AddCityWindow(
        ProfileDto profile,
        ICityService cityService,
        IWatchlistService watchlistService,
        IBaselineService baselineService
    )
    {
        _profile = profile;
        _cityService = cityService;
        _watchlistService = watchlistService;
        _baselineService = baselineService;

        InitializeComponent();

        ResultsList.ItemsSource = Results;

        Loaded += (_, _) => SearchTextBox.Focus();
    }

    private async void SearchButton_Click(
        object sender,
        RoutedEventArgs e
    )
    {
        await SearchAsync();
    }

    private async void SearchTextBox_KeyDown(
        object sender,
        KeyEventArgs e
    )
    {
        if (e.Key == Key.Enter)
        {
            await SearchAsync();
        }
    }

    private async Task SearchAsync()
    {
        var query = SearchTextBox.Text.Trim();

        if (query.Length < 2)
        {
            ShowError("Type at least 2 characters to search for a city.");
            return;
        }

        ResetPanels();

        SearchButton.IsEnabled = false;
        SearchingPanel.Visibility = Visibility.Visible;

        try
        {
            var searchResults = await _cityService.SearchAsync(query);

            Results.Clear();

            foreach (var city in searchResults)
            {
                Results.Add(city);
            }

            ResultCountText.Text = Results.Count == 1
                ? "1 result"
                : $"{Results.Count} results";

            if (Results.Count == 0)
            {
                NoResultsPanel.Visibility = Visibility.Visible;
            }
        }
        catch (ClimaPulseException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception)
        {
            ShowError(
                "City search needs an internet connection. Try again in a moment."
            );
        }
        finally
        {
            SearchingPanel.Visibility = Visibility.Collapsed;
            SearchButton.IsEnabled = true;
        }
    }

    private async void AddResultButton_Click(
        object sender,
        RoutedEventArgs e
    )
    {
        if (sender is not Button { Tag: CitySearchResult city })
        {
            return;
        }

        AlreadyAddedText.Visibility = Visibility.Collapsed;
        BaselineProgressPanel.Visibility = Visibility.Visible;

        SetImportingState(true);

        try
        {
            var storedCity = await _cityService.AddAsync(city);

            await _watchlistService.AddAsync(
                _profile.Id,
                new AddWatchlistRequest(storedCity.Id)
            );

            var progress = new Progress<int>(percentage =>
            {
                BaselineProgressBar.Value = percentage;

                BaselineProgressText.Text =
                    percentage switch
                    {
                        < 60 => "Downloading 30 years of historical weather data…",
                        < 100 => "Calculating monthly climate baselines…",
                        _ => "Baseline ready."
                    };
            });

            await _baselineService.RefreshAsync(
                storedCity.Id,
                progress
            );

            DialogResult = true;
        }
        catch (ConflictException)
        {
            AlreadyAddedText.Text =
                $"{city.Name} is already on your watchlist.";

            AlreadyAddedText.Visibility = Visibility.Visible;
            BaselineProgressPanel.Visibility = Visibility.Collapsed;
        }
        catch (ClimaPulseException ex)
        {
            AlreadyAddedText.Text = ex.Message;
            AlreadyAddedText.Visibility = Visibility.Visible;
            BaselineProgressPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            AlreadyAddedText.Text =
                "The city was added, but its baseline import failed. " +
                "You can retry it later from Settings.";

            AlreadyAddedText.Visibility = Visibility.Visible;
            BaselineProgressPanel.Visibility = Visibility.Collapsed;
        }
        finally
        {
            SetImportingState(false);
        }
    }

    private void CancelButton_Click(
        object sender,
        RoutedEventArgs e
    )
    {
        if (_isImportingBaseline)
        {
            return;
        }

        DialogResult = false;
    }

    private void SetImportingState(bool isImporting)
    {
        _isImportingBaseline = isImporting;

        SearchTextBox.IsEnabled = !isImporting;
        SearchButton.IsEnabled = !isImporting;
        ResultsList.IsEnabled = !isImporting;
    }

    private void ResetPanels()
    {
        Results.Clear();
        ResultCountText.Text = string.Empty;
        ErrorPanel.Visibility = Visibility.Collapsed;
        NoResultsPanel.Visibility = Visibility.Collapsed;
        AlreadyAddedText.Visibility = Visibility.Collapsed;
        BaselineProgressPanel.Visibility = Visibility.Collapsed;
        BaselineProgressBar.Value = 0;
        BaselineProgressText.Text = string.Empty;
    }

    private void ShowError(string message)
    {
        Results.Clear();
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}