using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LingoCue.App.Views;

/// <summary>
/// Draws broadcast safe-area guides over the video picture: action safe (93 %) and title safe (90 %).
/// Only guides — subtitles themselves are always drawn by libass. The picture rectangle follows the
/// video's aspect ratio inside the control, the way mpv letterboxes it.
/// </summary>
public sealed class SafeAreaOverlay : Control
{
    public static readonly StyledProperty<double> VideoWidthProperty =
        AvaloniaProperty.Register<SafeAreaOverlay, double>(nameof(VideoWidth));

    public static readonly StyledProperty<double> VideoHeightProperty =
        AvaloniaProperty.Register<SafeAreaOverlay, double>(nameof(VideoHeight));

    private static readonly IPen s_action = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1, DashStyle.Dash);
    private static readonly IPen s_title = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 210, 0)), 1, DashStyle.Dash);

    static SafeAreaOverlay()
    {
        AffectsRender<SafeAreaOverlay>(VideoWidthProperty, VideoHeightProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<SafeAreaOverlay>(false);
    }

    public double VideoWidth
    {
        get => GetValue(VideoWidthProperty);
        set => SetValue(VideoWidthProperty, value);
    }

    public double VideoHeight
    {
        get => GetValue(VideoHeightProperty);
        set => SetValue(VideoHeightProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var picture = PictureRect(Bounds.Size, VideoWidth, VideoHeight);
        if (picture.Width <= 0 || picture.Height <= 0)
        {
            return;
        }

        context.DrawRectangle(null, s_action, Inset(picture, 0.93));
        context.DrawRectangle(null, s_title, Inset(picture, 0.90));
    }

    /// <summary>Where the video picture sits inside the control (uniform fit, centered).</summary>
    internal static Rect PictureRect(Size area, double videoWidth, double videoHeight)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return default;
        }

        if (videoWidth <= 0 || videoHeight <= 0)
        {
            return new Rect(area);
        }

        var scale = Math.Min(area.Width / videoWidth, area.Height / videoHeight);
        var size = new Size(videoWidth * scale, videoHeight * scale);
        return new Rect(new Point((area.Width - size.Width) / 2, (area.Height - size.Height) / 2), size);
    }

    private static Rect Inset(Rect r, double fraction)
    {
        var dx = r.Width * (1 - fraction) / 2;
        var dy = r.Height * (1 - fraction) / 2;
        return r.Deflate(new Thickness(dx, dy));
    }
}
