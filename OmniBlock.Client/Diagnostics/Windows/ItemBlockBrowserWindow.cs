using System.Numerics;
using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Items;
using OmniBlock.Registries;
using OmniBlock.Util.Maths;
using Hexa.NET.ImGui;

namespace OmniBlock.Client.Diagnostics.Windows;

/// <summary>
/// A quick NEI-style browser over every registered item and block: search, inspect the basics,
/// give one to the local player. Recipes and any kind of editing are out of scope for now — this
/// is read-only.
/// </summary>
internal sealed class ItemBlockBrowserWindow : DebugWindow
{
    private readonly record struct BrowserEntry(
        string GiveName, string DisplayName, int ProtocolId, bool IsBlock,
        uint AtlasTexture, Vector2 Uv0, Vector2 Uv1);

    private enum Filter { All, Items, Blocks }

    public override string Title => "Item & Block Browser";

    private readonly DebugWindowContext _ctx;
    private readonly List<BrowserEntry> _entries = [];
    private string _search = string.Empty;
    private Filter _filter = Filter.All;
    private int _giveCount = 1;

    public ItemBlockBrowserWindow(DebugWindowContext ctx)
    {
        _ctx = ctx;

        uint itemsTexture = (uint)ctx.TextureManager.GetTextureId("/gui/items.png").Id;
        uint terrainTexture = (uint)ctx.TextureManager.GetTextureId("/terrain.png").Id;

        foreach (ItemDefinition def in DefaultRegistries.Items)
        {
            if (DefaultRegistries.Items.GetKey(def) is not { } location) continue;
            if (!ctx.Content.Items.TryGetByProtocolId(def.ProtocolId, out Item? item) || item is null) continue;

            _entries.Add(MakeEntry(location.Path, item.GetStatName(), def.ProtocolId, false, itemsTexture, item.GetTextureId(0)));
        }

        for (int id = 0; id < BlockRegistry.ProtocolIdCapacity; id++)
        {
            if (!BlockRegistry.TryGetByProtocolId(id, out Block? block)) continue;
            if (BlockRegistry.TryGetName(id) is not { } name) continue;

            _entries.Add(MakeEntry(name, block.TranslateBlockName(), id, true, terrainTexture, block.GetTexture(2.ToSide())));
        }

        _entries.Sort((a, b) => string.Compare(a.GiveName, b.GiveName, StringComparison.OrdinalIgnoreCase));
    }

    private static BrowserEntry MakeEntry(string giveName, string displayName, int protocolId, bool isBlock, uint atlasTexture, int textureIndex)
    {
        Vector2 uv0 = new(textureIndex % 16 / 16f, textureIndex / 16 / 16f);
        Vector2 uv1 = uv0 + new Vector2(1 / 16f, 1 / 16f);

        return new BrowserEntry(giveName, displayName, protocolId, isBlock, atlasTexture, uv0, uv1);
    }

    protected override void OnDraw()
    {
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputText("##Search", ref _search, 256);

        if (ImGui.RadioButton("All", _filter == Filter.All)) _filter = Filter.All;
        ImGui.SameLine();
        if (ImGui.RadioButton("Items", _filter == Filter.Items)) _filter = Filter.Items;
        ImGui.SameLine();
        if (ImGui.RadioButton("Blocks", _filter == Filter.Blocks)) _filter = Filter.Blocks;

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Give count", ref _giveCount);
        _giveCount = Math.Clamp(_giveCount, 1, 64);

        ImGui.Separator();

        bool canGive = _ctx.Player is not null;
        ImGuiTextSafe.TextDisabled(canGive
            ? "Click a tile to give yourself some. Hover for details."
            : "Not in a world — hover to inspect, click-to-give is unavailable.");

        if (ImGui.BeginChild("ItemBlockBrowserScrollview", Vector2.Zero))
        {
            const float iconSize = 28f;
            const float cellPadding = 16f;
            const float cellSize = iconSize + cellPadding;

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(cellPadding, cellPadding));

            int columns = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / cellSize));
            int column = 0;

            foreach (BrowserEntry entry in _entries)
            {
                if (_filter == Filter.Items && entry.IsBlock) continue;
                if (_filter == Filter.Blocks && !entry.IsBlock) continue;

                if (!string.IsNullOrWhiteSpace(_search) &&
                    !entry.GiveName.Contains(_search, StringComparison.OrdinalIgnoreCase) &&
                    !entry.DisplayName.Contains(_search, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (column > 0) ImGui.SameLine();
                DrawCell(entry, canGive, iconSize);
                column = (column + 1) % columns;
            }

            ImGui.PopStyleVar();
        }

        // Unconditional: BeginChild's return value is only whether the child is visible (scrolled
        // out, collapsed, zero size), not whether a matching EndChild is owed — that is owed every
        // time BeginChild is called, visible or not. Nesting this inside the if above meant a
        // window in the invisible state left ImGui's window stack unbalanced, and the very next
        // End() elsewhere asserted "Must call EndChild() and not End()!".
        ImGui.EndChild();
    }

    private void DrawCell(BrowserEntry entry, bool canGive, float iconSize)
    {
        string id = (entry.IsBlock ? "block_" : "item_") + entry.GiveName;

        if (!canGive) ImGui.BeginDisabled();

        bool clicked;
        unsafe
        {
            clicked = ImGui.ImageButton(id, new ImTextureRef(null, new ImTextureID((ulong)entry.AtlasTexture)), new Vector2(iconSize, iconSize), entry.Uv0, entry.Uv1);
        }

        if (!canGive) ImGui.EndDisabled();

        if (clicked && canGive)
        {
            _ctx.Player?.SendChatMessage($"/give {entry.GiveName} {_giveCount}");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGuiTextSafe.Text(entry.DisplayName);
            ImGuiTextSafe.TextDisabled(entry.GiveName);
            ImGuiTextSafe.TextDisabled($"id {entry.ProtocolId} · {(entry.IsBlock ? "block" : "item")}");
            ImGui.EndTooltip();
        }
    }
}
