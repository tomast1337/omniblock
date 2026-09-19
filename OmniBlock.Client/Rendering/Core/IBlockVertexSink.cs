using OmniBlock.Blocks;

namespace OmniBlock.Client.Rendering.Core;

/// <summary>
///     The vertex operations required by block renderers.
/// </summary>
/// <remarks>
///     Keeping this surface independent from <see cref="Tessellator" /> lets world chunk meshes be
///     compiled without inheriting immediate-mode drawing, GPU submission, or UI capture state.
/// </remarks>
public interface IBlockVertexSink
{
    void addVertexWithUV(double x, double y, double z, double u, double v);

    void setArrayLayer(int layer);

    void setColorOpaque_F(float red, float green, float blue);

    void setLight(float sky, float block);

    /// <summary>
    ///     Sets the minimum block-light level subsequent world geometry must retain when it is
    ///     relit. Immediate-mode sinks may ignore this hint.
    /// </summary>
    void setMinimumBlockLight(int minimumBlockLight)
    {
    }

    /// <summary>
    ///     Sets light resolved from a specific world cell and retains that sampling intent for
    ///     later light-only updates. Fluids additionally select the brighter of this cell and the
    ///     cell above it.
    /// </summary>
    void setLightSample(
        float sky,
        float block,
        int x,
        int y,
        int z,
        int minimumBlockLight,
        bool includeCellAbove = false) => setLight(sky, block);

    /// <summary>
    ///     Declares the outward side of the next complete quad. Immediate-mode sinks may ignore the
    ///     hint; chunk builders use it to create conservative directional draw ranges.
    /// </summary>
    void setQuadDirection(Side? side)
    {
    }

    /// <summary>Marks subsequent vertices as intentionally independent of world lighting.</summary>
    void setFullBright() => setLight(0.0f, 15.0f);

    void setTranslationF(float x, float y, float z);
}
