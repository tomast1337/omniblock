using System.Numerics;
using Hexa.NET.ImGui;

namespace BetaSharp.Client.Diagnostics;

/// <summary>
/// ImGui.Text/TextColored/TextDisabled treat their string argument as a printf format string
/// (forwarded to native vsnprintf). Any dynamic content containing a literal '%' (log messages,
/// translation strings, exception text, player-typed search text, etc.) segfaults. Use these
/// instead whenever the text isn't a compile-time constant.
///
/// Issue: https://git.gay/betasharp-official/betasharp/issues/37
///
/// Confirmed as Dear ImGui's own format-string contract, not a Hexa.NET.ImGui binding bug:
/// https://github.com/HexaEngine/Hexa.NET.ImGui/issues/130#issuecomment-5050373687 (see also
/// https://github.com/ocornut/imgui/issues/2210). That thread also names the older "%s" trick
/// (Text("%s", str) - pass a literal format string with the real text as its vararg) as a valid
/// fix; TextUnformatted was chosen here since it skips format parsing entirely rather than just
/// using it correctly. The commenter's suggested "optimal" fix - native
/// Text[Colored|Disabled|Wrapped]Unformatted() functions added to the cimgui fork this binding
/// wraps, replacing this file's multi-call C# wrappers with a single marshaled call - is not
/// upstream and nobody's committed to building it; the package maintainer has said he's out of
/// capacity to maintain the project. Don't wait on it.
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

    public static void TextWrapped(string text)
    {
        ImGui.PushTextWrapPos(0.0f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }
}
