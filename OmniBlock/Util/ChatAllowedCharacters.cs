using System.Globalization;

namespace OmniBlock.Util;

public static class ChatAllowedCharacters
{
    /// <summary>
    /// Characters that are invalid in file and folder names.
    /// Used to sanitise world names before they become directory names.
    /// </summary>
    public static readonly char[] InvalidFileNameChars =
    [
        '/', '\\', ':', '*', '?', '"', '<', '>', '|', '`',
        '\u0000', '\u000c', '\n', '\r', '\t',
    ];

    /// <summary>
    /// Returns true for any printable character a player can type in chat or on a sign.
    /// Accepts everything that is not a control character, surrogate, private-use code
    /// point, or unassigned, so the filter no longer ties the protocol to a specific
    /// legacy codepage.
    /// </summary>
    public static bool IsAllowedCharacter(char c)
    {
        if (char.IsControl(c)) return false;
        if (char.IsSurrogate(c)) return false;

        var cat = char.GetUnicodeCategory(c);
        return cat is not UnicodeCategory.PrivateUse
            and not UnicodeCategory.OtherNotAssigned;
    }
}
