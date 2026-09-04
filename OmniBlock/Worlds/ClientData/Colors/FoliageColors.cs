namespace OmniBlock.Worlds.Colors;

public class FoliageColors
{
    private static int[] foliageBuffer = new int[65536];

    public static void loadColors(int[] foliageBuffer) => FoliageColors.foliageBuffer = foliageBuffer;

    public static int getFoliageColor(double temperature, double downfall)
    {
        downfall *= temperature;
        var temperatureIndex = (int)((1.0D - temperature) * 255.0D);
        var downfallIndex = (int)((1.0D - downfall) * 255.0D);
        return foliageBuffer[(downfallIndex << 8) | temperatureIndex];
    }

    public static int getSpruceColor() => 0x619961;

    public static int getBirchColor() => 0x80A755;

    public static int getDefaultColor() => 0x48B518;
}
