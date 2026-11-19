using Avalonia.Controls;
using Avalonia.Interactivity;
using LingoCue.App.ViewModels;

namespace LingoCue.App.Views;

public partial class CorrectDialog : Window
{
    public CorrectDialog()
    {
        InitializeComponent();
    }

    private void OnStart(object? sender, RoutedEventArgs e) => Close((DataContext as CorrectDialogViewModel)?.BuildResult());

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
