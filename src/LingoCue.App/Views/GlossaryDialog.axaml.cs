using Avalonia.Controls;
using Avalonia.Interactivity;
using LingoCue.App.ViewModels;

namespace LingoCue.App.Views;

public partial class GlossaryDialog : Window
{
    public GlossaryDialog()
    {
        InitializeComponent();
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Close((DataContext as GlossaryViewModel)?.Apply() ?? false);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
