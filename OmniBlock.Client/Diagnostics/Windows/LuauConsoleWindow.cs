using System.Numerics;
using Hexa.NET.ImGui;
using OmniBlock.Luau;

namespace OmniBlock.Client.Diagnostics.Windows;

/// <summary>
///     Debug-menu console for ad hoc Luau scripts: type an expression, Send, get the result or
///     error printed below. Each submission runs in its own fresh <see cref="LuauQuickRun" />
///     VM (no state survives between sends — see docs/luau-e2e-execution-plan.md for why the
///     ephemeral shape was chosen over a persistent REPL VM). Disabled with an explanatory
///     message when <c>omniblock_luau</c> isn't resolvable, the same pattern
///     <see cref="ConsoleWindow" /> uses for "no player attached".
/// </summary>
internal sealed class LuauConsoleWindow : DebugWindow
{
    private readonly record struct Entry(string Input, string Output, bool Success);

    private readonly List<Entry> _history = [];
    private string _input = string.Empty;
    private bool _autoScroll = true;
    private bool _scrollToBottom;
    private bool _refocusInput;
    private readonly bool _available = LuauQuickRun.IsAvailable();

    public override string Title => "Luau Console";
    public override DebugDock DefaultDock => DebugDock.Bottom;

    protected override void OnDraw()
    {
        ImGui.Checkbox("Auto-scroll", ref _autoScroll);
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
            _history.Clear();

        ImGui.Separator();

        if (!_available)
        {
            ImGuiTextSafe.TextColored(new Vector4(1f, 0.8f, 0f, 1f),
                "omniblock_luau native library not found — run native/luau/build-local.sh (LUAU_NATIVE_LOCAL=1) for this checkout.");
        }

        float inputHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        ImGui.BeginChild("##luau_log_scroll", new Vector2(0f, -inputHeight), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

        foreach (Entry entry in _history)
        {
            ImGuiTextSafe.TextColored(new Vector4(0.6f, 0.75f, 1f, 1f), $"> {entry.Input}");
            Vector4 color = entry.Success ? new Vector4(1f, 1f, 1f, 1f) : new Vector4(1f, 0.35f, 0.35f, 1f);
            ImGuiTextSafe.TextColored(color, entry.Output);
        }

        if (_autoScroll && _scrollToBottom)
        {
            ImGui.SetScrollHereY(1f);
            _scrollToBottom = false;
        }

        ImGui.EndChild();

        ImGui.Separator();

        if (!_available)
            ImGui.BeginDisabled();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Send").X - ImGui.GetStyle().ItemSpacing.X * 2 - ImGui.GetStyle().FramePadding.X * 4);
        if (_refocusInput)
        {
            ImGui.SetKeyboardFocusHere();
            _refocusInput = false;
        }

        bool submitted = ImGui.InputText("##luau_console_input", ref _input, 1024, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        bool sendClicked = ImGui.Button("Send");

        if (!_available)
            ImGui.EndDisabled();

        if (_available && (submitted || sendClicked) && !string.IsNullOrWhiteSpace(_input))
        {
            string script = _input.Trim();
            bool success = LuauQuickRun.TryExecute(script, out string output);
            _history.Add(new Entry(script, output, success));
            _input = string.Empty;
            _scrollToBottom = true;
            _refocusInput = true;
        }
    }
}
