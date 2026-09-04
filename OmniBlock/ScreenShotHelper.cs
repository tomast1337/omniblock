using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OmniBlock;

public class ScreenShotHelper
{
    public static string saveScreenshot(string gameDir, int width, int height) => "Screenshots are not supported";

    public static string saveScreenshot(string gameDir, int width, int height, byte[] rgbPixels)
    {
        if (rgbPixels == null || rgbPixels.Length < width * height * 3)
            return "Failed to save: invalid pixel data";
        if (string.IsNullOrEmpty(gameDir))
            return "Failed to save: invalid game directory";

        try
        {
            var screenshotsPath = Path.Combine(gameDir, "screenshots");
            Directory.CreateDirectory(screenshotsPath);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH.mm.ss");
            var suffix = 1;
            string fileName;
            string fullPath;
            do
            {
                fileName = suffix == 1 ? timestamp + ".png" : timestamp + "_" + suffix + ".png";
                fullPath = Path.Combine(screenshotsPath, fileName);
                suffix++;
            } while (File.Exists(fullPath));

            // Input rows are bottom-to-top; flip to top-to-bottom for the image file. WebGPU's
            // caller reverses its rows before calling in, specifically to land here unchanged.
            var rowStride = width * 3;
            var flipped = new byte[rgbPixels.Length];
            for (var y = 0; y < height; y++)
            {
                var srcRow = height - 1 - y;
                var srcOffset = srcRow * rowStride;
                var dstOffset = y * rowStride;
                Buffer.BlockCopy(rgbPixels, srcOffset, flipped, dstOffset, rowStride);
            }

            using (var image = Image.LoadPixelData<Rgb24>(flipped, width, height))
            {
                image.SaveAsPng(fullPath);
            }

            return "Saved screenshot as " + fileName;
        }
        catch (Exception ex)
        {
            return "Failed to save: " + ex.Message;
        }
    }
}
