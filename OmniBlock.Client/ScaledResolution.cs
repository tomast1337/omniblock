using OmniBlock.Client.Options;

namespace OmniBlock.Client;

public class ScaledResolution
{
    public ScaledResolution(GameOptions options, int scaledWidth, int scaledHeight)
    {
        ScaledWidth = scaledWidth;
        ScaledHeight = scaledHeight;
        var guiScale = options.GuiScale;
        ScaleFactor = 1;

        if (guiScale == 0)
            guiScale = 1000;

        while (ScaleFactor < guiScale && ScaledWidth / (ScaleFactor + 1) >= 320 && ScaledHeight / (ScaleFactor + 1) >= 240)
        {
            ++ScaleFactor;
        }

        ScaledWidthDouble = ScaledWidth / (double)ScaleFactor;
        ScaledHeightDouble = ScaledHeight / (double)ScaleFactor;
        ScaledWidth = (int)ScaledWidthDouble;
        ScaledHeight = (int)ScaledHeightDouble;
    }

    public int ScaledWidth { get; }
    public int ScaledHeight { get; }
    public double ScaledWidthDouble { get; }
    public double ScaledHeightDouble { get; }
    public int ScaleFactor { get; }
}
