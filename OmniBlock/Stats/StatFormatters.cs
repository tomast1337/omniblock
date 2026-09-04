namespace OmniBlock.Stats;

internal static class StatFormatters
{
    public static string FormatDistance(int value)
    {
        var meters = value / 100.0;
        var kilometers = meters / 1000.0;

        if (kilometers > 0.5) return $"{kilometers:0.##} km";
        if (meters > 0.5) return $"{meters:0.##} m";

        return $"{value} cm";
    }

    public static string FormatTime(int value)
    {
        var seconds = value / 20.0;
        var minutes = seconds / 60.0;
        var hours = minutes / 60.0;
        var days = hours / 24.0;
        var years = days / 365.0;

        return value switch
        {
            _ when years > 0.5 => $"{years:0.##} y",
            _ when days > 0.5 => $"{days:0.##} d",
            _ when hours > 0.5 => $"{hours:0.##} h",
            _ when minutes > 0.5 => $"{minutes:0.##} m",
            _ => $"{seconds} s"
        };
    }
}
