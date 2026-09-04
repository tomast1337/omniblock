using Hexa.NET.ImGui;
using OmniBlock.Client.Sound;

namespace OmniBlock.Client.Diagnostics.Windows;

internal sealed class AudioDebugWindow(DebugWindowContext ctx) : DebugWindow
{
    public override string Title => "Audio";
    public override DebugDock DefaultDock => DebugDock.Right;

    protected override void OnDraw()
    {
        var sm = ctx.SoundManager;

        if (ImGui.CollapsingHeader("Channels", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawChannelsSection(sm);
        }

        if (ImGui.CollapsingHeader("Action Sounds", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawActionSoundsSection(sm);
        }

        if (ImGui.CollapsingHeader("Streaming", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawStreamingSection(sm);
        }

        if (ImGui.CollapsingHeader("Music", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawMusicSection(sm);
        }
    }

    private static void DrawChannelsSection(SoundManager sm) => ImGuiTextSafe.Text($"Active: {sm.ActiveChannelCount} / 32");

    private static void DrawActionSoundsSection(SoundManager sm)
    {
        ImGuiTextSafe.Text($"Unique names: {sm.LoadedSoundNameCount}");
        ImGuiTextSafe.Text($"Files loaded: {sm.LoadedSoundFileCount}");
    }

    private static void DrawStreamingSection(SoundManager sm)
    {
        ImGuiTextSafe.Text($"Files loaded: {sm.LoadedStreamingFileCount}");

        var status = sm.IsStreamingPlaying ? "Playing" : "Idle";
        ImGuiTextSafe.Text($"Status:       {status}");
        ImGuiTextSafe.Text($"Track:        {sm.CurrentStreamingName ?? "none"}");
    }

    private static void DrawMusicSection(SoundManager sm)
    {
        var activeCategory = sm.ActiveCategory != null ? sm.ActiveCategory.ToString() : "none";
        var musicStatus = sm.IsMusicPlaying ? "Playing" : "Idle";

        ImGuiTextSafe.Text($"Status:   {musicStatus}");
        ImGuiTextSafe.Text($"Track:    {sm.CurrentMusicName ?? "none"}");
        ImGuiTextSafe.Text($"Category: {activeCategory}");

        ImGui.Spacing();

        foreach (var (name, cat) in sm.MusicCategories)
        {
            ImGui.Separator();
            ImGuiTextSafe.Text($"[{name}]");
            ImGuiTextSafe.Text($"  Tracks:      {cat.Pool.LoadedSoundCount}");
            ImGuiTextSafe.Text($"  Delay range: {cat.MinDelayTicks} – {cat.MaxDelayTicks} ticks");
            ImGuiTextSafe.Text($"  Next in:     {cat.TicksBeforeNext} ticks");
        }
    }
}
