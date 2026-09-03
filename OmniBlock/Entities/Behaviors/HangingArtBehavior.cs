using OmniBlock.Blocks.Materials;
using OmniBlock.Entities.State;
using OmniBlock.Items;
using OmniBlock.NBT;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     A painting hung on a wall. Its box comes from its art, not from its definition; it re-checks
///     its backing every hundred-odd ticks, and drops as an item as soon as anything disturbs it: a
///     hit, a push, or the wall behind it going away.
///     <para>
///         Which art it wears and which block it hangs on are per-instance state.
///         <see cref="HangAt(IWorldContext, int, int, int, int)" /> picks a random art that fits; the
///         overload naming a title is how a saved or network-received painting comes back.
///     </para>
/// </summary>
public sealed class HangingArtBehavior : IEntityTicker, IEntityLifecycle, IEntityPersistence, IEntityPhysics
{
    /// <summary>How far the art's face sits from the middle of its block, leaving the wall behind it.</summary>
    private const float WallOffset = 9.0F / 16.0F;

    private readonly StateHandle<Painting> _art;

    private readonly int _checkInterval;
    private readonly StateHandle<int> _direction;
    private readonly Item _drop;
    private readonly StateHandle<int> _tickCounter;
    private readonly StateHandle<int> _tileX;
    private readonly StateHandle<int> _tileY;
    private readonly StateHandle<int> _tileZ;

    public HangingArtBehavior(EntityStateLayout layout, int checkInterval, Item drop)
    {
        _checkInterval = checkInterval;
        _drop = drop;

        _art = layout.DeclareRef<Painting>();
        _direction = layout.DeclareInt();
        _tileX = layout.DeclareInt();
        _tileY = layout.DeclareInt();
        _tileZ = layout.DeclareInt();
        _tickCounter = layout.DeclareInt();
    }

    /// <summary>Any hit knocks it down; the amount never matters.</summary>
    public bool? Damage(Entity self, Entity? attacker, int amount)
    {
        if (self.Dead || self.World.IsRemote)
        {
            return true;
        }

        self.VelocityModified = true;
        DropAsItem(self);

        return true;
    }

    public void OnWriteNbt(Entity self, NBTTagCompound nbt)
    {
        nbt.SetByte("Dir", (sbyte)self.State[_direction]);
        nbt.SetString("Motive", Art(self)?.Title);
        nbt.SetInteger("TileX", self.State[_tileX]);
        nbt.SetInteger("TileY", self.State[_tileY]);
        nbt.SetInteger("TileZ", self.State[_tileZ]);
    }

    public void OnReadNbt(Entity self, NBTTagCompound nbt)
    {
        self.State[_tileX] = nbt.GetInteger("TileX");
        self.State[_tileY] = nbt.GetInteger("TileY");
        self.State[_tileZ] = nbt.GetInteger("TileZ");
        self.State.SetRef(_art, ArtNamed(nbt.GetString("Motive")));
        SetFacing(self, nbt.GetByte("Dir"));
    }

    /// <summary>A painting does not move: being pushed knocks it off the wall.</summary>
    public bool OnMove(Entity self, double dx, double dy, double dz)
    {
        if (!self.World.IsRemote && dx * dx + dy * dy + dz * dz > 0.0D)
        {
            DropAsItem(self);
        }

        return true;
    }

    public bool OnAddVelocity(Entity self, double dx, double dy, double dz)
    {
        if (!self.World.IsRemote && dx * dx + dy * dy + dz * dz > 0.0D)
        {
            DropAsItem(self);
        }

        return true;
    }

    public bool OnTickEntity(Entity self)
    {
        if (self.State[_tickCounter]++ != _checkInterval || self.World.IsRemote)
        {
            return true;
        }

        self.State[_tickCounter] = 0;
        if (!CanHang(self))
        {
            DropAsItem(self);
        }

        return true;
    }

    /// <summary>
    ///     Hangs a painting wearing whichever art fits the space, chosen at random. The caller checks
    ///     <see cref="CanHang" /> before spawning it: an art that fits nowhere still produces an
    ///     entity, just one that cannot be placed.
    /// </summary>
    public static Entity HangAt(IWorldContext world, int x, int y, int z, int direction)
    {
        Entity painting = Create(world, x, y, z);
        HangingArtBehavior hanging = painting.Behaviors.Find<HangingArtBehavior>()!;

        List<Painting> fits = [];
        foreach (Painting art in Painting.Values)
        {
            painting.State.SetRef(hanging._art, art);
            hanging.SetFacing(painting, direction);
            if (hanging.CanHang(painting))
            {
                fits.Add(art);
            }
        }

        painting.State.SetRef(hanging._art, fits.Count > 0 ? fits[painting.Random.NextInt(fits.Count)] : Painting.Kebab);
        hanging.SetFacing(painting, direction);
        return painting;
    }

    /// <summary>Hangs a painting wearing a named art, for a saved or network-received one.</summary>
    public static Entity HangAt(IWorldContext world, int x, int y, int z, int direction, string title)
    {
        Entity painting = Create(world, x, y, z);
        HangingArtBehavior hanging = painting.Behaviors.Find<HangingArtBehavior>()!;
        painting.State.SetRef(hanging._art, ArtNamed(title));
        hanging.SetFacing(painting, direction);
        return painting;
    }

    private static Entity Create(IWorldContext world, int x, int y, int z)
    {
        Entity painting = world.Content.EntityTypes.Create("omniblock:painting", world);
        HangingArtBehavior hanging = painting.Behaviors.Find<HangingArtBehavior>()!;
        painting.State[hanging._tileX] = x;
        painting.State[hanging._tileY] = y;
        painting.State[hanging._tileZ] = z;
        return painting;
    }

    private static Painting ArtNamed(string title) =>
        Painting.Values.FirstOrDefault(art => art.Title == title) ?? Painting.Kebab;

    public Painting? Art(Entity self) => self.State.GetRef(_art);

    public int Direction(Entity self) => self.State[_direction];

    public int TileX(Entity self) => self.State[_tileX];
    public int TileY(Entity self) => self.State[_tileY];
    public int TileZ(Entity self) => self.State[_tileZ];

    /// <summary>
    ///     Places the art against its wall and sizes the box to the canvas. The box comes from the
    ///     art, not the definition, so a painting's dimensions never appear in its JSON.
    /// </summary>
    private void SetFacing(Entity self, int facing)
    {
        self.State[_direction] = facing;
        self.PrevYaw = self.Yaw = facing * 90;

        if (Art(self) is not { } art)
        {
            return;
        }

        float halfWidth = art.SizeX;
        float halfHeight = art.SizeY;
        float halfDepth = art.SizeX;

        if (facing != 0 && facing != 2)
        {
            halfWidth = 0.5F;
        }
        else
        {
            halfDepth = 0.5F;
        }

        halfWidth /= 32.0F;
        halfHeight /= 32.0F;
        halfDepth /= 32.0F;

        float centerX = self.State[_tileX] + 0.5F;
        float centerY = self.State[_tileY] + 0.5F;
        float centerZ = self.State[_tileZ] + 0.5F;

        switch (facing)
        {
            case 0:
                centerZ -= WallOffset;
                centerX -= ArtOffset(art.SizeX);
                break;
            case 1:
                centerX -= WallOffset;
                centerZ += ArtOffset(art.SizeX);
                break;
            case 2:
                centerZ += WallOffset;
                centerX += ArtOffset(art.SizeX);
                break;
            case 3:
                centerX += WallOffset;
                centerZ -= ArtOffset(art.SizeX);
                break;
        }

        centerY += ArtOffset(art.SizeY);
        self.SetPosition(centerX, centerY, centerZ);

        float margin = -(0.1F / 16.0F);
        self.BoundingBox = new Box(
            centerX - halfWidth - margin,
            centerY - halfHeight - margin,
            centerZ - halfDepth - margin,
            centerX + halfWidth + margin,
            centerY + halfHeight + margin,
            centerZ + halfDepth + margin);
    }

    /// <summary>An art spanning an even number of blocks hangs half a block off its anchor.</summary>
    private static float ArtOffset(int artSize) => artSize is 32 or 64 ? 0.5F : 0.0F;

    /// <summary>
    ///     Whether the wall behind the whole canvas is solid, nothing is standing in the way, and no
    ///     other painting already claims the space.
    /// </summary>
    public bool CanHang(Entity self)
    {
        if (self.World.Entities.GetEntityCollisionsScratch(self, self.BoundingBox).Count > 0)
        {
            return false;
        }

        if (Art(self) is { } art)
        {
            int widthInBlocks = art.SizeX / 16;
            int heightInBlocks = art.SizeY / 16;
            int direction = self.State[_direction];
            int startX = self.State[_tileX];
            int startZ = self.State[_tileZ];

            switch (direction)
            {
                case 0:
                case 2:
                    startX = MathHelper.Floor(self.X - art.SizeX / 32.0F);
                    break;
                case 1:
                case 3:
                    startZ = MathHelper.Floor(self.Z - art.SizeX / 32.0F);
                    break;
            }

            int startY = MathHelper.Floor(self.Y - art.SizeY / 32.0F);

            for (int dx = 0; dx < widthInBlocks; ++dx)
            {
                for (int dy = 0; dy < heightInBlocks; ++dy)
                {
                    Material material = direction != 0 && direction != 2
                        ? self.World.Reader.GetMaterial(self.State[_tileX], startY + dy, startZ + dx)
                        : self.World.Reader.GetMaterial(startX + dx, startY + dy, self.State[_tileZ]);

                    if (!material.IsSolid)
                    {
                        return false;
                    }
                }
            }
        }

        foreach (Entity entity in self.World.Entities.GetEntities(self, self.BoundingBox))
        {
            if (entity.Behaviors.Find<HangingArtBehavior>() is not null)
            {
                return false;
            }
        }

        return true;
    }

    private void DropAsItem(Entity self)
    {
        if (self.Dead || self.World.IsRemote)
        {
            return;
        }

        self.MarkDead();
        self.World.SpawnEntity(DroppedItemBehavior.Create(self.World, self.X, self.Y, self.Z, new ItemStack(_drop)));
    }
}
