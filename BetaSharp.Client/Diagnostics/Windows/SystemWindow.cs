using Hexa.NET.ImGui;

namespace BetaSharp.Client.Diagnostics.Windows;

internal sealed class SystemWindow(DebugWindowContext ctx) : DebugWindow
{
    public override string Title => "System";
    public override DebugDock DefaultDock => DebugDock.Right;

    protected override void OnDraw()
    {
        DebugSystemSnapshot s = ctx.DebugSystemSnapshot;

        ImGuiTextSafe.Text("Build: " + BetaSharp.Version);
        ImGuiTextSafe.Text($"OS:     {s.OsDescription}");
        ImGuiTextSafe.Text($"Runtime:{s.DotNetRuntime}");

        if (ImGui.CollapsingHeader("GPU", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGuiTextSafe.Text($"Name:       {s.GpuName}");
            ImGuiTextSafe.Text($"VRAM:       {s.GpuVram}");
            ImGuiTextSafe.Text($"OpenGL:     {s.OpenGlVersion}");
            ImGuiTextSafe.Text($"GLSL:       {s.GlslVersion}");
            ImGuiTextSafe.Text($"Driver:     {s.DriverVersion}");
        }

        if (ImGui.CollapsingHeader("CPU", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGuiTextSafe.Text($"Name:  {s.CpuName}");
            ImGuiTextSafe.Text($"Cores: {s.CpuCoreCount}");
        }
    }
}
