using System.Windows;

namespace Emke.AiMarker.App.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
    }

    private void DoneButton_OnClick(object sender, RoutedEventArgs e) => Close();
}
