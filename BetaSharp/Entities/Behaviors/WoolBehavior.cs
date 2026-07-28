using BetaSharp.Blocks;
using BetaSharp.Items;
using BetaSharp.NBT;
using BetaSharp.Util;
using BetaSharp.Util.Maths;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     A sheep's fleece: its colour, whether it has been sheared, and the shears interaction that
///     changes both. One behavior filling three slots, because all of it is the same one byte.
///     <para>
///         That byte is a protocol fact — colour in the low four bits, the sheared flag in bit 16,
///         at datawatcher id 16 — so it cannot be split into two declared properties. Everything
///         that needs to read it goes through <see cref="ColorOf" /> and <see cref="IsShearedOn" />
///         rather than through a sheep class.
///     </para>
/// </summary>
public sealed class WoolBehavior : IEntityInteractable, IEntityPersistence, IEntityLifecycle
{
    private const int ShearedBit = 16;
    private const int ColorMask = 15;

    /// <summary>Per-colour render tints, indexed by fleece colour.</summary>
    public static readonly float[][] ColorTable =
    [
        [1.0F, 1.0F, 1.0F], [0.95F, 0.7F, 0.2F], [0.9F, 0.5F, 0.85F], [0.6F, 0.7F, 0.95F], [0.9F, 0.9F, 0.2F], [0.5F, 0.8F, 0.1F], [0.95F, 0.7F, 0.8F], [0.3F, 0.3F, 0.3F], [0.6F, 0.6F, 0.6F], [0.3F, 0.6F, 0.7F], [0.7F, 0.4F, 0.9F],
        [0.2F, 0.4F, 0.8F], [0.5F, 0.4F, 0.3F], [0.4F, 0.5F, 0.2F], [0.8F, 0.3F, 0.3F], [0.1F, 0.1F, 0.1F]
    ];

    private readonly string _property;
    private readonly Item _tool;
    private readonly int _minDrop;
    private readonly int _dropRange;

    public WoolBehavior(in EntityBehaviorContext context)
    {
        _property = context.Json.TryGetProperty("property", out System.Text.Json.JsonElement name)
            ? name.GetString() ?? "wool"
            : "wool";
        _tool = Item.ByName(ResourceLocation.Parse(context.Json.GetProperty("tool").GetString()!).Path);
        _minDrop = context.Int("min_drop", 2);
        _dropRange = context.Int("drop_range", 3);
    }

    private SyncedProperty<byte>? Data(Entity self) => self.Synced<byte>(_property);

    /// <summary>Fleece colour of any entity carrying a wool byte, or <c>-1</c> if it carries none.</summary>
    public int ColorOf(Entity self) => Data(self) is { } data ? data.Value & ColorMask : -1;

    public void SetColorOn(Entity self, int color)
    {
        if (Data(self) is not { } data) return;

        data.Value = (byte)((data.Value & 0xF0) | (color & ColorMask));
    }

    public bool IsShearedOn(Entity self) => Data(self) is { } data && (data.Value & ShearedBit) != 0;

    private void SetShearedOn(Entity self, bool sheared)
    {
        if (Data(self) is not { } data) return;

        data.Value = sheared
            ? (byte)(data.Value | ShearedBit)
            : (byte)(data.Value & unchecked((byte)~ShearedBit));
    }

    /// <summary>A newly spawned sheep rolls for its colour; most come out white.</summary>
    public void OnPostSpawn(EntityLiving self) => SetColorOn(self, RandomColor(self.World.Random));

    public bool OnInteract(Entity self, EntityPlayer player)
    {
        ItemStack? held = player.Inventory.ItemInHand;
        if (held == null || held.ItemId != _tool.Id || IsShearedOn(self)) return false;

        if (!self.World.IsRemote)
        {
            SetShearedOn(self, true);
            int count = _minDrop + self.Random.NextInt(_dropRange);

            for (int i = 0; i < count; ++i)
            {
                EntityItem wool = self.DropItem(new ItemStack(BlockRegistry.Get("wool").id, 1, ColorOf(self)), 1.0F);
                wool.VelocityY += self.Random.NextFloat() * 0.05F;
                wool.VelocityX += (self.Random.NextFloat() - self.Random.NextFloat()) * 0.1F;
                wool.VelocityZ += (self.Random.NextFloat() - self.Random.NextFloat()) * 0.1F;
            }
        }

        held.DamageItem(1, player);

        // False on purpose: shearing does not consume the interaction, matching the original.
        return false;
    }

    public void OnWriteNbt(Entity self, NBTTagCompound nbt)
    {
        nbt.SetBoolean("Sheared", IsShearedOn(self));
        nbt.SetByte("Color", (sbyte)ColorOf(self));
    }

    public void OnReadNbt(Entity self, NBTTagCompound nbt)
    {
        SetShearedOn(self, nbt.GetBoolean("Sheared"));
        SetColorOn(self, nbt.GetByte("Color"));
    }

    private static int RandomColor(JavaRandom random) => random.NextInt(100) switch
    {
        < 5 => 15, // White
        < 10 => 7, // Gray
        < 15 => 8, // Silver
        < 18 => 12, // Brown
        _ => random.NextInt(500) == 0 ? 6 : 0 // Pink (rare) or White
    };
}
