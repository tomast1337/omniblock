using Hexa.NET.ImGui;
using OmniBlock.Blocks;
using OmniBlock.Util.Hit;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Diagnostics.Windows;

internal sealed class LocalPlayerInfoWindow(DebugWindowContext ctx) : DebugWindow
{
    private static readonly string[] s_cardinalDirections = ["south", "west", "north", "east"];
    private static readonly string[] s_towards = ["positive Z", "negative X", "negative Z", "positive X"];

    public override string Title => "Local Player";

    protected override void OnDraw()
    {
        if (ctx.Player == null || ctx.World == null)
        {
            ImGuiTextSafe.TextDisabled("No player in world.");
            return;
        }

        if (ImGui.CollapsingHeader("Position", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawPositionSection();
        }

        if (ImGui.CollapsingHeader("Targeted Block", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawTargetedBlockSection();
        }
    }

    private void DrawPositionSection()
    {
        var player = ctx.Player;
        var world = ctx.World;
        if (player is null || world is null) return;
        var x = Math.Floor(player.X * 1000) / 1000;
        var y = Math.Floor(player.Y * 100000) / 100000;
        var z = Math.Floor(player.Z * 1000) / 1000;

        var bx = (int)Math.Floor(player.X);
        var by = (int)Math.Floor(player.Y);
        var bz = (int)Math.Floor(player.Z);

        var facingIndex = MathHelper.Floor(player.Yaw * 4.0F / 360.0F + 0.5D) & 3;
        var cardinal = facingIndex is >= 0 and < 4 ? s_cardinalDirections[facingIndex] : "N/A";
        var towards = facingIndex is >= 0 and < 4 ? s_towards[facingIndex] : "N/A";
        var vertical = player.Pitch <= -45f ? "up" : player.Pitch >= 45f ? "down" : "level";

        var yaw = player.Yaw % 360f;
        if (yaw >= 180f) yaw -= 360f;
        if (yaw < -180f) yaw += 360f;
        var pitch = player.Pitch;

        var biome = world.Dimension.BiomeSource.GetBiome(bx, bz).Name;
        var light = world.Lighting.GetLightLevel(bx, by, bz);

        ImGuiTextSafe.Text($"XYZ:    {x:F3} / {y:F5} / {z:F3}");
        ImGuiTextSafe.Text($"Block:  {bx} {by} {bz}");
        ImGuiTextSafe.Text($"Facing: {cardinal} {vertical} (towards {towards})");
        ImGuiTextSafe.Text($"Yaw / Pitch: {yaw:F1} / {pitch:F1}");
        ImGuiTextSafe.Text($"Biome:  {biome}");
        ImGuiTextSafe.Text($"Light:  {light}");
    }

    private void DrawTargetedBlockSection()
    {
        var world = ctx.World;
        if (world is null) return;
        if (ctx.ObjectMouseOver.Type != HitResultType.Tile)
        {
            ImGuiTextSafe.TextDisabled("Nothing targeted.");
            return;
        }

        var bx = ctx.ObjectMouseOver.BlockX;
        var by = ctx.ObjectMouseOver.BlockY;
        var bz = ctx.ObjectMouseOver.BlockZ;
        var id = world.Reader.GetBlockId(bx, by, bz);
        var meta = world.Reader.GetBlockMeta(bx, by, bz);
        var side = ctx.ObjectMouseOver.Side.ToSide();

        var name = "Unknown";
        if (id == 0)
        {
            name = "Air";
        }
        else if (id > 0 && world.Content.Blocks.TryGetByProtocolId(id, out var block))
        {
            var t = block.TranslateBlockName();
            name = !string.IsNullOrWhiteSpace(t) ? t : block.BlockName;
        }

        var sideName = side.ToString();

        GetAdjacentBlockForFaceLight(bx, by, bz, side, out var ax, out var ay, out var az);
        var faceLight = world.Lighting.GetLightLevel(ax, ay, az);

        ImGuiTextSafe.Text($"{name} ({id}:{meta})");
        ImGuiTextSafe.Text($"XYZ:  {bx} / {by} / {bz}");
        ImGuiTextSafe.Text($"Face: {sideName} (light {faceLight})");
    }

    private static void GetAdjacentBlockForFaceLight(int bx, int by, int bz, Side side, out int ax, out int ay, out int az)
    {
        switch (side)
        {
            case Side.Down:
                ax = bx;
                ay = by - 1;
                az = bz;
                break;
            case Side.Up:
                ax = bx;
                ay = by + 1;
                az = bz;
                break;
            case Side.North:
                ax = bx;
                ay = by;
                az = bz - 1;
                break;
            case Side.South:
                ax = bx;
                ay = by;
                az = bz + 1;
                break;
            case Side.West:
                ax = bx - 1;
                ay = by;
                az = bz;
                break;
            case Side.East:
                ax = bx + 1;
                ay = by;
                az = bz;
                break;
            default:
                ax = bx;
                ay = by;
                az = bz;
                break;
        }
    }
}
