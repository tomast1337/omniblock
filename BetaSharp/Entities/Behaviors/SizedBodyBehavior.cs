using System.Text.Json;
using BetaSharp.Entities.State;
using BetaSharp.NBT;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     A mob that comes in sizes, where the size sets the bounding box and the health and everything
///     else scales off it. Synced, because the client draws the mob from it, and persisted with an
///     offset, because the on-disk value counts from zero while the live one counts from one.
///     <para>
///         Size is rolled once at creation, so a mob split off a bigger one or loaded from disk gets
///         a throwaway roll first and is then set properly.
///     </para>
/// </summary>
public sealed class SizedBodyBehavior : IEntityLifecycle, IEntityPersistence
{
    private readonly int[] _choices;
    private readonly int _healthPerSizeSquared;
    private readonly string _nbtKey;
    private readonly int _nbtOffset;
    private readonly SyncedHandle<byte> _size;
    private readonly float _widthPerSize;

    public SizedBodyBehavior(in EntityBehaviorContext context)
    {
        List<int> choices = [];
        foreach (JsonElement choice in context.Json.GetProperty("sizes").EnumerateArray())
        {
            choices.Add(choice.GetInt32());
        }

        _choices = [.. choices];

        _widthPerSize = context.Float("width_per_size", 0.6F);
        _healthPerSizeSquared = context.Int("health_per_size_squared", 1);
        _nbtKey = context.Json.GetProperty("nbt_key").GetString()!;
        _nbtOffset = context.Int("nbt_offset", 0);
        _size = context.Synced<byte>("size");
    }

    public void OnCreated(Entity self)
    {
        if (self is EntityLiving mob)
        {
            SetSize(mob, _choices[self.Random.NextInt(_choices.Length)]);
        }
    }

    public void OnWriteNbt(Entity self, NBTTagCompound nbt) => nbt.SetInteger(_nbtKey, Size(self) + _nbtOffset);

    public void OnReadNbt(Entity self, NBTTagCompound nbt)
    {
        if (self is EntityLiving mob)
        {
            SetSize(mob, nbt.GetInteger(_nbtKey) - _nbtOffset);
        }
    }

    public int Size(Entity self) => self.DataSynchronizer.Get<byte>(_size.Id).Value;

    /// <summary>
    ///     Resizes the mob around its current position. The box grows from the centre, so the
    ///     position has to be re-applied for the new box to sit where the mob is.
    /// </summary>
    public void SetSize(EntityLiving self, int size)
    {
        self.DataSynchronizer.Get<byte>(_size.Id).Value = (byte)size;
        self.SetBoundingBoxSpacing(_widthPerSize * size, _widthPerSize * size);
        self.Health = size * size * _healthPerSizeSquared;
        self.SetPosition(self.X, self.Y, self.Z);
    }
}
