namespace BetaSharp.Util;

public static class UnixTime
{
    // i feel very stupid
    public static long GetCurrentTimeMillis()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
