using OmniBlock.Blocks.Behaviors;
using OmniBlock.Blocks.Materials;

namespace OmniBlock.Blocks;

internal static class BlockFactory
{
    public static Block Create(BlockDefinition def, in BlockBuildContext context)
    {
        var material = context.Behaviors.ResolveMaterial(ResourceLocation.Parse(def.Material));
        // An unset TextureId keeps the implicit default the int field used to have.
        var textureId = string.IsNullOrEmpty(def.TextureId) ? 0 : context.Behaviors.ResolveTerrainTexture(def.TextureId);
        Block block = new(def.ProtocolId, textureId, material, context.ResolveSoundGroup("omniblock:powder"));
        BlockDraft draft = new(block)
        {
            TextureId = textureId,
            IsOpaque = !def.NonOpaque,
            Luminance = def.Luminance,
            TickRandomly = def.TickRandomly,
            IgnoreMetaUpdates = def.IgnoreMetaUpdates,
            EnableStats = def.TrackStatistics,
            TopVariance = def.TopVariance,
            BottomVariance = def.BottomVariance,
            SideVariance = def.SideVariance,
            BurnChance = def.BurnChance,
            SpreadChance = def.SpreadChance,
            RenderType = Enum.Parse<BlockRendererType>(def.RenderType, true),
            RenderLayer = def.RenderLayer,
            TickRate = def.TickRate,
            HasCollisionBox = !def.NoCollision,
            PreservesMetaOnDrop = def.PreservesMetaOnDrop,
            BlockName = def.TranslationKey ?? def.Name
        };

        block.SetHardness(def.Hardness);
        block.SetResistance(def.Resistance);

        if (def.Opacity >= 0) draft.Opacity = def.Opacity;
        else draft.Opacity = draft.IsOpaque ? 255 : 0;
        if (def.SoundGroup is { } sg) draft.SoundGroup = context.ResolveSoundGroup(ResourceLocation.Parse(sg));
        if (def.FaceTextures is { } faces)
        {
            foreach (var (sideName, faceTextureId) in faces)
                block.SetFaceTexture(Enum.Parse<Side>(sideName, true), context.Behaviors.ResolveTerrainTexture(faceTextureId));
        }

        block.SetSlipperiness(def.Slipperiness);
        if (def.NotFullCube) block.SetNotFullCube();
        if (def.BoundingBox is { } box) block.SetBoundingBox(box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ);

        if (def.PistonBehavior is { } pistonBehavior) block.SetPistonBehavior(Enum.Parse<PistonBehavior>(pistonBehavior, true));

        if (def.DropCount is { } dropCount) draft.DropCount = dropCount;
        if (def.BlockAlias is { Length: > 0 } aliases) block.SetBlockAlias(aliases);

        draft.Apply();

        return block;
    }

    internal static void AttachBehaviors(
        Block block,
        BlockDefinition def,
        IBlockBehaviorProviderRegistry behaviorProviders,
        BlockBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(behaviorProviders);
        BlockDraft draft = new(block);

        foreach (var entry in def.Behaviors)
        {
            var type = entry.GetProperty("Type").GetString()
                       ?? throw new ArgumentException("Behavior entry is missing its 'Type' property.");
            var behavior = behaviorProviders.Build(ResourceLocation.Parse(type), entry, context.Behaviors);

            foreach (var slotJson in entry.GetProperty("Slots").EnumerateArray())
            {
                var slot = slotJson.GetString()
                           ?? throw new ArgumentException($"Behavior entry of type '{type}' has a null entry in 'Slots'.");

                switch (slot)
                {
                    case "Ticker":
                        draft.Ticker = (IBlockTicker)behavior;
                        break;
                    case "Physics":
                        draft.Physics = (IBlockPhysics)behavior;
                        break;
                    case "Lifecycle":
                        draft.Lifecycle = (IBlockLifecycle)behavior;
                        break;
                    case "Visuals":
                        draft.Visuals = (IBlockVisuals)behavior;
                        break;
                    case "Interactable":
                        draft.Interactable = (IBlockInteractable)behavior;
                        break;
                    case "Redstone":
                        draft.Redstone = (IRedstoneComponent)behavior;
                        break;
                    default:
                        throw new ArgumentException($"Unknown behavior slot '{slot}'.");
                }
            }
        }

        draft.Apply();

        if (def.LootTable is { } loot)
        {
            var entries = loot.Entries
                .Select(e =>
                {
                    var itemId = context.ResolveLootItemOrBlockId(ResourceLocation.Parse(e.ItemName));
                    return new LootEntry(() => itemId, e.Weight);
                })
                .ToArray();
            block.SetLootTable(new LootTable(entries), loot.MinCount, loot.MaxCount, loot.Meta);
        }

        if (def.TileEntity is { } tileEntity) block.SetHasTileEntity(context.ResolveBlockEntityFactory(ResourceLocation.Parse(tileEntity)));
    }
}
