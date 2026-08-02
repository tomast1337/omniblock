using BetaSharp.Client.Rendering.Core.OpenGL;
using Silk.NET.OpenGL;

namespace BetaSharp.Client.Rendering.Core;

public class GLManager
{
    public static IGL GL { get; private set; }

    /// <summary>
    ///     The same object as <see cref="GL" />, seen through the fixed-function half of the API.
    /// </summary>
    /// <remarks>
    ///     A call site that reaches for this is one that still needs something GL 4.3 core does not
    ///     have, so the count of references is the size of what is left to port. Nothing changes at
    ///     runtime by moving a call here; it is the same instance, and the emulation was already
    ///     what was executing.
    /// </remarks>
    public static IFixedFunctionGL Legacy => GL;

    public static void Init(GL silkGl)
    {
        GL = new EmulatedGL(silkGl);
    }
}
