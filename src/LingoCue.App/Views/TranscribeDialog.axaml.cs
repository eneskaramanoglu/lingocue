using Avalonia.Controls;
using Avalonia.Interactivity;
using LingoCue.App.ViewModels;

namespace LingoCue.App.Views;

public partial class TranscribeDialog : Window
{
    public TranscribeDialog()
    {
        InitializeComponent();
    }

    private void OnStart(object? sender, RoutedEventArgs e)
        => Close((DataContext as TranscribeDialogViewModel)?.BuildResult());

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
