using System.ComponentModel;

namespace LingoCue.App.Playback;

/// <summary>
/// Playback abstraction the UI talks to. The mpv implementation lives behind it so the engine can
/// be swapped (see docs/adr/0001-video-player.md). All properties are raised on the UI thread.
/// </summary>
public interface IVideoPlayer : INotifyPropertyChanged
{
    bool IsReady { get; }

    bool IsLoaded { get; }

    bool IsPlaying { get; }

    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    Task OpenAsync(string filePath);

    void Play();

    void Pause();

    void TogglePlayPause();

    /// <param name="exact">Frame-accurate seek (slower); otherwise seeks to the nearest keyframe while scrubbing.</param>
    void Seek(TimeSpan position, bool exact = true);

    void SeekRelative(TimeSpan offset);

    void StepFrame(bool forward);

    /// <summary>Loads (or reloads) an external subtitle file, e.g. the generated preview ASS.</summary>
    void LoadSubtitle(string path);

    void Close();
}
