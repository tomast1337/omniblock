using System.Numerics;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Logging;

namespace OmniBlock.Client.Diagnostics.Windows;

internal sealed unsafe class ConsoleWindow : DebugWindow
{
    private static readonly ILogger s_luauLogger = Log.Instance.For("Luau");
    private string _input = string.Empty;
    private bool _autoScroll = true;
    private bool _scrollToBottom;
    private int _prevEntryCount;
    private bool _refocusInput;
    private readonly DebugWindowContext _ctx;
    private readonly LuauCompletion _completion = new();
    private readonly ImGuiInputTextCallback _completionCallback;
    private string _completionHint = "Tab completes Luau names";

    public ConsoleWindow(DebugWindowContext ctx)
    {
        _ctx = ctx;
        _completionCallback = OnComplete;
    }

    private static readonly Dictionary<LogLevel, Vector4> s_levelColors = new()
    {
        [LogLevel.Trace] = new Vector4(0.5f, 0.5f, 0.5f, 1f),
        [LogLevel.Debug] = new Vector4(0.7f, 0.7f, 0.7f, 1f),
        [LogLevel.Information] = new Vector4(1f, 1f, 1f, 1f),
        [LogLevel.Warning] = new Vector4(1f, 0.8f, 0f, 1f),
        [LogLevel.Error] = new Vector4(1f, 0.35f, 0.35f, 1f),
        [LogLevel.Critical] = new Vector4(1f, 0f, 0.5f, 1f),
    };

    private static readonly Dictionary<LogLevel, string> s_levelTags = new()
    {
        [LogLevel.Trace] = "TRC",
        [LogLevel.Debug] = "DBG",
        [LogLevel.Information] = "INF",
        [LogLevel.Warning] = "WRN",
        [LogLevel.Error] = "ERR",
        [LogLevel.Critical] = "CRT",
    };

    public override string Title => "Console";
    public override DebugDock DefaultDock => DebugDock.Bottom;

    protected override void OnDraw()
    {
        ImGui.Checkbox("Auto-scroll", ref _autoScroll);
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
            Log.Instance.ClearLog();

        ImGui.Separator();

        float inputHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        ImGui.BeginChild("##log_scroll", new Vector2(0f, -inputHeight), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

        LogEntry[] entries = Log.Instance.GetRecentEntries();

        if (entries.Length != _prevEntryCount)
        {
            _prevEntryCount = entries.Length;
            _scrollToBottom = true;
        }

        foreach (LogEntry entry in entries)
        {
            Vector4 color = s_levelColors.TryGetValue(entry.Level, out Vector4 c) ? c : Vector4.One;
            string tag = s_levelTags.TryGetValue(entry.Level, out string? t) ? t : "???";

            ImGuiTextSafe.TextColored(color, $"[{entry.Timestamp:HH:mm:ss}] [{tag}] {entry.Category}: {entry.Message}");

            if (entry.Exception is not null)
                ImGuiTextSafe.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), entry.Exception.ToString());
        }

        if (_autoScroll && _scrollToBottom)
        {
            ImGui.SetScrollHereY(1f);
            _scrollToBottom = false;
        }

        ImGui.EndChild();

        ImGui.Separator();

        bool inputAvailable = _ctx.LuauState != null;
        if (!inputAvailable)
            ImGui.BeginDisabled();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Send").X - ImGui.GetStyle().ItemSpacing.X * 2 - ImGui.GetStyle().FramePadding.X * 4);
        if (_refocusInput)
        {
            ImGui.SetKeyboardFocusHere();
            _refocusInput = false;
        }

        bool submitted = ImGui.InputText("##console_input", ref _input, 4096,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackCompletion,
            _completionCallback);
        ImGui.SameLine();
        bool sendClicked = ImGui.Button("Send");

        ImGuiTextSafe.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), _completionHint);

        if (!inputAvailable)
            ImGui.EndDisabled();

        if ((submitted || sendClicked) && inputAvailable && !string.IsNullOrWhiteSpace(_input))
        {
            string input = _input.Trim();
            s_luauLogger.LogInformation("> {Source}", input);
            if (_ctx.LuauState!.TryExecute(input, out string output))
            {
                s_luauLogger.LogInformation("{Output}", output);
                _completion.ObserveSuccessfulSubmission(input);
            }
            else
            {
                s_luauLogger.LogError("{Output}", output);
            }

            _input = string.Empty;
            _scrollToBottom = true;
            _refocusInput = true;
        }
    }

    private int OnComplete(ImGuiInputTextCallbackData* rawData)
    {
        ImGuiInputTextCallbackDataPtr data = new(rawData);
        int cursor = data.CursorPos;
        string source = System.Text.Encoding.UTF8.GetString(data.Buf, cursor);
        CompletionEdit edit = _completion.Complete(source, source.Length);

        if (edit.Replacement != source[edit.Start..])
        {
            // ImGui cursor/edit positions are UTF-8 byte offsets, while the completion engine
            // deliberately works in ordinary C# character offsets.
            int editStartBytes = System.Text.Encoding.UTF8.GetByteCount(source.AsSpan(0, edit.Start));
            int editLengthBytes = System.Text.Encoding.UTF8.GetByteCount(source.AsSpan(edit.Start, edit.Length));
            data.DeleteChars(editStartBytes, editLengthBytes);
            data.InsertChars(editStartBytes, edit.Replacement);
        }

        _completionHint = edit.Matches.Count switch
        {
            0 => "No completion",
            1 => edit.Matches[0],
            _ => string.Join("  ", edit.Matches.Take(8)) + (edit.Matches.Count > 8 ? "  …" : string.Empty)
        };
        return 0;
    }
}
