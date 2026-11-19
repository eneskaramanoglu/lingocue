using LingoCue.Core.Formats;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using Shouldly;

namespace LingoCue.Core.Tests.Subtitles;

public class KaraokeTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static Cue CueWithWords(string text, params (string Text, double Start, double End)[] words)
    {
        var cue = new Cue(1, S(10), S(12), text);
        cue.Words.AddRange(words.Select(w => new Word(w.Text, S(w.Start), S(w.End))));
        return cue;
    }

    [Fact]
    public void Exact_words_take_their_asr_start()
    {
        var cue = CueWithWords("bugün hava güzel", ("bugün", 10.0, 10.4), ("hava", 10.5, 10.9), ("güzel", 11.0, 11.6));

        var timed = KaraokeTiming.Align(cue)!;

        timed.Select(t => t.Start).ShouldBe([S(10.0), S(10.5), S(11.0)]);
        timed.Select(t => t.Text).ShouldBe(["bugün", "hava", "güzel"]);
    }

    [Fact]
    public void Corrected_spelling_and_punctuation_still_match()
    {
        // ASR: "istanbula gittik ankaradan" → corrected "İstanbul'a gittik, Ankara'dan."
        var cue = CueWithWords("İstanbul'a gittik,\nAnkara'dan.", ("istanbula", 10.1, 10.6), ("gittik", 10.7, 11.0), ("ankaradan", 11.2, 11.8));

        var timed = KaraokeTiming.Align(cue)!;

        timed.Select(t => t.Start).ShouldBe([S(10.1), S(10.7), S(11.2)]);
        timed[2].Separator.ShouldBe("\n");
    }

    [Fact]
    public void Added_words_share_time_with_their_neighbours()
    {
        // "çok" was added by the correction pass: it gets a start between "hava" and "güzel".
        var cue = CueWithWords("hava çok güzel", ("hava", 10.0, 10.5), ("güzel", 11.0, 11.5));

        var timed = KaraokeTiming.Align(cue)!;

        timed[0].Start.ShouldBe(S(10.0));
        timed[1].Start.ShouldBeGreaterThan(S(10.0));
        timed[1].Start.ShouldBeLessThan(S(11.0));
        timed[2].Start.ShouldBe(S(11.0));
    }

    [Fact]
    public void Merged_words_use_the_first_match()
    {
        var cue = CueWithWords("Ankara'dan geldik", ("ankara", 10.0, 10.3), ("dan", 10.3, 10.5), ("geldik", 10.6, 11.0));

        var timed = KaraokeTiming.Align(cue)!;

        timed.Select(t => t.Start).ShouldBe([S(10.0), S(10.6)]);
    }

    [Fact]
    public void Starts_stay_inside_the_cue_and_in_order()
    {
        var cue = CueWithWords("bir iki üç", ("bir", 9.0, 9.5), ("üç", 10.5, 10.8), ("iki", 13.0, 13.2));

        var timed = KaraokeTiming.Align(cue)!;

        timed.ShouldAllBe(t => t.Start >= cue.Start && t.Start <= cue.End);
        timed.Select(t => t.Start).ShouldBeInOrder();
    }

    [Fact]
    public void No_words_or_no_match_means_no_karaoke()
    {
        KaraokeTiming.Align(new Cue(1, S(0), S(1), "hello world")).ShouldBeNull();
        KaraokeTiming.Align(CueWithWords("hello world", ("merhaba", 10, 10.5), ("dünya", 10.5, 11))).ShouldBeNull();
    }

    [Fact]
    public void Fill_mode_writes_kf_durations_that_add_up_to_the_cue()
    {
        var project = new Project
        {
            Presentation = new PresentationOptions { Karaoke = new KaraokeOptions { Mode = KaraokeMode.Fill, HighlightColor = new AssColor(255, 255, 0) } },
        };
        var track = project.EnsureActiveTrack();
        track.Cues.Add(CueWithWords("bugün hava", ("bugün", 10.2, 10.6), ("hava", 10.7, 11.1)));

        var ass = AssScriptBuilder.Build(project, track);

        // 20 cs lead-in, "bugün" until 10.7 (50 cs), "hava" until the cue end 12.0 (130 cs) = 200 cs.
        ass.ShouldContain(@"Dialogue: 0,0:00:10.00,0:00:12.00,Default,,0,0,0,,{\1c&H00FFFF&\2c&HFFFFFF&}{\k20}{\kf50}bugün {\kf130}hava");
    }

    [Fact]
    public void Active_word_writes_one_event_per_word_with_fades_at_the_ends()
    {
        var project = new Project
        {
            Presentation = new PresentationOptions
            {
                UpperCase = true,
                FadeInMs = 100,
                FadeOutMs = 200,
                Karaoke = new KaraokeOptions { Mode = KaraokeMode.ActiveWord, HighlightColor = new AssColor(255, 255, 0) },
            },
        };
        var track = project.EnsureActiveTrack();
        track.Cues.Add(CueWithWords("bugün hava", ("bugün", 10.2, 10.6), ("hava", 10.7, 11.1)));

        var ass = AssScriptBuilder.Build(project, track);

        ass.ShouldContain(@"Dialogue: 0,0:00:10.00,0:00:10.20,Default,,0,0,0,,{\fad(100,0)}BUGÜN HAVA");
        ass.ShouldContain(@"Dialogue: 0,0:00:10.20,0:00:10.70,Default,,0,0,0,,{\1c&H00FFFF&}BUGÜN{\1c&HFFFFFF&} HAVA");
        ass.ShouldContain(@"Dialogue: 0,0:00:10.70,0:00:12.00,Default,,0,0,0,,{\fad(0,200)}BUGÜN {\1c&H00FFFF&}HAVA{\1c&HFFFFFF&}");
    }

    [Fact]
    public void Cues_without_words_stay_plain_in_karaoke_mode()
    {
        var project = new Project { Presentation = new PresentationOptions { Karaoke = new KaraokeOptions { Mode = KaraokeMode.ActiveWord } } };
        var track = project.EnsureActiveTrack();
        track.AddCue(S(1), S(2), "çeviri satırı");

        AssScriptBuilder.Build(project, track).ShouldContain(",,çeviri satırı");
    }
}
