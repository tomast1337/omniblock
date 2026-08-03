using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;

namespace BetaSharp.Client.Rendering.Core;

/// <summary>
///     Puts a <see cref="RenderState" /> into effect, issuing only the calls that change something.
/// </summary>
/// <remarks>
///     <para>
///         The cache is only correct because nothing else in the client writes these six pieces of
///         state. Every blend, depth, cull and write mask the renderer wants now comes through here,
///         so what was last applied is what is actually set, and a field can safely be skipped when
///         it already holds the value being asked for.
///     </para>
///     <para>
///         That invariant is the whole thing. Reintroducing a bare <c>Enable</c>, <c>BlendFunc</c>
///         or <c>DepthMask</c> anywhere in the client breaks it silently: the cache keeps claiming a
///         value the driver no longer holds, and the next <see cref="Apply" /> asking for that value
///         issues nothing. The symptom is not a wrong colour in the renderer that cheated, it is a
///         wrong colour in some unrelated one drawn afterwards. Foreign code that sets this state
///         out of our reach — the ImGui backend, the frame-hash harness — has to say so by calling
///         <see cref="Invalidate" />.
///     </para>
/// </remarks>
public sealed class RenderStateApplier
{
    private RenderState _current;
    private bool _known;

    /// <summary>
    ///     Forgets what is believed to be set, so the next <see cref="Apply" /> writes every field.
    /// </summary>
    public void Invalidate() => _known = false;

    public void Apply(in RenderState state)
    {
        if (_known && _current == state)
        {
            return;
        }

        bool all = !_known;

        if (all || _current.Blend != state.Blend)
        {
            ApplyBlend(state.Blend);
        }

        if (all || _current.DepthTest != state.DepthTest)
        {
            if (state.DepthTest) GLManager.GL.Enable(GLEnum.DepthTest);
            else GLManager.GL.Disable(GLEnum.DepthTest);
        }

        if (all || _current.DepthWrite != state.DepthWrite)
        {
            GLManager.GL.DepthMask(state.DepthWrite);
        }

        if (all || _current.DepthCompare != state.DepthCompare)
        {
            GLManager.GL.DepthFunc(state.DepthCompare == DepthCompare.Equal ? GLEnum.Equal : GLEnum.Lequal);
        }

        if (all || _current.Cull != state.Cull)
        {
            if (state.Cull == CullMode.None)
            {
                GLManager.GL.Disable(GLEnum.CullFace);
            }
            else
            {
                GLManager.GL.Enable(GLEnum.CullFace);
                GLManager.GL.CullFace(GLEnum.Back);
            }
        }

        if (all || _current.ColorWrite != state.ColorWrite)
        {
            GLManager.GL.ColorMask(state.ColorWrite, state.ColorWrite, state.ColorWrite, state.ColorWrite);
        }

        _current = state;
        _known = true;
    }

    private static void ApplyBlend(BlendMode mode)
    {
        if (mode == BlendMode.None)
        {
            GLManager.GL.Disable(GLEnum.Blend);
            return;
        }

        GLManager.GL.Enable(GLEnum.Blend);

        (GLEnum source, GLEnum destination) = mode switch
        {
            BlendMode.Alpha => (GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha),
            BlendMode.Additive => (GLEnum.One, GLEnum.One),
            BlendMode.AdditiveByAlpha => (GLEnum.SrcAlpha, GLEnum.One),
            BlendMode.SourceToDestinationAlpha => (GLEnum.SrcAlpha, GLEnum.DstAlpha),
            BlendMode.Multiply => (GLEnum.DstColor, GLEnum.SrcColor),
            BlendMode.Invert => (GLEnum.OneMinusDstColor, GLEnum.OneMinusSrcColor),
            BlendMode.Darken => (GLEnum.Zero, GLEnum.OneMinusSrcColor),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unhandled blend mode.")
        };

        GLManager.GL.BlendFunc(source, destination);
    }
}
