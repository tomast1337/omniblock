using System.Numerics;
using Hexa.NET.ImGui;
using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Items;

namespace OmniBlock.Client.Diagnostics.Windows;

/// <summary>
///     A quick NEI-style browser over every registered item and block: search, inspect the basics,
///     give one to the local player. Recipes and any kind of editing are out of scope for now — this
///     is read-only.
/// </summary>
internal sealed class ItemBlockBrowserWindow : DebugWindow
{
    private readonly DebugWindowContext _ctx;
    private readonly List<BrowserEntry> _entries = [];
    private Filter _filter = Filter.All;
    private int _giveCount = 1;
    private int _metadata;
    private string _search = string.Empty;
    private BrowserEntry? _selected;

    public ItemBlockBrowserWindow(DebugWindowContext ctx)
    {
        _ctx = ctx;

        var itemsTexture = ctx.TextureManager.GetTextureId("/gui/items.png");
        var terrainTexture = ctx.TextureManager.GetTextureId("/terrain.png");

        foreach (var (location, protocolId) in ctx.Content.Manifest.ItemIds)
        {
            if (!ctx.Content.Items.TryGetByProtocolId(protocolId, out var item) || item is null) continue;

            _entries.Add(new BrowserEntry(location, item.GetStatName(), protocolId, false, itemsTexture, item, null));
        }

        foreach (var location in ctx.Content.Blocks.Keys)
        {
            var block = ctx.Content.Blocks.Get(location);
            if (!ctx.Content.Items.TryGetByProtocolId(block.Id, out var item) || item is null) continue;

            _entries.Add(new BrowserEntry(location, block.TranslateBlockName(), block.Id, true, terrainTexture, item, block));
        }

        _entries.Sort((a, b) => string.Compare(a.Key.ToString(), b.Key.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    public override string Title => "Item & Block Browser";

    protected override void OnDraw()
    {
        ImGui.SetNextItemWidth(Math.Max(180, ImGui.GetContentRegionAvail().X - 245));
        ImGui.InputTextWithHint("##catalog_search", "Search name, namespace, or protocol ID...", ref _search, 256);
        ImGui.SameLine();
        if (ImGui.Button("Clear")) _search = string.Empty;

        ImGui.TextUnformatted("Show:");
        ImGui.SameLine();
        if (ImGui.RadioButton("All", _filter == Filter.All)) _filter = Filter.All;
        ImGui.SameLine();
        if (ImGui.RadioButton("Items", _filter == Filter.Items)) _filter = Filter.Items;
        ImGui.SameLine();
        if (ImGui.RadioButton("Blocks", _filter == Filter.Blocks)) _filter = Filter.Blocks;

        ImGui.Separator();

        var canGive = _ctx.Player is not null;
        var visible = _entries.Where(IsVisible).ToList();
        ImGuiTextSafe.TextDisabled($"{visible.Count} of {_entries.Count} entries · click an icon to inspect");

        var inspectorWidth = Math.Clamp(ImGui.GetContentRegionAvail().X * 0.34f, 230f, 330f);
        if (ImGui.BeginChild("ItemBlockCatalog", new Vector2(-inspectorWidth - 8, 0)))
        {
            const float iconSize = 32f;
            const float cellPadding = 10f;
            const float cellSize = iconSize + cellPadding + 8f;

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(cellPadding, cellPadding + 8));

            var columns = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / cellSize));
            var column = 0;

            foreach (var entry in visible)
            {
                if (column > 0) ImGui.SameLine();
                DrawCell(entry, iconSize);
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
        ImGui.SameLine();

        if (ImGui.BeginChild("ItemBlockInspector", Vector2.Zero))
            DrawInspector(canGive);
        ImGui.EndChild();
    }

    private bool IsVisible(BrowserEntry entry)
    {
        if (_filter == Filter.Items && entry.IsBlock) return false;
        if (_filter == Filter.Blocks && !entry.IsBlock) return false;
        if (string.IsNullOrWhiteSpace(_search)) return true;

        var query = _search.Trim();
        return entry.Key.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
               entry.Key.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               entry.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               entry.ProtocolId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void DrawCell(BrowserEntry entry, float iconSize)
    {
        var id = (entry.IsBlock ? "block_" : "item_") + entry.Key;
        var textureIndex = entry.Block?.GetTexture(2.ToSide()) ?? entry.Item.GetTextureId(_metadata);
        Vector2 uv0 = new(textureIndex % 16 / 16f, textureIndex / 16 / 16f);
        var uv1 = uv0 + new Vector2(1 / 16f, 1 / 16f);

        bool clicked;
        var textureId = _ctx.GetImGuiTextureId(entry.AtlasTexture);
        unsafe
        {
            clicked = textureId != 0 && ImGui.ImageButton(id, new ImTextureRef(null, new ImTextureID(textureId)), new Vector2(iconSize, iconSize), uv0, uv1);
        }

        if (clicked)
        {
            _selected = entry;
            _metadata = 0;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGuiTextSafe.Text(entry.DisplayName);
            ImGuiTextSafe.TextDisabled(entry.Key.ToString());
            ImGuiTextSafe.TextDisabled($"id {entry.ProtocolId} · {(entry.IsBlock ? "block" : "item")}");
            ImGui.EndTooltip();
        }
    }

    private void DrawInspector(bool canGive)
    {
        if (_selected is not { } entry)
        {
            ImGuiTextSafe.TextDisabled("Select an entry to inspect it.");
            return;
        }

        var textureIndex = entry.Block?.GetTexture(2.ToSide()) ?? entry.Item.GetTextureId(_metadata);
        Vector2 uv0 = new(textureIndex % 16 / 16f, textureIndex / 16 / 16f);
        var uv1 = uv0 + new Vector2(1 / 16f, 1 / 16f);
        var textureId = _ctx.GetImGuiTextureId(entry.AtlasTexture);
        unsafe
        {
            if (textureId != 0)
                ImGui.Image(new ImTextureRef(null, new ImTextureID(textureId)), new Vector2(64), uv0, uv1);
        }

        ImGuiTextSafe.Text(entry.DisplayName);
        ImGuiTextSafe.TextDisabled(entry.Key.ToString());
        ImGui.Separator();
        Detail("Kind", entry.IsBlock ? "Block + item" : "Item");
        Detail("Protocol ID", entry.ProtocolId.ToString());
        Detail("Runtime type", entry.Item.GetType().Name);
        Detail("Max stack", entry.Item.GetMaxCount().ToString());
        Detail("Durability", entry.Item.GetMaxDamage().ToString());
        Detail("Subtypes", entry.Item.GetHasSubtypes() ? "yes" : "no");
        Detail("Behaviors", entry.Item.BehaviorCount.ToString());
        Detail("Frozen", entry.Item.IsFrozen ? "yes" : "no");

        if (entry.Block is { } block)
        {
            ImGui.Spacing();
            Detail("Block type", block.GetType().Name);
            Detail("Solid / fluid", $"{(block.Material.IsSolid ? "solid" : "non-solid")} / {(block.Material.IsFluid ? "fluid" : "dry")}");
            Detail("Hardness", block.Hardness.ToString("0.###"));
            Detail("Opacity", block.Opacity.ToString());
            Detail("Light", block.LightEmission.ToString());
            Detail("Random tick", block.TickRandomly ? "yes" : "no");
            Detail("Block entity", block.HasBlockEntity ? "yes" : "no");
        }

        ImGui.Separator();
        ImGui.SetNextItemWidth(90);
        ImGui.InputInt("Count", ref _giveCount);
        _giveCount = Math.Clamp(_giveCount, 1, entry.Item.GetMaxCount());
        ImGui.SetNextItemWidth(90);
        ImGui.InputInt("Metadata", ref _metadata);
        _metadata = Math.Clamp(_metadata, 0, 32767);

        if (!canGive) ImGui.BeginDisabled();
        if (ImGui.Button("Give to player"))
        {
            var itemArgument = _metadata == 0 ? entry.Key.ToString() : $"{entry.Key}:{_metadata}";
            _ctx.Player?.SendChatMessage($"/give {itemArgument} {_giveCount}");
        }

        if (!canGive) ImGui.EndDisabled();
        if (!canGive) ImGuiTextSafe.TextDisabled("Join a world to use /give.");
    }

    private static void Detail(string label, string value)
    {
        ImGuiTextSafe.TextDisabled(label);
        ImGui.SameLine(105);
        ImGuiTextSafe.Text(value);
    }

    private readonly record struct BrowserEntry(
        ResourceLocation Key,
        string DisplayName,
        int ProtocolId,
        bool IsBlock,
        TextureHandle AtlasTexture,
        Item Item,
        Block? Block);

    private enum Filter
    {
        All,
        Items,
        Blocks
    }
}
