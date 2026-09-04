using OmniBlock.Blocks;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Util.Hit;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class BucketBehavior : IItemBehavior
{
    private readonly Item _bucket;
    private readonly Item _bucketLava;

    private readonly Item _bucketWater;

    // Deferred: BucketBehaviorDefinition.Build() runs during ItemFactory.Create(), which runs
    // before BlockRegistry.Initialize() — resolving "flowing_water"/"flowing_lava" eagerly here
    // would run before blocks exist. Evaluated lazily, well after boot completes.
    private readonly Func<int> _isFullFactory;

    internal BucketBehavior(Func<int> isFull, Item bucket, Item bucketWater, Item bucketLava)
    {
        _isFullFactory = isFull;
        _bucket = bucket;
        _bucketWater = bucketWater;
        _bucketLava = bucketLava;
    }

    private int _isFull => _isFullFactory();

    public ItemStack Use(Item item, ItemStack itemStack, IWorldContext world, EntityPlayer player)
    {
        var partialTick = 1.0F;
        var pitch = player.PrevPitch + (player.Pitch - player.PrevPitch) * partialTick;
        var yaw = player.PrevYaw + (player.Yaw - player.PrevYaw) * partialTick;
        var x = player.PrevX + (player.X - player.PrevX) * partialTick;
        var y = player.PrevY + (player.Y - player.PrevY) * partialTick + 1.62D - player.StandingEyeHeight;
        var z = player.PrevZ + (player.Z - player.PrevZ) * partialTick;
        Vec3D rayStart = new(x, y, z);
        var cosYaw = MathHelper.Cos(-yaw * ((float)Math.PI / 180.0F) - (float)Math.PI);
        var sinYaw = MathHelper.Sin(-yaw * ((float)Math.PI / 180.0F) - (float)Math.PI);
        var cosPitch = -MathHelper.Cos(-pitch * ((float)Math.PI / 180.0F));
        var sinPitch = MathHelper.Sin(-pitch * ((float)Math.PI / 180.0F));
        var dirX = sinYaw * cosPitch;
        var dirZ = cosYaw * cosPitch;
        var reach = player.GameMode.BlockReach;
        var rayEnd = rayStart + new Vec3D(dirX * reach, sinPitch * reach, dirZ * reach);
        var hitResult = world.Reader.Raycast(rayStart, rayEnd, _isFull == 0);

        if (hitResult.Type == HitResultType.Miss)
        {
            return itemStack;
        }

        if (hitResult.Type == HitResultType.Tile)
        {
            var hitX = hitResult.BlockX;
            var hitY = hitResult.BlockY;
            var hitZ = hitResult.BlockZ;
            if (!world.CanInteract(player, hitX, hitY, hitZ))
            {
                return itemStack;
            }

            if (_isFull == 0)
            {
                if (world.Reader.GetMaterial(hitX, hitY, hitZ) == Material.Water && world.Reader.GetBlockMeta(hitX, hitY, hitZ) == 0)
                {
                    world.Writer.SetBlock(hitX, hitY, hitZ, 0);
                    return new ItemStack(_bucketWater);
                }

                if (world.Reader.GetMaterial(hitX, hitY, hitZ) == Material.Lava && world.Reader.GetBlockMeta(hitX, hitY, hitZ) == 0)
                {
                    world.Writer.SetBlock(hitX, hitY, hitZ, 0);
                    return new ItemStack(_bucketLava);
                }
            }
            else
            {
                if (_isFull < 0)
                {
                    return new ItemStack(_bucket);
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
                    if (world.Dimension.EvaporatesWater && _isFull == BlockRegistry.Get("flowing_water").Id)
                    {
                        world.Broadcaster.PlaySoundAtPos(x + 0.5D, y + 0.5D, z + 0.5D, "random.fizz", 0.5F, 2.6F + (world.Random.NextFloat() - world.Random.NextFloat()) * 0.8F);
                        for (var i = 0; i < 8; ++i)
                        {
                            world.Broadcaster.AddParticle("largesmoke", hitX + Random.Shared.NextDouble(), hitY + Random.Shared.NextDouble(), hitZ + Random.Shared.NextDouble(), 0.0D, 0.0D, 0.0D);
                        }
                    }
                    else
                    {
                        world.Writer.SetBlock(hitX, hitY, hitZ, _isFull, 0);
                    }

                    return new ItemStack(_bucket);
                }
            }
        }

        return itemStack;
    }
}
