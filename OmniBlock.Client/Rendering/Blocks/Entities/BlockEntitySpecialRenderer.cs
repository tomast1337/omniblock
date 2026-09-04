using OmniBlock.Blocks.Entities;
using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Client.Rendering.Blocks.Entities;

public abstract class BlockEntitySpecialRenderer
{
    protected BlockEntityRenderer tileEntityRenderer;

    public abstract void renderTileEntityAt(BlockEntity blockEntity, double x, double y, double z, float tickDelta);

    /// <summary>
    ///     Binds a texture for a block entity's model, telling the entity batch about it as well.
    /// </summary>
    /// <remarks>
    ///     The same three steps as <c>EntityRenderer.loadTexture</c>, and for the same reasons.
    ///     Geometry that reaches the batch is drawn later with whichever texture the batch believes
    ///     is current, so binding without saying so leaves a block entity wearing whatever the last
    ///     mob was wearing. <c>SetTexture</c> comes before the bind because it drains geometry
    ///     belonging to the previous texture, and that flush binds the previous texture.
    /// </remarks>
    protected void bindTextureByName(string texturePath)
    {
        var textureManager = tileEntityRenderer.TextureManager;
        var handle = textureManager.GetTextureId(texturePath);

        EntityBatchRenderer.Instance.RegisterTextureByPath(texturePath, (uint)handle.Id);
        EntityBatchRenderer.Instance.SetTexture((uint)handle.Id);
        textureManager.BindTexture(handle);
    }

    public void setTileEntityRenderer(BlockEntityRenderer renderer) => tileEntityRenderer = renderer;

    public virtual void func_31069_a(World world)
    {
    }

    public TextRenderer getFontRenderer() => tileEntityRenderer.GetFontRenderer();
}
