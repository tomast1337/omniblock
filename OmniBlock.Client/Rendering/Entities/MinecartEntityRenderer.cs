using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Entities;

public class MinecartEntityRenderer : EntityRenderer
{
    private readonly ModelMinecart _modelMinecart = new();

    public MinecartEntityRenderer() => ShadowRadius = 0.5F;

    public void render(Entity minecart, double x, double y, double z, float yaw, float tickDelta)
    {
        var cart = minecart.Behaviors.Find<MinecartBehavior>()!;
        GLManager.ModelView.Push();
        var interpX = minecart.LastTickX + (minecart.X - minecart.LastTickX) * tickDelta;
        var interpY = minecart.LastTickY + (minecart.Y - minecart.LastTickY) * tickDelta;
        var interpZ = minecart.LastTickZ + (minecart.Z - minecart.LastTickZ) * tickDelta;
        var trackOffset = (double)0.3F;
        var trackPos = cart.GetTrackPosition(minecart, interpX, interpY, interpZ);
        var pitch = minecart.PrevPitch + (minecart.Pitch - minecart.PrevPitch) * tickDelta;
        if (trackPos != null)
        {
            var forwardTrackPos = cart.GetTrackPositionOffset(minecart, interpX, interpY, interpZ, trackOffset) ?? trackPos.Value;
            var backTrackPos = cart.GetTrackPositionOffset(minecart, interpX, interpY, interpZ, -trackOffset) ?? trackPos.Value;

            x += trackPos.Value.X - interpX;
            y += (forwardTrackPos.Y + backTrackPos.Y) / 2.0D - interpY;
            z += trackPos.Value.Z - interpZ;
            var trackDirection = backTrackPos - forwardTrackPos;
            if (trackDirection.Magnitude() != 0.0D)
            {
                trackDirection = trackDirection.Normalize();
                yaw = (float)(Math.Atan2(trackDirection.Z, trackDirection.X) * 180.0D / Math.PI);
                pitch = (float)(Math.Atan(trackDirection.Y) * 73.0D);
            }
        }

        GLManager.ModelView.Translate((float)x, (float)y, (float)z);
        GLManager.ModelView.Rotate(180.0F - yaw, 0.0F, 1.0F, 0.0F);
        GLManager.ModelView.Rotate(-pitch, 0.0F, 0.0F, 1.0F);
        var timeSinceHit = cart.TimeSinceHit(minecart) - tickDelta;
        var damageTaken = cart.Damage(minecart) - tickDelta;
        if (damageTaken < 0.0F)
        {
            damageTaken = 0.0F;
        }

        if (timeSinceHit > 0.0F)
        {
            GLManager.ModelView.Rotate(MathHelper.Sin(timeSinceHit) * timeSinceHit * damageTaken / 10.0F * cart.RockDirection(minecart), 1.0F, 0.0F, 0.0F);
        }

        var cartType = cart.Type(minecart);
        if (cartType != MinecartBehavior.Rideable)
        {
            loadTexture("/terrain.png");
            var blockScale = 12.0F / 16.0F;
            GLManager.ModelView.Scale(blockScale, blockScale, blockScale);
            GLManager.ModelView.Translate(0.0F, 5.0F / 16.0F, 0.0F);
            GLManager.ModelView.Rotate(90.0F, 0.0F, 1.0F, 0.0F);
            if (cartType == MinecartBehavior.Chest)
            {
                BlockRenderer.RenderBlockOnInventory(World.Content.Blocks, World.Content.Blocks.Get("chest"), 0, minecart.GetBrightnessAtEyes(tickDelta), Tessellator.instance);
            }
            else if (cartType == MinecartBehavior.Furnace)
            {
                BlockRenderer.RenderBlockOnInventory(World.Content.Blocks, World.Content.Blocks.Get("furnace"), 0, minecart.GetBrightnessAtEyes(tickDelta), Tessellator.instance);
            }

            GLManager.ModelView.Rotate(-90.0F, 0.0F, 1.0F, 0.0F);
            GLManager.ModelView.Translate(0.0F, -(5.0F / 16.0F), 0.0F);
            GLManager.ModelView.Scale(1.0F / blockScale, 1.0F / blockScale, 1.0F / blockScale);
        }

        loadTexture("/item/cart.png");
        GLManager.ModelView.Scale(-1.0F, -1.0F, 1.0F);
        _modelMinecart.Render(0.0F, 0.0F, -0.1F, 0.0F, 0.0F, 1.0F / 16.0F);
        GLManager.ModelView.Pop();
    }

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta) => render(target, x, y, z, yaw, tickDelta);
}
