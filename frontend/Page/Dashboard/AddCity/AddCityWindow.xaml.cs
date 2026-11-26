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

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await SearchAsync();
    }

    private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
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

            ResultCountText.Text = Results.Count == 1 ? "1 result" : $"{Results.Count} results";

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
            ShowError("City search needs an internet connection. Try again in a moment.");
        }
        finally
        {
            SearchingPanel.Visibility = Visibility.Collapsed;
            SearchButton.IsEnabled = true;
        }
    }

    private async void AddResultButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CitySearchResult city })
        {
            return;
        }

        AlreadyAddedText.Visibility = Visibility.Collapsed;

        CityDto storedCity;

        try
        {
            storedCity = await _cityService.AddAsync(city);

            await _watchlistService.AddAsync(_profile.Id, new AddWatchlistRequest(storedCity.Id));
        }
        catch (ConflictException)
        {
            AlreadyAddedText.Text = $"{city.Name} is already on your watchlist.";

            AlreadyAddedText.Visibility = Visibility.Visible;

            return;
        }
        catch (ClimaPulseException ex)
        {
            AlreadyAddedText.Text = ex.Message;
            AlreadyAddedText.Visibility = Visibility.Visible;

            return;
        }
        catch (Exception)
        {
            AlreadyAddedText.Text =
                "The city could not be added. Check the database connection and try again.";

            AlreadyAddedText.Visibility = Visibility.Visible;

            return;
        }

        SetImportingState(true);

        ImportOverlay.Visibility = Visibility.Visible;

        try
        {
            var completed = await BaselineImport.RunAsync(_baselineService, storedCity);

            if (completed)
            {
                DialogResult = true;
            }
        }
        finally
        {
            SetImportingState(false);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
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
        ImportOverlay.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        Results.Clear();
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}
