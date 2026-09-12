using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit.Abstractions;

namespace OmniBlock.Tests.Rendering;

/// <summary>Opt-in analysis of actual E2E screenshots, not substitutes for shader/occlusion testing.</summary>
public sealed class EntityImpostorCaptureArtifactTests(ITestOutputHelper output)
{
    [SkippableFact]
    public void Orbit_pairs_have_present_similarly_positioned_cow_silhouettes()
    {
        var directory = Environment.GetEnvironmentVariable("OMNIBLOCK_IMPOSTOR_ORBIT_IMAGES");
        Skip.If(string.IsNullOrEmpty(directory), "Set OMNIBLOCK_IMPOSTOR_ORBIT_IMAGES to the orbit screenshot directory.");
        var paths = Directory.GetFiles(directory!, "*.png").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(52, paths.Length);
        for (var view = 0; view < 26; view++)
        {
            using var reference = Image.Load<Rgba32>(paths[view * 2]);
            using var candidate = Image.Load<Rgba32>(paths[view * 2 + 1]);
            Assert.Equal(reference.Size, candidate.Size);
            var a = Bounds(reference); var b = Bounds(candidate);
            output.WriteLine($"view {view}: 3D {a}, impostor {b}");
            // Gross orientation/pivot/scale regression tripwire at the deliberately enlarged
            // 20-block / FOV30 inspection view. NOT the 1-pixel switch-distance quality budget.
            Assert.InRange(b.Width / (double)a.Width, .8, 1.2);
            Assert.InRange(b.Height / (double)a.Height, .8, 1.2);
            Assert.InRange(Math.Abs((a.Left + a.Right) - (b.Left + b.Right)) / 2.0, 0, 12);
            Assert.InRange(Math.Abs((a.Top + a.Bottom) - (b.Top + b.Bottom)) / 2.0, 0, 12);
        }
    }

    private static Rectangle Bounds(Image<Rgba32> image)
    {
        var left = image.Width; var top = image.Height; var right = -1; var bottom = -1; var count = 0;
        for (var y = image.Height / 2 - 220; y < image.Height / 2 + 220; y++)
        for (var x = image.Width / 2 - 220; x < image.Width / 2 + 220; x++)
        {
            if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) continue;
            var pixel = image[x, y];
            // This fixture uses default brown/neutral cow textures against blue sky, ocean and
            // white sun/clouds. Deliberately not a general segmentation method for arbitrary packs.
            if (pixel.B > pixel.R + 12 || pixel.R > 225 || pixel.B > 225) continue;
            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); count++;
        }
        Assert.True(count > 50, "Cow pixels were missing; submission counters alone are insufficient.");
        return new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }
}
