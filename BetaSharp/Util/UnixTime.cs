namespace BetaSharp.Util;

public static class UnixTime
{
    public static long GetCurrentTimeMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
