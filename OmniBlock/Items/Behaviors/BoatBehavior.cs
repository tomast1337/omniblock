using OmniBlock.Entities;
using OmniBlock.Util.Hit;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class BoatBehavior : IItemBehavior
{
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
        var hitResult = world.Reader.Raycast(rayStart, rayEnd, true);

        if (hitResult.Type == HitResultType.Miss)
        {
            return itemStack;
        }

        if (hitResult.Type == HitResultType.Tile)
        {
            var hitX = hitResult.BlockX;
            var hitY = hitResult.BlockY;
            var hitZ = hitResult.BlockZ;
            if (!world.IsRemote)
            {
                if (world.Reader.GetBlockId(hitX, hitY, hitZ) == world.Content.Blocks.Get("omniblock:snow").Id)
                {
                    --hitY;
                }

                world.SpawnEntity(Entities.Behaviors.BoatBehavior.Launch(world, hitX + 0.5F, hitY + 1.0F, hitZ + 0.5F));
            }

            itemStack.ConsumeItem(player);
        }

        return itemStack;
    }
}
