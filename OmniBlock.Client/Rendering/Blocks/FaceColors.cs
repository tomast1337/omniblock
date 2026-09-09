using OmniBlock.Client.Rendering.Core;

namespace OmniBlock.Client.Rendering.Blocks;

/// <summary>The two light levels at one corner of a face, each 0..15 and usually fractional.</summary>
/// <remarks>
///     Fractional because smooth lighting means a corner is the mean of the four cells that meet
///     there. Levels rather than brightness — the ramp is applied by the terrain shader.
/// </remarks>
internal readonly record struct CornerLight(float Sky, float Block)
{
    /// <summary>
    ///     A single number for deciding which way to split the quad, so the seam runs along the
    ///     darker diagonal.
    /// </summary>
    /// <remarks>
    ///     The sum of the channels rather than what they collapse to, so the decision does not
    ///     depend on the time of day. It used to, implicitly, and a mesh that flipped its diagonals
    ///     at dusk would have to be rebuilt for it.
    /// </remarks>
    public float FlipWeight => Sky + Block;

    /// <summary>The mean of four cells, which is what a smooth-lit corner is.</summary>
    public static CornerLight Mean(CornerLight a, CornerLight b, CornerLight c, CornerLight d) =>
        new((a.Sky + b.Sky + c.Sky + d.Sky) * 0.25f, (a.Block + b.Block + c.Block + d.Block) * 0.25f);
}

/// <summary>The colour and light at each of a face's four corners.</summary>
/// <remarks>
///     Read only when the context has ambient occlusion on. The sub-renderers turn it off and set
///     one colour and one light for a whole primitive themselves, so what they pass here is ignored.
/// </remarks>
internal readonly ref struct FaceColors(
    float rTl,
    float gTl,
    float bTl,
    float rBl,
    float gBl,
    float bBl,
    float rBr,
    float gBr,
    float bBr,
    float rTr,
    float gTr,
    float bTr,
    CornerLight lTl,
    CornerLight lBl,
    CornerLight lBr,
    CornerLight lTr)
{
    public readonly float RedTopLeft = rTl, GreenTopLeft = gTl, BlueTopLeft = bTl;
    public readonly float RedBottomLeft = rBl, GreenBottomLeft = gBl, BlueBottomLeft = bBl;
    public readonly float RedBottomRight = rBr, GreenBottomRight = gBr, BlueBottomRight = bBr;
    public readonly float RedTopRight = rTr, GreenTopRight = gTr, BlueTopRight = bTr;

    public readonly CornerLight LightTopLeft = lTl;
    public readonly CornerLight LightBottomLeft = lBl;
    public readonly CornerLight LightBottomRight = lBr;
    public readonly CornerLight LightTopRight = lTr;

    /// <summary>
    ///     The block's own colour and its face shading, with no light in it.
    /// </summary>
    /// <remarks>
    ///     How lit a corner is used to be multiplied in here, which is what made terrain
    ///     impossible for a shader pack to relight — by the time anything downstream saw a vertex,
    ///     its colour and its brightness were one number. The light now rides alongside.
    /// </remarks>
    internal static FaceColors AssignVertexColors(
        CornerLight l0, CornerLight l1, CornerLight l2, CornerLight l3,
        float r, float g, float b, float faceShadow, bool tint)
    {
        var tr = (tint ? r : 1.0F) * faceShadow;
        var tg = (tint ? g : 1.0F) * faceShadow;
        var tb = (tint ? b : 1.0F) * faceShadow;

        return new FaceColors(
            tr, tg, tb,
            tr, tg, tb,
            tr, tg, tb,
            tr, tg, tb,
            l0, l1, l2, l3);
    }

    public readonly void ApplyTopLeft(IBlockVertexSink tess)
    {
        tess.setColorOpaque_F(RedTopLeft, GreenTopLeft, BlueTopLeft);
        tess.setLight(LightTopLeft.Sky, LightTopLeft.Block);
    }

    public readonly void ApplyBottomLeft(IBlockVertexSink tess)
    {
        tess.setColorOpaque_F(RedBottomLeft, GreenBottomLeft, BlueBottomLeft);
        tess.setLight(LightBottomLeft.Sky, LightBottomLeft.Block);
    }

    public readonly void ApplyBottomRight(IBlockVertexSink tess)
    {
        tess.setColorOpaque_F(RedBottomRight, GreenBottomRight, BlueBottomRight);
        tess.setLight(LightBottomRight.Sky, LightBottomRight.Block);
    }

    public readonly void ApplyTopRight(IBlockVertexSink tess)
    {
        tess.setColorOpaque_F(RedTopRight, GreenTopRight, BlueTopRight);
        tess.setLight(LightTopRight.Sky, LightTopRight.Block);
    }
}
