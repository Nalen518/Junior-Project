using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClimaPulse.Contracts;

namespace ClimaPulse.Desktop.Page.Auth;

public partial class FirstCityWindow : Window
{
    private readonly ProfileDto _profile;
    private readonly ICityService _cityService;
    private readonly IWatchlistService _watchlistService;

    public ObservableCollection<CitySearchResult> Results { get; } = [];

    public string WelcomeText =>
        $"Welcome, {_profile.DisplayName}. Add the first city you want ClimaPulse to monitor.";

    public FirstCityWindow(
        ProfileDto profile,
        ICityService cityService,
        IWatchlistService watchlistService)
    {
        _profile = profile;
        _cityService = cityService;
        _watchlistService = watchlistService;

        InitializeComponent();

        ResultsList.ItemsSource = Results;
        DataContext = this;

        Loaded += (_, _) => SearchTextBox.Focus();
    }

    private async void SearchButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await SearchAsync();
    }

    private async void SearchTextBox_KeyDown(
        object sender,
        KeyEventArgs e)
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

        SearchButton.IsEnabled = false;
        AddCityButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        Results.Clear();
        StatusText.Text = "Searching Open-Meteo…";

        try
        {
            var results = await _cityService.SearchAsync(query);

            foreach (var result in results)
            {
                Results.Add(result);
            }

            StatusText.Text = Results.Count == 0
                ? "No cities found. Try a different spelling."
                : string.Empty;
        }
        catch (ClimaPulseException ex)
        {
            StatusText.Text = string.Empty;
            ShowError(ex.Message);
        }
        catch (Exception)
        {
            StatusText.Text = string.Empty;
            ShowError(
                "The city search failed. Check your internet connection and try again.");
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private void ResultsList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        AddCityButton.IsEnabled =
            ResultsList.SelectedItem is CitySearchResult;
    }

    private async void AddCityButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not CitySearchResult selectedCity)
        {
            return;
        }

        AddCityButton.IsEnabled = false;
        SearchButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        StatusText.Text = $"Adding {selectedCity.Name}…";

        try
        {
            var storedCity = await _cityService.AddAsync(selectedCity);

            await _watchlistService.AddAsync(
                _profile.Id,
                new AddWatchlistRequest(storedCity.Id));

            DialogResult = true;
        }
        catch (ConflictException)
        {
            // The city is already on this profile's watchlist.
            // It is safe to continue to the application.
            DialogResult = true;
        }
        catch (ClimaPulseException ex)
        {
            StatusText.Text = string.Empty;
            ShowError(ex.Message);
        }
        catch (Exception)
        {
            StatusText.Text = string.Empty;
            ShowError(
                "The city could not be added. Check the database connection and try again.");
        }
        finally
        {
            AddCityButton.IsEnabled = true;
            SearchButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}