using OmniBlock.Blocks.Entities;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Rules;
using OmniBlock.Util.Hit;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks;

public class Block
{
    [ThreadStatic] private static Dictionary<Block, Box>? s_runtimeBoundingBoxes;
    private Func<BlockEntity>? _blockEntityFactory;
    private Box _boundingBox;
    private int _droppedItemMetaValue;
    private int?[]? _faceTextureIds;
    private bool _isFullCube = true;
    private LootTable? _lootTable;
    private int _maxDroppedCount = 1;
    private int _minDroppedCount = 1;
    private PistonBehavior? _pistonBehaviorOverride;
    private float _resistance;

    private Block(int id, Material material, BlockSoundGroup defaultSoundGroup)
    {
        EnableStats = true;
        SoundGroup = defaultSoundGroup;
        ParticleFallSpeedModifier = 1.0F;
        Slipperiness = 0.6F;
        Material = material;
        Id = id;
        SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
        Opacity = IsOpaque ? 255 : 0;
    }

    protected internal Block(int id, int textureId, Material material, BlockSoundGroup defaultSoundGroup)
        : this(id, material, defaultSoundGroup) =>
        TextureId = textureId;

    public int Id { get; }
    public Material Material { get; }

    public Box BoundingBox => s_runtimeBoundingBoxes is not null
                              && s_runtimeBoundingBoxes.TryGetValue(this, out var runtimeBounds)
        ? runtimeBounds
        : _boundingBox;

    public float Hardness { get; private set; }
    public float ParticleFallSpeedModifier { get; }
    public float Slipperiness { get; private set; }
    public BlockSoundGroup SoundGroup { get; private set; }
    public int TextureId { get; private set; }

    public static BlockSoundGroup SoundStoneFootstep => SoundGroupRegistry.Get("stone");

    public TextureVariance TopVariance { get; private set; } = TextureVariance.None;
    public TextureVariance BottomVariance { get; private set; } = TextureVariance.None;
    public TextureVariance SideVariance { get; private set; } = TextureVariance.None;

    public IBlockTicker? Ticker { get; private set; }

    public IBlockInteractable? Interactable { get; private set; }

    public IBlockVisuals? Visuals { get; private set; }

    public IBlockLifecycle? Lifecycle { get; private set; }

    public IBlockPhysics? Physics { get; private set; }

    public IRedstoneComponent? Redstone { get; private set; }

    public IReadOnlyList<string> GetBlockAlias { get; private set; } = [];

    public bool IsFrozen { get; private set; }

    public BlockRendererType RenderType { get; private set; } = BlockRendererType.Standard;

    public bool HasCollisionBox { get; private set; } = true;

    public byte BurnChance { get; private set; }
    public byte SpreadChance { get; private set; }

    public bool IsOpaque
    {
        get => Visuals?.IsOpaque(this, field) ?? field;
        private set
        {
            field = value;
            Opacity = value ? 255 : 0;
        }
    } = true;

    public int TickRate { get; private set; } = 10;

    public int RenderLayer { get; private set; }

    public string BlockName
    {
        get;
        private set => field = $"tile.{value}";
    } = "";

    public bool EnableStats { get; private set; }

    public PistonBehavior PistonBehavior => _pistonBehaviorOverride ?? Material.PistonBehavior;

    public bool IgnoreMetaUpdates { get; private set; }

    public bool TickRandomly { get; private set; }

    public int Opacity { get; private set; }

    public int LightEmission { get; private set; }

    public float Luminance
    {
        get => LightEmission / 15.0F;
        private set => LightEmission = (int)(15.0F * value);
    }

    public bool AllowsVision => !Material.BlocksVision;
    public bool HasBlockEntity => _blockEntityFactory is not null;

    public bool PreservesMetaOnDrop { get; private set; }

    public int DropCount
    {
        get => _minDroppedCount;
        private set
        {
            _minDroppedCount = value;
            _maxDroppedCount = value;
        }
    }

    public bool IsFullCube() => _isFullCube;

    protected internal void Init() => Lifecycle?.OnInit(this);

    protected internal void SetResistance(float resistance)
    {
        EnsureMutable();
        _resistance = resistance * 3.0F;
    }

    protected internal void SetFaceTexture(Side side, int textureId)
    {
        EnsureMutable();
        _faceTextureIds ??= new int?[6];
        _faceTextureIds[(int)side] = textureId;
    }

    protected internal void SetLootTable(LootTable table, int minCount = 1, int maxCount = -1, int meta = 0)
    {
        EnsureMutable();
        _lootTable = table;
        _minDroppedCount = minCount;
        _maxDroppedCount = maxCount < 0 ? minCount : maxCount;
        _droppedItemMetaValue = meta;
    }

    public (int primaryMeta, int backupItemId, int backupMeta) GetPickBlockItem(int blockMeta)
    {
        var defaultBackupId = _lootTable?.GetPrimaryItemId() ?? 0;
        var defaultBackupMeta = PreservesMetaOnDrop ? blockMeta : -1;
        return Lifecycle?.GetPickBlockItem(this, blockMeta, defaultBackupId, defaultBackupMeta) ?? (blockMeta, defaultBackupId, defaultBackupMeta);
    }

    protected internal void SetBlockAlias(params string[] aliases)
    {
        EnsureMutable();
        GetBlockAlias = Array.AsReadOnly([.. aliases]);
    }


    protected internal void SetNotFullCube()
    {
        EnsureMutable();
        _isFullCube = false;
    }

    protected internal void SetHardness(float hardness)
    {
        EnsureMutable();
        Hardness = hardness;
        if (_resistance < hardness * 5.0F) _resistance = hardness * 5.0F;
    }

    protected internal void SetBoundingBox(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        EnsureMutable();
        _boundingBox = new Box(minX, minY, minZ, maxX, maxY, maxZ);
    }

    internal void SetRuntimeBoundingBox(float minX, float minY, float minZ, float maxX, float maxY, float maxZ) => SetRuntimeBoundingBox(new Box(minX, minY, minZ, maxX, maxY, maxZ));

    internal void SetRuntimeBoundingBox(Box box) => (s_runtimeBoundingBoxes ??= [])[this] = box;

    public float GetLuminance(ILightProvider? lighting, int x, int y, int z)
    {
        float baseLuminance;
        if (lighting != null)
        {
            baseLuminance = lighting.GetNaturalBrightness(x, y, z, LightEmission);
        }
        else
        {
            var baseLum = LightEmission;
            baseLuminance = baseLum > 0 ? baseLum / 15.0f : 1.0f;
        }

        return Visuals?.GetLuminance(this, lighting!, x, y, z, baseLuminance) ?? baseLuminance;
    }

    /// <inheritdoc cref="ILightProvider.GetLightLevels" />
    /// <remarks>
    ///     The counterpart to <see cref="GetLuminance" />, stopping before the ramp. With no provider
    ///     there is no world to read, so a block that emits reads as its own light and one that does
    ///     not reads as fully lit — which is what the luminance path does with the same inputs.
    /// </remarks>
    public LightLevels GetLightLevels(ILightProvider? lighting, int x, int y, int z)
    {
        var emission = LightEmission;

        var baseLevels = lighting != null
            ? lighting.GetLightLevels(x, y, z, emission)
            : emission > 0
                ? LightLevels.Of(0, emission)
                : LightLevels.FullSky;

        return Visuals?.GetLightLevels(this, lighting!, x, y, z, baseLevels) ?? baseLevels;
    }

    public bool IsSideVisible(IBlockReader iBlockReader, int x, int y, int z, Side side)
    {
        var minX = BoundingBox.MinX;
        var minY = BoundingBox.MinY;
        var minZ = BoundingBox.MinZ;
        var maxX = BoundingBox.MaxX;
        var maxY = BoundingBox.MaxY;
        var maxZ = BoundingBox.MaxZ;

        var baseVisibility = !side.IsValidSide()
            ? !iBlockReader.IsOpaque(x, y, z)
            : side switch
            {
                Side.Down => minY > 0.0D || !iBlockReader.IsOpaque(x, y, z),
                Side.Up => maxY < 1.0D || !iBlockReader.IsOpaque(x, y, z),
                Side.North => minZ > 0.0D || !iBlockReader.IsOpaque(x, y, z),
                Side.South => maxZ < 1.0D || !iBlockReader.IsOpaque(x, y, z),
                Side.West => minX > 0.0D || !iBlockReader.IsOpaque(x, y, z),
                Side.East => maxX < 1.0D || !iBlockReader.IsOpaque(x, y, z),
                _ => !iBlockReader.IsOpaque(x, y, z)
            };
        return Visuals?.IsSideVisible(this, iBlockReader, x, y, z, side, baseVisibility) ?? baseVisibility;
    }

    public int GetTextureId(IBlockReader iBlockReader, int x, int y, int z, Side side)
    {
        var baseTexture = GetTexture(side, iBlockReader.GetBlockMeta(x, y, z));
        return Visuals?.GetTextureId(this, iBlockReader, x, y, z, side, baseTexture) ?? baseTexture;
    }

    public int GetTexture(Side side, int meta)
    {
        var baseTexture = GetTexture(side);
        return Visuals?.GetTexture(this, side, meta, baseTexture) ?? baseTexture;
    }

    public int GetTexture(Side side)
    {
        var baseTexture = _faceTextureIds?[(int)side] ?? TextureId;
        return Visuals?.GetTexture(this, side, baseTexture) ?? baseTexture;
    }

    public Box GetBoundingBox(IBlockReader world, EntityManager entities, int x, int y, int z)
    {
        UpdateBoundingBox(world, entities, x, y, z);
        return BoundingBox.Offset(x, y, z);
    }

    public void AddIntersectingBoundingBox(IBlockReader world, EntityManager entities, int x, int y, int z, Box box, List<Box> boxes)
    {
        if (Physics != null)
        {
            var countBefore = boxes.Count;
            Physics.AddCollisionBoxes(this, world, x, y, z, box, boxes);
            if (boxes.Count > countBefore) return;
        }

        var collisionBox = GetCollisionShape(world, entities, x, y, z);
        if (collisionBox != null && box.Intersects(collisionBox.Value)) boxes.Add(collisionBox.Value);
    }

    public Box? GetCollisionShape(IBlockReader world, EntityManager entities, int x, int y, int z)
    {
        UpdateBoundingBox(world, entities, x, y, z);
        Box? defaultShape = HasCollisionBox ? BoundingBox.Offset(x, y, z) : null;
        return Physics == null ? defaultShape : Physics.GetCollisionShape(this, world, entities, x, y, z, defaultShape);
    }

    public bool HasCollision(int meta, bool allowLiquids) => Physics?.HasCollision(this, meta, allowLiquids, HasCollision()) ?? HasCollision();

    private bool HasCollision() => Physics == null || Physics.HasCollision(this, true);

    public void OnTick(OnTickEvent e) => Ticker?.OnTick(this, e);

    public void RandomDisplayTick(OnTickEvent e) => Ticker?.RandomDisplayTick(this, e);

    public void OnMetadataChange(OnMetadataChangeEvent ctx) => Lifecycle?.OnMetadataChange(this, ctx);

    public void NeighborUpdate(OnTickEvent e) => Physics?.NeighborUpdate(this, e);

    public void OnPlaced(OnPlacedEvent e)
    {
        Lifecycle?.OnPlaced(this, e);
        GameEvents.PublishBlockPlaced(new BlockPlacedEvent(e.X, e.Y, e.Z, Id, e.World.Reader.GetBlockMeta(e.X, e.Y, e.Z)));
    }

    public void OnBreak(OnBreakEvent e)
    {
        Lifecycle?.OnBreak(this, e);
        GameEvents.PublishBlockBreak(new BlockBreakEvent(e.X, e.Y, e.Z, Id));
    }

    public int GetDroppedItemCount()
    {
        var defaultCount = _minDroppedCount == _maxDroppedCount ? _minDroppedCount : _minDroppedCount + Random.Shared.Next(_maxDroppedCount - _minDroppedCount + 1);
        return Lifecycle?.GetDroppedItemCount(this, defaultCount) ?? defaultCount;
    }

    public int GetDroppedItemId(int blockMeta)
    {
        var defaultId = _lootTable?.Roll(Random.Shared) ?? Id;
        return Lifecycle?.GetDroppedItemId(this, blockMeta, defaultId) ?? defaultId;
    }

    public float GetHardness(EntityPlayer player) => Hardness < 0.0F ? 0.0F : !player.CanHarvest(this) ? 1.0F / Hardness / 100.0F : player.GetBlockBreakingSpeed(this) / Hardness / 30.0F;

    public void DropStacks(OnDropEvent ctx)
    {
        if (!ctx.World.IsRemote && ctx.World.Rules.GetBool(DefaultRules.DoTileDrops))
        {
            var dropCount = GetDroppedItemCount();

            for (var attempt = 0; attempt < dropCount; ++attempt)
            {
                if (!(Random.Shared.NextSingle() <= ctx.Luck)) continue;

                var itemId = GetDroppedItemId(ctx.Meta);
                if (itemId > 0) DropStack(ctx.World, ctx.X, ctx.Y, ctx.Z, new ItemStack(ctx.World.Content.Items, itemId, 1, GetDroppedItemMeta(ctx.Meta)));
            }
        }

        Lifecycle?.OnDropStacks(this, ctx);
    }

    public static void DropStack(IWorldContext world, int x, int y, int z, ItemStack itemStack)
    {
        if (world.IsRemote || !world.Rules.GetBool(DefaultRules.DoTileDrops)) return;

        const float spreadFactor = 0.7F;
        var offsetX = Random.Shared.NextSingle() * spreadFactor + (1.0F - spreadFactor) * 0.5D;
        var offsetY = Random.Shared.NextSingle() * spreadFactor + (1.0F - spreadFactor) * 0.5D;
        var offsetZ = Random.Shared.NextSingle() * spreadFactor + (1.0F - spreadFactor) * 0.5D;
        world.SpawnItemDrop(x + offsetX, y + offsetY, z + offsetZ, itemStack);
    }

    private int GetDroppedItemMeta(int blockMeta)
    {
        var defaultMeta = PreservesMetaOnDrop ? blockMeta : _droppedItemMetaValue;
        return Lifecycle?.GetDroppedItemMeta(this, blockMeta, defaultMeta) ?? defaultMeta;
    }

    public float GetBlastResistance(Entity entity) => _resistance / 5.0F;

    public HitResult Raycast(IBlockReader world, EntityManager entities, int x, int y, int z, Vec3D startPos, Vec3D endPos)
    {
        UpdateBoundingBox(world, entities, x, y, z);
        Vec3D pos = new(x, y, z);
        var res = BoundingBox.Raycast(startPos - pos, endPos - pos);
        if (res.Type == HitResultType.Miss) return new HitResult(HitResultType.Miss);

        res.BlockX = x;
        res.BlockY = y;
        res.BlockZ = z;
        res.Pos += pos;
        return res;
    }

    public void OnDestroyedByExplosion(OnDestroyedByExplosionEvent @event) => Lifecycle?.OnDestroyedByExplosion(this, @event);

    protected internal void SetSlipperiness(float slipperiness)
    {
        EnsureMutable();
        Slipperiness = slipperiness;
    }

    public bool CanPlaceAt(CanPlaceAtContext evt)
    {
        var blockId = evt.World.Reader.GetBlockId(evt.X, evt.Y, evt.Z);
        var baseResult = blockId == 0 || evt.World.Content.Blocks.GetByProtocolId(blockId).Material.IsReplaceable;
        return Physics == null ? baseResult : baseResult && Physics.CanPlaceAt(this, evt);
    }

    public bool OnUse(OnUseEvent ctx) => Interactable?.OnUse(this, ctx) ?? false;

    public void onSteppedOn(OnEntityStepEvent @event) => Interactable?.OnSteppedOn(this, @event);

    public void OnBlockBreakStart(OnBlockBreakStartEvent @event) => Interactable?.OnBlockBreakStart(this, @event);

    public Vec3D ApplyVelocity(OnApplyVelocityEvent @event) => Physics?.ApplyVelocity(this, @event, Vec3D.Zero) ?? Vec3D.Zero;

    public void UpdateBoundingBox(IBlockReader blockReader, int x, int y, int z) => UpdateBoundingBox(blockReader, null, x, y, z);

    public void UpdateBoundingBox(IBlockReader blockReader, EntityManager? entities, int x, int y, int z) => Physics?.UpdateBoundingBox(this, blockReader, entities, x, y, z);

    public int GetColor(int meta) => Visuals?.GetColor(this, meta, 0xFFFFFF) ?? 0xFFFFFF;

    public int GetColorForFace(int meta, int face)
    {
        var baseColor = GetColor(meta);
        return Visuals?.GetColorForFace(this, meta, face, baseColor) ?? baseColor;
    }

    public int GetColorMultiplier(IBlockReader iBlockReader, int x, int y, int z) => Visuals?.GetColorMultiplier(this, iBlockReader, x, y, z, 0xFFFFFF) ?? 0xFFFFFF;

    public int GetColorMultiplier(IBlockReader iBlockReader, int x, int y, int z, int knownMeta) =>
        Visuals?.GetColorMultiplier(this, iBlockReader, x, y, z, knownMeta, 0xFFFFFF) ?? 0xFFFFFF;

    public bool IsPoweringSide(IBlockReader iBlockReader, int x, int y, int z, int side) => Redstone != null && Redstone.IsPoweringSide(this, iBlockReader, x, y, z, side);

    public bool CanEmitRedstonePower() => Redstone != null && Redstone.CanEmitRedstonePower(this);

    public bool IsFlammable(IBlockReader iBlockReader, int x, int y, int z) => Physics != null && Physics.IsFlammable(this, iBlockReader, x, y, z, false);

    public void OnEntityCollision(OnEntityCollisionEvent @event) => Interactable?.OnEntityCollision(this, @event);

    public bool IsStrongPoweringSide(IBlockReader world, int x, int y, int z, int side) => Redstone != null && Redstone.IsStrongPoweringSide(this, world, x, y, z, side);

    public void SetupRenderBoundingBox() => Physics?.SetupRenderBoundingBox(this);

    public void OnAfterBreak(OnAfterBreakEvent ctx)
    {
        ctx.Player.IncreaseStat(Stats.Stats.MineBlockStatArray[Id], 1);
        DropStacks(new OnDropEvent(ctx.World, ctx.X, ctx.Y, ctx.Z, ctx.Meta));
        Lifecycle?.OnAfterBreak(this, ctx);
    }

    public bool CanGrow(OnTickEvent ctx) => Physics == null || Physics.CanGrow(this, ctx);

    public string TranslateBlockName() => Translations.Get($"{BlockName}.name");

    public void OnBlockAction(OnBlockActionEvent ctx) => Lifecycle?.OnBlockAction(this, ctx);

    protected internal void SetPistonBehavior(PistonBehavior behavior)
    {
        EnsureMutable();
        _pistonBehaviorOverride = behavior;
    }

    protected internal void SetHasTileEntity(Func<BlockEntity> factory)
    {
        EnsureMutable();
        _blockEntityFactory = factory;
    }

    public BlockEntity? GetBlockEntity() => _blockEntityFactory?.Invoke();

    internal void ApplyDraft(BlockDraft draft)
    {
        EnsureMutable();
        TopVariance = draft.TopVariance;
        BottomVariance = draft.BottomVariance;
        SideVariance = draft.SideVariance;
        Ticker = draft.Ticker;
        Interactable = draft.Interactable;
        Visuals = draft.Visuals;
        Lifecycle = draft.Lifecycle;
        Physics = draft.Physics;
        Redstone = draft.Redstone;
        RenderType = draft.RenderType;
        HasCollisionBox = draft.HasCollisionBox;
        BurnChance = draft.BurnChance;
        SpreadChance = draft.SpreadChance;
        IsOpaque = draft.IsOpaque;
        TickRate = draft.TickRate;
        RenderLayer = draft.RenderLayer;
        BlockName = draft.BlockName;
        EnableStats = draft.EnableStats;
        IgnoreMetaUpdates = draft.IgnoreMetaUpdates;
        TickRandomly = draft.TickRandomly;
        Opacity = draft.Opacity;
        Luminance = draft.Luminance;
        SoundGroup = draft.SoundGroup;
        TextureId = draft.TextureId;
        DropCount = draft.DropCount;
        PreservesMetaOnDrop = draft.PreservesMetaOnDrop;
    }

    internal void Freeze() => IsFrozen = true;

    private void EnsureMutable()
    {
        if (IsFrozen) throw new InvalidOperationException($"Block {Id} is part of a finalized content runtime and cannot be mutated.");
    }
}
