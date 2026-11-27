using System.Windows;
using System.Windows.Controls;

namespace ClimaPulse.Desktop.Controls;

public partial class StateMessage : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(StateMessage),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message),
            typeof(string),
            typeof(StateMessage),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public StateMessage()
    {
        InitializeComponent();
        ApplyText();
    }

    private static void OnTextChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        ((StateMessage)dependencyObject).ApplyText();
    }

    private void ApplyText()
    {
        TitleText.Text = Title;
        MessageText.Text = Message;
    }
}