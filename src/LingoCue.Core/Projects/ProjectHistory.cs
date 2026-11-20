namespace LingoCue.Core.Projects;

/// <summary>
/// Undo/redo by snapshots: after every edit the whole project is captured as JSON (it is small, and
/// one mechanism then covers cue edits, timing, tracks, style and LLM passes alike). The speech
/// recognition cache (<see cref="Project.AsrRuns"/>) is left out — it is the largest part, only ever
/// grows and is kept as it is when a snapshot is restored.
/// </summary>
public sealed class ProjectHistory(int capacity = 100, Func<DateTime>? clock = null)
{
    /// <summary>Edits with the same key this close together become one undo step (e.g. typing).</summary>
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(1.5);

    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private string? _current;
    private string? _saved;
    private string? _lastKey;
    private DateTime _lastTime;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    /// <summary>True when the project equals the last saved (or opened) state.</summary>
    public bool IsAtSavedState => _current == _saved;

    /// <summary>Starts a new history for a freshly opened or created project.</summary>
    public void Reset(Project project)
    {
        _undo.Clear();
        _redo.Clear();
        _current = Snapshot(project);
        _saved = _current;
        _lastKey = null;
    }

    public void MarkSaved(Project project)
    {
        _current = Snapshot(project);
        _saved = _current;
        _lastKey = null; // the next edit starts a new step
    }

    /// <summary>
    /// Records the project after an edit. Returns false when nothing changed. A step with the same
    /// <paramref name="coalesceKey"/> as the previous one within <see cref="CoalesceWindow"/> replaces it.
    /// </summary>
    public bool Record(Project project, string? coalesceKey = null)
    {
        var snapshot = Snapshot(project);
        if (snapshot == _current)
        {
            return false;
        }

        var now = _clock();
        var coalesce = coalesceKey is not null && coalesceKey == _lastKey && now - _lastTime <= CoalesceWindow && _undo.Count > 0;
        if (!coalesce && _current is not null)
        {
            _undo.Add(_current);
            if (_undo.Count > capacity)
            {
                _undo.RemoveAt(0);
            }
        }

        _redo.Clear();
        _current = snapshot;
        _lastKey = coalesceKey;
        _lastTime = now;
        return true;
    }

    /// <summary>The previous state, built on <paramref name="current"/>'s recognition cache; null when there is none.</summary>
    public Project? Undo(Project current) => Step(_undo, _redo, current);

    public Project? Redo(Project current) => Step(_redo, _undo, current);

    private Project? Step(List<string> from, List<string> to, Project current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (from.Count == 0 || _current is null)
        {
            return null;
        }

        to.Add(_current);
        _current = from[^1];
        from.RemoveAt(from.Count - 1);
        _lastKey = null;

        var restored = ProjectSerializer.Deserialize(_current);
        restored.AsrRuns.AddRange(current.AsrRuns);
        return restored;
    }

    private static string Snapshot(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var runs = project.AsrRuns.ToList();
        project.AsrRuns.Clear();
        try
        {
            return ProjectSerializer.Serialize(project);
        }
        finally
        {
            project.AsrRuns.AddRange(runs);
        }
    }
}
