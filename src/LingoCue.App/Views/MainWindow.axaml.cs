using Avalonia.Controls;
using LingoCue.App.ViewModels;

namespace LingoCue.App.Views;

public partial class MainWindow : Window
{
    // Used by the XAML previewer.
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}
