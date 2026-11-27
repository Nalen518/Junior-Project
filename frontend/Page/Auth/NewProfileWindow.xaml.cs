using System.Windows;
using ClimaPulse.Contracts;

namespace ClimaPulse.Desktop.Page.Auth;

public partial class NewProfileWindow : Window
{
    private readonly IProfileService _profileService;

    public ProfileDto? CreatedProfile { get; private set; }

    public NewProfileWindow(IProfileService profileService)
    {
        _profileService = profileService;

        InitializeComponent();

        Loaded += (_, _) => NameTextBox.Focus();
    }

    private void NameTextBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private async void CreateButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();

        if (name.Length is < 1 or > 50)
        {
            ShowError($"Enter a name between 1 and 50 characters. This one has {name.Length}.");
            return;
        }

        CreateButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;

        try
        {
            CreatedProfile = await _profileService.CreateAsync(
                new CreateProfileRequest(name));

            DialogResult = true;
        }
        catch (ValidationException ex)
        {
            var message = ex.Errors.Values
                .SelectMany(errors => errors)
                .FirstOrDefault()
                ?? "The profile could not be created.";

            ShowError(message);
        }
        catch (ClimaPulseException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception)
        {
            ShowError(
                "The profile could not be created. Check the database connection and try again.");
        }
        finally
        {
            CreateButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}