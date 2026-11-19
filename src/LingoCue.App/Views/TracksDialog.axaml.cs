using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LingoCue.App.Views;

public partial class TracksDialog : Window
{
    public TracksDialog()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
