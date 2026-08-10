using OmniBlock.Blocks.Entities;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Piston base (normal or sticky, per <paramref name="sticky" />): tracks extend/retract via
///     redstone quasi-connectivity, drives the block-action animation packet, and on retract either
///     pulls the adjacent block along (sticky) or just clears the head. The multi-block push/pull
///     dance temporarily writes <see cref="BlockRegistry.Get("moving_piston")" /> placeholders backed by
///     <see cref="BlockEntityPiston" /> for the client-visible slide animation.
/// </summary>
public sealed class PistonBaseBehavior(bool sticky, int top, int side, int bottom, int extensionSide) : IBlockPhysics, IBlockLifecycle, IBlockTicker, IBlockVisuals
{
    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (@event.Placer is EntityPlayer player)
        {
            int facing = GetFacingForPlacement(@event.X, @event.Y, @event.Z, player);
            @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, facing);
        }

        if (!@event.World.IsRemote)
        {
            CheckExtended(@event.World, @event.X, @event.Y, @event.Z);
        }
    }

    public void OnBlockAction(Block block, OnBlockActionEvent @event)
    {
        int actionId = @event.Data1;
        int facing = @event.Data2;

        switch (actionId)
        {
            case 0: // Extending
                if (Push(block, @event.World, @event.X, @event.Y, @event.Z, facing))
                {
                    @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, facing | 8);
                    @event.World.Broadcaster.PlaySoundAtPos(@event.X + 0.5D, @event.Y + 0.5D, @event.Z + 0.5D, "tile.piston.out", 0.5F, Random.Shared.NextSingle() * 0.25F + 0.6F);
                }

                break;
            case 1: // Retracting

                int headX = @event.X + PistonConstants.HeadOffsetX[facing];
                int headY = @event.Y + PistonConstants.HeadOffsetY[facing];
                int headZ = @event.Z + PistonConstants.HeadOffsetZ[facing];

                BlockEntity? entityAtHead = @event.World.Entities.GetBlockEntity<BlockEntityPiston>(headX, headY, headZ);
                if (entityAtHead is BlockEntityPiston extendingPiston)
                {
                    extendingPiston.Finish();
                }

                @event.World.Writer.SetBlockWithoutNotifyingNeighbors(@event.X, @event.Y, @event.Z, BlockRegistry.Get("moving_piston").Id, facing);
                @event.World.Entities.SetBlockEntity(@event.X, @event.Y, @event.Z, PistonMovingBehavior.CreatePistonBlockEntity(block.Id, facing, facing, false, true));

                if (sticky)
                {
                    int targetX = headX + PistonConstants.HeadOffsetX[facing];
                    int targetY = headY + PistonConstants.HeadOffsetY[facing];
                    int targetZ = headZ + PistonConstants.HeadOffsetZ[facing];

                    int targetId = @event.World.Reader.GetBlockId(targetX, targetY, targetZ);
                    int targetMeta = @event.World.Reader.GetBlockMeta(targetX, targetY, targetZ);
                    bool stickySpit = false;

                    if (targetId == BlockRegistry.Get("moving_piston").Id)
                    {
                        BlockEntity? movingTarget = @event.World.Entities.GetBlockEntity<BlockEntityPiston>(targetX, targetY, targetZ);
                        if (movingTarget is BlockEntityPiston movingPistonTarget && movingPistonTarget.Facing == facing && movingPistonTarget.IsExtending)
                        {
                            if (movingPistonTarget.IsExtensionIncomplete)
                            {
                                movingPistonTarget.AbandonExtensionToStaticBlock();
                                stickySpit = true;
                            }
                            else
                            {
                                movingPistonTarget.Finish();
                                targetId = movingPistonTarget.PushedBlockId;
                                targetMeta = movingPistonTarget.PushedBlockData;
                            }
                        }
                    }

                    if (stickySpit)
                    {
                        @event.World.Writer.SetBlock(headX, headY, headZ, 0);
                    }
                    else if (targetId > 0 && CanMoveBlock(targetId, @event.World, targetX, targetY, targetZ, false) &&
                             (Block.Blocks[targetId].PistonBehavior == PistonBehavior.Normal || targetId == BlockRegistry.Get("piston").Id || targetId == BlockRegistry.Get("sticky_piston").Id))
                    {
                        @event.World.Writer.SetBlock(headX, headY, headZ, 0);
                        @event.World.Writer.SetBlock(targetX, targetY, targetZ, 0);

                        @event.World.Writer.SetBlockWithoutNotifyingNeighbors(headX, headY, headZ, BlockRegistry.Get("moving_piston").Id, targetMeta);
                        @event.World.Entities.SetBlockEntity(headX, headY, headZ, PistonMovingBehavior.CreatePistonBlockEntity(targetId, targetMeta, facing, false, false));
                    }
                    else
                    {
                        @event.World.Writer.SetBlock(headX, headY, headZ, 0);
                    }
                }
                else
                {
                    @event.World.Writer.SetBlock(headX, headY, headZ, 0);
                }

                @event.World.Broadcaster.PlaySoundAtPos(@event.X + 0.5D, @event.Y + 0.5D, @event.Z + 0.5D, "tile.piston.in", 0.5F, Random.Shared.NextSingle() * 0.15F + 0.6F);
                break;
        }
    }

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        int meta = reader.GetBlockMeta(x, y, z);
        if (IsExtended(meta))
        {
            switch (GetFacing(meta))
            {
                case 0: block.SetBoundingBox(0.0F, 0.25F, 0.0F, 1.0F, 1.0F, 1.0F); break;
                case 1: block.SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 12.0F / 16.0F, 1.0F); break;
                case 2: block.SetBoundingBox(0.0F, 0.0F, 0.25F, 1.0F, 1.0F, 1.0F); break;
                case 3: block.SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 12.0F / 16.0F); break;
                case 4: block.SetBoundingBox(0.25F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F); break;
                case 5: block.SetBoundingBox(0.0F, 0.0F, 0.0F, 12.0F / 16.0F, 1.0F, 1.0F); break;
            }
        }
        else
        {
            block.SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
        }
    }

    public void SetupRenderBoundingBox(Block block) => block.SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (!@event.World.IsRemote && @event.World.Entities.GetBlockEntity<BlockEntity>(@event.X, @event.Y, @event.Z) == null)
        {
            CheckExtended(@event.World, @event.X, @event.Y, @event.Z);
        }
    }

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (!@event.World.IsRemote && @event.World.Entities.GetBlockEntity<BlockEntity>(@event.X, @event.Y, @event.Z) == null)
        {
            CheckExtended(@event.World, @event.X, @event.Y, @event.Z);
        }
    }

    public int GetTexture(Block block, Side renderSide, int defaultTexture) => renderSide switch
    {
        Side.Up => GetTopTexture(),
        Side.Down => bottom,
        _ => side
    };

    public int GetTexture(Block block, Side renderSide, int meta, int defaultTexture)
    {
        Side facing = GetFacing(meta).ToSide();
        if (facing > Side.East)
        {
            return block.TextureId;
        }

        if (renderSide == facing)
        {
            return !IsExtended(meta) &&
                   block.BoundingBox is { MinX: <= 0.0D, MinY: <= 0.0D, MinZ: <= 0.0D, MaxX: >= 1.0D, MaxY: >= 1.0D, MaxZ: >= 1.0D }
                ? block.TextureId
                : extensionSide;
        }

        return renderSide == facing.OppositeFace() ? bottom : side;
    }

    public static int GetFacing(int meta) => meta & 7;

    public static bool IsExtended(int meta) => (meta & 8) != 0;

    public int GetTopTexture() => top;

    private static void CheckExtended(IWorldContext ctx, int x, int y, int z)
    {
        int meta = ctx.Reader.GetBlockMeta(x, y, z);
        int facing = GetFacing(meta);
        bool needsExtension = ShouldExtend(ctx, x, y, z, facing);

        if (meta == 7) return;

        switch (needsExtension)
        {
            case true when !IsExtended(meta):
                if (!CanExtend(ctx, x, y, z, facing)) return;

                ctx.Writer.SetBlockMetaWithoutNotifyingNeighbors(x, y, z, facing | 8);
                ctx.Broadcaster.PlayNote(x, y, z, 0, facing); // 0 = Extending
                break;
            case false when IsExtended(meta):
                ctx.Writer.SetBlockMetaWithoutNotifyingNeighbors(x, y, z, facing);
                ctx.Broadcaster.PlayNote(x, y, z, 1, facing); // 1 = Retracting
                break;
        }
    }

    private static bool ShouldExtend(IWorldContext ctx, int x, int y, int z, int facing) =>
        (facing != 0 && ctx.Redstone.IsPoweringSide(x, y - 1, z, 0)) ||
        (facing != 1 && ctx.Redstone.IsPoweringSide(x, y + 1, z, 1)) ||
        (facing != 2 && ctx.Redstone.IsPoweringSide(x, y, z - 1, 2)) ||
        (facing != 3 && ctx.Redstone.IsPoweringSide(x, y, z + 1, 3)) ||
        (facing != 4 && ctx.Redstone.IsPoweringSide(x - 1, y, z, 4)) ||
        (facing != 5 && ctx.Redstone.IsPoweringSide(x + 1, y, z, 5)) ||
        ctx.Redstone.IsPoweringSide(x, y, z, 0) ||
        ctx.Redstone.IsPoweringSide(x, y + 2, z, 1) ||
        ctx.Redstone.IsPoweringSide(x, y + 1, z - 1, 2) ||
        ctx.Redstone.IsPoweringSide(x, y + 1, z + 1, 3) ||
        ctx.Redstone.IsPoweringSide(x - 1, y + 1, z, 4) ||
        ctx.Redstone.IsPoweringSide(x + 1, y + 1, z, 5);

    private static int GetFacingForPlacement(int x, int y, int z, EntityPlayer player)
    {
        if (MathF.Abs((float)player.X - x) < 2.0F && MathF.Abs((float)player.Z - z) < 2.0F)
        {
            double diffY = player.Y + 1.82D - player.StandingEyeHeight;
            if (diffY - y > 2.0D)
            {
                return 1;
            }

            if (y - diffY > 0.0D)
            {
                return 0;
            }
        }

        int playerYaw = MathHelper.Floor(player.Yaw * 4.0F / 360.0F + 0.5D) & 3;
        return playerYaw switch
        {
            0 => 2,
            1 => 5,
            2 => 3,
            3 => 4,
            _ => 0
        };
    }

    private static bool CanMoveBlock(int id, IWorldContext ctx, int x, int y, int z, bool allowBreaking)
    {
        if (id == BlockRegistry.Get("obsidian").Id) return false;

        if (id != BlockRegistry.Get("piston").Id && id != BlockRegistry.Get("sticky_piston").Id)
        {
            if (Math.Abs(Block.Blocks[id].Hardness - (-1.0F)) < 0.001F) return false;
            if (Block.Blocks[id].PistonBehavior == PistonBehavior.Unpushable) return false;
            if (!allowBreaking && Block.Blocks[id].PistonBehavior == PistonBehavior.Destroy) return false;
        }
        else if (IsExtended(ctx.Reader.GetBlockMeta(x, y, z))) return false;

        BlockEntity? targetEntity = ctx.Entities.GetBlockEntity<BlockEntity>(x, y, z);
        return targetEntity == null;
    }

    private static bool CanExtend(IWorldContext ctx, int x, int y, int z, int dir)
    {
        int checkX = x + PistonConstants.HeadOffsetX[dir];
        int checkY = y + PistonConstants.HeadOffsetY[dir];
        int checkZ = z + PistonConstants.HeadOffsetZ[dir];
        int pushCount = 0;

        while (true)
        {
            if (pushCount >= 13) return true;

            if (checkY <= 0 || checkY >= ChuckFormat.WorldHeight - 1) return false;

            int blockId = ctx.Reader.GetBlockId(checkX, checkY, checkZ);
            if (blockId == 0) return true;

            if (!CanMoveBlock(blockId, ctx, checkX, checkY, checkZ, true))
            {
                return false;
            }

            if (Block.Blocks[blockId].PistonBehavior == PistonBehavior.Destroy) return true;

            if (pushCount == 12) return false;

            checkX += PistonConstants.HeadOffsetX[dir];
            checkY += PistonConstants.HeadOffsetY[dir];
            checkZ += PistonConstants.HeadOffsetZ[dir];
            ++pushCount;
        }
    }

    private bool Push(Block block, IWorldContext ctx, int x, int y, int z, int dir)
    {
        int nextX = x + PistonConstants.HeadOffsetX[dir];
        int nextY = y + PistonConstants.HeadOffsetY[dir];
        int nextZ = z + PistonConstants.HeadOffsetZ[dir];
        int pushCount = 0;

        while (true)
        {
            if (pushCount < 13)
            {
                if (nextY <= 0 || nextY >= ChuckFormat.WorldHeight - 1) return false;

                int blockId = ctx.Reader.GetBlockId(nextX, nextY, nextZ);
                if (blockId != 0)
                {
                    if (!CanMoveBlock(blockId, ctx, nextX, nextY, nextZ, true))
                    {
                        return false;
                    }

                    if (Block.Blocks[blockId].PistonBehavior != PistonBehavior.Destroy)
                    {
                        if (pushCount == 12)
                        {
                            return false;
                        }

                        nextX += PistonConstants.HeadOffsetX[dir];
                        nextY += PistonConstants.HeadOffsetY[dir];
                        nextZ += PistonConstants.HeadOffsetZ[dir];
                        ++pushCount;
                        continue;
                    }

                    Block.Blocks[blockId].DropStacks(new OnDropEvent(ctx, nextX, nextY, nextZ, ctx.Reader.GetBlockMeta(nextX, nextY, nextZ)));
                    ctx.Writer.SetBlock(nextX, nextY, nextZ, 0);
                }
            }

            while (nextX != x || nextY != y || nextZ != z)
            {
                int prevX = nextX - PistonConstants.HeadOffsetX[dir];
                int prevY = nextY - PistonConstants.HeadOffsetY[dir];
                int prevZ = nextZ - PistonConstants.HeadOffsetZ[dir];

                int prevBlockId = ctx.Reader.GetBlockId(prevX, prevY, prevZ);
                int prevMeta = ctx.Reader.GetBlockMeta(prevX, prevY, prevZ);

                if (prevBlockId == block.Id && prevX == x && prevY == y && prevZ == z)
                {
                    ctx.Writer.SetBlockWithoutNotifyingNeighbors(nextX, nextY, nextZ, BlockRegistry.Get("moving_piston").Id, dir | (sticky ? 8 : 0));
                    ctx.Entities.SetBlockEntity(nextX, nextY, nextZ, PistonMovingBehavior.CreatePistonBlockEntity(BlockRegistry.Get("piston_head").Id, dir | (sticky ? 8 : 0), dir, true, false));
                }
                else
                {
                    ctx.Writer.SetBlockWithoutNotifyingNeighbors(nextX, nextY, nextZ, BlockRegistry.Get("moving_piston").Id, prevMeta);
                    ctx.Entities.SetBlockEntity(nextX, nextY, nextZ, PistonMovingBehavior.CreatePistonBlockEntity(prevBlockId, prevMeta, dir, true, false));
                }

                nextX = prevX;
                nextY = prevY;
                nextZ = prevZ;
            }

            return true;
        }
    }
}
