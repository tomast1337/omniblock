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
    public static readonly Block[] Blocks = new Block[256];
    public static readonly bool[] BlocksRandomTick = new bool[256];
    public static readonly bool[] BlocksOpaque = new bool[256];
    public static readonly bool[] BlocksWithEntity = new bool[256];
    public static readonly int[] BlockLightOpacity = new int[256];
    public static readonly bool[] BlocksAllowVision = new bool[256];
    public static readonly int[] BlocksLightLuminance = new int[256];
    public static readonly bool[] BlocksIgnoreMetaUpdate = new bool[256];

    public readonly int Id;
    public readonly Material Material;
    private string[]? _blockAlias;
    private Func<BlockEntity>? _blockEntityFactory;
    private int _droppedItemMetaValue;
    private bool _dropsWithBlockMeta;
    private int?[]? _faceTextureIds;
    private bool _isFullCube = true;
    private LootTable? _lootTable;
    private int _maxDroppedCount = 1;
    private int _minDroppedCount = 1;
    private PistonBehavior? _pistonBehaviorOverride;
    public Box BoundingBox;
    public float Hardness;
    public readonly float ParticleFallSpeedModifier;
    private float _resistance;
    public float Slipperiness;
    public BlockSoundGroup SoundGroup;
    public int TextureId;

    private Block(int id, Material material)
    {
        EnableStats = true;
        SoundGroup = SoundPowderFootstep;
        ParticleFallSpeedModifier = 1.0F;
        Slipperiness = 0.6F;
        if (Blocks[id] != null)
        {
            throw new ArgumentException($"Slot {id} is already occupied by {Blocks[id]} when adding {this}", nameof(id));
        }

        this.Material = material;
        Blocks[id] = this;
        this.Id = id;
        SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
        BlocksOpaque[id] = IsOpaque;
        BlockLightOpacity[id] = IsOpaque ? 255 : 0;
        BlocksAllowVision[id] = !material.BlocksVision;
        BlocksWithEntity[id] = false;
    }

    protected internal Block(int id, int textureId, Material material) : this(id, material) => TextureId = textureId;

    private static BlockSoundGroup SoundPowderFootstep => SoundGroupRegistry.Get("powder");
    public static BlockSoundGroup SoundStoneFootstep => SoundGroupRegistry.Get("stone");

    public TextureVariance TopVariance { get; protected internal set; } = TextureVariance.None;
    public TextureVariance BottomVariance { get; protected internal set; } = TextureVariance.None;
    public TextureVariance SideVariance { get; protected internal set; } = TextureVariance.None;

    public IBlockTicker? Ticker { get; internal set; }

    public IBlockInteractable? Interactable { get; internal set; }

    public IBlockVisuals? Visuals { get; internal set; }

    public IBlockLifecycle? Lifecycle { get; internal set; }

    public IBlockPhysics? Physics { get; internal set; }

    public IRedstoneComponent? Redstone { get; internal set; }

    public IReadOnlyList<string> GetBlockAlias => _blockAlias ?? [];

    public BlockRendererType RenderType { get; protected internal set; } = BlockRendererType.Standard;

    public bool HasCollisionBox { get; protected internal set; } = true;

    public byte BurnChance { get; protected internal set; }
    public byte SpreadChance { get; protected internal set; }

    public bool IsOpaque
    {
        get => Visuals?.IsOpaque(this, field) ?? field;
        protected internal set
        {
            field = value;
            BlocksOpaque[Id] = value;
            BlockLightOpacity[Id] = value ? 255 : 0;
        }
    } = true;

    public int TickRate { get; protected internal set; } = 10;

    public int RenderLayer { get; protected internal set; }

    public string BlockName
    {
        get;
        set => field = $"tile.{value}";
    } = "";

    public bool EnableStats { get; protected internal set; }

    public PistonBehavior PistonBehavior => _pistonBehaviorOverride ?? Material.PistonBehavior;
    public bool IsFullCube() => _isFullCube;

    public bool IgnoreMetaUpdates
    {
        get => BlocksIgnoreMetaUpdate[Id];
        protected internal set => BlocksIgnoreMetaUpdate[Id] = value;
    }

    public bool TickRandomly
    {
        get => BlocksRandomTick[Id];
        protected internal set => BlocksRandomTick[Id] = value;
    }

    public int Opacity
    {
        get => BlockLightOpacity[Id];
        protected internal set => BlockLightOpacity[Id] = value;
    }

    public float Luminance
    {
        get => BlocksLightLuminance[Id] / 15.0F;
        protected internal set => BlocksLightLuminance[Id] = (int)(15.0F * value);
    }

    public bool PreservesMetaOnDrop
    {
        get => _dropsWithBlockMeta;
        protected internal set => _dropsWithBlockMeta = value;
    }

    public int DropCount
    {
        get => _minDroppedCount;
        protected internal set
        {
            _minDroppedCount = value;
            _maxDroppedCount = value;
        }
    }

    protected internal void Init() => Lifecycle?.OnInit(this);

    protected internal void SetResistance(float resistance) => this._resistance = resistance * 3.0F;

    protected internal void SetFaceTexture(Side side, int textureId)
    {
        _faceTextureIds ??= new int?[6];
        _faceTextureIds[(int)side] = textureId;
    }

    protected internal void SetLootTable(LootTable table, int minCount = 1, int maxCount = -1, int meta = 0)
    {
        _lootTable = table;
        _minDroppedCount = minCount;
        _maxDroppedCount = maxCount < 0 ? minCount : maxCount;
        _droppedItemMetaValue = meta;
    }

    public (int primaryMeta, int backupItemId, int backupMeta) GetPickBlockItem(int blockMeta)
    {
        int defaultBackupId = _lootTable?.GetPrimaryItemId() ?? 0;
        int defaultBackupMeta = _dropsWithBlockMeta ? blockMeta : -1;
        return Lifecycle?.GetPickBlockItem(this, blockMeta, defaultBackupId, defaultBackupMeta) ?? (blockMeta, defaultBackupId, defaultBackupMeta);
    }

    protected internal void SetBlockAlias(params string[] aliases) => _blockAlias = aliases;


    protected internal void SetNotFullCube() => _isFullCube = false;

    protected internal void SetHardness(float hardness)
    {
        Hardness = hardness;
        if (_resistance < hardness * 5.0F)
        {
            _resistance = hardness * 5.0F;
        }
    }

    public void SetBoundingBox(float minX, float minY, float minZ, float maxX, float maxY, float maxZ) => BoundingBox = new Box(minX, minY, minZ, maxX, maxY, maxZ);

    public float GetLuminance(ILightProvider? lighting, int x, int y, int z)
    {
        float baseLuminance;
        if (lighting != null)
        {
            baseLuminance = lighting.GetNaturalBrightness(x, y, z, BlocksLightLuminance[Id]);
        }
        else
        {
            int baseLum = BlocksLightLuminance[Id];
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
        int emission = BlocksLightLuminance[Id];

        LightLevels baseLevels = lighting != null
            ? lighting.GetLightLevels(x, y, z, emission)
            : emission > 0 ? LightLevels.Of(0, emission) : LightLevels.FullSky;

        return Visuals?.GetLightLevels(this, lighting!, x, y, z, baseLevels) ?? baseLevels;
    }

    public bool IsSideVisible(IBlockReader iBlockReader, int x, int y, int z, Side side)
    {
        double minX = BoundingBox.MinX;
        double minY = BoundingBox.MinY;
        double minZ = BoundingBox.MinZ;
        double maxX = BoundingBox.MaxX;
        double maxY = BoundingBox.MaxY;
        double maxZ = BoundingBox.MaxZ;

        bool baseVisibility = !side.IsValidSide()
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
        int baseTexture = GetTexture(side, iBlockReader.GetBlockMeta(x, y, z));
        return Visuals?.GetTextureId(this, iBlockReader, x, y, z, side, baseTexture) ?? baseTexture;
    }

    public int GetTexture(Side side, int meta)
    {
        int baseTexture = GetTexture(side);
        return Visuals?.GetTexture(this, side, meta, baseTexture) ?? baseTexture;
    }

    public int GetTexture(Side side)
    {
        int baseTexture = _faceTextureIds?[(int)side] ?? TextureId;
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
            int countBefore = boxes.Count;
            Physics.AddCollisionBoxes(this, world, x, y, z, box, boxes);
            if (boxes.Count > countBefore) return;
        }

        Box? collisionBox = GetCollisionShape(world, entities, x, y, z);
        if (collisionBox != null && box.Intersects(collisionBox.Value))
        {
            boxes.Add(collisionBox.Value);
        }
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

    public void OnPlaced(OnPlacedEvent e) => Lifecycle?.OnPlaced(this, e);

    public void OnBreak(OnBreakEvent e) => Lifecycle?.OnBreak(this, e);

    public int GetDroppedItemCount()
    {
        int defaultCount = _minDroppedCount == _maxDroppedCount ? _minDroppedCount : _minDroppedCount + Random.Shared.Next(_maxDroppedCount - _minDroppedCount + 1);
        return Lifecycle?.GetDroppedItemCount(this, defaultCount) ?? defaultCount;
    }

    public int GetDroppedItemId(int blockMeta)
    {
        int defaultId = _lootTable?.Roll(Random.Shared) ?? Id;
        return Lifecycle?.GetDroppedItemId(this, blockMeta, defaultId) ?? defaultId;
    }

    public float GetHardness(EntityPlayer player) => Hardness < 0.0F ? 0.0F : !player.CanHarvest(this) ? 1.0F / Hardness / 100.0F : player.GetBlockBreakingSpeed(this) / Hardness / 30.0F;

    public void DropStacks(OnDropEvent ctx)
    {
        if (!ctx.World.IsRemote && ctx.World.Rules.GetBool(DefaultRules.DoTileDrops))
        {
            int dropCount = GetDroppedItemCount();

            for (int attempt = 0; attempt < dropCount; ++attempt)
            {
                if (!(Random.Shared.NextSingle() <= ctx.Luck)) continue;

                int itemId = GetDroppedItemId(ctx.Meta);
                if (itemId > 0)
                {
                    DropStack(ctx.World, ctx.X, ctx.Y, ctx.Z, new ItemStack(itemId, 1, GetDroppedItemMeta(ctx.Meta)));
                }
            }
        }

        Lifecycle?.OnDropStacks(this, ctx);
    }

    public static void DropStack(IWorldContext world, int x, int y, int z, ItemStack itemStack)
    {
        if (world.IsRemote || !world.Rules.GetBool(DefaultRules.DoTileDrops)) return;

        const float spreadFactor = 0.7F;
        double offsetX = Random.Shared.NextSingle() * spreadFactor + (1.0F - spreadFactor) * 0.5D;
        double offsetY = Random.Shared.NextSingle() * spreadFactor + (1.0F - spreadFactor) * 0.5D;
        double offsetZ = Random.Shared.NextSingle() * spreadFactor + (1.0F - spreadFactor) * 0.5D;
        world.SpawnItemDrop(x + offsetX, y + offsetY, z + offsetZ, itemStack);
    }

    private int GetDroppedItemMeta(int blockMeta)
    {
        int defaultMeta = _dropsWithBlockMeta ? blockMeta : _droppedItemMetaValue;
        return Lifecycle?.GetDroppedItemMeta(this, blockMeta, defaultMeta) ?? defaultMeta;
    }

    public float GetBlastResistance(Entity entity) => _resistance / 5.0F;

    public HitResult Raycast(IBlockReader world, EntityManager entities, int x, int y, int z, Vec3D startPos, Vec3D endPos)
    {
        UpdateBoundingBox(world, entities, x, y, z);
        Vec3D pos = new(x, y, z);
        HitResult res = BoundingBox.Raycast(startPos - pos, endPos - pos);
        if (res.Type == HitResultType.Miss)
        {
            return new HitResult(HitResultType.Miss);
        }

        res.BlockX = x;
        res.BlockY = y;
        res.BlockZ = z;
        res.Pos += pos;
        return res;
    }

    public void OnDestroyedByExplosion(OnDestroyedByExplosionEvent @event) => Lifecycle?.OnDestroyedByExplosion(this, @event);

    protected internal void SetSlipperiness(float slipperiness) => this.Slipperiness = slipperiness;

    public bool CanPlaceAt(CanPlaceAtContext evt)
    {
        int blockId = evt.World.Reader.GetBlockId(evt.X, evt.Y, evt.Z);
        bool baseResult = blockId == 0 || Blocks[blockId].Material.IsReplaceable;
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
        int baseColor = GetColor(meta);
        return Visuals?.GetColorForFace(this, meta, face, baseColor) ?? baseColor;
    }

    public int GetColorMultiplier(IBlockReader iBlockReader, int x, int y, int z) => Visuals?.GetColorMultiplier(this, iBlockReader, x, y, z, 0xFFFFFF) ?? 0xFFFFFF;

    public int GetColorMultiplier(IBlockReader iBlockReader, int x, int y, int z, int knownMeta)
    {
        int baseColor = GetColorMultiplier(iBlockReader, x, y, z);
        return Visuals?.GetColorMultiplier(this, iBlockReader, x, y, z, knownMeta, baseColor) ?? baseColor;
    }

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

    protected internal void SetPistonBehavior(PistonBehavior behavior) => _pistonBehaviorOverride = behavior;

    protected internal void SetHasTileEntity(Func<BlockEntity> factory)
    {
        BlocksWithEntity[Id] = true;
        _blockEntityFactory = factory;
    }

    public BlockEntity? GetBlockEntity() => _blockEntityFactory?.Invoke();
}
