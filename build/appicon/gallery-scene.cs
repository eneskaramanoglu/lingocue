#:package SkiaSharp@3.119.4
#:property PublishAot=false
#:property ManagePackageVersionsCentrally=false
using SkiaSharp;

// A stylized Istanbul skyline at sunset (no photos, no real people): the website demo video and
// src/LingoCue.App/Assets/gallery-background.jpg (then: ffmpeg -vf crop=2133:1200:133:75,scale=1280:720).
// Usage: dotnet run --file build/appicon/gallery-scene.cs -- scene.png
const int W = 2400, H = 1350;
using var surface = SKSurface.Create(new SKImageInfo(W, H));
var c = surface.Canvas;
var horizon = 820f;

// Sky
using (var sky = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, horizon),
    [new SKColor(0x1b, 0x1f, 0x4b), new SKColor(0x6b, 0x2f, 0x7a), new SKColor(0xe0, 0x6c, 0x55), new SKColor(0xf6, 0xb2, 0x6b)],
    [0f, 0.45f, 0.82f, 1f], SKShaderTileMode.Clamp) })
{
    c.DrawRect(0, 0, W, horizon, sky);
}

// Sun with glow
using (var glow = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(new SKPoint(1500, horizon - 40), 520,
    [new SKColor(0xff, 0xd9, 0x8a, 200), new SKColor(0xff, 0xb0, 0x70, 0)], SKShaderTileMode.Clamp) })
{
    c.DrawCircle(1500, horizon - 40, 520, glow);
}

using (var sun = new SKPaint { IsAntialias = true, Color = new SKColor(0xff, 0xe2, 0xa0) })
{
    c.DrawCircle(1500, horizon - 40, 95, sun);
}

// Far hills (Asian side)
using (var far = new SKPaint { IsAntialias = true, Color = new SKColor(0x4a, 0x2a, 0x5c, 200) })
{
    using var p = new SKPath();
    p.MoveTo(0, horizon);
    for (var x = 0; x <= W; x += 60)
    {
        p.LineTo(x, horizon - 40 - (float)(25 * Math.Sin(x / 210.0) + 18 * Math.Sin(x / 77.0)));
    }

    p.LineTo(W, horizon);
    p.Close();
    c.DrawPath(p, far);
}

// Skyline silhouette: domes, minarets, a tower
using var ink = new SKPaint { IsAntialias = true, Color = new SKColor(0x1c, 0x12, 0x2e) };
void Mosque(float cx, float baseY, float scale)
{
    c.DrawRect(cx - (150 * scale), baseY - (70 * scale), 300 * scale, 70 * scale, ink);
    c.DrawCircle(cx, baseY - (70 * scale), 95 * scale, ink);                     // main dome
    foreach (var dx in new[] { -120f, 120f })
    {
        c.DrawCircle(cx + (dx * scale), baseY - (60 * scale), 45 * scale, ink);   // half domes
    }

    foreach (var dx in new[] { -230f, -190f, 190f, 230f })                        // minarets
    {
        var x = cx + (dx * scale);
        c.DrawRect(x - (7 * scale), baseY - (300 * scale), 14 * scale, 300 * scale, ink);
        using var tip = new SKPath();
        tip.MoveTo(x - (9 * scale), baseY - (300 * scale));
        tip.LineTo(x, baseY - (360 * scale));
        tip.LineTo(x + (9 * scale), baseY - (300 * scale));
        tip.Close();
        c.DrawPath(tip, ink);
        c.DrawRect(x - (11 * scale), baseY - (230 * scale), 22 * scale, 6 * scale, ink); // balcony
    }
}

using (var land = new SKPath())
{
    land.MoveTo(0, horizon);
    land.LineTo(0, horizon - 60);
    land.CubicTo(300, horizon - 120, 700, horizon - 90, 1050, horizon - 70);
    land.LineTo(1150, horizon);
    land.Close();
    c.DrawPath(land, ink);
}

Mosque(330, horizon - 95, 0.9f);
Mosque(820, horizon - 70, 0.6f);

// Tower on the right bank
using (var right = new SKPath())
{
    right.MoveTo(1750, horizon);
    right.CubicTo(1900, horizon - 70, 2150, horizon - 110, W, horizon - 120);
    right.LineTo(W, horizon);
    right.Close();
    c.DrawPath(right, ink);
}

c.DrawRect(2080, horizon - 330, 70, 240, ink);
using (var cone = new SKPath())
{
    cone.MoveTo(2070, horizon - 330);
    cone.LineTo(2115, horizon - 430);
    cone.LineTo(2160, horizon - 330);
    cone.Close();
    c.DrawPath(cone, ink);
}

// Small houses on the right
var rnd = new Random(7);
for (var x = 1780; x < W; x += 46)
{
    var h = 30 + rnd.Next(60);
    c.DrawRect(x, horizon - 60 - h + (float)((x - 1780) * -0.04), 38, h + 60, ink);
}

// Water
using (var water = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, horizon), new SKPoint(0, H),
    [new SKColor(0x3a, 0x2a, 0x5e), new SKColor(0x14, 0x1a, 0x3a)], SKShaderTileMode.Clamp) })
{
    c.DrawRect(0, horizon, W, H - horizon, water);
}

// Sun reflection streaks
using var streak = new SKPaint { IsAntialias = true };
for (var i = 0; i < 26; i++)
{
    var y = horizon + 12 + (i * 18);
    var w = 220 - (i * 6) + rnd.Next(40);
    streak.Color = new SKColor(0xff, 0xc9, 0x88, (byte)Math.Max(20, 170 - (i * 6)));
    c.DrawRoundRect(1500 - (w / 2f) + rnd.Next(-30, 30), y, w, 5, 2.5f, 2.5f, streak);
}

// Seagulls
using var gull = new SKPaint { IsAntialias = true, Color = new SKColor(0x22, 0x18, 0x33), Style = SKPaintStyle.Stroke, StrokeWidth = 5, StrokeCap = SKStrokeCap.Round };
foreach (var (gx, gy, s) in new[] { (1150f, 300f, 1f), (1240f, 250f, 0.8f), (1330f, 330f, 0.7f), (700f, 220f, 0.9f), (1880f, 380f, 0.6f) })
{
    using var g = new SKPath();
    g.MoveTo(gx - (26 * s), gy);
    g.QuadTo(gx - (12 * s), gy - (16 * s), gx, gy);
    g.QuadTo(gx + (12 * s), gy - (16 * s), gx + (26 * s), gy);
    c.DrawPath(g, gull);
}

using var image = surface.Snapshot();
using var data = image.Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes(args[0], data.ToArray());
Console.WriteLine("scene written");
