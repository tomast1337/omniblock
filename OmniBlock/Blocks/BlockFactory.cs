using OmniBlock.Blocks.Behaviors;
using OmniBlock.Blocks.Materials;
using OmniBlock.Worlds.Lod;

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
            TerrainLod = CompileTerrainLod(def.TerrainLod),
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

    private static BlockTerrainLodDescriptor? CompileTerrainLod(BlockTerrainLodDefinition? definition)
    {
        if (definition is null) return null;
        if (!Enum.TryParse<TerrainLodGeometryClass>(definition.Geometry, true, out var geometry) ||
            !Enum.IsDefined(geometry))
        {
            throw new ArgumentException(
                $"Unknown terrain LOD geometry '{definition.Geometry}'.",
                nameof(definition));
        }
        if (geometry == TerrainLodGeometryClass.Air)
        {
            throw new ArgumentException(
                "A registered block cannot use the terrain LOD air geometry.",
                nameof(definition));
        }

        var canOcclude = geometry is TerrainLodGeometryClass.Opaque or
            TerrainLodGeometryClass.ConservativeCube;
        var occludesFaces = definition.OccludesFaces ?? canOcclude;
        if (occludesFaces && !canOcclude)
        {
            throw new ArgumentException(
                $"Terrain LOD geometry '{geometry}' cannot conservatively occlude neighboring faces.",
                nameof(definition));
        }
        var maxSampleSize = definition.MaxSampleSize ??
            (geometry == TerrainLodGeometryClass.CrossedQuad ? 1 : int.MaxValue);
        if (maxSampleSize != int.MaxValue &&
            (maxSampleSize is < 1 or > 64 || !System.Numerics.BitOperations.IsPow2((uint)maxSampleSize)))
            throw new ArgumentException(
                $"Terrain LOD MaxSampleSize '{maxSampleSize}' must be a power of two between 1 and 64.",
                nameof(definition));
        var metadataHeightLevels = definition.MetadataHeightLevels ?? 0;
        if (metadataHeightLevels != 0 &&
            (geometry != TerrainLodGeometryClass.SurfaceLayer ||
             metadataHeightLevels is < 2 or > 16 ||
             !System.Numerics.BitOperations.IsPow2((uint)metadataHeightLevels)))
            throw new ArgumentException(
                $"Terrain LOD MetadataHeightLevels '{metadataHeightLevels}' requires a surface layer " +
                "and a power of two between 2 and 16.", nameof(definition));
        return new BlockTerrainLodDescriptor(
            geometry, occludesFaces, maxSampleSize, metadataHeightLevels);
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
