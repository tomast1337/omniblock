using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Util.Hit;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

internal sealed class BoatBehavior : IItemBehavior
{
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
        HitResult hitResult = world.Reader.Raycast(rayStart, rayEnd, true);

        if (hitResult.Type == HitResultType.MISS)
        {
            return itemStack;
        }

        if (hitResult.Type == HitResultType.TILE)
        {
            int hitX = hitResult.BlockX;
            int hitY = hitResult.BlockY;
            int hitZ = hitResult.BlockZ;
            if (!world.IsRemote)
            {
                if (world.Reader.GetBlockId(hitX, hitY, hitZ) == Block.Snow.id)
                {
                    --hitY;
                }

                world.SpawnEntity(new EntityBoat(world, hitX + 0.5F, hitY + 1.0F, hitZ + 0.5F));
            }

            itemStack.ConsumeItem(player);
        }

        return itemStack;
    }
}
