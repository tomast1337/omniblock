using BetaSharp.Blocks;
using BetaSharp.Blocks.Materials;
using BetaSharp.Entities;
using BetaSharp.Util.Hit;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

internal sealed class BucketBehavior : IItemBehavior
{
    private readonly int _isFull;

    private static readonly Item s_bucket = Item.ByName("bucket");
    private static readonly Item s_bucketWater = Item.ByName("bucket_water");
    private static readonly Item s_bucketLava = Item.ByName("bucket_lava");
    private static readonly Item s_milk = Item.ByName("milk");

    internal BucketBehavior(int isFull) => _isFull = isFull;

    public ItemStack Use(Item item, ItemStack itemStack, IWorldContext world, EntityPlayer player)
    {
        float partialTick = 1.0F;
        float pitch = player.PrevPitch + (player.Pitch - player.PrevPitch) * partialTick;
        float yaw = player.PrevYaw + (player.Yaw - player.PrevYaw) * partialTick;
        double x = player.PrevX + (player.X - player.PrevX) * partialTick;
        double y = player.PrevY + (player.Y - player.PrevY) * partialTick + 1.62D - player.StandingEyeHeight;
        double z = player.PrevZ + (player.Z - player.PrevZ) * partialTick;
        Vec3D rayStart = new(x, y, z);
        float cosYaw = MathHelper.Cos(-yaw * ((float)Math.PI / 180.0F) - (float)Math.PI);
        float sinYaw = MathHelper.Sin(-yaw * ((float)Math.PI / 180.0F) - (float)Math.PI);
        float cosPitch = -MathHelper.Cos(-pitch * ((float)Math.PI / 180.0F));
        float sinPitch = MathHelper.Sin(-pitch * ((float)Math.PI / 180.0F));
        float dirX = sinYaw * cosPitch;
        float dirZ = cosYaw * cosPitch;
        float reach = player.GameMode.BlockReach;
        Vec3D rayEnd = rayStart + new Vec3D(dirX * reach, sinPitch * reach, dirZ * reach);
        HitResult hitResult = world.Reader.Raycast(rayStart, rayEnd, _isFull == 0);

        if (hitResult.Type == HitResultType.MISS)
        {
            return itemStack;
        }

        if (hitResult.Type == HitResultType.TILE)
        {
            int hitX = hitResult.BlockX;
            int hitY = hitResult.BlockY;
            int hitZ = hitResult.BlockZ;
            if (!world.CanInteract(player, hitX, hitY, hitZ))
            {
                return itemStack;
            }

            if (_isFull == 0)
            {
                if (world.Reader.GetMaterial(hitX, hitY, hitZ) == Material.Water && world.Reader.GetBlockMeta(hitX, hitY, hitZ) == 0)
                {
                    world.Writer.SetBlock(hitX, hitY, hitZ, 0);
                    return new ItemStack(s_bucketWater);
                }

                if (world.Reader.GetMaterial(hitX, hitY, hitZ) == Material.Lava && world.Reader.GetBlockMeta(hitX, hitY, hitZ) == 0)
                {
                    world.Writer.SetBlock(hitX, hitY, hitZ, 0);
                    return new ItemStack(s_bucketLava);
                }
            }
            else
            {
                if (_isFull < 0)
                {
                    return new ItemStack(s_bucket);
                }

                if (hitResult.Side == 0)
                {
                    --hitY;
                }

                if (hitResult.Side == 1)
                {
                    ++hitY;
                }

                if (hitResult.Side == 2)
                {
                    --hitZ;
                }

                if (hitResult.Side == 3)
                {
                    ++hitZ;
                }

                if (hitResult.Side == 4)
                {
                    --hitX;
                }

                if (hitResult.Side == 5)
                {
                    ++hitX;
                }

                if (world.Reader.IsAir(hitX, hitY, hitZ) || !world.Reader.GetMaterial(hitX, hitY, hitZ).IsSolid)
                {
                    if (world.Dimension.EvaporatesWater && _isFull == Block.FlowingWater.id)
                    {
                        world.Broadcaster.PlaySoundAtPos(x + 0.5D, y + 0.5D, z + 0.5D, "random.fizz", 0.5F, 2.6F + (world.Random.NextFloat() - world.Random.NextFloat()) * 0.8F);
                        for (int i = 0; i < 8; ++i)
                        {
                            world.Broadcaster.AddParticle("largesmoke", hitX + Random.Shared.NextDouble(), hitY + Random.Shared.NextDouble(), hitZ + Random.Shared.NextDouble(), 0.0D, 0.0D, 0.0D);
                        }
                    }
                    else
                    {
                        world.Writer.SetBlock(hitX, hitY, hitZ, _isFull, 0);
                    }

                    return new ItemStack(s_bucket);
                }
            }
        }
        else if (_isFull == 0 && hitResult.Entity is EntityCow)
        {
            return new ItemStack(s_milk);
        }

        return itemStack;
    }
}
