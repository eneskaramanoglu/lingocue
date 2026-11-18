using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using LingoCue.App.ViewModels;

namespace LingoCue.App.Views;

public partial class MainWindow : Window
{
    // Used by the XAML previewer.
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Tunnel so playback shortcuts work regardless of which control has focus
        // (text inputs keep their own arrow/space handling).
        AddHandler(KeyDownEvent, OnPreviewKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    public MainWindow(MainViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private static void OnDragOver(object? sender, DragEventArgs e)
        => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null && ViewModel is { } vm)
        {
            e.Handled = true;
            await vm.OpenFileAsync(path);
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel?.Player is not { IsLoaded: true } player || e.Source is TextBox)
        {
            return;
        }

        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var command = e.Key switch
        {
            Key.Space => player.TogglePlayPauseCommand,
            Key.Left when shift => player.FrameBackCommand,
            Key.Right when shift => player.FrameForwardCommand,
            Key.Left => player.JumpBackCommand,
            Key.Right => player.JumpForwardCommand,
            _ => null,
        };

        if (command is not null && command.CanExecute(null))
        {
            command.Execute(null);
            e.Handled = true;
        }
    }
}
