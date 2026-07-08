using System.Text.Json;
using BetaSharp.Blocks.Behaviors;
using BetaSharp.Blocks.Entities;
using BetaSharp.Blocks.Materials;

// ItemLookup lives in the root BetaSharp namespace; BetaSharp.Blocks does not see it implicitly.
using ItemLookup = BetaSharp.ItemLookup;

namespace BetaSharp.Blocks;

internal static class BlockFactory
{
    public static Block Create(BlockDefinition def)
    {
        Material material = MaterialRegistry.Get(def.Material);
        Block block = new(def.ProtocolId, def.TextureId, material);

        // def.Hardness == -1 already reproduces SetUnbreakable() exactly (it's sugar for
        // SetHardness(-1)) — no separate call needed.
        block.SetHardness(def.Hardness).SetResistance(def.Resistance);
        // Must run before the explicit Opacity override below, so an explicit value
        // (rare, but distinct from "just non-opaque") still wins if both are set.
        if (def.NonOpaque) block.SetNonOpaque();
        if (def.Luminance > 0) block.SetLuminance(def.Luminance);
        if (def.Opacity >= 0) block.setOpacity(def.Opacity);
        if (def.TickRandomly) block.SetTickRandomly(true);
        if (def.IgnoreMetaUpdates) block.IgnoreMetaUpdates();
        if (!def.TrackStatistics) block.DisableStats();
        if (def.SoundGroup is { } sg) block.setSoundGroup(SoundGroupRegistry.Get(sg));
        if (def.FaceTextures is { } faces)
        {
            foreach ((string sideName, int textureId) in faces)
            {
                block.SetFaceTexture(Enum.Parse<Side>(sideName, true), textureId);
            }
        }

        block.SetVariance(def.TopVariance, def.BottomVariance, def.SideVariance);
        block.SetRenderType(Enum.Parse<BlockRendererType>(def.RenderType, true));
        block.SetRenderLayer(def.RenderLayer);
        block.SetTickRate(def.TickRate);
        block.SetSlipperiness(def.Slipperiness);
        if (def.NotFullCube) block.SetNotFullCube();
        if (def.NoCollision) block.SetNoCollision();
        if (def.BoundingBox is { } box)
        {
            block.SetBoundingBox(box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ);
        }

        if (def.PistonBehavior is { } pistonBehavior)
        {
            block.SetPistonBehavior(Enum.Parse<PistonBehavior>(pistonBehavior, true));
        }

        if (def.DropCount is { } dropCount) block.SetDropCount(dropCount);
        if (def.PreservesMetaOnDrop) block.preserveMetaOnDrop();
        if (def.BlockAlias is { Length: > 0 } aliases) block.SetBlockAlias(aliases);

        block.SetBlockName(def.TranslationKey ?? def.Name);

        return block;
    }

    public static void AttachBehaviors(Block block, BlockDefinition def)
    {
        // Behaviors is keyed by SLOT ("Ticker", "Physics", ...), not by behavior type — a class
        // implementing several capability interfaces (e.g. PlantSurvivalBehavior implements both
        // IBlockTicker and IBlockPhysics) is common, and a given block may only want it wired into
        // SOME of those slots (Ladder uses WallMountBehavior for Physics+Lifecycle only, never
        // Ticker). Building one instance per named slot and wiring it directly — rather than
        // probing one shared instance against every interface it happens to implement — is what
        // actually reproduces the original fluent chains; probing over-attaches whenever a second
        // behavior in the same block implements an interface the first one also implements.
        foreach ((string slot, JsonElement json) in def.Behaviors)
        {
            string type = json.GetProperty("Type").GetString()
                ?? throw new ArgumentException($"Behavior slot '{slot}' is missing its 'Type' property.");
            object behavior = BehaviorRegistry.Build(type, json);

            switch (slot)
            {
                case "Ticker":
                    block.SetTicker((IBlockTicker)behavior);
                    break;
                case "Physics":
                    block.SetPhysics((IBlockPhysics)behavior);
                    break;
                case "Lifecycle":
                    block.SetLifecycle((IBlockLifecycle)behavior);
                    break;
                case "Visuals":
                    block.SetVisuals((IBlockVisuals)behavior);
                    break;
                case "Interactable":
                    block.SetInteractable((IBlockInteractable)behavior);
                    break;
                case "Redstone":
                    block.SetRedstone((IRedstoneComponent)behavior);
                    break;
                default:
                    throw new ArgumentException($"Unknown behavior slot '{slot}'.");
            }
        }

        if (def.LootTable is { } loot)
        {
            // Loot entries commonly name another BLOCK (Stone drops "cobblestone", GrassBlock
            // drops "dirt") as well as standalone items — Item.ByName only covers the JSON item
            // registry (256+), not block-derived item ids (0-255). ItemLookup is the one table
            // that already resolves both, so both directions of this round-trip (the dumper's
            // reverse lookup and this forward one) go through it rather than a second,
            // block-unaware mechanism.
            LootEntry[] entries = loot.Entries
                .Select(e => new LootEntry(() => ResolveItemOrBlockId(e.ItemName), e.Weight))
                .ToArray();
            block.SetLootTable(new LootTable(entries), loot.MinCount, loot.MaxCount, loot.Meta);
        }

        if (def.TileEntity is { } tileEntity)
        {
            block.SetHasTileEntity(BlockEntityFactoryRegistry.Get(tileEntity));
        }
    }

    private static int ResolveItemOrBlockId(string name) =>
        ItemLookup.TryGetItemId(name, out int id) ? id : throw new ArgumentException($"Unknown item or block: '{name}'");
}
