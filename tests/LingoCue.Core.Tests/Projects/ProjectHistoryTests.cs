using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using Shouldly;

namespace LingoCue.Core.Tests.Projects;

public class ProjectHistoryTests
{
    private DateTime _now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

    private ProjectHistory History(int capacity = 100) => new(capacity, () => _now);

    private static Project Sample()
    {
        var project = new Project();
        project.EnsureActiveTrack().AddCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "bir");
        project.AsrRuns.Add(new AsrRun { Engine = "whisper", Model = "m", Words = [new Word("bir", TimeSpan.Zero, TimeSpan.FromSeconds(1))] });
        return project;
    }

    [Fact]
    public void Undo_and_redo_walk_through_recorded_states()
    {
        var history = History();
        var project = Sample();
        history.Reset(project);

        project.ActiveTrack!.Cues[0].Text = "iki";
        history.Record(project).ShouldBeTrue();
        project.ActiveTrack!.Cues[0].Text = "üç";
        history.Record(project);

        var undone = history.Undo(project)!;
        undone.ActiveTrack!.Cues[0].Text.ShouldBe("iki");
        var undone2 = history.Undo(undone)!;
        undone2.ActiveTrack!.Cues[0].Text.ShouldBe("bir");
        history.CanUndo.ShouldBeFalse();
        history.Undo(undone2).ShouldBeNull();

        history.Redo(undone2)!.ActiveTrack!.Cues[0].Text.ShouldBe("iki");
        history.CanRedo.ShouldBeTrue();
    }

    [Fact]
    public void A_new_edit_after_undo_clears_redo()
    {
        var history = History();
        var project = Sample();
        history.Reset(project);
        project.ActiveTrack!.Cues[0].Text = "iki";
        history.Record(project);

        var undone = history.Undo(project)!;
        undone.ActiveTrack!.Cues[0].Text = "dört";
        history.Record(undone);

        history.CanRedo.ShouldBeFalse();
    }

    [Fact]
    public void Same_key_edits_close_together_are_one_step()
    {
        var history = History();
        var project = Sample();
        history.Reset(project);

        foreach (var text in new[] { "b", "bi", "bir şey" })
        {
            project.ActiveTrack!.Cues[0].Text = text;
            history.Record(project, "text:1");
            _now += TimeSpan.FromMilliseconds(300);
        }

        history.UndoCount.ShouldBe(1);
        history.Undo(project)!.ActiveTrack!.Cues[0].Text.ShouldBe("bir");

        // After a pause the same key starts a new step.
        var restored = Sample();
        history.Reset(restored);
        restored.ActiveTrack!.Cues[0].Text = "x";
        history.Record(restored, "text:1");
        _now += TimeSpan.FromSeconds(5);
        restored.ActiveTrack!.Cues[0].Text = "xy";
        history.Record(restored, "text:1");
        history.UndoCount.ShouldBe(2);
    }

    [Fact]
    public void Unchanged_projects_record_nothing_and_saved_state_is_tracked()
    {
        var history = History();
        var project = Sample();
        history.Reset(project);

        history.Record(project).ShouldBeFalse();
        history.IsAtSavedState.ShouldBeTrue();

        project.Style = project.Style with { FontSize = 70 };
        history.Record(project);
        history.IsAtSavedState.ShouldBeFalse();

        var undone = history.Undo(project)!;
        history.IsAtSavedState.ShouldBeTrue(); // back to what is on disk
        undone.Style.FontSize.ShouldBe(new SubtitleStyle().FontSize);

        history.MarkSaved(undone);
        history.IsAtSavedState.ShouldBeTrue();
    }

    [Fact]
    public void Recognition_cache_is_kept_but_not_snapshotted()
    {
        var history = History();
        var project = Sample();
        history.Reset(project);
        project.ActiveTrack!.Cues[0].Text = "iki";
        history.Record(project);
        project.AsrRuns.Add(new AsrRun { Engine = "gemini", Model = "t" });

        var undone = history.Undo(project)!;

        undone.AsrRuns.Select(r => r.Engine).ShouldBe(["whisper", "gemini"]); // the current cache
        project.AsrRuns.Count.ShouldBe(2); // the live project was not changed by snapshotting
    }

    [Fact]
    public void Capacity_drops_the_oldest_steps()
    {
        var history = History(capacity: 3);
        var project = Sample();
        history.Reset(project);
        for (var i = 0; i < 5; i++)
        {
            project.ActiveTrack!.Cues[0].Text = $"t{i}";
            history.Record(project);
        }

        history.UndoCount.ShouldBe(3);
    }
}
