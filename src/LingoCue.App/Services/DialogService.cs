using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using LingoCue.App.ViewModels;
using LingoCue.App.Views;

namespace LingoCue.App.Services;

public enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel,
}

public interface IDialogService
{
    Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string documentName);

    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText = "Vazgeç");

    Task<TranscribeOptions?> AskTranscribeOptionsAsync(TranscribeDialogViewModel viewModel);

    Task ShowApiKeysAsync(ApiKeysViewModel viewModel);
}

/// <summary>Small modal dialogs built in code (Avalonia has no built-in message box).</summary>
internal sealed class DialogService : IDialogService
{
    public async Task<TranscribeOptions?> AskTranscribeOptionsAsync(TranscribeDialogViewModel viewModel)
        => Owner() is { } owner
            ? await new TranscribeDialog { DataContext = viewModel }.ShowDialog<TranscribeOptions?>(owner)
            : null;

    public async Task ShowApiKeysAsync(ApiKeysViewModel viewModel)
    {
        if (Owner() is { } owner)
        {
            await new ApiKeysDialog { DataContext = viewModel }.ShowDialog(owner);
        }
    }

    private static Window? Owner()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } w } ? w : null;

    public Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string documentName)
        => ShowAsync(
            "Kaydedilmemiş değişiklikler",
            $"\"{documentName}\" üzerinde kaydedilmemiş değişiklikler var. Kaydetmek ister misiniz?",
            UnsavedChangesChoice.Cancel,
            ("Kaydet", UnsavedChangesChoice.Save, true),
            ("Kaydetme", UnsavedChangesChoice.Discard, false),
            ("Vazgeç", UnsavedChangesChoice.Cancel, false));

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText = "Vazgeç")
        => ShowAsync(title, message, false, (confirmText, true, true), (cancelText, false, false));

    /// <param name="dismissed">Result when the window is closed without pressing a button (Esc / close box).</param>
    private static async Task<T> ShowAsync<T>(string title, string message, T dismissed, params (string Text, T Value, bool IsDefault)[] buttons)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return dismissed;
        }

        var dialog = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        foreach (var (text, value, isDefault) in buttons)
        {
            var button = new Button { Content = text, IsDefault = isDefault, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (isDefault)
            {
                button.Classes.Add("accent");
            }

            button.Click += (_, _) => dialog.Close(value);
            buttonRow.Children.Add(button);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 20,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                buttonRow,
            },
        };

        return await dialog.ShowDialog<object?>(owner) is T result ? result : dismissed;
    }
}
