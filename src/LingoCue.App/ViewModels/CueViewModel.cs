using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LingoCue.Core.Formats;
using LingoCue.Core.Subtitles;
using LingoCue.Core.Time;

namespace LingoCue.App.ViewModels;

/// <summary>Row/editor view of one <see cref="Cue"/>. Writes go straight to the model and raise <see cref="Changed"/>.</summary>
public sealed partial class CueViewModel(Cue cue, ReadingRules rules) : ObservableObject
{
    private static readonly CultureInfo s_tr = CultureInfo.GetCultureInfo("tr-TR");

    public event EventHandler? Changed;

    /// <summary>Raised when the start time changed, so the list can re-sort.</summary>
    public event EventHandler? StartChanged;

    public Cue Model => cue;

    /// <summary>1-based row number in the current order (not the stable id).</summary>
    [ObservableProperty]
    public partial int Number { get; set; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    public TimeSpan Start => cue.Start;

    public TimeSpan End => cue.End;

    public string StartText => Timecode.Format(cue.Start);

    public string EndText => Timecode.Format(cue.End);

    public string DurationText => cue.Duration.TotalSeconds.ToString("0.00", s_tr);

    /// <summary>Editable start time (<c>hh:mm:ss.fff</c>). Invalid input or a start after the end is rejected.</summary>
    public string StartInput
    {
        get => StartText;
        set
        {
            if (SubtitleTime.TryParseSrtOrVtt(value, out var start) && start < cue.End && start != cue.Start)
            {
                cue.Start = start;
                OnTimesChanged();
                StartChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                OnPropertyChanged(); // revert the text box
            }
        }
    }

    /// <summary>Editable end time. Must be after the start.</summary>
    public string EndInput
    {
        get => EndText;
        set
        {
            if (SubtitleTime.TryParseSrtOrVtt(value, out var end) && end > cue.Start && end != cue.End)
            {
                cue.End = end;
                OnTimesChanged();
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    public string Text
    {
        get => cue.Text;
        set
        {
            var normalized = (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            if (normalized == cue.Text)
            {
                return;
            }

            cue.Text = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OneLineText));
            OnReadingChanged();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Text for the grid cell: formatting tags hidden, line breaks shown as a separator.</summary>
    public string OneLineText => SubtitleText.StripTags(cue.Text).Replace("\n", " ⏎ ", StringComparison.Ordinal);

    public string? Speaker
    {
        get => cue.Speaker;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (normalized == cue.Speaker)
            {
                return;
            }

            cue.Speaker = normalized;
            OnPropertyChanged();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public string CpsText => cue.CharactersPerSecond.ToString("0.0", s_tr);

    public CueIssues Issues => rules.Evaluate(cue);

    public bool HasError => (Issues & (CueIssues.ReadingSpeedTooHigh | CueIssues.TooManyLines | CueIssues.Empty)) != 0;

    public bool HasWarning => !HasError && Issues != CueIssues.None;

    public bool CpsTooHigh => Issues.HasFlag(CueIssues.ReadingSpeedTooHigh);

    public bool CpsHigh => Issues.HasFlag(CueIssues.ReadingSpeedHigh);

    /// <summary>Human-readable list of problems for the tooltip/editor.</summary>
    public string IssuesText
    {
        get
        {
            var issues = Issues;
            var parts = new List<string>();
            if (issues.HasFlag(CueIssues.Empty))
            {
                parts.Add("Boş satır");
            }

            if (issues.HasFlag(CueIssues.ReadingSpeedTooHigh))
            {
                parts.Add($"Okuma hızı çok yüksek ({CpsText} > {rules.MaxCps} CPS)");
            }
            else if (issues.HasFlag(CueIssues.ReadingSpeedHigh))
            {
                parts.Add($"Okuma hızı yüksek ({CpsText} > {rules.TargetCps} CPS)");
            }

            if (issues.HasFlag(CueIssues.LineTooLong))
            {
                parts.Add($"Satır {rules.MaxCharsPerLine} karakterden uzun");
            }

            if (issues.HasFlag(CueIssues.TooManyLines))
            {
                parts.Add($"{rules.MaxLines} satırdan fazla");
            }

            if (issues.HasFlag(CueIssues.TooShort))
            {
                parts.Add($"{rules.MinDuration.TotalSeconds:0.0} sn'den kısa");
            }

            if (issues.HasFlag(CueIssues.TooLong))
            {
                parts.Add($"{rules.MaxDuration.TotalSeconds:0} sn'den uzun");
            }

            return string.Join(" · ", parts);
        }
    }

    public void SetStart(TimeSpan start) => StartInput = Timecode.Format(start);

    public void SetEnd(TimeSpan end) => EndInput = Timecode.Format(end);

    private void OnTimesChanged()
    {
        OnPropertyChanged(nameof(Start));
        OnPropertyChanged(nameof(End));
        OnPropertyChanged(nameof(StartText));
        OnPropertyChanged(nameof(EndText));
        OnPropertyChanged(nameof(StartInput));
        OnPropertyChanged(nameof(EndInput));
        OnPropertyChanged(nameof(DurationText));
        OnReadingChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnReadingChanged()
    {
        OnPropertyChanged(nameof(CpsText));
        OnPropertyChanged(nameof(Issues));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasWarning));
        OnPropertyChanged(nameof(CpsTooHigh));
        OnPropertyChanged(nameof(CpsHigh));
        OnPropertyChanged(nameof(IssuesText));
    }
}
