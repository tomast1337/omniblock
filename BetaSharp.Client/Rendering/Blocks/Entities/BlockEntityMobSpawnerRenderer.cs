using OmniBlock.Blocks.Entities;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Entities;

namespace OmniBlock.Client.Rendering.Blocks.Entities;

public class BlockEntityMobSpawnerRenderer : BlockEntitySpecialRenderer
{

    private readonly Dictionary<string, Entity> _entityDict = [];

    public void renderTileEntityMobSpawner(BlockEntityMobSpawner spawner, double x, double y, double z, float tickDelta)
    {
        GLManager.ModelView.Push();
        GLManager.ModelView.Translate((float)x + 0.5F, (float)y, (float)z + 0.5F);
        _entityDict.TryGetValue(spawner.GetSpawnedEntityId(), out Entity? displayEntity);
        if (displayEntity == null)
        {
            displayEntity = EntityRegistry.Create(spawner.GetSpawnedEntityId(), null);
            _entityDict.Add(spawner.GetSpawnedEntityId(), displayEntity);
        }

        if (displayEntity != null)
        {
            displayEntity.SetWorld(spawner.World);
            float scale = 7.0F / 16.0F;
            GLManager.ModelView.Translate(0.0F, 0.4F, 0.0F);
            GLManager.ModelView.Rotate((float)(spawner.LastRotation + (spawner.Rotation - spawner.LastRotation) * (double)tickDelta) * 10.0F, 0.0F, 1.0F, 0.0F);
            GLManager.ModelView.Rotate(-30.0F, 1.0F, 0.0F, 0.0F);
            GLManager.ModelView.Translate(0.0F, -0.4F, 0.0F);
            GLManager.ModelView.Scale(scale, scale, scale);
            displayEntity.SetPositionAndAnglesKeepPrevAngles(x, y, z, 0.0F, 0.0F);
            EntityRenderDispatcher.Instance.RenderEntityWithPosYaw(displayEntity, 0.0D, 0.0D, 0.0D, 0.0F, tickDelta);
        }

        GLManager.ModelView.Pop();
    }

    public override void renderTileEntityAt(BlockEntity blockEntity, double x, double y, double z, float tickDelta)
    {
        renderTileEntityMobSpawner((BlockEntityMobSpawner)blockEntity, x, y, z, tickDelta);
    }
}
