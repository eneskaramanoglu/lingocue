#:package SkiaSharp@3.119.4
#:property PublishAot=false
#:property ManagePackageVersionsCentrally=false
// Builds the website's icons and social preview images from the app icon and a rendered frame.
// Usage (repository root): dotnet run --file website/tools/make-images.cs
using SkiaSharp;

var root = Directory.GetCurrentDirectory();
var assets = Path.Combine(root, "website", "assets");
var ico = File.ReadAllBytes(Path.Combine(root, "src", "LingoCue.App", "Assets", "lingocue.ico"));

// The 256 px entry of the app's .ico (the Windows-style tile that fills its square).
byte[] Entry(int size)
{
    var count = BitConverter.ToInt16(ico, 4);
    for (var i = 0; i < count; i++)
    {
        var e = 6 + (16 * i);
        var s = ico[e] == 0 ? 256 : ico[e];
        if (s == size)
        {
            var length = BitConverter.ToInt32(ico, e + 8);
            var offset = BitConverter.ToInt32(ico, e + 12);
            return ico[offset..(offset + length)];
        }
    }

    throw new InvalidOperationException($"No {size} px icon");
}

var icon256 = Entry(256);
File.WriteAllBytes(Path.Combine(assets, "icon-256.png"), icon256);
File.Copy(Path.Combine(root, "src", "LingoCue.App", "Assets", "lingocue.ico"), Path.Combine(root, "website", "favicon.ico"), overwrite: true);
using (var bitmap = SKBitmap.Decode(icon256))
using (var small = bitmap.Resize(new SKImageInfo(180, 180), new SKSamplingOptions(SKCubicResampler.Mitchell)))
using (var image = SKImage.FromBitmap(small))
using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
{
    File.WriteAllBytes(Path.Combine(assets, "icon-180.png"), data.ToArray());
}

void SocialImage(string file, string tagline, string sub)
{
    const int W = 1200, H = 630;
    using var surface = SKSurface.Create(new SKImageInfo(W, H));
    var c = surface.Canvas;
    using (var frame = SKBitmap.Decode(Path.Combine(assets, "img", "style-block.webp")))
    {
        // The frame at 90 %, shifted right so its subtitle sits beside the text.
        c.Clear(new SKColor(10, 12, 24));
        c.DrawBitmap(frame, new SKRect(284, 0, 284 + (frame.Width * 0.9f), frame.Height * 0.9f));
    }

    using (var shade = new SKPaint
    {
        Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(W, 0),
            [new SKColor(10, 12, 24, 255), new SKColor(10, 12, 24, 240), new SKColor(10, 12, 24, 0)], [0f, 0.42f, 0.5f], SKShaderTileMode.Clamp),
    })
    {
        c.DrawRect(0, 0, W, H, shade);
    }

    using (var logo = SKBitmap.Decode(icon256))
    {
        c.DrawBitmap(logo, new SKRect(72, 92, 72 + 112, 92 + 112), new SKPaint { IsAntialias = true });
    }

    var bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
    var regular = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal) ?? SKTypeface.Default;
    using var white = new SKPaint { IsAntialias = true, Color = SKColors.White };
    using var soft = new SKPaint { IsAntialias = true, Color = new SKColor(0xc2, 0xc8, 0xd6) };
    using var accent = new SKPaint { IsAntialias = true, Color = new SKColor(0x5b, 0x9b, 0xff) };
    c.DrawText("LingoCue", 72, 300, new SKFont(bold, 84), white);
    var y = 372f;
    foreach (var line in tagline.Split('\n'))
    {
        c.DrawText(line, 72, y, new SKFont(bold, 40), accent);
        y += 52;
    }

    c.DrawText(sub, 72, y + 34, new SKFont(regular, 25), soft);

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(Path.Combine(assets, file), data.ToArray());
}

SocialImage("og-tr.png", "Yapay zekâ destekli\naltyazı editörü", "Windows ve Mac · Ücretsiz ve açık kaynak");
SocialImage("og-en.png", "AI-assisted\nsubtitle editor", "Windows and Mac · Free and open source");
Console.WriteLine("icons and social images written");
