using System;
using System.Collections.Generic;
using BetaSharp.Blocks.Behaviors;
using BetaSharp.Blocks.Entities;
using BetaSharp.Blocks.Materials;
using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Rules;
using BetaSharp.Util.Hit;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks;

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
    private string _blockName = "";
    private int _droppedItemMetaValue;
    private bool _dropsWithBlockMeta;
    private int?[]? _faceTextureIds;
    private bool _hasCollision = true;
    private bool _isFullCube = true;
    private bool _isOpaque = true;
    private LootTable? _lootTable;
    private int _maxDroppedCount = 1;
    private int _minDroppedCount = 1;
    private PistonBehavior? _pistonBehaviorOverride;
    private int _renderLayer;
    private BlockRendererType _renderType = BlockRendererType.Standard;
    private int _tickRate = 10;
    public Box BoundingBox;
    public float Hardness;
    public float ParticleFallSpeedModifier;
    public float Resistance;
    protected bool ShouldTrackStatistics;
    public float Slipperiness;
    public BlockSoundGroup SoundGroup;
    public int TextureId;

    protected Block(int id, Material material)
    {
        ShouldTrackStatistics = true;
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
        BlocksOpaque[id] = IsOpaque();
        BlockLightOpacity[id] = IsOpaque() ? 255 : 0;
        BlocksAllowVision[id] = !material.BlocksVision;
        BlocksWithEntity[id] = false;
    }

    protected internal Block(int id, int textureId, Material material) : this(id, material) => TextureId = textureId;

    public static BlockSoundGroup SoundPowderFootstep => SoundGroupRegistry.Get("powder");
    public static BlockSoundGroup SoundWoodFootstep => SoundGroupRegistry.Get("wood");
    public static BlockSoundGroup SoundGravelFootstep => SoundGroupRegistry.Get("gravel");
    public static BlockSoundGroup SoundGrassFootstep => SoundGroupRegistry.Get("grass");
    public static BlockSoundGroup SoundStoneFootstep => SoundGroupRegistry.Get("stone");
    public static BlockSoundGroup SoundMetalFootstep => SoundGroupRegistry.Get("metal");
    public static BlockSoundGroup SoundGlassFootstep => SoundGroupRegistry.Get("glass");
    public static BlockSoundGroup SoundClothFootstep => SoundGroupRegistry.Get("cloth");
    public static BlockSoundGroup SoundSandFootstep => SoundGroupRegistry.Get("sand");

    public TextureVariance TopVariance { get; private set; } = TextureVariance.None;
    public TextureVariance BottomVariance { get; private set; } = TextureVariance.None;
    public TextureVariance SideVariance { get; private set; } = TextureVariance.None;

    public IBlockTicker? Ticker { get; private set; }

    public IBlockInteractable? Interactable { get; private set; }

    public IBlockVisuals? Visuals { get; private set; }

    public IBlockLifecycle? Lifecycle { get; private set; }

    public IBlockPhysics? Physics { get; private set; }

    public IRedstoneComponent? Redstone { get; private set; }

    public virtual IReadOnlyList<string> GetBlockAlias => _blockAlias ?? [];

    protected internal Block IgnoreMetaUpdates()
    {
        BlocksIgnoreMetaUpdate[Id] = true;
        return this;
    }

    protected internal virtual void Init() => Lifecycle?.OnInit(this);

    protected internal Block setSoundGroup(BlockSoundGroup soundGroup)
    {
        SoundGroup = soundGroup;
        return this;
    }

    protected internal Block setOpacity(int opacity)
    {
        BlockLightOpacity[Id] = opacity;
        return this;
    }

    protected internal Block SetNonOpaque()
    {
        // The constructor caches isOpaque() into these arrays before fluent setters run,
        // so they must be refreshed here.
        _isOpaque = false;
        BlocksOpaque[Id] = false;
        BlockLightOpacity[Id] = 0;
        return this;
    }

    protected internal Block SetLuminance(float fractionalValue)
    {
        BlocksLightLuminance[Id] = (int)(15.0F * fractionalValue);
        return this;
    }

    protected internal Block SetResistance(float resistance)
    {
        this.Resistance = resistance * 3.0F;
        return this;
    }

    protected internal Block SetTopBottomTextures(int topTextureId, int bottomTextureId) => SetFaceTexture(Side.Up, topTextureId).SetFaceTexture(Side.Down, bottomTextureId);

    protected internal Block SetFaceTexture(Side side, int textureId)
    {
        _faceTextureIds ??= new int?[6];
        _faceTextureIds[(int)side] = textureId;
        return this;
    }

    /// <summary>
    ///     Configures the block's weighted drop table (e.g. gravel's flint chance). Item ids inside
    ///     each <see cref="LootEntry" /> are deferred, so they may safely reference another block's or item's
    ///     static field regardless of declaration order.
    /// </summary>
    protected internal Block SetLootTable(LootTable table, int minCount = 1, int maxCount = -1, int meta = 0)
    {
        _lootTable = table;
        _minDroppedCount = minCount;
        _maxDroppedCount = maxCount < 0 ? minCount : maxCount;
        _droppedItemMetaValue = meta;
        return this;
    }

    protected internal Block SetDropCount(int count)
    {
        _minDroppedCount = count;
        _maxDroppedCount = count;
        return this;
    }

    /// <summary>Makes drops carry the broken block's metadata (e.g. wool color) instead of a fixed value.</summary>
    protected internal Block preserveMetaOnDrop()
    {
        _dropsWithBlockMeta = true;
        return this;
    }

    protected internal Block SetBlockAlias(params string[] aliases)
    {
        _blockAlias = aliases;
        return this;
    }

    public virtual bool IsFullCube() => _isFullCube;

    public virtual BlockRendererType GetRenderType() => _renderType;

    protected internal Block SetNotFullCube()
    {
        _isFullCube = false;
        return this;
    }

    protected internal Block SetRenderType(BlockRendererType renderType)
    {
        _renderType = renderType;
        return this;
    }

    /// <summary>Entities pass through this block (plants, portals, ...).</summary>
    protected internal Block SetNoCollision()
    {
        _hasCollision = false;
        return this;
    }

    public Block SetVariance(TextureVariance allFaces)
    {
        TopVariance = allFaces;
        BottomVariance = allFaces;
        SideVariance = allFaces;
        return this;
    }

    public Block SetVariance(TextureVariance topBottom, TextureVariance sides)
    {
        TopVariance = topBottom;
        BottomVariance = topBottom;
        SideVariance = sides;
        return this;
    }

    public Block SetVariance(TextureVariance top, TextureVariance bottom, TextureVariance sides)
    {
        TopVariance = top;
        BottomVariance = bottom;
        SideVariance = sides;
        return this;
    }

    protected internal Block SetHardness(float hardness)
    {
        this.Hardness = hardness;
        if (Resistance < hardness * 5.0F) Resistance = hardness * 5.0F;

        return this;
    }

    protected internal Block SetUnbreakable()
    {
        SetHardness(-1.0F);
        return this;
    }

    public float GetHardness() => Hardness;

    protected internal Block SetTickRandomly(bool tickRandomly)
    {
        BlocksRandomTick[Id] = tickRandomly;
        return this;
    }

    public Block SetBoundingBox(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        BoundingBox = new Box(minX, minY, minZ, maxX, maxY, maxZ);
        return this;
    }

    public virtual float GetLuminance(ILightProvider? lighting, int x, int y, int z)
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

    public virtual bool IsSideVisible(IBlockReader iBlockReader, int x, int y, int z, Side side)
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

    public virtual bool IsSolidFace(IBlockReader iBlockReader, int x, int y, int z, int face) => iBlockReader.GetMaterial(x, y, z).IsSolid;

    public virtual int GetTextureId(IBlockReader iBlockReader, int x, int y, int z, Side side)
    {
        int baseTexture = GetTexture(side, iBlockReader.GetBlockMeta(x, y, z));
        return Visuals?.GetTextureId(this, iBlockReader, x, y, z, side, baseTexture) ?? baseTexture;
    }

    public virtual int GetTexture(Side side, int meta)
    {
        int baseTexture = GetTexture(side);
        return Visuals?.GetTexture(this, side, meta, baseTexture) ?? baseTexture;
    }

    public virtual int GetTexture(Side side)
    {
        int baseTexture = _faceTextureIds?[(int)side] ?? TextureId;
        return Visuals?.GetTexture(this, side, baseTexture) ?? baseTexture;
    }

    public virtual Box GetBoundingBox(IBlockReader world, EntityManager entities, int x, int y, int z)
    {
        UpdateBoundingBox(world, entities, x, y, z);
        return BoundingBox.Offset(x, y, z);
    }

    public virtual void AddIntersectingBoundingBox(IBlockReader world, EntityManager entities, int x, int y, int z, Box box, List<Box> boxes)
    {
        if (Physics != null)
        {
            int countBefore = boxes.Count;
            Physics.AddCollisionBoxes(this, world, x, y, z, box, boxes);
            if (boxes.Count > countBefore)
            {
                return;
            }
        }

        Box? collisionBox = GetCollisionShape(world, entities, x, y, z);
        if (collisionBox != null && box.Intersects(collisionBox.Value))
        {
            boxes.Add(collisionBox.Value);
        }
    }

    public virtual Box? GetCollisionShape(IBlockReader world, EntityManager entities, int x, int y, int z)
    {
        UpdateBoundingBox(world, entities, x, y, z);
        Box? defaultShape = _hasCollision ? BoundingBox.Offset(x, y, z) : null;
        return Physics == null ? defaultShape : Physics.GetCollisionShape(this, world, entities, x, y, z, defaultShape);
    }

    public virtual bool IsOpaque() => Visuals?.IsOpaque(this, _isOpaque) ?? _isOpaque;

    public virtual bool HasCollision(int meta, bool allowLiquids) => Physics?.HasCollision(this, meta, allowLiquids, HasCollision()) ?? HasCollision();

    public virtual bool HasCollision() => Physics == null || Physics.HasCollision(this, true);

    public Block SetTicker(IBlockTicker ticker)
    {
        Ticker = ticker;
        return this;
    }

    public virtual void OnTick(OnTickEvent e) => Ticker?.OnTick(this, e);

    public virtual void RandomDisplayTick(OnTickEvent e) => Ticker?.RandomDisplayTick(this, e);

    public virtual void OnMetadataChange(OnMetadataChangeEvent ctx) => Lifecycle?.OnMetadataChange(this, ctx);

    public virtual void NeighborUpdate(OnTickEvent e) => Physics?.NeighborUpdate(this, e);

    public virtual int GetTickRate() => _tickRate;

    public virtual void OnPlaced(OnPlacedEvent e) => Lifecycle?.OnPlaced(this, e);

    protected internal Block SetTickRate(int rate)
    {
        _tickRate = rate;
        return this;
    }

    public virtual void OnBreak(OnBreakEvent e) => Lifecycle?.OnBreak(this, e);

    public virtual int GetDroppedItemCount()
    {
        int defaultCount = _minDroppedCount == _maxDroppedCount ? _minDroppedCount : _minDroppedCount + Random.Shared.Next(_maxDroppedCount - _minDroppedCount + 1);
        return Lifecycle?.GetDroppedItemCount(this, defaultCount) ?? defaultCount;
    }

    public virtual int GetDroppedItemId(int blockMeta)
    {
        int defaultId = _lootTable?.Roll(Random.Shared) ?? Id;
        return Lifecycle?.GetDroppedItemId(this, blockMeta, defaultId) ?? defaultId;
    }

    public float GetHardness(EntityPlayer player) => Hardness < 0.0F ? 0.0F : !player.CanHarvest(this) ? 1.0F / Hardness / 100.0F : player.GetBlockBreakingSpeed(this) / Hardness / 30.0F;

    public virtual void DropStacks(OnDropEvent ctx)
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

    protected virtual int GetDroppedItemMeta(int blockMeta)
    {
        int defaultMeta = _dropsWithBlockMeta ? blockMeta : _droppedItemMetaValue;
        return Lifecycle?.GetDroppedItemMeta(this, blockMeta, defaultMeta) ?? defaultMeta;
    }

    public virtual float GetBlastResistance(Entity entity) => Resistance / 5.0F;

    public virtual HitResult Raycast(IBlockReader world, EntityManager entities, int x, int y, int z, Vec3D startPos, Vec3D endPos)
    {
        UpdateBoundingBox(world, entities, x, y, z);
        Vec3D pos = new(x, y, z);
        HitResult res = BoundingBox.Raycast(startPos - pos, endPos - pos);
        if (res.Type == HitResultType.MISS)
        {
            return new HitResult(HitResultType.MISS);
        }

        res.BlockX = x;
        res.BlockY = y;
        res.BlockZ = z;
        res.Pos += pos;
        return res;
    }

    public virtual void OnDestroyedByExplosion(OnDestroyedByExplosionEvent @event) => Lifecycle?.OnDestroyedByExplosion(this, @event);

    public virtual int GetRenderLayer() => _renderLayer;

    protected internal Block SetRenderLayer(int layer)
    {
        _renderLayer = layer;
        return this;
    }

    protected internal Block SetSlipperiness(float slipperiness)
    {
        Slipperiness = slipperiness;
        return this;
    }

    public virtual bool CanPlaceAt(CanPlaceAtContext evt)
    {
        int blockId = evt.World.Reader.GetBlockId(evt.X, evt.Y, evt.Z);
        bool baseResult = blockId == 0 || Blocks[blockId].Material.IsReplaceable;
        return Physics == null ? baseResult : baseResult && Physics.CanPlaceAt(this, evt);
    }

    public Block SetInteractable(IBlockInteractable interactable)
    {
        Interactable = interactable;
        return this;
    }

    public Block SetVisuals(IBlockVisuals visuals)
    {
        Visuals = visuals;
        return this;
    }

    public Block SetLifecycle(IBlockLifecycle lifecycle)
    {
        Lifecycle = lifecycle;
        return this;
    }

    public Block SetPhysics(IBlockPhysics physics)
    {
        Physics = physics;
        return this;
    }

    public Block SetRedstone(IRedstoneComponent redstone)
    {
        Redstone = redstone;
        return this;
    }

    public virtual bool OnUse(OnUseEvent ctx) => Interactable?.OnUse(this, ctx) ?? false;

    public virtual void onSteppedOn(OnEntityStepEvent @event) => Interactable?.OnSteppedOn(this, @event);

    public virtual void OnBlockBreakStart(OnBlockBreakStartEvent @event) => Interactable?.OnBlockBreakStart(this, @event);

    public virtual Vec3D ApplyVelocity(OnApplyVelocityEvent @event) => Physics?.ApplyVelocity(this, @event, Vec3D.Zero) ?? Vec3D.Zero;

    public void UpdateBoundingBox(IBlockReader blockReader, int x, int y, int z) => UpdateBoundingBox(blockReader, null, x, y, z);

    public virtual void UpdateBoundingBox(IBlockReader blockReader, EntityManager? entities, int x, int y, int z) => Physics?.UpdateBoundingBox(this, blockReader, entities, x, y, z);

    public virtual int GetColor(int meta) => Visuals?.GetColor(this, meta, 0xFFFFFF) ?? 0xFFFFFF;

    public virtual int GetColorForFace(int meta, int face)
    {
        int baseColor = GetColor(meta);
        return Visuals?.GetColorForFace(this, meta, face, baseColor) ?? baseColor;
    }

    public virtual int GetColorMultiplier(IBlockReader iBlockReader, int x, int y, int z) => Visuals?.GetColorMultiplier(this, iBlockReader, x, y, z, 0xFFFFFF) ?? 0xFFFFFF;

    public virtual int GetColorMultiplier(IBlockReader iBlockReader, int x, int y, int z, int knownMeta)
    {
        int baseColor = GetColorMultiplier(iBlockReader, x, y, z);
        return Visuals?.GetColorMultiplier(this, iBlockReader, x, y, z, knownMeta, baseColor) ?? baseColor;
    }

    public virtual bool IsPoweringSide(IBlockReader iBlockReader, int x, int y, int z, int side) => Redstone != null && Redstone.IsPoweringSide(this, iBlockReader, x, y, z, side);

    public virtual bool CanEmitRedstonePower() => Redstone != null && Redstone.CanEmitRedstonePower(this);

    public virtual bool IsFlammable(IBlockReader iBlockReader, int x, int y, int z) => Physics != null && Physics.IsFlammable(this, iBlockReader, x, y, z, false);

    public virtual void OnEntityCollision(OnEntityCollisionEvent @event) => Interactable?.OnEntityCollision(this, @event);

    public virtual bool IsStrongPoweringSide(IBlockReader world, int x, int y, int z, int side) => Redstone != null && Redstone.IsStrongPoweringSide(this, world, x, y, z, side);

    public virtual void SetupRenderBoundingBox() => Physics?.SetupRenderBoundingBox(this);

    public virtual void OnAfterBreak(OnAfterBreakEvent ctx)
    {
        ctx.Player.IncreaseStat(Stats.Stats.MineBlockStatArray[Id], 1);
        DropStacks(new OnDropEvent(ctx.World, ctx.X, ctx.Y, ctx.Z, ctx.Meta));
        Lifecycle?.OnAfterBreak(this, ctx);
    }

    public virtual bool CanGrow(OnTickEvent ctx) => Physics == null || Physics.CanGrow(this, ctx);

    public Block SetBlockName(string name)
    {
        _blockName = $"tile.{name}";
        return this;
    }

    public string TranslateBlockName() => Translations.Get($"{GetBlockName()}.name");

    public string GetBlockName() => _blockName;

    /// <summary>
    /// Block name without the "tile." prefix, suitable for registry / ResourceLocation lookup
    /// (e.g. "stone", "reeds", "cake").
    /// </summary>
    public string RegistryName => _blockName.StartsWith("tile.") ? _blockName[5..] : _blockName;

    private static Dictionary<string, int>? s_registryNameToId;

    /// <summary>
    /// Looks up a block by its namespaced name (e.g. "betasharp:reeds" or "reeds").
    /// Returns null when the namespace is not <see cref="Namespace.BetaSharp"/> or the name is unknown.
    /// </summary>
    public static Block? ByName(string raw)
    {
        var location = ResourceLocation.Parse(raw);
        if (location.Namespace != Namespace.BetaSharp) return null;

        if (s_registryNameToId is null)
        {
            s_registryNameToId = [];
            foreach (Block? b in Blocks)
            {
                if (b is not null) s_registryNameToId.TryAdd(b.RegistryName, b.Id);
            }
        }

        return s_registryNameToId.TryGetValue(location.Path, out int id) ? Blocks[id] : null;
    }

    public virtual void OnBlockAction(OnBlockActionEvent ctx) => Lifecycle?.OnBlockAction(this, ctx);

    public bool GetEnableStats() => ShouldTrackStatistics;

    protected internal Block DisableStats()
    {
        ShouldTrackStatistics = false;
        return this;
    }

    public virtual PistonBehavior GetPistonBehavior() => _pistonBehaviorOverride ?? Material.PistonBehavior;

    /// <summary>Overrides the material-derived piston behavior (e.g. plates are destroyed when pushed).</summary>
    protected internal Block SetPistonBehavior(PistonBehavior behavior)
    {
        _pistonBehaviorOverride = behavior;
        return this;
    }

    /// <summary>
    ///     Declares that this block carries a tile entity, created by <paramref name="factory" />.
    ///     The factory is deferred, so it may safely reference types regardless of declaration order.
    /// </summary>
    protected internal Block SetHasTileEntity(Func<BlockEntity> factory)
    {
        BlocksWithEntity[Id] = true;
        _blockEntityFactory = factory;
        return this;
    }

    public virtual BlockEntity? GetBlockEntity() => _blockEntityFactory?.Invoke();
}
