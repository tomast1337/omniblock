using Silk.NET.OpenGL;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;

namespace BetaSharp.Client.Rendering.Core;

/// <summary>
///     The part of the rendering API that no longer exists on the hardware.
/// </summary>
/// <remarks>
///     <para>
///         None of this runs as fixed-function anything. The client asks for a GL 4.3 core context,
///         where the matrix stack, immediate mode, and display lists are all gone;
///         <c>EmulatedGL</c> and <c>FixedFunctionShader</c> reimplement them on top of shaders and
///         buffers. So this is not a driver dependency to escape, it is an API shape to retire, and
///         moving a call site off it changes nothing about what reaches the GPU.
///     </para>
///     <para>
///         Split out so the dependency is greppable and countable rather than spread through one
///         interface with everything else. <see cref="IGL" /> still inherits it, which is what keeps
///         the migration incremental: call sites move to <c>GLManager.Legacy</c> a directory at a
///         time, each step compiling and running. Removing that inheritance is the last step, and
///         the point at which the compiler starts refusing new uses.
///     </para>
///     <para>
///         <see cref="IGL.Enable" /> and <see cref="IGL.Disable" /> are not here despite taking
///         fixed-function capabilities like fog and alpha test. They are the same calls modern code
///         uses; only some of the enum values passed to them are legacy, and an interface split
///         cannot express that.
///     </para>
/// </remarks>
public unsafe interface IFixedFunctionGL
{
    // Matrix stack.
    void LoadIdentity();
    void MatrixMode(GLEnum mode);
    void PopMatrix();
    void PushMatrix();
    void Rotate(float angle, float x, float y, float z);
    void Scale(float x, float y, float z);
    void Scale(double x, double y, double z);
    void Translate(float x, float y, float z);
    void Frustum(double left, double right, double bottom, double top, double zNear, double zFar);
    void Ortho(double left, double right, double bottom, double top, double zNear, double zFar);

    // Immediate-mode vertex attributes.
    void Color3(float red, float green, float blue);
    void Color3(byte red, byte green, byte blue);
    void Color4(float red, float green, float blue, float alpha);
    void Normal3(float nx, float ny, float nz);

    // Client-side array pointers, superseded by vertex attributes.
    void ColorPointer(int size, ColorPointerType type, uint stride, void* pointer);
    void NormalPointer(NormalPointerType type, uint stride, void* pointer);
    void TexCoordPointer(int size, GLEnum type, uint stride, void* pointer);
    void VertexPointer(int size, GLEnum type, uint stride, void* pointer);
    void DisableClientState(GLEnum array);
    void EnableClientState(GLEnum array);

    // Fixed pipeline state, all of it expressible as shader uniforms.
    void AlphaFunc(GLEnum func, float refValue);
    void ColorMaterial(GLEnum face, GLEnum mode);
    void Fog(GLEnum pname, float param);
    void Fog(GLEnum pname, ReadOnlySpan<float> params_);
    void Light(GLEnum light, GLEnum pname, float* params_);
    void LightModel(GLEnum pname, float* params_);
    void ShadeModel(GLEnum mode);

    /// <summary>
    ///     Hands the emulated matrix state to a shader the caller binds itself, and takes it back
    ///     afterwards. Exists only because the two worlds coexist; it goes when they stop.
    /// </summary>
    void BeginExternalShader(int mvLoc, int projLoc, int texMatLoc = -1);

    /// <inheritdoc cref="BeginExternalShader" />
    void EndExternalShader();
}
