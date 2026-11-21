using Avalonia.Controls;
using Avalonia.Interactivity;
using LingoCue.Core;
using LingoCue.Core.Updates;

namespace LingoCue.App.Views;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        VersionText.Text = $"Sürüm {AppInfo.Version}";
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private async void OnOpenRepository(object? sender, RoutedEventArgs e)
        => await Launcher.LaunchUriAsync(ReleaseVersions.RepositoryUrl);
}
