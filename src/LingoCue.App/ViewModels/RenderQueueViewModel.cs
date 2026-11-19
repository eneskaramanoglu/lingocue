using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.Core.Platform;
using LingoCue.Media.Rendering;
using LingoCue.Media.Tools;
using Microsoft.Extensions.Logging;

namespace LingoCue.App.ViewModels;

public enum RenderJobState
{
    Waiting,
    Running,
    Done,
    Failed,
    Cancelled,
}

/// <summary>One export in the queue.</summary>
public sealed partial class RenderJobViewModel : ObservableObject
{
    private readonly IFileManager? _files;

    public RenderJobViewModel(RenderJob job, IFileManager? files)
    {
        Job = job;
        _files = files;
        Warning = job.Warnings.Count > 0 ? string.Join(" ", job.Warnings) : null;
    }

    public RenderJob Job { get; }

    public string Title => Job.Title;

    public string OutputPath => Job.Command.OutputPath;

    public string? Warning { get; }

    internal CancellationTokenSource Cancellation { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(IsDone), nameof(StateText))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand), nameof(RevealCommand))]
    public partial RenderJobState State { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    public partial string? Detail { get; set; }

    public bool IsActive => State is RenderJobState.Waiting or RenderJobState.Running;

    public bool IsDone => State == RenderJobState.Done;

    public string StateText => State switch
    {
        RenderJobState.Waiting => "Sırada",
        RenderJobState.Running => Detail ?? "Çalışıyor",
        RenderJobState.Done => "Bitti",
        RenderJobState.Cancelled => "İptal edildi",
        _ => "Hata: " + Detail,
    };

    [RelayCommand(CanExecute = nameof(IsActive))]
    private void Cancel() => Cancellation.Cancel();

    [RelayCommand(CanExecute = nameof(IsDone))]
    private void Reveal() => _files?.Reveal(OutputPath);

    internal void Report(RenderProgress p)
    {
        Progress = p.Fraction;
        var culture = CultureInfo.GetCultureInfo("tr-TR");
        var speed = p.Speed is { } s ? string.Create(culture, $" · {s:0.0}x") : string.Empty;
        var left = p.Remaining is { } r && p.Fraction < 1 ? $" · kalan {(r.TotalHours >= 1 ? r.ToString(@"h\:mm\:ss", culture) : r.ToString(@"m\:ss", culture))}" : string.Empty;
        Detail = string.Create(culture, $"%{p.Fraction * 100:0}{speed}{left}");
    }
}

/// <summary>
/// Runs exports one after another (FFmpeg already uses every core, so parallel jobs would only
/// slow each other down). Lives for the whole app run; the status bar shows it.
/// </summary>
public sealed partial class RenderQueueViewModel : ViewModelBase
{
    private readonly IVideoRenderer? _renderer;
    private readonly IFileManager? _files;
    private readonly ILogger<RenderQueueViewModel>? _logger;
    private bool _running;

    public RenderQueueViewModel(IVideoRenderer renderer, IFileManager files, ILogger<RenderQueueViewModel> logger)
    {
        _renderer = renderer;
        _files = files;
        _logger = logger;
    }

    // Designer constructor.
    private RenderQueueViewModel()
    {
    }

    public static RenderQueueViewModel CreateDesignInstance() => new();

    /// <summary>Raised when a job finishes, with a one-line Turkish summary for the status bar.</summary>
    public event EventHandler<string>? JobFinished;

    public ObservableCollection<RenderJobViewModel> Jobs { get; } = [];

    public bool HasJobs => Jobs.Count > 0;

    public int ActiveCount => Jobs.Count(j => j.IsActive);

    public bool IsBusy => ActiveCount > 0;

    public string Summary => ActiveCount > 0 ? $"İşler ({ActiveCount})" : "İşler";

    public void Enqueue(RenderJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var vm = new RenderJobViewModel(job, _files);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RenderJobViewModel.State))
            {
                OnCountsChanged();
            }
        };
        Jobs.Insert(0, vm);
        OnCountsChanged();
        _ = RunAsync();
    }

    /// <summary>Cancels everything still waiting or running (e.g. when the app closes).</summary>
    public void CancelAll()
    {
        foreach (var job in Jobs.Where(j => j.IsActive))
        {
            job.Cancellation.Cancel();
        }
    }

    [RelayCommand]
    private void ClearFinished()
    {
        foreach (var job in Jobs.Where(j => !j.IsActive).ToList())
        {
            Jobs.Remove(job);
        }

        OnCountsChanged();
    }

    private async Task RunAsync()
    {
        if (_running || _renderer is null)
        {
            return;
        }

        _running = true;
        try
        {
            while (Jobs.LastOrDefault(j => j.State == RenderJobState.Waiting) is { } job)
            {
                await RunJobAsync(_renderer, job);
            }
        }
        finally
        {
            _running = false;
        }
    }

    private async Task RunJobAsync(IVideoRenderer renderer, RenderJobViewModel job)
    {
        if (job.Cancellation.IsCancellationRequested)
        {
            job.State = RenderJobState.Cancelled;
            ExportJobs.Cleanup(job.Job);
            return;
        }

        job.State = RenderJobState.Running;
        var progress = new Progress<RenderProgress>(job.Report);
        try
        {
            await renderer.RenderAsync(job.Job.Command, job.Job.Duration, progress, job.Cancellation.Token);
            job.Progress = 1;
            job.State = RenderJobState.Done;
            JobFinished?.Invoke(this, $"Dışa aktarıldı: {job.OutputPath}");
        }
        catch (OperationCanceledException)
        {
            job.State = RenderJobState.Cancelled;
            JobFinished?.Invoke(this, $"Dışa aktarma iptal edildi: {Path.GetFileName(job.OutputPath)}");
        }
        catch (Exception ex) when (ex is RenderException or MediaToolNotFoundException or IOException or UnauthorizedAccessException)
        {
            if (_logger is not null)
            {
                LogFailed(_logger, ex, job.OutputPath);
            }

            job.Detail = ex is MediaToolNotFoundException ? "FFmpeg bulunamadı." : ex.Message;
            job.State = RenderJobState.Failed;
            JobFinished?.Invoke(this, $"Dışa aktarma başarısız: {Path.GetFileName(job.OutputPath)}");
        }
        finally
        {
            ExportJobs.Cleanup(job.Job);
        }
    }

    private void OnCountsChanged()
    {
        OnPropertyChanged(nameof(HasJobs));
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(Summary));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Export failed: {Output}")]
    private static partial void LogFailed(ILogger logger, Exception ex, string output);
}
