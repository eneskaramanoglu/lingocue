#:package SkiaSharp@3.119.4
#:property PublishAot=false
#:property ManagePackageVersionsCentrally=false
// Draws the LingoCue icon (the app bar logo on the accent tile) and writes:
//   build/appicon/lingocue-1024.png     master image; macOS turns it into AppIcon.icns with sips + iconutil
//   src/LingoCue.App/Assets/lingocue.ico   16–256 px, PNG-compressed entries
// Usage (from the repository root): dotnet run --file build/appicon/make-icon.cs
using SkiaSharp;

const string Glyph = "M20,4H4A2,2 0 0,0 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V6A2,2 0 0,0 20,4M4,12H8V14H4V12M14,18H4V16H14V18M20,18H16V16H20V18M20,14H10V12H20V14Z";
var root = Directory.GetCurrentDirectory();

// inset: 100 for the macOS tile (with shadow), small for Windows where icons fill their square.
byte[] Render(int size, float inset = 100)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);
    var s = size / 1024f;

    // macOS: an 824 px tile in the 1024 canvas with a soft shadow. Windows: the same art filling the square.
    var tile = new SKRect(inset * s, inset * s, (1024 - inset) * s, (1024 - inset) * s);
    var radius = (1024 - (2 * inset)) * 0.225f * s;
    if (inset >= 60)
    {
        using var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 70), ImageFilter = SKImageFilter.CreateBlur(18 * s, 18 * s) };
        canvas.DrawRoundRect(new SKRect(tile.Left, tile.Top + (14 * s), tile.Right, tile.Bottom + (14 * s)), radius, radius, shadow);
    }

    using (var fill = new SKPaint
    {
        IsAntialias = true,
        Shader = SKShader.CreateLinearGradient(new SKPoint(0, tile.Top), new SKPoint(0, tile.Bottom),
            [new SKColor(0x5B, 0x9B, 0xFF), new SKColor(0x1D, 0x4E, 0xD8)], SKShaderTileMode.Clamp),
    })
    {
        canvas.DrawRoundRect(tile, radius, radius, fill);
    }

    using var path = SKPath.ParseSvgPathData(Glyph);
    var glyphSize = (1024 - (2 * inset)) * 0.655f * s;
    var scale = glyphSize / 20f; // the glyph spans 2..22 × 4..20 of its 24-unit box
    var matrix = SKMatrix.CreateScale(scale, scale);
    matrix = matrix.PostConcat(SKMatrix.CreateTranslation((size / 2f) - (12 * scale), (size / 2f) - (12 * scale)));
    path.Transform(matrix);
    using var glyph = new SKPaint { IsAntialias = true, Color = SKColors.White };
    canvas.DrawPath(path, glyph);

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

Directory.CreateDirectory(Path.Combine(root, "build", "appicon"));
File.WriteAllBytes(Path.Combine(root, "build", "appicon", "lingocue-1024.png"), Render(1024));

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var images = sizes.Select(size => Render(size, inset: 24)).ToArray();
using var ico = new BinaryWriter(File.Create(Path.Combine(root, "src", "LingoCue.App", "Assets", "lingocue.ico")));
ico.Write((short)0);
ico.Write((short)1);
ico.Write((short)sizes.Length);
var offset = 6 + (16 * sizes.Length);
for (var i = 0; i < sizes.Length; i++)
{
    ico.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
    ico.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
    ico.Write((byte)0);
    ico.Write((byte)0);
    ico.Write((short)1);
    ico.Write((short)32);
    ico.Write(images[i].Length);
    ico.Write(offset);
    offset += images[i].Length;
}

foreach (var image in images)
{
    ico.Write(image);
}

Console.WriteLine($"Wrote lingocue-1024.png and lingocue.ico ({sizes.Length} sizes)");
