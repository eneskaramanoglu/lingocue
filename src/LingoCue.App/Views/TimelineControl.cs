using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using LingoCue.App.ViewModels;
using LingoCue.Core.Subtitles;

namespace LingoCue.App.Views;

/// <summary>
/// Waveform + cue blocks + playhead, drawn in code (one <see cref="Render"/> pass, only the visible
/// part). Click: seek or select; drag a block's edge or body to retime it (clamped so it never
/// overlaps a neighbour); Ctrl/⌘+wheel zooms around the pointer, the wheel scrolls.
/// </summary>
/// <remarks>
/// A custom control instead of an ItemsControl of rectangles: a long video has thousands of cues and
/// the waveform has a value per 10 ms, so drawing directly is far cheaper than a visual per item.
/// </remarks>
public sealed class TimelineControl : Control, ICustomHitTest
{
    private const double RulerHeight = 18;
    private const double EdgeGrip = 6;
    private const double MinPixelsPerSecond = 2;
    private const double MaxPixelsPerSecond = 1000;
    private static readonly TimeSpan s_dragSeekInterval = TimeSpan.FromMilliseconds(200);

    private static readonly IBrush s_waveBrush = new SolidColorBrush(Color.FromArgb(150, 96, 165, 250));
    private static readonly IBrush s_cueBrush = new SolidColorBrush(Color.FromArgb(70, 59, 130, 246));
    private static readonly IBrush s_cueSelectedBrush = new SolidColorBrush(Color.FromArgb(120, 245, 158, 11));
    private static readonly IBrush s_cueErrorBrush = new SolidColorBrush(Color.FromArgb(90, 239, 68, 68));
    private static readonly IPen s_cuePen = new Pen(new SolidColorBrush(Color.FromArgb(200, 59, 130, 246)), 1);
    private static readonly IPen s_cueSelectedPen = new Pen(new SolidColorBrush(Color.FromRgb(245, 158, 11)), 2);
    private static readonly IPen s_playheadPen = new Pen(new SolidColorBrush(Color.FromRgb(239, 68, 68)), 1.5);
    private static readonly IPen s_ghostPen = new Pen(new SolidColorBrush(Color.FromRgb(245, 158, 11)), 1, DashStyle.Dash);
    private static readonly Typeface s_typeface = new(FontFamily.Default);

    private TimelineViewModel? _vm;
    private double _pixelsPerSecond = 80;
    private double _viewStart; // seconds at the left edge

    // Drag state
    private CueViewModel? _dragRow;
    private DragHandle _dragHandle;
    private Point _pressPoint;
    private bool _dragMoved;
    private (TimeSpan Start, TimeSpan End)? _ghost;
    private readonly Stopwatch _sinceDragSeek = new();

    static TimelineControl()
    {
        FocusableProperty.OverrideDefaultValue<TimelineControl>(false);
        ClipToBoundsProperty.OverrideDefaultValue<TimelineControl>(true);
    }

    private static readonly Cursor s_resizeCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor s_handCursor = new(StandardCursorType.Hand);

    /// <summary>The whole area takes pointer input, also where nothing is drawn.</summary>
    public bool HitTest(Point point) => true;

    private IBrush TextBrush => ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? Brushes.White : Brushes.Black;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.Invalidated -= OnInvalidated;
            _vm.SelectionChanged -= OnSelectionChanged;
        }

        _vm = DataContext as TimelineViewModel;
        if (_vm is not null)
        {
            _vm.Invalidated += OnInvalidated;
            _vm.SelectionChanged += OnSelectionChanged;
        }

        InvalidateVisual();
    }

    private double ContentWidth => Math.Max(1, Bounds.Width);

    private double ViewSeconds => ContentWidth / _pixelsPerSecond;

    private double TimeToX(TimeSpan t) => (t.TotalSeconds - _viewStart) * _pixelsPerSecond;

    private TimeSpan XToTime(double x) => TimeSpan.FromSeconds(Math.Max(0, _viewStart + (x / _pixelsPerSecond)));

    private void OnInvalidated(object? sender, EventArgs e)
    {
        FollowPlayhead();
        InvalidateVisual();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        // Bring a line picked elsewhere (list, QA panel, find) into view, unless it is being dragged.
        if (_dragRow is null && _vm?.SelectedCue is { } row &&
            (row.End.TotalSeconds < _viewStart || row.Start.TotalSeconds > _viewStart + ViewSeconds))
        {
            ScrollTo(row.Start.TotalSeconds - (ViewSeconds * 0.2));
        }
    }

    /// <summary>While playing, page forward when the playhead nears the right edge (or left the view).</summary>
    private void FollowPlayhead()
    {
        if (_vm is not { IsPlaying: true, FollowPlayback: true } vm || _dragRow is not null)
        {
            return;
        }

        var position = vm.Position.TotalSeconds;
        if (position < _viewStart || position > _viewStart + (ViewSeconds * 0.9))
        {
            ScrollTo(position - (ViewSeconds * 0.1));
        }
    }

    private void ScrollTo(double seconds)
    {
        var duration = _vm?.Duration.TotalSeconds ?? 0;
        var max = Math.Max(0, duration - (ViewSeconds * 0.5));
        _viewStart = Math.Clamp(seconds, 0, duration > 0 ? max : double.MaxValue);
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- drawing

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);

        if (_vm is not { } vm)
        {
            return;
        }

        var foreground = this.TryFindResource("MutedTextBrush", ActualThemeVariant, out var muted) && muted is IBrush b
            ? b
            : Brushes.Gray;
        var area = new Rect(0, RulerHeight, bounds.Width, Math.Max(0, bounds.Height - RulerHeight));

        DrawWaveform(context, vm, area);
        DrawRuler(context, bounds.Width, foreground);
        DrawCues(context, vm, area);

        if (_ghost is { } ghost)
        {
            var x1 = TimeToX(ghost.Start);
            var x2 = TimeToX(ghost.End);
            context.DrawRectangle(null, s_ghostPen, new Rect(x1, area.Top + 1, Math.Max(1, x2 - x1), Math.Max(0, area.Height - 2)));
        }

        if (vm.IsLoaded)
        {
            var x = TimeToX(vm.Position);
            if (x >= 0 && x <= bounds.Width)
            {
                context.DrawLine(s_playheadPen, new Point(x, 0), new Point(x, bounds.Height));
            }
        }

        if (vm.Waveform is null && vm.IsWaveformLoading)
        {
            DrawText(context, "Ses dalgası hazırlanıyor…", new Point(8, RulerHeight + 4), foreground, 11);
        }
    }

    private void DrawWaveform(DrawingContext context, TimelineViewModel vm, Rect area)
    {
        if (vm.Waveform is not { } waveform || area.Height <= 0)
        {
            return;
        }

        var middle = area.Top + (area.Height / 2);
        var half = (area.Height / 2) - 2;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            var end = Math.Min(area.Width, TimeToX(waveform.Duration));
            for (var x = Math.Max(0, TimeToX(TimeSpan.Zero)); x < end; x += 1)
            {
                var from = XToTime(x);
                var peak = waveform.PeakBetween(from, from + TimeSpan.FromSeconds(1 / _pixelsPerSecond));
                var h = Math.Max(0.5, peak * half);
                g.BeginFigure(new Point(x, middle - h), isFilled: true);
                g.LineTo(new Point(x + 1, middle - h));
                g.LineTo(new Point(x + 1, middle + h));
                g.LineTo(new Point(x, middle + h));
                g.EndFigure(isClosed: true);
            }
        }

        context.DrawGeometry(s_waveBrush, null, geometry);
    }

    private void DrawRuler(DrawingContext context, double width, IBrush foreground)
    {
        var pen = new Pen(foreground, 1);
        context.DrawLine(pen, new Point(0, RulerHeight - 0.5), new Point(width, RulerHeight - 0.5));

        // Label every n seconds so labels stay ~80 px apart.
        double[] steps = [0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600];
        var step = steps.FirstOrDefault(s => s * _pixelsPerSecond >= 80, 3600);
        var first = Math.Floor(_viewStart / step) * step;
        for (var t = first; t <= _viewStart + ViewSeconds + step; t += step)
        {
            var x = (t - _viewStart) * _pixelsPerSecond;
            if (x < -60)
            {
                continue;
            }

            context.DrawLine(pen, new Point(x, RulerHeight - 6), new Point(x, RulerHeight));
            DrawText(context, Label(t, step), new Point(x + 3, 1), foreground, 10);
        }
    }

    private static string Label(double seconds, double step)
    {
        var t = TimeSpan.FromSeconds(Math.Round(seconds, 1));
        var text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        return step < 1 ? text + "." + (t.Milliseconds / 100).ToString(CultureInfo.InvariantCulture) : text;
    }

    private void DrawCues(DrawingContext context, TimelineViewModel vm, Rect area)
    {
        var viewEnd = _viewStart + ViewSeconds;
        var top = area.Top + 2;
        var height = Math.Max(0, area.Height - 4);
        foreach (var row in vm.Cues)
        {
            if (row.End.TotalSeconds < _viewStart)
            {
                continue;
            }

            if (row.Start.TotalSeconds > viewEnd)
            {
                break; // rows are sorted by start
            }

            var selected = row == vm.SelectedCue;
            var x1 = TimeToX(row.Start);
            var x2 = TimeToX(row.End);
            var rect = new Rect(x1, top, Math.Max(1, x2 - x1), height);
            var fill = selected ? s_cueSelectedBrush : row.HasError ? s_cueErrorBrush : s_cueBrush;
            context.DrawRectangle(fill, selected ? s_cueSelectedPen : s_cuePen, rect, 3, 3);

            if (rect.Width > 24)
            {
                using (context.PushClip(rect.Deflate(new Thickness(4, 2))))
                {
                    DrawText(context, $"{row.Number}  {row.OneLineText}", new Point(x1 + 4, top + 3), TextBrush, 11);
                }
            }
        }
    }

    private static void DrawText(DrawingContext context, string text, Point origin, IBrush brush, double size)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, s_typeface, size, brush);
        context.DrawText(formatted, origin);
    }

    // ---------------------------------------------------------------- input

    /// <summary>The block (and which part of it) under a point; edges win over the body.</summary>
    private (CueViewModel Row, DragHandle Handle)? FindAt(Point point)
    {
        if (_vm is null || point.Y < RulerHeight)
        {
            return null;
        }

        var t = XToTime(point.X).TotalSeconds;
        var grip = EdgeGrip / _pixelsPerSecond;
        (CueViewModel, DragHandle)? body = null;
        foreach (var row in _vm.Cues)
        {
            if (row.Start.TotalSeconds - grip > t)
            {
                break;
            }

            if (row.End.TotalSeconds + grip < t)
            {
                continue;
            }

            if (Math.Abs(TimeToX(row.Start) - point.X) <= EdgeGrip)
            {
                return (row, DragHandle.Start);
            }

            if (Math.Abs(TimeToX(row.End) - point.X) <= EdgeGrip)
            {
                return (row, DragHandle.End);
            }

            if (t >= row.Start.TotalSeconds && t <= row.End.TotalSeconds)
            {
                body = (row, DragHandle.Body);
            }
        }

        return body;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(this);
        _pressPoint = point;
        _dragMoved = false;
        if (FindAt(point) is { } hit)
        {
            _dragRow = hit.Row;
            _dragHandle = hit.Handle;
            if (_vm.SelectedCue != hit.Row)
            {
                _vm.Select(hit.Row);
            }
        }
        else
        {
            _dragRow = null;
            _vm.Seek(XToTime(point.X));
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_vm is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (_dragRow is null)
        {
            // Scrubbing on empty space while the button is held.
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && ReferenceEquals(e.Pointer.Captured, this))
            {
                _vm.Seek(XToTime(point.X), exact: false);
            }

            Cursor = FindAt(point) switch
            {
                { Handle: DragHandle.Start or DragHandle.End } => s_resizeCursor,
                { Handle: DragHandle.Body } => s_handCursor,
                _ => Cursor.Default,
            };
            return;
        }

        if (!_dragMoved && Math.Abs(point.X - _pressPoint.X) < 3)
        {
            return; // a click, not a drag yet
        }

        _dragMoved = true;
        var delta = TimeSpan.FromSeconds((point.X - _pressPoint.X) / _pixelsPerSecond);
        _ghost = _vm.Clamp(_dragRow, _dragHandle, delta);
        InvalidateVisual();

        // Show the frame at the moving edge now and then, not on every pixel.
        if (!_sinceDragSeek.IsRunning || _sinceDragSeek.Elapsed >= s_dragSeekInterval)
        {
            _vm.Seek(_dragHandle == DragHandle.End ? _ghost.Value.End : _ghost.Value.Start, exact: false);
            _sinceDragSeek.Restart();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_vm is not null && _dragRow is { } row && _dragMoved && _ghost is { } ghost)
        {
            _vm.Apply(row, ghost.Start, ghost.End);
            _vm.Seek(_dragHandle == DragHandle.End ? ghost.End : ghost.Start);
        }

        _dragRow = null;
        _ghost = null;
        _dragMoved = false;
        _sinceDragSeek.Reset();
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragRow = null;
        _ghost = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var zoom = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (zoom)
        {
            // Keep the time under the pointer in place.
            var x = e.GetPosition(this).X;
            var anchor = _viewStart + (x / _pixelsPerSecond);
            var duration = _vm?.Duration.TotalSeconds ?? 0;
            var min = duration > 0 ? Math.Max(MinPixelsPerSecond, ContentWidth / duration) : MinPixelsPerSecond;
            _pixelsPerSecond = Math.Clamp(_pixelsPerSecond * Math.Pow(1.25, e.Delta.Y), Math.Min(min, MaxPixelsPerSecond), MaxPixelsPerSecond);
            ScrollTo(anchor - (x / _pixelsPerSecond));
        }
        else
        {
            // Vertical wheel or horizontal swipe scrolls; one notch moves a tenth of the view.
            var notches = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? -e.Delta.X : -e.Delta.Y;
            ScrollTo(_viewStart + (notches * ViewSeconds * 0.1));
        }

        e.Handled = true;
    }
}
