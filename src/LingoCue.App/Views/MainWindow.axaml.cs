using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LingoCue.App.ViewModels;

namespace LingoCue.App.Views;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    // Used by the XAML previewer.
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Tunnel so playback shortcuts work regardless of which control has focus
        // (text inputs keep their own arrow/space handling).
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        CueGrid.SelectionChanged += (_, _) =>
        {
            if (CueGrid.SelectedItem is { } item)
            {
                CueGrid.ScrollIntoView(item, null);
            }
        };
    }

    public MainWindow(MainViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        if (viewModel.Editor is { } editor)
        {
            // Ctrl+B splits at the caret when the text box has focus.
            editor.TextCaret = () => CueTextBox.IsFocused ? CueTextBox.CaretIndex : null;

            // Opening the find bar puts the caret in the search box with its text selected.
            editor.FindOpened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                FindBox.Focus();
                FindBox.SelectAll();
            });
        }
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || ViewModel is not { } vm || (!vm.IsDirty && vm.RenderQueue is not { IsBusy: true }))
        {
            return;
        }

        // Ask asynchronously, then close again if the user did not cancel.
        e.Cancel = true;
        if (await vm.ConfirmCloseAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

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
        // The find box: Enter = next match, Shift+Enter = previous, Esc = close.
        if (ReferenceEquals(e.Source, FindBox) && ViewModel?.Editor is { } editor)
        {
            if (e.Key == Key.Enter)
            {
                (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? editor.FindPreviousCommand : editor.FindNextCommand).Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                editor.CloseFindCommand.Execute(null);
                CueGrid.Focus();
                e.Handled = true;
                return;
            }
        }

        // Line shortcuts with Ctrl/⌘ work everywhere, also while typing (Ctrl+B splits at the caret).
        var command = ViewModel?.Editor is { } lines && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            && !e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            ? e.Key switch
            {
                Key.Enter => lines.AddCueCommand,
                Key.B => lines.SplitCueCommand,
                Key.M => lines.MergeWithNextCommand,
                _ => null,
            }
            : null;
        if (Run(command, e))
        {
            return;
        }

        // Enter in a single-line box (start/end time) commits it by moving focus to the list.
        if (e.Key == Key.Enter && e.Source is TextBox { AcceptsReturn: false })
        {
            CueGrid.Focus();
            e.Handled = true;
            return;
        }

        // Everything below is inactive while typing in a text box.
        if (e.Source is TextBox)
        {
            return;
        }

        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (ViewModel?.Editor is { } editor2)
        {
            // [ and ] by the typed symbol: on a Turkish keyboard they are AltGr+8 / AltGr+9.
            command = e.KeySymbol switch
            {
                "[" => editor2.SetStartToPlayheadCommand,
                "]" => editor2.SetEndToPlayheadCommand,
                _ => e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control) ? e.Key switch
                {
                    Key.Left when shift => editor2.NudgeStartBackCommand,
                    Key.Right when shift => editor2.NudgeStartForwardCommand,
                    Key.Left => editor2.NudgeBackCommand,
                    Key.Right => editor2.NudgeForwardCommand,
                    _ => null,
                } : null,
            };
            if (Run(command, e))
            {
                return;
            }
        }

        if (ViewModel?.Player is not { IsLoaded: true } player || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return;
        }

        command = e.Key switch
        {
            Key.Space => player.TogglePlayPauseCommand,
            Key.Left when shift => player.FrameBackCommand,
            Key.Right when shift => player.FrameForwardCommand,
            Key.Left => player.JumpBackCommand,
            Key.Right => player.JumpForwardCommand,
            _ => null,
        };

        Run(command, e);
    }

    /// <summary>Runs the command for a shortcut; the key counts as handled whenever a command matched.</summary>
    private static bool Run(System.Windows.Input.ICommand? command, KeyEventArgs e)
    {
        if (command is null)
        {
            return false;
        }

        if (command.CanExecute(null))
        {
            command.Execute(null);
        }

        e.Handled = true;
        return true;
    }

    private async void OnShowShortcuts(object? sender, RoutedEventArgs e) => await new ShortcutsDialog().ShowDialog(this);
}
