namespace BetaSharp.Util;

public static class ChatAllowedCharacters
{
    public static readonly char[] allowedCharactersArray = ['/', '\n', '\r', '\t', '\u0000', '\f', '`', '?', '*', '\\', '<', '>', '|', '\"', ':'];

    public static bool IsAllowedCharacter(char c) =>
        c is >= ' ' and <= '~' || "⌂ÇüéâäàåçêëèïîìÄÅÉæÆôöòûùÿÖÜø£Ø×ƒáíóúñÑªº¿®¬½¼¡«»;".Contains(c);
}
