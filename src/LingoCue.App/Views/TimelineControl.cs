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
    private const double RulerHeight = 24;
    private const double EdgeGrip = 6;
    private const double MinPixelsPerSecond = 2;
    private const double MaxPixelsPerSecond = 1000;
    private static readonly TimeSpan s_dragSeekInterval = TimeSpan.FromMilliseconds(200);

    private static readonly IBrush s_playheadBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x5D, 0x5D));
    private static readonly IPen s_playheadPen = new Pen(s_playheadBrush, 2);
    private static readonly Typeface s_typeface = new(FontFamily.Default);

    /// <summary>Brushes from the theme tokens (Styles/Shell.axaml), rebuilt when the theme changes.</summary>
    private sealed record Palette(
        IBrush Wave, IBrush Cue, IPen CuePen, IBrush Selected, IPen SelectedPen, IBrush Warning, IPen WarningPen,
        IBrush Error, IPen ErrorPen, IPen Ghost, IPen Ruler, IBrush Muted, IBrush Text);

    private Palette? _palette;
    private Avalonia.Styling.ThemeVariant? _paletteVariant;

    private Palette Pal
    {
        get
        {
            if (_palette is not null && Equals(_paletteVariant, ActualThemeVariant))
            {
                return _palette;
            }

            var accent = ResourceColor("AccentBrush", Color.FromRgb(0x5B, 0x9B, 0xFF));
            var warning = ResourceColor("WarningForegroundBrush", Color.FromRgb(0xFB, 0xBF, 0x24));
            var error = ResourceColor("ErrorForegroundBrush", Color.FromRgb(0xF8, 0x71, 0x71));
            var text = ResourceColor("PrimaryTextBrush", Colors.White);
            var muted = ResourceColor("MutedTextBrush", Colors.Gray);
            var border = ResourceColor("PanelBorderBrush", Colors.DimGray);
            _paletteVariant = ActualThemeVariant;
            return _palette = new Palette(
                Wave: Solid(muted, 0x40),
                Cue: Solid(text, 0x10),
                CuePen: new Pen(Solid(text, 0x24), 1),
                Selected: Solid(accent, 0x33),
                SelectedPen: new Pen(new SolidColorBrush(accent), 1.5),
                Warning: Solid(warning, 0x29),
                WarningPen: new Pen(Solid(warning, 0x8C), 1),
                Error: Solid(error, 0x29),
                ErrorPen: new Pen(Solid(error, 0x8C), 1),
                Ghost: new Pen(new SolidColorBrush(accent), 1, DashStyle.Dash),
                Ruler: new Pen(new SolidColorBrush(border), 1),
                Muted: new SolidColorBrush(muted),
                Text: new SolidColorBrush(text));

            static IBrush Solid(Color c, byte alpha) => new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }
    }

    private Color ResourceColor(string key, Color fallback)
        => this.TryFindResource(key, ActualThemeVariant, out var value) && value is ISolidColorBrush brush ? brush.Color : fallback;

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

        var foreground = Pal.Muted;
        var area = new Rect(0, RulerHeight, bounds.Width, Math.Max(0, bounds.Height - RulerHeight));

        DrawWaveform(context, vm, area);
        DrawRuler(context, bounds.Width, foreground);
        DrawCues(context, vm, area);

        if (_ghost is { } ghost)
        {
            var x1 = TimeToX(ghost.Start);
            var x2 = TimeToX(ghost.End);
            context.DrawRectangle(null, Pal.Ghost, new Rect(x1, area.Top + 6, Math.Max(1, x2 - x1), Math.Max(0, area.Height - 12)), 8, 8);
        }

        if (vm.IsLoaded)
        {
            var x = TimeToX(vm.Position);
            if (x >= 0 && x <= bounds.Width)
            {
                context.DrawLine(s_playheadPen, new Point(x, 0), new Point(x, bounds.Height));
                context.DrawRectangle(s_playheadBrush, null, new Rect(x - 6, 0, 12, 8), 2, 2);
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

        context.DrawGeometry(Pal.Wave, null, geometry);
    }

    private void DrawRuler(DrawingContext context, double width, IBrush foreground)
    {
        var pen = Pal.Ruler;
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
        var top = area.Top + 8;
        var height = Math.Max(0, area.Height - 16);
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
            var palette = Pal;
            var (fill, outline) = selected ? (palette.Selected, palette.SelectedPen)
                : row.HasError ? (palette.Error, palette.ErrorPen)
                : row.HasWarning ? (palette.Warning, palette.WarningPen)
                : (palette.Cue, palette.CuePen);
            context.DrawRectangle(fill, outline, rect, 8, 8);

            if (rect.Width > 24)
            {
                using (context.PushClip(rect.Deflate(new Thickness(6, 2))))
                {
                    DrawText(context, row.Number.ToString(CultureInfo.InvariantCulture), new Point(x1 + 8, top + 6), palette.Muted, 11);
                    DrawText(context, row.OneLineText, new Point(x1 + 8 + (row.Number >= 100 ? 24 : row.Number >= 10 ? 18 : 12), top + 6), palette.Text, 11);
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
