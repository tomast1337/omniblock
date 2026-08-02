using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;

namespace BetaSharp.Client.Rendering.Core;

/// <summary>
///     Puts a <see cref="RenderState" /> into effect, issuing only the calls that change something.
/// </summary>
/// <remarks>
///     <para>
///         The diffing is not an optimisation, it is what makes adoption safe. A renderer that says
///         what it wants rather than which globals to toggle would otherwise re-issue the whole
///         block on every draw, and the surrounding code still sets these globals itself.
///     </para>
///     <para>
///         Which is also why <see cref="Invalidate" /> exists. Anything still calling
///         <c>Enable</c>, <c>BlendFunc</c> or <c>DepthMask</c> directly changes state this has no
///         way to observe, so what is cached here stops being true. Until the last of those call
///         sites is gone, a caller that mixes the two has to say so.
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
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unhandled blend mode.")
        };

        GLManager.GL.BlendFunc(source, destination);
    }
}
