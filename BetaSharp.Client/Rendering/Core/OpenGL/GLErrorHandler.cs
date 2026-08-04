using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;

namespace BetaSharp.Client.Rendering.Core.OpenGL;

internal class GLErrorHandler
{
    /// <summary>
    ///     Keeps the installed handler reachable.
    /// </summary>
    /// <remarks>
    ///     OpenGL holds a raw function pointer to <see cref="_debugProcCallback" /> and nothing on
    ///     the GL side keeps the managed delegate alive. If the owning handler is collected, the
    ///     pointer dangles and the next GL message faults in native code. The reference lives here
    ///     rather than at the call site so that discarding the result of <see cref="Install" />
    ///     cannot break it.
    /// </remarks>
    private static GLErrorHandler? s_installed;

    private readonly ILogger _logger = Log.Instance.For<GLErrorHandler>();
    private readonly DebugProc _debugProcCallback;

    /// <summary>
    ///     Call sites already reported, so a fault that repeats every frame is described once.
    /// </summary>
    /// <remarks>
    ///     Without this the log is thousands of identical lines that say what went wrong and never
    ///     where. The message text alone does not distinguish two draws failing for the same reason.
    /// </remarks>
    private readonly HashSet<string> _reportedSites = [];

    /// <summary>
    ///     Routes GL debug messages to the log. Replaces any handler installed before it, so a
    ///     recreated context gets a callback bound to the context that is current now.
    /// </summary>
    public static void Install() => s_installed = new GLErrorHandler();

    private unsafe GLErrorHandler()
    {
        GL gl = Display.getGL()!;

        _debugProcCallback = DebugCallback;

        gl.Enable(EnableCap.DebugOutput);
        gl.Enable(EnableCap.DebugOutputSynchronous);
        gl.DebugMessageCallback(_debugProcCallback, (void*)0);
        gl.DebugMessageControl(
            DebugSource.DontCare,
            DebugType.DontCare,
            DebugSeverity.DontCare,
            0, (uint*)0, true);

        gl.DebugMessageControl(
            DebugSource.DontCare,
            DebugType.DebugTypePerformance,
            DebugSeverity.DontCare,
            0, (uint*)0, false);
    }

    private void DebugCallback(
        Silk.NET.OpenGL.GLEnum source,
        Silk.NET.OpenGL.GLEnum type,
        int id,
        Silk.NET.OpenGL.GLEnum severity,
        int length,
        nint message,
        nint _)
    {
        if (severity == Silk.NET.OpenGL.GLEnum.DebugSeverityNotification) return;

        string msg = Marshal.PtrToStringAnsi(message, length) ?? "(null)";

        LogLevel logLevel = type == Silk.NET.OpenGL.GLEnum.DebugTypeError
            ? LogLevel.Error
            : LogLevel.Warning;

        _logger.Log(logLevel,
            "[GL] [{Severity}] [{Source}] [{Type}] (id={Id}): {Message}",
            severity, source, type, id, msg);

        // Debug output is synchronous, so the managed frames that issued the call are still on the
        // stack. That is the only thing here that says which draw is at fault; the message says
        // what GL disliked and never where.
        if (type == Silk.NET.OpenGL.GLEnum.DebugTypeError)
        {
            ReportCallSite(msg);
        }

        Debugger.Break();
    }

    /// <summary>
    ///     Names the draw that faulted, and the two pieces of state that most often explain a
    ///     GL_INVALID_OPERATION on a draw in a core profile.
    /// </summary>
    /// <remarks>
    ///     A core context has no default vertex array and no fixed-function program, so drawing with
    ///     either bound to zero is an error rather than a fallback. Both are easy to leave that way
    ///     — every path here unbinds after itself — and neither is visible in the GL message.
    /// </remarks>
    private void ReportCallSite(string message)
    {
        string stack = new StackTrace(2, true).ToString();
        string site = message + stack;

        if (!_reportedSites.Add(site))
        {
            return;
        }

        GL gl = Display.getGL()!;
        gl.GetInteger(GetPName.CurrentProgram, out int program);
        gl.GetInteger(GetPName.VertexArrayBinding, out int vertexArray);

        _logger.LogError(
            "[GL] first occurrence of the above. Bound program {Program}, bound vertex array " +
            "{VertexArray} (zero for either is itself an error on a draw).\n{Stack}",
            program, vertexArray, stack);
    }
}
