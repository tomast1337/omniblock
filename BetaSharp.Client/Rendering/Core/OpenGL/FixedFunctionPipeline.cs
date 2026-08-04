using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;

namespace BetaSharp.Client.Rendering.Core.OpenGL;

/// <summary>The state a draw is shaded by, for the programs that ask for it.</summary>
/// <remarks>
///     <para>
///         This was <c>EmulatedGL</c>, and the name was accurate while it emulated fixed-function
///         entry points over a core context. There are none left to emulate, and there is no longer
///         a shader here either: what used to be applied on the way past <see cref="DrawArrays" /> is
///         now read by whichever <c>ISlotProgram</c> a draw names, which is why every draw has to
///         name one.
///     </para>
///     <para>
///         What is left is the state itself — the matrix stacks, the tint, the facing, the fog and
///         the lights — held here because it is per-context and assigned from everywhere. A backend
///         with no fixed-function heritage would keep exactly this and drop the class name.
///     </para>
/// </remarks>
public unsafe class FixedFunctionPipeline : LegacyGL
{
    private readonly MatrixStack _modelViewStack = new();
    private readonly MatrixStack _projectionStack = new();
    private readonly MatrixStack _textureStack = new();

    public FixedFunctionPipeline(GL gl) : base(gl)
    {
    }

    /// <inheritdoc cref="GLManager.ModelView" />
    public MatrixStack ModelView => _modelViewStack;

    /// <inheritdoc cref="ModelView" />
    public MatrixStack Projection => _projectionStack;

    /// <inheritdoc cref="ModelView" />
    public MatrixStack TextureMatrix => _textureStack;

    /// <inheritdoc cref="GLManager.Color" />
    public Vector4D<float> Color
    {
        get;
        set
        {
            field = value;
            SilkGL.VertexAttrib4(1, value.X, value.Y, value.Z, value.W);
        }
    } = Vector4D<float>.One;

    /// <inheritdoc cref="GLManager.Normal" />
    public Vector3D<float> Normal
    {
        get;
        set
        {
            field = value;
            SilkGL.VertexAttrib3(3, value.X, value.Y, value.Z);
        }
    }

    /// <inheritdoc cref="GLManager.Fog" />
    public FogState Fog { get; set; } = FogState.Default;

    /// <inheritdoc cref="GLManager.Lighting" />
    public LightingState Lighting { get; set; } = LightingState.Default;

    /// <summary>
    ///     The four capabilities a core context does not have, which are values a program reads here.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read rather than applied: a slot's program uploads whichever of these it declares, and
    ///         the batching renderers key their buckets on them. Nothing here touches GL.
    ///     </para>
    ///     <para>
    ///         Only the alpha test and fog raise <see cref="LegacyGL.RasterStateChanging" />, which
    ///         is what drains <c>EntityBatchRenderer</c>. That batch bakes lighting into vertex
    ///         colours and tracks its own texture, so those two it already accounts for; the alpha
    ///         threshold and the fog it does not, and queued geometry would draw under the wrong
    ///         one. Both deduplicate because redundant assignment is constant and a flush is not
    ///         free.
    ///     </para>
    /// </remarks>
    public bool TextureEnabled { get; set; }

    /// <inheritdoc cref="TextureEnabled" />
    public bool LightingEnabled { get; set; }

    /// <inheritdoc cref="TextureEnabled" />
    public bool AlphaTestEnabled
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnRasterStateChanging();
        }
    }

    /// <inheritdoc cref="TextureEnabled" />
    public bool FogEnabled
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnRasterStateChanging();
        }
    }

    /// <inheritdoc cref="GLManager.ShadeModel" />
    public ShadeModel ShadeModel { get; set; } = ShadeModel.Smooth;

    /// <inheritdoc cref="GLManager.AlphaThreshold" />
    public float AlphaThreshold { get; set; } = 0.1f;

    public override void BufferData(GLEnum target, nuint size, void* data, GLEnum usage)
    {
        SilkGL.BufferData(target.ToModern(), size, data, usage.ToModern());
    }

    /// <summary>
    ///     Whether to check what is bound before each draw. Switches itself off once it has found
    ///     something, so the cost is paid only until the first report.
    /// </summary>
    /// <remarks>
    ///     Temporary. This exists because the GL debug callback reported nothing useful, and unlike
    ///     that callback this runs on the thread that issued the draw, where a stack trace is
    ///     guaranteed to mean something. Delete it once the fault it is looking for is fixed.
    /// </remarks>
    private static bool s_validateDraws = true;

    private static readonly ILogger s_logger = Log.Instance.For(nameof(FixedFunctionPipeline));

    public override void DrawArrays(GLEnum mode, int first, uint count)
    {
        OnImmediateGeometryDrawing();

        if (s_validateDraws)
        {
            SilkGL.GetInteger(GetPName.CurrentProgram, out int program);
            SilkGL.GetInteger(GetPName.VertexArrayBinding, out int vertexArray);

            // A core profile has neither a default program nor a default vertex array, so either at
            // zero fails the draw outright.
            if (program == 0 || vertexArray == 0)
            {
                s_validateDraws = false;
                s_logger.LogError(
                    "Draw with program {Program} and vertex array {VertexArray}; zero for either " +
                    "cannot draw. Further checks disabled.\n{Stack}",
                    program, vertexArray, new StackTrace(1, true).ToString());
            }
        }

        SilkGL.DrawArrays(mode.ToModern(), first, count);
    }

    public override void LineWidth(float width)
    {
        // TODO: ADD A BETTER WAY TO DO LINE WIDTH
        SilkGL.LineWidth(1.0f); // > 1.0 IS DEPRECATED
    }
}
