using BetaSharp.Entities;
using BetaSharp.NBT;
using BetaSharp.Network.Messages;
using BetaSharp.Network.Packets;
using BetaSharp.Registries;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Blocks.Entities;

public abstract class BlockEntity : IEntity
{
    private static readonly IRegistry<BlockEntityType> s_registry = DefaultRegistries.BlockEntityTypes;
    private static readonly ILogger<BlockEntity> s_logger = Log.Instance.For<BlockEntity>();

    public Vec3D Position => new(X, Y, Z);

    public static readonly BlockEntityType Furnace = Register(() => new BlockEntityFurnace(), "Furnace");
    public static readonly BlockEntityType Chest = Register(() => new BlockEntityChest(), "Chest");
    public static readonly BlockEntityType RecordPlayer = Register(() => new BlockEntityRecordPlayer(), "RecordPlayer");
    public static readonly BlockEntityType Dispenser = Register(() => new BlockEntityDispenser(), "Trap");
    public static readonly BlockEntityType Sign = Register(() => new BlockEntitySign(), "Sign");
    public static readonly BlockEntityType MobSpawner = Register(() => new BlockEntityMobSpawner(), "MobSpawner");
    public static readonly BlockEntityType Note = Register(() => new BlockEntityNote(), "Music");
    public static readonly BlockEntityType Piston = Register(() => new BlockEntityPiston(), "Piston");

    private bool _removed;

    public IWorldContext? World;

    public int X;
    public int Y;
    public int Z;

    static BlockEntity()
    {
    }

    protected abstract BlockEntityType Type { get; }

    public int PushedBlockData => World!.Reader.GetBlockMeta(X, Y, Z);

    private static BlockEntityType Register<T>(Func<T> factory, string id) where T : BlockEntity
    {
        BlockEntityType type = new(factory, id);
        s_registry.Register(ResourceLocation.Parse(id.ToLower()), type);
        return type;
    }

    protected virtual void ReadNbt(NBTTagCompound nbt)
    {
        X = nbt.GetInteger("x");
        Y = nbt.GetInteger("y");
        Z = nbt.GetInteger("z");
    }

    public virtual void WriteNbt(NBTTagCompound nbt)
    {
        nbt.SetString("id", Type.Id);
        nbt.SetInteger("x", X);
        nbt.SetInteger("y", Y);
        nbt.SetInteger("z", Z);
    }

    public virtual void Tick(EntityManager entities)
    {
    }

    void IEntity.Read(NBTTagCompound nbt) => ReadNbt(nbt);

    void IEntity.Write(NBTTagCompound nbt) => WriteNbt(nbt);

    void IEntity.Tick() => Tick(World!.Entities);

    int IEntity.GetId() => GetBlock().Id;

    IWorldContext IEntity.World => World!;

    public static BlockEntity? CreateFromNbt(NBTTagCompound nbt)
    {
        string id = nbt.GetString("id");
        if (string.IsNullOrEmpty(id)) return null;

        BlockEntityType? type = s_registry.Get(ResourceLocation.Parse(id.ToLower()))?.Value;
        if (type == null)
        {
            s_logger.LogInformation($"{id} is missing a mapping!");
            return null;
        }

        try
        {
            BlockEntity blockEntity = type.Create();
            blockEntity.ReadNbt(nbt);
            return blockEntity;
        }
        catch (Exception exception)
        {
            s_logger.LogError(exception, $"Failed to create block entity for id {id}");
            return null;
        }
    }

    public void MarkDirty()
    {
        if (World is not { } world || world.IsRemote) return;
        world.Broadcaster.UpdateBlockEntity(X, Y, Z, this);
    }

    public double distanceFrom(double x, double y, double z)
    {
        double dx = X + 0.5D - x;
        double dy = Y + 0.5D - y;
        double dz = Z + 0.5D - z;
        return dx * dx + dy * dy + dz * dz;
    }

    public Block GetBlock() => Block.Blocks[World!.Reader.GetBlockId(X, Y, Z)];

    /// <summary>
    ///     What to send a client that has just loaded this block entity, or null when its NBT is
    ///     everything the client needs.
    /// </summary>
    public virtual Message? CreateUpdateMessage() => null;

    public bool IsRemoved()
    {
        if (_removed) return true;
        if (World is not { } world) return false;
        int id = world.Reader.GetBlockId(X, Y, Z);
        return id == 0 || !Block.BlocksWithEntity[id];
    }

    public void MarkRemoved() => _removed = true;

    public void CancelRemoval() => _removed = false;
}
