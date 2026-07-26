using System.Text.Json;
using BetaSharp.Blocks.Behaviors;
using BetaSharp.Blocks.Entities;
using BetaSharp.Blocks.Materials;

namespace BetaSharp.Blocks;

internal static class BlockFactory
{
    public static Block Create(BlockDefinition def)
    {
        Material material = MaterialRegistry.Get(def.Material);
        Block block = new(def.ProtocolId, def.TextureId, material);

        block.SetHardness(def.Hardness);
        block.SetResistance(def.Resistance);

        block.IsOpaque = !def.NonOpaque;
        block.Luminance = def.Luminance;
        if (def.Opacity >= 0) block.Opacity = def.Opacity;
        block.TickRandomly = def.TickRandomly;
        block.IgnoreMetaUpdates = def.IgnoreMetaUpdates;
        block.EnableStats = def.TrackStatistics;
        if (def.SoundGroup is { } sg) block.SoundGroup = SoundGroupRegistry.Get(sg);
        if (def.FaceTextures is { } faces)
        {
            foreach ((string sideName, int textureId) in faces)
            {
                block.SetFaceTexture(Enum.Parse<Side>(sideName, true), textureId);
            }
        }

        block.TopVariance = def.TopVariance;
        block.BottomVariance = def.BottomVariance;
        block.SideVariance = def.SideVariance;
        block.BurnChance = def.BurnChance;
        block.SpreadChance = def.SpreadChance;
        block.RenderType = Enum.Parse<BlockRendererType>(def.RenderType, true);
        block.RenderLayer = def.RenderLayer;
        block.TickRate = def.TickRate;
        block.SetSlipperiness(def.Slipperiness);
        if (def.NotFullCube) block.SetNotFullCube();
        if (def.NoCollision) block.HasCollisionBox = false;
        if (def.BoundingBox is { } box)
        {
            block.SetBoundingBox(box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ);
        }

        if (def.PistonBehavior is { } pistonBehavior)
        {
            block.SetPistonBehavior(Enum.Parse<PistonBehavior>(pistonBehavior, true));
        }

        if (def.DropCount is { } dropCount) block.DropCount = dropCount;
        block.PreservesMetaOnDrop = def.PreservesMetaOnDrop;
        if (def.BlockAlias is { Length: > 0 } aliases) block.SetBlockAlias(aliases);

        block.BlockName = def.TranslationKey ?? def.Name;

        return block;
    }

    public static void AttachBehaviors(Block block, BlockDefinition def)
    {
        foreach (JsonElement entry in def.Behaviors)
        {
            string type = entry.GetProperty("Type").GetString()
                ?? throw new ArgumentException("Behavior entry is missing its 'Type' property.");
            object behavior = BehaviorRegistry.Build(type, entry);

            foreach (JsonElement slotJson in entry.GetProperty("Slots").EnumerateArray())
            {
                string slot = slotJson.GetString()
                    ?? throw new ArgumentException($"Behavior entry of type '{type}' has a null entry in 'Slots'.");

                switch (slot)
                {
                    case "Ticker":
                        block.Ticker = (IBlockTicker)behavior;
                        break;
                    case "Physics":
                        block.Physics = (IBlockPhysics)behavior;
                        break;
                    case "Lifecycle":
                        block.Lifecycle = (IBlockLifecycle)behavior;
                        break;
                    case "Visuals":
                        block.Visuals = (IBlockVisuals)behavior;
                        break;
                    case "Interactable":
                        block.Interactable = (IBlockInteractable)behavior;
                        break;
                    case "Redstone":
                        block.Redstone = (IRedstoneComponent)behavior;
                        break;
                    default:
                        throw new ArgumentException($"Unknown behavior slot '{slot}'.");
                }
            }
        }

        if (def.LootTable is { } loot)
        {
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
