using System.Windows;
using System.Windows.Controls;

namespace ClimaPulse.Desktop.Controls;

public partial class CityCard : UserControl
{
    public event EventHandler? CityClicked;

    public static readonly DependencyProperty CityNameProperty = RegisterTextProperty(
        nameof(CityName)
    );

    public static readonly DependencyProperty LocationProperty = RegisterTextProperty(
        nameof(Location)
    );

    public static readonly DependencyProperty TierProperty = RegisterTextProperty(
        nameof(Tier),
        "Normal"
    );

    public static readonly DependencyProperty PrimaryTextProperty = RegisterTextProperty(
        nameof(PrimaryText)
    );

    public static readonly DependencyProperty SecondaryTextProperty = RegisterTextProperty(
        nameof(SecondaryText)
    );

    public static readonly DependencyProperty FooterTextProperty = RegisterTextProperty(
        nameof(FooterText)
    );

    public static readonly DependencyProperty EntryProperty = DependencyProperty.Register(
        nameof(Entry),
        typeof(object),
        typeof(CityCard),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty ZScoreProperty = DependencyProperty.Register(
        nameof(ZScore),
        typeof(double?),
        typeof(CityCard),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty ActionTextProperty = RegisterTextProperty(
        nameof(ActionText)
    );

    /// <summary>Raised by the card's action button (for example Build baseline).</summary>
    public event EventHandler? ActionClicked;

    public double? ZScore
    {
        get => (double?)GetValue(ZScoreProperty);
        set => SetValue(ZScoreProperty, value);
    }

    /// <summary>Empty hides the action button.</summary>
    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public string CityName
    {
        get => (string)GetValue(CityNameProperty);
        set => SetValue(CityNameProperty, value);
    }

    public string Location
    {
        get => (string)GetValue(LocationProperty);
        set => SetValue(LocationProperty, value);
    }

    public string Tier
    {
        get => (string)GetValue(TierProperty);
        set => SetValue(TierProperty, value);
    }

    public string PrimaryText
    {
        get => (string)GetValue(PrimaryTextProperty);
        set => SetValue(PrimaryTextProperty, value);
    }

    public string SecondaryText
    {
        get => (string)GetValue(SecondaryTextProperty);
        set => SetValue(SecondaryTextProperty, value);
    }

    public string FooterText
    {
        get => (string)GetValue(FooterTextProperty);
        set => SetValue(FooterTextProperty, value);
    }

    public object? Entry
    {
        get => GetValue(EntryProperty);
        set => SetValue(EntryProperty, value);
    }

    public CityCard()
    {
        InitializeComponent();
    }

    private void CityCardButton_Click(object sender, RoutedEventArgs e)
    {
        CityClicked?.Invoke(this, EventArgs.Empty);
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        // Stop the click bubbling to the card button, which would open city detail.
        e.Handled = true;
        ActionClicked?.Invoke(this, EventArgs.Empty);
    }

    private static DependencyProperty RegisterTextProperty(string name, string defaultValue = "")
    {
        return DependencyProperty.Register(
            name,
            typeof(string),
            typeof(CityCard),
            new PropertyMetadata(defaultValue)
        );
    }
}
