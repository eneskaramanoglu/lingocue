using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LingoCue.App.Views;

public partial class ApiKeysDialog : Window
{
    public ApiKeysDialog()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
