using System.Windows;
using System.Windows.Input;

namespace Emke.AiMarker.App.Views;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog(
        string dialogTitle,
        string message,
        string affirmativeText,
        string cancelText)
    {
        DialogTitle = dialogTitle;
        Message = message;
        AffirmativeText = affirmativeText;
        CancelText = cancelText;
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => Keyboard.Focus(CancelButton);
    }

    public string DialogTitle { get; }

    public string Message { get; }

    public string AffirmativeText { get; }

    public string CancelText { get; }

    private void AffirmativeButton_OnClick(object sender, RoutedEventArgs e) =>
        DialogResult = true;

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
