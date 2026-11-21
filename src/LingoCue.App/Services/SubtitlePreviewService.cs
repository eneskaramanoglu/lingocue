using System.Text;
using Avalonia.Threading;
using LingoCue.App.Playback;
using LingoCue.Core.Formats;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using Microsoft.Extensions.Logging;

namespace LingoCue.App.Services;

/// <summary>
/// Keeps the player's subtitle in sync with the editor: renders the active track to a temporary
/// ASS file (the same script burn-in will use) and asks mpv to (re)load it. Edits are debounced.
/// </summary>
public sealed partial class SubtitlePreviewService : IDisposable
{
    private static readonly TimeSpan s_debounce = TimeSpan.FromMilliseconds(300);
    private readonly IVideoPlayer _player;
    private readonly ILogger<SubtitlePreviewService> _logger;
    private readonly DispatcherTimer _timer;
    private readonly string _path;
    private Func<string>? _pending;

    public SubtitlePreviewService(IVideoPlayer player, ILogger<SubtitlePreviewService> logger)
    {
        _player = player;
        _logger = logger;
        var directory = Path.Combine(Path.GetTempPath(), "LingoCue");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, $"preview-{Environment.ProcessId}.ass");
        _timer = new DispatcherTimer { Interval = s_debounce };
        _timer.Tick += (_, _) => Flush();
    }

    public string PreviewPath => _path;

    /// <summary>Schedules a refresh with the latest state; repeated calls within 300 ms collapse into one.</summary>
    /// <param name="measurer">Measures text for rounded block boxes (they are square without it).</param>
    public void Request(Project project, SubtitleTrack? track, int videoWidth, int videoHeight, Core.Fonts.ITextMeasurer? measurer = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var empty = new SubtitleTrack();
        _pending = () => AssScriptBuilder.Build(project, track ?? empty, videoWidth, videoHeight, measurer: measurer);
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Writes now (e.g. right after opening a video).</summary>
    public void Flush()
    {
        _timer.Stop();
        if (_pending is not { } build)
        {
            return;
        }

        _pending = null;
        try
        {
            File.WriteAllText(_path, build(), new UTF8Encoding(false));
            _player.LoadSubtitle(_path);
        }
        catch (IOException ex)
        {
            LogWriteFailed(ex, _path);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // mpv may still hold it on Windows; the temp folder is cleaned eventually.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write preview subtitle {Path}")]
    private partial void LogWriteFailed(Exception ex, string path);
}
