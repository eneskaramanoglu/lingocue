using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Settings;

/// <summary>
/// User preferences stored as JSON in the app-data folder. Never contains secrets (API keys live in
/// the OS secret store). Every property has a safe default so older/partial files load fine.
/// </summary>
public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;

    public TranscriptionSettings Transcription { get; init; } = new();

    public LlmSettings Llm { get; init; } = new();

    public ReadingRulesSettings Reading { get; init; } = new();

    /// <summary>System, Light or Dark.</summary>
    public string Theme { get; init; } = "System";

    /// <summary>Recently opened projects and videos, newest first.</summary>
    public IReadOnlyList<string> RecentFiles { get; init; } = [];

    public const int MaxRecentFiles = 10;

    /// <summary>Puts <paramref name="path"/> first (case-insensitive de-duplication), keeping at most <see cref="MaxRecentFiles"/>.</summary>
    public AppSettings WithRecentFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        return this with
        {
            RecentFiles = [full, .. RecentFiles.Where(p => !string.Equals(p, full, StringComparison.OrdinalIgnoreCase)).Take(MaxRecentFiles - 1)],
        };
    }

    public AppSettings WithoutRecentFile(string path)
        => this with { RecentFiles = [.. RecentFiles.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))] };
}

public sealed record TranscriptionSettings
{
    public string DefaultEngine { get; init; } = "whisper";

    public string WhisperModel { get; init; } = "large-v3-q5_0";

    /// <summary>BCP 47; null = auto-detect.</summary>
    public string? Language { get; init; } = "tr";
}

public sealed record LlmSettings
{
    public string Provider { get; init; } = "gemini";

    /// <summary>Model for everyday passes; null = newest stable Flash from the provider's list.</summary>
    public string? FastModel { get; init; }

    /// <summary>Model for quality mode; null = newest Pro from the provider's list.</summary>
    public string? QualityModel { get; init; }

    public bool UseQualityModel { get; init; }

    /// <summary>Lines per request (20–40 works well).</summary>
    public int BatchSize { get; init; } = 30;

    /// <summary>Requests in flight. Free tiers have low per-minute limits.</summary>
    public int Parallelism { get; init; } = 2;

    /// <summary>Second "review and improve" pass (roughly doubles cost).</summary>
    public bool Reflect { get; init; }

    /// <summary>Ask the model to shorten lines above the reading-speed limit, keeping the meaning.</summary>
    public bool ShortenOverlongLines { get; init; } = true;

    /// <summary>Keep circumflex spellings (rüzgâr, kâr) instead of the common modern forms.</summary>
    public bool KeepCircumflex { get; init; }

    /// <summary>Retries for a batch whose answer fails validation.</summary>
    public int MaxRetries { get; init; } = 2;
}

/// <summary>Serializable form of <see cref="ReadingRules"/> (seconds as numbers).</summary>
public sealed record ReadingRulesSettings
{
    public int MaxCharsPerLine { get; init; } = 42;

    public int MaxLines { get; init; } = 2;

    public double MinDurationSeconds { get; init; } = 0.8;

    public double MaxDurationSeconds { get; init; } = 7;

    public double TargetCps { get; init; } = 17;

    public double MaxCps { get; init; } = 21;

    public double PauseThresholdMs { get; init; } = 400;

    /// <summary>English is read faster than Turkish (Netflix: 20 CPS vs 17), so its limits are higher.</summary>
    public const double EnglishCpsBonus = 3;

    /// <summary>
    /// Korean (Netflix Korean style guide): 16 characters per line, at most 12 characters per second for
    /// adults, Latin letters, spaces and punctuation counting half. These replace the user's Turkish-oriented
    /// numbers because the scales differ.
    /// </summary>
    public static ReadingRules KoreanRules(ReadingRules baseRules)
    {
        ArgumentNullException.ThrowIfNull(baseRules);
        return baseRules with { MaxCharsPerLine = 16, TargetCps = 10, MaxCps = 12, Counting = LengthCounting.HalfWidthLatin };
    }

    /// <param name="language">Track language; English gets <see cref="EnglishCpsBonus"/> more characters per second,
    /// Korean uses <see cref="KoreanRules"/>.</param>
    public ReadingRules ToRules(string? language = null)
    {
        if (language is not null && language.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
        {
            return KoreanRules(ToRules(null));
        }

        var bonus = language is not null && language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? EnglishCpsBonus : 0;
        return new()
        {
            MaxCharsPerLine = Math.Clamp(MaxCharsPerLine, 10, 120),
            MaxLines = Math.Clamp(MaxLines, 1, 4),
            MinDuration = TimeSpan.FromSeconds(Math.Clamp(MinDurationSeconds, 0.2, 5)),
            MaxDuration = TimeSpan.FromSeconds(Math.Clamp(MaxDurationSeconds, 1, 30)),
            TargetCps = Math.Clamp(TargetCps + bonus, 5, 45),
            MaxCps = Math.Clamp(Math.Max(MaxCps, TargetCps) + bonus, 5, 55),
        };
    }
}

public interface ISettingsStore
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? Changed;

    void Save(AppSettings settings);
}
