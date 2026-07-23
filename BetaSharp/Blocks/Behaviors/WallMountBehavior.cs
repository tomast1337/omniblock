using BetaSharp.Entities;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Wall-mounted blocks: torches (meta 1-5, custom wall-attach encoding, may also stand on the
///     ground or a fence post) and ladders (meta is the <see cref="Side" /> facing directly, wall-only).
///     The two encodings and placement rules are different enough that every hook branches on
///     <c>_isLadder</c> rather than sharing formulas; only the random-tick/particle hooks are
///     torch-only (ladders never get a Ticker slot assigned, so those simply aren't invoked for them).
/// </summary>
public sealed class WallMountBehavior : IBlockPhysics, IBlockLifecycle, IBlockTicker
{
    private const float TorchWidth = 0.15F;
    private const float TorchWidthGround = 0.1F;
    private const float LadderThickness = 2.0F / 16.0F;

    private readonly bool _isLadder;

    public WallMountBehavior(bool isLadder) => _isLadder = isLadder;

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (_isLadder)
        {
            OnLadderPlaced(@event);
        }
        else
        {
            OnTorchPlaced(@event);
        }
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
    {
        IBlockReader reader = @event.World.Reader;
        int x = @event.X, y = @event.Y, z = @event.Z;

        if (_isLadder)
        {
            return reader.ShouldSuffocate(x - 1, y, z) ||
                   reader.ShouldSuffocate(x + 1, y, z) ||
                   reader.ShouldSuffocate(x, y, z - 1) ||
                   reader.ShouldSuffocate(x, y, z + 1);
        }

        return reader.ShouldSuffocate(x - 1, y, z) ||
               reader.ShouldSuffocate(x + 1, y, z) ||
               reader.ShouldSuffocate(x, y, z - 1) ||
               reader.ShouldSuffocate(x, y, z + 1) ||
               CanPlaceOnGround(reader, x, y - 1, z);
    }

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (_isLadder) LadderNeighborUpdate(block, @event);
        else TorchNeighborUpdate(block, @event);
    }

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        if (_isLadder) UpdateLadderBoundingBox(block, reader, x, y, z);
        else UpdateTorchBoundingBox(block, reader, x, y, z);
    }

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (@event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z) != 0) return;

        int resolved = ResolveTorchMetaVanillaOrder(@event.World.Reader, @event.X, @event.Y, @event.Z);
        if (resolved != -1)
        {
            @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, resolved);
        }

        BreakIfCannotPlaceAt(block, @event, @event.X, @event.Y, @event.Z);
    }

    public void RandomDisplayTick(Block block, OnTickEvent @event)
    {
        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        float flameX = @event.X + 0.5F;
        float flameY = @event.Y + 0.7F;
        float flameZ = @event.Z + 0.5F;
        const float yOffset = 0.22F;
        const float xOffset = 0.27F;

        switch (meta)
        {
            case 1:
                @event.World.Broadcaster.AddParticle("smoke", flameX - xOffset, flameY + yOffset, flameZ, 0.0D, 0.0D, 0.0D);
                @event.World.Broadcaster.AddParticle("flame", flameX - xOffset, flameY + yOffset, flameZ, 0.0D, 0.0D, 0.0D);
                break;
            case 2:
                @event.World.Broadcaster.AddParticle("smoke", flameX + xOffset, flameY + yOffset, flameZ, 0.0D, 0.0D, 0.0D);
                @event.World.Broadcaster.AddParticle("flame", flameX + xOffset, flameY + yOffset, flameZ, 0.0D, 0.0D, 0.0D);
                break;
            case 3:
                @event.World.Broadcaster.AddParticle("smoke", flameX, flameY + yOffset, flameZ - xOffset, 0.0D, 0.0D, 0.0D);
                @event.World.Broadcaster.AddParticle("flame", flameX, flameY + yOffset, flameZ - xOffset, 0.0D, 0.0D, 0.0D);
                break;
            case 4:
                @event.World.Broadcaster.AddParticle("smoke", flameX, flameY + yOffset, flameZ + xOffset, 0.0D, 0.0D, 0.0D);
                @event.World.Broadcaster.AddParticle("flame", flameX, flameY + yOffset, flameZ + xOffset, 0.0D, 0.0D, 0.0D);
                break;
            default:
                @event.World.Broadcaster.AddParticle("smoke", flameX, flameY, flameZ, 0.0D, 0.0D, 0.0D);
                @event.World.Broadcaster.AddParticle("flame", flameX, flameY, flameZ, 0.0D, 0.0D, 0.0D);
                break;
        }
    }

    private static bool CanPlaceOnGround(IBlockReader world, int x, int y, int z)
        => world.ShouldSuffocate(x, y, z) || world.GetBlockId(x, y, z) == BlockRegistry.Get("fence").id;

    private static void OnLadderPlaced(OnPlacedEvent ctx)
    {
        Side rotation = ctx.World.Reader.GetBlockMeta(ctx.X, ctx.Y, ctx.Z).ToSide();
        if ((rotation == 0 || ctx.Direction == Side.North) && ctx.World.Reader.ShouldSuffocate(ctx.X, ctx.Y, ctx.Z + 1))
        {
            rotation = Side.North;
        }

        if ((rotation == 0 || ctx.Direction == Side.South) && ctx.World.Reader.ShouldSuffocate(ctx.X, ctx.Y, ctx.Z - 1))
        {
            rotation = Side.South;
        }

        if ((rotation == 0 || ctx.Direction == Side.West) && ctx.World.Reader.ShouldSuffocate(ctx.X + 1, ctx.Y, ctx.Z))
        {
            rotation = Side.West;
        }

        if ((rotation == 0 || ctx.Direction == Side.East) && ctx.World.Reader.ShouldSuffocate(ctx.X - 1, ctx.Y, ctx.Z))
        {
            rotation = Side.East;
        }

        ctx.World.Writer.SetBlockMeta(ctx.X, ctx.Y, ctx.Z, rotation.ToInt());
    }

    private static void OnTorchPlaced(OnPlacedEvent @event)
    {
        IBlockReader reader = @event.World.Reader;
        int meta = reader.GetBlockMeta(@event.X, @event.Y, @event.Z);

        switch (@event.Direction)
        {
            case Side.Up when CanPlaceOnGround(reader, @event.X, @event.Y - 1, @event.Z):
                meta = 5;
                break;
            case Side.North when reader.ShouldSuffocate(@event.X, @event.Y, @event.Z + 1):
                meta = 4;
                break;
            case Side.South when reader.ShouldSuffocate(@event.X, @event.Y, @event.Z - 1):
                meta = 3;
                break;
            case Side.West when reader.ShouldSuffocate(@event.X + 1, @event.Y, @event.Z):
                meta = 2;
                break;
            case Side.East when reader.ShouldSuffocate(@event.X - 1, @event.Y, @event.Z):
                meta = 1;
                break;
            case Side.Down:
                {
                    int? resolved = TryResolveTorchMetaForDownPlacement(reader, @event.X, @event.Y, @event.Z, @event.Placer);
                    if (resolved.HasValue)
                    {
                        meta = resolved.Value;
                    }

                    break;
                }
        }

        @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, meta);
    }

    private static int ResolveTorchMetaVanillaOrder(IBlockReader reader, int x, int y, int z)
    {
        if (reader.ShouldSuffocate(x - 1, y, z)) return 1;
        if (reader.ShouldSuffocate(x + 1, y, z)) return 2;
        if (reader.ShouldSuffocate(x, y, z - 1)) return 3;
        if (reader.ShouldSuffocate(x, y, z + 1)) return 4;
        if (CanPlaceOnGround(reader, x, y - 1, z)) return 5;
        return -1;
    }

    private static bool TryGetHorizontalWallPickRay(EntityLiving placer, int torchX, int torchZ, out double lx, out double lz)
    {
        Vec3D look = placer.GetLook(1.0F);
        double h = Math.Sqrt(look.x * look.x + look.z * look.z);
        if (h >= 1e-3)
        {
            lx = look.x / h;
            lz = look.z / h;
            return true;
        }

        double vx = placer.X - (torchX + 0.5);
        double vz = placer.Z - (torchZ + 0.5);
        h = Math.Sqrt(vx * vx + vz * vz);
        if (h >= 1e-4)
        {
            lx = vx / h;
            lz = vz / h;
            return true;
        }

        lx = 0.0;
        lz = 0.0;
        return false;
    }

    private static int? TryResolveTorchMetaForDownPlacement(IBlockReader reader, int x, int y, int z, EntityLiving? placer)
    {
        bool ceiling = reader.ShouldSuffocate(x, y + 1, z);
        bool west = reader.ShouldSuffocate(x - 1, y, z);
        bool east = reader.ShouldSuffocate(x + 1, y, z);
        bool north = reader.ShouldSuffocate(x, y, z - 1);
        bool south = reader.ShouldSuffocate(x, y, z + 1);
        int wallCount = (west ? 1 : 0) + (east ? 1 : 0) + (north ? 1 : 0) + (south ? 1 : 0);
        if (ceiling && wallCount >= 2 && placer is not null)
        {
            if (!TryGetHorizontalWallPickRay(placer, x, z, out double lx, out double lz))
            {
                int v = ResolveTorchMetaVanillaOrder(reader, x, y, z);
                return v == -1 ? null : v;
            }

            const double tieEps = 1e-4;
            double westScore = west ? lx * -1.0 + lz * 0.0 : double.NegativeInfinity;
            double eastScore = east ? lx * 1.0 + lz * 0.0 : double.NegativeInfinity;
            double northScore = north ? lx * 0.0 + lz * -1.0 : double.NegativeInfinity;
            double southScore = south ? lx * 0.0 + lz * 1.0 : double.NegativeInfinity;
            double maxD = Math.Max(Math.Max(westScore, eastScore), Math.Max(northScore, southScore));

            for (int meta = 1; meta <= 4; meta++)
            {
                double d = meta switch
                {
                    1 => westScore,
                    2 => eastScore,
                    3 => northScore,
                    4 => southScore,
                    _ => double.NegativeInfinity
                };
                bool solid = meta switch
                {
                    1 => west,
                    2 => east,
                    3 => north,
                    4 => south,
                    _ => false
                };
                if (solid && Math.Abs(d - maxD) < tieEps)
                {
                    return meta;
                }
            }
        }

        int vanilla = ResolveTorchMetaVanillaOrder(reader, x, y, z);
        return vanilla == -1 ? null : vanilla;
    }

    private static void LadderNeighborUpdate(Block block, OnTickEvent ctx)
    {
        Side rotation = ctx.World.Reader.GetBlockMeta(ctx.X, ctx.Y, ctx.Z).ToSide();
        bool hasSupport = rotation == Side.North && ctx.World.Reader.ShouldSuffocate(ctx.X, ctx.Y, ctx.Z + 1) ||
                          rotation == Side.South && ctx.World.Reader.ShouldSuffocate(ctx.X, ctx.Y, ctx.Z - 1) ||
                          rotation == Side.West && ctx.World.Reader.ShouldSuffocate(ctx.X + 1, ctx.Y, ctx.Z) ||
                          rotation == Side.East && ctx.World.Reader.ShouldSuffocate(ctx.X - 1, ctx.Y, ctx.Z);

        if (hasSupport) return;
        block.DropStacks(new OnDropEvent(ctx.World, ctx.X, ctx.Y, ctx.Z, rotation.ToInt()));
        ctx.World.Writer.SetBlock(ctx.X, ctx.Y, ctx.Z, 0);
    }

    private void TorchNeighborUpdate(Block block, OnTickEvent @event)
    {
        if (!BreakIfCannotPlaceAt(block, @event, @event.X, @event.Y, @event.Z)) return;

        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        bool shouldDrop = !@event.World.Reader.ShouldSuffocate(@event.X - 1, @event.Y, @event.Z) && meta == 1 ||
                          !@event.World.Reader.ShouldSuffocate(@event.X + 1, @event.Y, @event.Z) && meta == 2 ||
                          !@event.World.Reader.ShouldSuffocate(@event.X, @event.Y, @event.Z - 1) && meta == 3 ||
                          !@event.World.Reader.ShouldSuffocate(@event.X, @event.Y, @event.Z + 1) && meta == 4 ||
                          !CanPlaceOnGround(@event.World.Reader, @event.X, @event.Y - 1, @event.Z) && meta == 5;

        if (!shouldDrop) return;
        block.DropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
    }

    private bool BreakIfCannotPlaceAt(Block block, OnTickEvent @event, int x, int y, int z)
    {
        if (CanPlaceAt(block, new CanPlaceAtContext(@event.World, 0, x, y, z))) return true;
        block.DropStacks(new OnDropEvent(@event.World, x, y, z, @event.World.Reader.GetBlockMeta(x, y, z)));
        @event.World.Writer.SetBlock(x, y, z, 0);
        return false;
    }

    private static void UpdateLadderBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        Side rotation = reader.GetBlockMeta(x, y, z).ToSide();
        switch (rotation)
        {
            case Side.North:
                block.SetBoundingBox(0.0F, 0.0F, 1.0F - LadderThickness, 1.0F, 1.0F, 1.0F);
                break;
            case Side.South:
                block.SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, LadderThickness);
                break;
            case Side.West:
                block.SetBoundingBox(1.0F - LadderThickness, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
                break;
            case Side.East:
                block.SetBoundingBox(0.0F, 0.0F, 0.0F, LadderThickness, 1.0F, 1.0F);
                break;
        }
    }

    private static void UpdateTorchBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        int rotation = reader.GetBlockMeta(x, y, z) & 7;
        switch (rotation)
        {
            case 1:
                block.SetBoundingBox(0.0F, 0.2F, 0.5F - TorchWidth, TorchWidth * 2.0F, 0.8F, 0.5F + TorchWidth);
                break;
            case 2:
                block.SetBoundingBox(1.0F - TorchWidth * 2.0F, 0.2F, 0.5F - TorchWidth, 1.0F, 0.8F, 0.5F + TorchWidth);
                break;
            case 3:
                block.SetBoundingBox(0.5F - TorchWidth, 0.2F, 0.0F, 0.5F + TorchWidth, 0.8F, TorchWidth * 2.0F);
                break;
            case 4:
                block.SetBoundingBox(0.5F - TorchWidth, 0.2F, 1.0F - TorchWidth * 2.0F, 0.5F + TorchWidth, 0.8F, 1.0F);
                break;
            default:
                block.SetBoundingBox(0.5F - TorchWidthGround, 0.0F, 0.5F - TorchWidthGround, 0.5F + TorchWidthGround, 0.6F, 0.5F + TorchWidthGround);
                break;
        }
    }
}
