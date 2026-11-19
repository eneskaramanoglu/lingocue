using Avalonia.Controls;
using Avalonia.Interactivity;
using LingoCue.App.ViewModels;

namespace LingoCue.App.Views;

public partial class ExportDialog : Window
{
    public ExportDialog()
    {
        InitializeComponent();
    }

    private void OnStart(object? sender, RoutedEventArgs e) => Close((DataContext as ExportDialogViewModel)?.BuildResult());

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
