using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Tests.Projects;

public sealed class ProjectSerializerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lingocue-project-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private Project CreateProject()
    {
        var project = new Project { VideoPath = Path.Combine(_dir, "videos", "röportaj.mp4") };
        var track = project.EnsureActiveTrack();
        var cue = track.AddCue(TimeSpan.FromMilliseconds(1_234), TimeSpan.FromMilliseconds(3_456), "Merhaba, İstanbul!\nığüşöç");
        cue.Speaker = "Ayşe";
        cue.Marks = CueMarks.NeedsReview | CueMarks.Locked;
        cue.Words.Add(new Word("Merhaba", TimeSpan.FromMilliseconds(1_234), TimeSpan.FromMilliseconds(1_700), 0.93));
        cue.Format = new CueFormatData { Style = "Kutu", Layer = 1 };
        project.Tracks.Add(new SubtitleTrack { Language = "en", Kind = TrackKind.Translation, SourceTrackId = track.Id, Name = "English" });
        project.Style = project.Style with { FontName = "Inter", PrimaryColor = new AssColor(255, 255, 0, 200), Bold = true };
        return project;
    }

    [Fact]
    public async Task Round_trips_everything()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(_dir, "proje.lingocue");
        var original = CreateProject();

        await ProjectSerializer.SaveAsync(original, path, ct);
        var loaded = await ProjectSerializer.LoadAsync(path, ct);

        loaded.VideoPath.ShouldBe(original.VideoPath);
        loaded.ActiveTrackId.ShouldBe(original.ActiveTrackId);
        loaded.Style.ShouldBe(original.Style);
        loaded.Tracks.Count.ShouldBe(2);
        loaded.Tracks[1].Kind.ShouldBe(TrackKind.Translation);
        loaded.Tracks[1].SourceTrackId.ShouldBe(original.Tracks[0].Id);

        var cue = loaded.Tracks[0].Cues.ShouldHaveSingleItem();
        cue.Start.ShouldBe(TimeSpan.FromMilliseconds(1_234));
        cue.Text.ShouldBe("Merhaba, İstanbul!\nığüşöç");
        cue.Speaker.ShouldBe("Ayşe");
        cue.Marks.ShouldBe(CueMarks.NeedsReview | CueMarks.Locked);
        cue.Format.ShouldBe(original.Tracks[0].Cues[0].Format);
        cue.Words.ShouldHaveSingleItem().Confidence.ShouldBe(0.93);
    }

    [Fact]
    public void Stores_readable_json_with_relative_forward_slash_video_path()
    {
        var json = ProjectSerializer.Serialize(CreateProject(), Path.Combine(_dir, "proje.lingocue"));

        json.ShouldContain("\"videoPath\": \"videos/röportaj.mp4\"");
        json.ShouldContain("\"start\": 1234");
        json.ShouldContain("ığüşöç");
        json.ShouldContain("\"primaryColor\": \"#FFFF00C8\"");
        json.ShouldContain("\"kind\": \"translation\"");
        json.ShouldNotContain("charactersPerSecond");
        json.ShouldNotContain("duration");
    }

    [Fact]
    public void Serializing_does_not_change_the_in_memory_path()
    {
        var project = CreateProject();
        var before = project.VideoPath;

        ProjectSerializer.Serialize(project, Path.Combine(_dir, "proje.lingocue"));

        project.VideoPath.ShouldBe(before);
    }

    [Fact]
    public void Resolves_relative_path_from_another_folder()
    {
        var resolved = ProjectSerializer.FromStoredPath("../media/a.mp4", Path.Combine(_dir, "proj", "x.lingocue"));

        resolved.ShouldBe(Path.GetFullPath(Path.Combine(_dir, "media", "a.mp4")));
    }

    [Fact]
    public void Rejects_newer_schema_and_garbage()
    {
        Should.Throw<ProjectFormatException>(() => ProjectSerializer.Deserialize("{\"schemaVersion\": 99}"))
            .Message.ShouldContain("newer");
        Should.Throw<ProjectFormatException>(() => ProjectSerializer.Deserialize("not json"));
    }

    [Fact]
    public void Missing_fields_get_defaults()
    {
        var project = ProjectSerializer.Deserialize("{\"schemaVersion\": 1}");

        project.Tracks.ShouldBeEmpty();
        project.Style.FontName.ShouldBe("Arial");
        project.EnsureActiveTrack().Language.ShouldBe("tr");
    }
}
