using Avalonia.Controls;
using Avalonia.Interactivity;
using LingoCue.App.ViewModels;

namespace LingoCue.App.Views;

public partial class TranslateDialog : Window
{
    public TranslateDialog()
    {
        InitializeComponent();
    }

    private void OnStart(object? sender, RoutedEventArgs e) => Close((DataContext as TranslateDialogViewModel)?.BuildResult());

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
