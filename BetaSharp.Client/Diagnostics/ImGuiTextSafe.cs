using System.Numerics;
using Hexa.NET.ImGui;

namespace BetaSharp.Client.Diagnostics;

/// <summary>
/// ImGui.Text/TextColored/TextDisabled treat their string argument as a printf format string
/// (forwarded to native vsnprintf). Any dynamic content containing a literal '%' (log messages,
/// translation strings, exception text, player-typed search text, etc.) segfaults. Use these
/// instead whenever the text isn't a compile-time constant.
/// </summary>
internal static class ImGuiTextSafe
{
    public static void Text(string text) => ImGui.TextUnformatted(text);

    public static void TextColored(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    public static unsafe void TextDisabled(string text)
    {
        Vector4 disabledColor = ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
        TextColored(disabledColor, text);
    }
}
