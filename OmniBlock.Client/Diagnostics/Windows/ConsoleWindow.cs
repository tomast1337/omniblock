using System.Numerics;
using System.Text;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Logging;

namespace OmniBlock.Client.Diagnostics.Windows;

internal sealed unsafe class ConsoleWindow : DebugWindow
{
    private static readonly ILogger s_luauLogger = Log.Instance.For("Luau");

    private static readonly Dictionary<LogLevel, Vector4> s_levelColors = new()
    {
        [LogLevel.Trace] = new Vector4(0.5f, 0.5f, 0.5f, 1f),
        [LogLevel.Debug] = new Vector4(0.7f, 0.7f, 0.7f, 1f),
        [LogLevel.Information] = new Vector4(1f, 1f, 1f, 1f),
        [LogLevel.Warning] = new Vector4(1f, 0.8f, 0f, 1f),
        [LogLevel.Error] = new Vector4(1f, 0.35f, 0.35f, 1f),
        [LogLevel.Critical] = new Vector4(1f, 0f, 0.5f, 1f)
    };

    private static readonly Dictionary<LogLevel, string> s_levelTags = new()
    {
        [LogLevel.Trace] = "TRC",
        [LogLevel.Debug] = "DBG",
        [LogLevel.Information] = "INF",
        [LogLevel.Warning] = "WRN",
        [LogLevel.Error] = "ERR",
        [LogLevel.Critical] = "CRT"
    };

    private readonly LuauCompletion _completion = new();
    private readonly ImGuiInputTextCallback _completionCallback;
    private readonly DebugWindowContext _ctx;
    private readonly LuauCommandHistory _history;
    private bool _autoScroll = true;
    private string _completionHint = "Tab completes Luau names";
    private string _input = string.Empty;
    private int _prevEntryCount;
    private bool _refocusInput;
    private bool _scrollToBottom;
    private int _selectedEntry = -1;

    public ConsoleWindow(DebugWindowContext ctx)
    {
        _ctx = ctx;
        _history = new LuauCommandHistory(Path.Combine(ctx.GameDataDir, ".luau_history"));
        _completionCallback = OnComplete;
    }

    public override string Title => "Console";
    public override DebugDock DefaultDock => DebugDock.Bottom;

    protected override void OnDraw()
    {
        ImGui.Checkbox("Auto-scroll", ref _autoScroll);
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            Log.Instance.ClearLog();
            _selectedEntry = -1;
        }

        ImGui.SameLine();
        var hasSelection = _selectedEntry >= 0;
        if (!hasSelection)
            ImGui.BeginDisabled();
        if (ImGui.Button("Copy selected") && hasSelection)
        {
            var currentEntries = Log.Instance.GetRecentEntries();
            if (_selectedEntry < currentEntries.Length)
                Display.SetClipboardString(FormatEntry(currentEntries[_selectedEntry]));
        }

        if (!hasSelection)
            ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Copy all"))
            Display.SetClipboardString(FormatEntries(Log.Instance.GetRecentEntries()));

        ImGui.Separator();

        var inputHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        ImGui.BeginChild("##log_scroll", new Vector2(0f, -inputHeight), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

        var entries = Log.Instance.GetRecentEntries();

        if (entries.Length != _prevEntryCount)
        {
            _prevEntryCount = entries.Length;
            _scrollToBottom = true;
        }

        if (_selectedEntry >= entries.Length)
            _selectedEntry = -1;

        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var color = s_levelColors.TryGetValue(entry.Level, out var c) ? c : Vector4.One;
            var text = FormatEntry(entry);

            ImGui.PushStyleColor(ImGuiCol.Text, color);
            if (ImGui.Selectable($"{text}##log_{index}", _selectedEntry == index,
                    ImGuiSelectableFlags.AllowDoubleClick))
            {
                _selectedEntry = index;
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    Display.SetClipboardString(text);
            }

            ImGui.PopStyleColor();
        }

        if (_autoScroll && _scrollToBottom)
        {
            ImGui.SetScrollHereY(1f);
            _scrollToBottom = false;
        }

        ImGui.EndChild();

        ImGui.Separator();

        var inputAvailable = _ctx.LuauState != null;
        if (!inputAvailable)
            ImGui.BeginDisabled();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Send").X - ImGui.GetStyle().ItemSpacing.X * 2 - ImGui.GetStyle().FramePadding.X * 4);
        if (_refocusInput)
        {
            ImGui.SetKeyboardFocusHere();
            _refocusInput = false;
        }

        var submitted = ImGui.InputText("##console_input", ref _input, 4096,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackCompletion |
            ImGuiInputTextFlags.CallbackHistory,
            _completionCallback);
        ImGui.SameLine();
        var sendClicked = ImGui.Button("Send");

        ImGuiTextSafe.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), _completionHint);

        if (!inputAvailable)
            ImGui.EndDisabled();

        if ((submitted || sendClicked) && inputAvailable && !string.IsNullOrWhiteSpace(_input))
        {
            var input = _input.Trim();
            _history.Add(input);
            s_luauLogger.LogInformation("> {Source}", input);
            if (_ctx.LuauState!.TryExecute(input, out var output))
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

    private static string FormatEntry(LogEntry entry)
    {
        var tag = s_levelTags.TryGetValue(entry.Level, out var value) ? value : "???";
        var line = $"[{entry.Timestamp:HH:mm:ss}] [{tag}] {entry.Category}: {entry.Message}";
        return entry.Exception is null ? line : line + Environment.NewLine + entry.Exception;
    }

    private static string FormatEntries(IEnumerable<LogEntry> entries)
    {
        StringBuilder output = new();
        foreach (var entry in entries)
        {
            if (output.Length > 0)
                output.AppendLine();
            output.Append(FormatEntry(entry));
        }

        return output.ToString();
    }

    private int OnComplete(ImGuiInputTextCallbackData* rawData)
    {
        ImGuiInputTextCallbackDataPtr data = new(rawData);
        if (data.EventFlag == ImGuiInputTextFlags.CallbackHistory)
        {
            var current = Encoding.UTF8.GetString(data.Buf, data.BufTextLen);
            var replacement = _history.Navigate(current, data.EventKey == ImGuiKey.UpArrow);
            if (replacement != current)
            {
                data.DeleteChars(0, data.BufTextLen);
                data.InsertChars(0, replacement);
            }

            return 0;
        }

        var cursor = data.CursorPos;
        var source = Encoding.UTF8.GetString(data.Buf, cursor);
        var edit = _completion.Complete(source, source.Length);

        if (edit.Replacement != source[edit.Start..])
        {
            // ImGui cursor/edit positions are UTF-8 byte offsets, while the completion engine
            // deliberately works in ordinary C# character offsets.
            var editStartBytes = Encoding.UTF8.GetByteCount(source.AsSpan(0, edit.Start));
            var editLengthBytes = Encoding.UTF8.GetByteCount(source.AsSpan(edit.Start, edit.Length));
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
