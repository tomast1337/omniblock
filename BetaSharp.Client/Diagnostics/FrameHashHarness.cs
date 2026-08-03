using System.Security.Cryptography;
using BetaSharp.Client.Rendering.Core;
using Microsoft.Extensions.Logging;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;

namespace BetaSharp.Client.Diagnostics;

/// <summary>
///     Renders a fixed set of scenes and writes a hash of each one's pixels.
/// </summary>
/// <remarks>
///     <para>
///         For checking that a rendering change drew the same thing it drew before. Run it, keep the
///         file, make the change, run it again, diff the two. A scene whose hash moved is a scene
///         that renders differently, and the name says which part of the pipeline it was covering.
///     </para>
///     <para>
///         Not a unit test and cannot be one: every line of this needs a live GL context, which
///         <c>dotnet test</c> has no way to give it. It is a mode of the client instead.
///     </para>
///     <para>
///         The scenes deliberately avoid the world, entities and assets. They draw known geometry
///         through known state, so a hash changes when the rendering layer changes and not when
///         terrain generation, a texture or a model does. That is a narrower check than a
///         screenshot of real gameplay, and a far less brittle one.
///     </para>
/// </remarks>
internal static unsafe class FrameHashHarness
{
    /// <summary>
    ///     Fixed so the result does not depend on the window, the monitor, or the scaling factor.
    /// </summary>
    private const int Width = 256;

    private const int Height = 256;

    /// <summary>Bound by the scenes that need a texture; owned by <see cref="Run" />.</summary>
    private static uint s_checkerboard;

    public static void Run(string outputPath)
    {
        ILogger logger = Log.Instance.For(nameof(FrameHashHarness));

        uint texture = GLManager.GL.GenTexture();
        GLManager.GL.BindTexture(GLEnum.Texture2D, texture);
        GLManager.GL.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba8, Width, Height, 0, GLEnum.Rgba, GLEnum.UnsignedByte, null);
        GLManager.GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
        GLManager.GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);

        Span<uint> renderbuffers = stackalloc uint[1];
        GLManager.GL.GenRenderbuffers(renderbuffers);
        uint depth = renderbuffers[0];
        GLManager.GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depth);
        GLManager.GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, Width, Height);

        uint framebuffer = GLManager.GL.GenFramebuffer();
        GLManager.GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GLManager.GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
        GLManager.GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depth);

        Silk.NET.OpenGL.GLEnum status = GLManager.GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != Silk.NET.OpenGL.GLEnum.FramebufferComplete)
        {
            logger.LogError("Frame hash target is incomplete ({Status}); wrote nothing.", status);
            return;
        }

        s_checkerboard = CreateCheckerboard();
        List<string> lines = [];

        foreach ((string name, Action draw) in Scenes())
        {
            ResetState();

            GLManager.GL.Viewport(0, 0, Width, Height);
            GLManager.GL.ClearColor(0.0f, 0.0f, 0.0f, 0.4f);
            GLManager.GL.ClearDepth(1.0);
            GLManager.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            draw();

            lines.Add($"{name} {HashPixels()}");
        }

        GLManager.GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GLManager.GL.DeleteFramebuffer(framebuffer);
        GLManager.GL.DeleteRenderbuffer(depth);
        GLManager.GL.DeleteTexture(texture);
        GLManager.GL.DeleteTexture(s_checkerboard);

        File.WriteAllLines(outputPath, lines);
        logger.LogInformation("Wrote {Count} frame hashes to {Path}", lines.Count, outputPath);
    }

    private static string HashPixels()
    {
        byte[] pixels = new byte[Width * Height * 4];

        fixed (byte* p = pixels)
        {
            GLManager.GL.ReadPixels(0, 0, Width, Height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }

        return Convert.ToHexString(SHA256.HashData(pixels))[..16];
    }

    /// <summary>
    ///     Puts the shared state back to a known point, so a scene cannot inherit anything from the
    ///     one before it and the file does not depend on the order they run in.
    /// </summary>
    private static void ResetState()
    {
        // Everything below is a raw call, so what the applier believes is set stops being true.
        GLManager.State.Invalidate();

        GLManager.ModelView.LoadIdentity();
        GLManager.Projection.LoadIdentity();
        GLManager.TextureMatrix.LoadIdentity();

        GLManager.GL.Disable(GLEnum.Blend);
        GLManager.FogEnabled = false;
        GLManager.AlphaTestEnabled = false;
        GLManager.LightingEnabled = false;
        GLManager.GL.Disable(GLEnum.CullFace);
        GLManager.TextureEnabled = false;
        GLManager.GL.Enable(GLEnum.DepthTest);
        GLManager.GL.DepthFunc(GLEnum.Lequal);
        GLManager.GL.DepthMask(true);
        GLManager.GL.ColorMask(true, true, true, true);
        GLManager.Color = new(1.0f, 1.0f, 1.0f, 1.0f);
    }

    /// <summary>
    ///     A texture owing nothing to the asset pack, so a resource change cannot move a hash.
    /// </summary>
    private static uint CreateCheckerboard()
    {
        const int size = 16;
        byte[] pixels = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                byte value = (byte)(((x / 4 + y / 4) % 2 == 0) ? 255 : 40);
                int i = (y * size + x) * 4;
                pixels[i] = value;
                pixels[i + 1] = (byte)(255 - value);
                pixels[i + 2] = value;
                pixels[i + 3] = 255;
            }
        }

        uint texture = GLManager.GL.GenTexture();
        GLManager.GL.BindTexture(GLEnum.Texture2D, texture);

        fixed (byte* p = pixels)
        {
            GLManager.GL.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba8, size, size, 0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
        }

        GLManager.GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
        GLManager.GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
        return texture;
    }

    private static void Ortho() => GLManager.Projection.Ortho(0, Width, Height, 0, -100.0, 100.0);

    private static void Quad(double x, double y, double w, double h) => Quad(x, y, w, h, 0.0);

    private static void Quad(double x, double y, double w, double h, double z)
    {
        Tessellator tessellator = Tessellator.instance;
        tessellator.startDrawingQuads();
        tessellator.addVertex(x, y + h, z);
        tessellator.addVertex(x + w, y + h, z);
        tessellator.addVertex(x + w, y, z);
        tessellator.addVertex(x, y, z);
        tessellator.draw();
    }

    /// <summary>The same quad wound the other way round, so back-face culling removes it.</summary>
    private static void BackFacingQuad(double x, double y, double w, double h, double z)
    {
        Tessellator tessellator = Tessellator.instance;
        tessellator.startDrawingQuads();
        tessellator.addVertex(x, y, z);
        tessellator.addVertex(x + w, y, z);
        tessellator.addVertex(x + w, y + h, z);
        tessellator.addVertex(x, y + h, z);
        tessellator.draw();
    }

    private static void TexturedQuad(double x, double y, double w, double h)
    {
        Tessellator tessellator = Tessellator.instance;
        tessellator.startDrawingQuads();
        tessellator.addVertexWithUV(x, y + h, 0.0, 0.0, 1.0);
        tessellator.addVertexWithUV(x + w, y + h, 0.0, 1.0, 1.0);
        tessellator.addVertexWithUV(x + w, y, 0.0, 1.0, 0.0);
        tessellator.addVertexWithUV(x, y, 0.0, 0.0, 0.0);
        tessellator.draw();
    }

    /// <summary>
    ///     Puts something in the depth buffer for a depth test to reject, under the harness baseline
    ///     rather than the state being measured, so the state under test cannot decide whether the
    ///     priming happened.
    /// </summary>
    private static void PrimeDepth()
    {
        GLManager.Color = new(0.15f, 0.15f, 0.15f, 1.0f);
        Quad(0, 0, 128, 256, 5.0);
    }

    /// <summary>
    ///     Geometry that every field of a <see cref="RenderState" /> changes the look of.
    /// </summary>
    /// <remarks>
    ///     Each piece exists to separate two states that would otherwise hash the same: a quad
    ///     behind the primed depth for the depth test, a back-wound one for culling, and a partly
    ///     transparent one over a destination whose alpha is not 1 so that blending against
    ///     destination alpha differs from blending against a constant.
    /// </remarks>
    private static void OverlappingQuads()
    {
        GLManager.Color = new(1.0f, 0.25f, 0.25f, 1.0f);
        Quad(32, 32, 128, 128, 10.0);

        GLManager.Color = new(0.25f, 1.0f, 0.35f, 1.0f);
        BackFacingQuad(140, 32, 80, 80, 0.0);

        GLManager.Color = new(0.25f, 0.45f, 1.0f, 0.5f);
        Quad(96, 96, 128, 128, 0.0);
    }

    /// <summary>
    ///     A <see cref="RenderState" /> paired with the raw calls it is supposed to stand for.
    /// </summary>
    /// <remarks>
    ///     The raw side sets every field the state does, not only the ones that differ from the
    ///     harness baseline. A pair that only matched because both inherited the same default would
    ///     prove nothing about the applier. It is also written out by hand rather than derived from
    ///     the state, since a check that shares the mapping it is checking proves nothing either.
    /// </remarks>
    private static IEnumerable<(string Name, RenderState State, Action Raw)> StateEquivalences()
    {
        yield return ("opaque", RenderState.Opaque, RawOpaque);
        yield return ("translucent", RenderState.Translucent, RawTranslucent);
        yield return ("additive", RenderState.Additive, RawAdditive);
        yield return ("interface", RenderState.Interface, RawInterface);

        // The three blend modes with a single caller each, which are the easiest to get wrong.
        yield return ("additive-by-alpha", RenderState.Translucent with { Blend = BlendMode.AdditiveByAlpha }, RawAdditiveByAlpha);
        yield return ("multiply", RenderState.Translucent with { Blend = BlendMode.Multiply }, RawMultiply);
        yield return ("source-to-destination-alpha", RenderState.Translucent with { Blend = BlendMode.SourceToDestinationAlpha }, RawSourceToDestinationAlpha);
    }

    /// <summary>Depth, cull and mask settings shared by every state below.</summary>
    private static void RawDepthCullAndMask(bool depthTest, bool depthWrite, bool cull)
    {
        if (depthTest) GLManager.GL.Enable(GLEnum.DepthTest);
        else GLManager.GL.Disable(GLEnum.DepthTest);

        GLManager.GL.DepthMask(depthWrite);
        GLManager.GL.DepthFunc(GLEnum.Lequal);

        if (cull)
        {
            GLManager.GL.Enable(GLEnum.CullFace);
            GLManager.GL.CullFace(GLEnum.Back);
        }
        else
        {
            GLManager.GL.Disable(GLEnum.CullFace);
        }

        GLManager.GL.ColorMask(true, true, true, true);
    }

    private static void RawOpaque()
    {
        GLManager.GL.Disable(GLEnum.Blend);
        RawDepthCullAndMask(depthTest: true, depthWrite: true, cull: true);
    }

    private static void RawTranslucent()
    {
        GLManager.GL.Enable(GLEnum.Blend);
        GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        RawDepthCullAndMask(depthTest: true, depthWrite: false, cull: true);
    }

    private static void RawAdditive()
    {
        GLManager.GL.Enable(GLEnum.Blend);
        GLManager.GL.BlendFunc(GLEnum.One, GLEnum.One);
        RawDepthCullAndMask(depthTest: true, depthWrite: false, cull: true);
    }

    private static void RawInterface()
    {
        GLManager.GL.Enable(GLEnum.Blend);
        GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        RawDepthCullAndMask(depthTest: false, depthWrite: false, cull: false);
    }

    private static void RawAdditiveByAlpha()
    {
        GLManager.GL.Enable(GLEnum.Blend);
        GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.One);
        RawDepthCullAndMask(depthTest: true, depthWrite: false, cull: true);
    }

    private static void RawMultiply()
    {
        GLManager.GL.Enable(GLEnum.Blend);
        GLManager.GL.BlendFunc(GLEnum.DstColor, GLEnum.SrcColor);
        RawDepthCullAndMask(depthTest: true, depthWrite: false, cull: true);
    }

    private static void RawSourceToDestinationAlpha()
    {
        GLManager.GL.Enable(GLEnum.Blend);
        GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.DstAlpha);
        RawDepthCullAndMask(depthTest: true, depthWrite: false, cull: true);
    }

    /// <summary>
    ///     One scene per thing the fixed-function layer still does, so a hash that moves names the
    ///     group that broke.
    /// </summary>
    private static IEnumerable<(string Name, Action Draw)> Scenes()
    {
        yield return ("flat-quad", FlatQuad);
        yield return ("modelview-translate", ModelViewTranslate);
        yield return ("modelview-rotate-scale", ModelViewRotateScale);
        yield return ("modelview-push-pop-nesting", ModelViewPushPopNesting);
        yield return ("projection-frustum", ProjectionFrustum);
        yield return ("texture", TexturedScene);
        yield return ("texture-matrix", TextureMatrixScene);
        yield return ("blend-additive", BlendAdditive);
        yield return ("blend-alpha", BlendAlpha);
        yield return ("alpha-test", AlphaTest);
        yield return ("depth-ordering", DepthOrdering);
        yield return ("fog-linear", FogLinear);
        yield return ("static-mesh", StaticMeshScene);

        // Same geometry under the fixed-function shader and under the slot's own program. The two
        // hashes have to match: a slot exists to take state explicitly, not to draw differently.
        // Nothing else covers this — the game's untextured draws are the selection box and the
        // chunk-border overlay, and the harness draws neither world nor entities.
        yield return ("slot-basic-fallback", () => BasicSlotScene(t => t.draw()));
        yield return ("slot-basic-program", () => BasicSlotScene(t => t.draw(ProgramSlot.Basic)));
        yield return ("slot-basic-fog-fallback", () => BasicSlotFogScene(t => t.draw()));
        yield return ("slot-basic-fog-program", () => BasicSlotFogScene(t => t.draw(ProgramSlot.Basic)));
        yield return ("slot-textured-fallback", () => TexturedSlotScene(t => t.draw()));
        yield return ("slot-textured-program", () => TexturedSlotScene(t => t.draw(ProgramSlot.Textured)));

        // Each of these is drawn twice, once through the raw calls and once through the state it is
        // meant to be equivalent to. The two hashes have to match, which is the property every
        // migration onto RenderState rests on.
        foreach ((string name, RenderState state, Action raw) in StateEquivalences())
        {
            yield return ($"state-{name}-raw", () => StateScene(raw));
            yield return ($"state-{name}-applied", () => StateScene(() => GLManager.State.Apply(state)));
        }
    }

    private static void StateScene(Action setState)
    {
        Ortho();
        PrimeDepth();
        setState();
        OverlappingQuads();
    }

    private static void FlatQuad()
    {
        Ortho();
        GLManager.Color = new(0.9f, 0.2f, 0.3f, 1.0f);
        Quad(32, 32, 192, 192);
    }

    private static void ModelViewTranslate()
    {
        Ortho();
        GLManager.ModelView.Translate(40.0f, 20.0f, 0.0f);
        GLManager.Color = new(0.2f, 0.8f, 0.4f, 1.0f);
        Quad(0, 0, 128, 128);
    }

    private static void ModelViewRotateScale()
    {
        Ortho();
        GLManager.ModelView.Translate(128.0f, 128.0f, 0.0f);
        GLManager.ModelView.Rotate(30.0f, 0.0f, 0.0f, 1.0f);
        GLManager.ModelView.Scale(1.5f, 0.5f, 1.0f);
        GLManager.Color = new(0.3f, 0.5f, 0.9f, 1.0f);
        Quad(-64, -64, 128, 128);
    }

    /// <summary>Nesting is the property a stack exists for: the inner transform must not survive the pop.</summary>
    private static void ModelViewPushPopNesting()
    {
        Ortho();
        GLManager.ModelView.Translate(64.0f, 64.0f, 0.0f);

        GLManager.ModelView.Push();
        GLManager.ModelView.Translate(64.0f, 0.0f, 0.0f);
        GLManager.ModelView.Rotate(45.0f, 0.0f, 0.0f, 1.0f);
        GLManager.Color = new(1.0f, 0.6f, 0.1f, 1.0f);
        Quad(-24, -24, 48, 48);
        GLManager.ModelView.Pop();

        GLManager.Color = new(0.1f, 0.6f, 1.0f, 1.0f);
        Quad(-24, -24, 48, 48);
    }

    private static void ProjectionFrustum()
    {
        GLManager.Projection.Frustum(-1.0, 1.0, -1.0, 1.0, 1.0, 100.0);
        GLManager.ModelView.Translate(0.0f, 0.0f, -4.0f);
        GLManager.ModelView.Rotate(35.0f, 1.0f, 1.0f, 0.0f);
        GLManager.Color = new(0.8f, 0.8f, 0.2f, 1.0f);
        Quad(-1, -1, 2, 2);
    }

    private static void TexturedScene()
    {
        Ortho();
        GLManager.TextureEnabled = true;
        GLManager.GL.BindTexture(GLEnum.Texture2D, s_checkerboard);
        TexturedQuad(16, 16, 224, 224);
    }

    /// <summary>The least exercised of the three stacks, and the one the clouds animate through.</summary>
    private static void TextureMatrixScene()
    {
        Ortho();
        GLManager.TextureEnabled = true;
        GLManager.GL.BindTexture(GLEnum.Texture2D, s_checkerboard);
        GLManager.TextureMatrix.Translate(0.25f, 0.5f, 0.0f);
        GLManager.TextureMatrix.Scale(2.0f, 2.0f, 1.0f);
        TexturedQuad(16, 16, 224, 224);
    }

    private static void BlendAdditive()
    {
        Ortho();
        GLManager.GL.Enable(GLEnum.Blend);
        GLManager.GL.BlendFunc(GLEnum.One, GLEnum.One);
        GLManager.Color = new(0.5f, 0.1f, 0.1f, 1.0f);
        Quad(32, 32, 128, 128);
        GLManager.Color = new(0.1f, 0.1f, 0.5f, 1.0f);
        Quad(96, 96, 128, 128);
    }

    private static void BlendAlpha()
    {
        Ortho();
        GLManager.GL.Enable(GLEnum.Blend);
        GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        GLManager.Color = new(1.0f, 0.0f, 0.0f, 1.0f);
        Quad(32, 32, 128, 128);
        GLManager.Color = new(0.0f, 0.0f, 1.0f, 0.5f);
        Quad(96, 96, 128, 128);
    }

    private static void AlphaTest()
    {
        Ortho();
        GLManager.TextureEnabled = true;
        GLManager.GL.BindTexture(GLEnum.Texture2D, s_checkerboard);
        GLManager.AlphaTestEnabled = true;
        GLManager.AlphaThreshold = 0.5f;
        GLManager.Color = new(1.0f, 1.0f, 1.0f, 0.75f);
        TexturedQuad(16, 16, 224, 224);
    }

    private static void DepthOrdering()
    {
        Ortho();
        GLManager.ModelView.Translate(0.0f, 0.0f, 10.0f);
        GLManager.Color = new(0.2f, 0.9f, 0.2f, 1.0f);
        Quad(32, 32, 128, 128);
        GLManager.ModelView.Translate(0.0f, 0.0f, -20.0f);
        GLManager.Color = new(0.9f, 0.2f, 0.9f, 1.0f);
        Quad(96, 96, 128, 128);
    }

    private static void FogLinear()
    {
        GLManager.Projection.Frustum(-1.0, 1.0, -1.0, 1.0, 1.0, 100.0);
        GLManager.FogEnabled = true;
        GLManager.Fog = new FogState(
            FogCurve.Linear,
            new Vector4D<float>(0.4f, 0.5f, 0.9f, 1.0f),
            Start: 2.0f,
            End: 12.0f,
            Density: 1.0f);

        for (int i = 0; i < 5; i++)
        {
            GLManager.ModelView.LoadIdentity();
            GLManager.ModelView.Translate(0.0f, 0.0f, -2.0f - i * 2.0f);
            GLManager.Color = new(1.0f, 1.0f, 1.0f, 1.0f);
            Quad(-0.8, -0.8, 1.6, 1.6);
        }
    }

    /// <summary>
    ///     The three shapes untextured geometry comes in, drawn however <paramref name="finish" />
    ///     says to finish a batch.
    /// </summary>
    /// <remarks>
    ///     The first two differ in where the colour comes from, which is the part most easily got
    ///     wrong: the Tessellator binds an array to the colour attribute only for geometry built
    ///     with per-vertex colours, so a quad with one colour throughout is relying on the
    ///     attribute's default value instead, and a program that reads the attribute the wrong way
    ///     gets one of the two cases right and looks fine.
    /// </remarks>
    private static void BasicSlotScene(Action<Tessellator> finish)
    {
        Ortho();
        Tessellator tessellator = Tessellator.instance;

        GLManager.Color = new(0.2f, 0.7f, 0.9f, 1.0f);
        tessellator.startDrawingQuads();
        tessellator.addVertex(24, 104, 0.0);
        tessellator.addVertex(104, 104, 0.0);
        tessellator.addVertex(104, 24, 0.0);
        tessellator.addVertex(24, 24, 0.0);
        finish(tessellator);

        tessellator.startDrawingQuads();
        tessellator.setColorRGBA_F(1.0f, 0.0f, 0.0f, 1.0f);
        tessellator.addVertex(152, 104, 0.0);
        tessellator.setColorRGBA_F(0.0f, 1.0f, 0.0f, 1.0f);
        tessellator.addVertex(232, 104, 0.0);
        tessellator.setColorRGBA_F(0.0f, 0.0f, 1.0f, 1.0f);
        tessellator.addVertex(232, 24, 0.0);
        tessellator.setColorRGBA_F(1.0f, 1.0f, 0.0f, 1.0f);
        tessellator.addVertex(152, 24, 0.0);
        finish(tessellator);

        // Lines, which is the whole of what the slot draws in the game.
        tessellator.startDrawing(1);
        tessellator.setColorRGBA_F(1.0f, 1.0f, 0.0f, 1.0f);
        for (int i = 0; i <= 8; i++)
        {
            tessellator.addVertex(24 + i * 26, 152, 0.0);
            tessellator.addVertex(24 + i * 26, 232, 0.0);
        }

        finish(tessellator);
    }

    /// <summary>The same, under the fog and the alpha test that a slot's program has to carry itself.</summary>
    private static void BasicSlotFogScene(Action<Tessellator> finish)
    {
        GLManager.Projection.Frustum(-1.0, 1.0, -1.0, 1.0, 1.0, 100.0);
        GLManager.FogEnabled = true;
        GLManager.Fog = new FogState(
            FogCurve.Linear,
            new Vector4D<float>(0.4f, 0.5f, 0.9f, 1.0f),
            Start: 2.0f,
            End: 12.0f,
            Density: 1.0f);

        GLManager.AlphaTestEnabled = true;
        GLManager.AlphaThreshold = 0.5f;

        Tessellator tessellator = Tessellator.instance;

        for (int i = 0; i < 5; i++)
        {
            GLManager.ModelView.LoadIdentity();
            GLManager.ModelView.Translate(0.0f, 0.0f, -2.0f - i * 2.0f);

            // Alternating either side of the threshold, so half of these have to be discarded.
            GLManager.Color = new(1.0f, 1.0f, 1.0f, i % 2 == 0 ? 1.0f : 0.25f);

            tessellator.startDrawingQuads();
            tessellator.addVertex(-0.8, 0.8, 0.0);
            tessellator.addVertex(0.8, 0.8, 0.0);
            tessellator.addVertex(0.8, -0.8, 0.0);
            tessellator.addVertex(-0.8, -0.8, 0.0);
            finish(tessellator);
        }
    }

    /// <summary>
    ///     Textured geometry, drawn however <paramref name="finish" /> says to finish a batch.
    /// </summary>
    /// <remarks>
    ///     Covers the two places the tint comes from, the texture matrix, and the alpha test, which
    ///     for a textured draw is applied after the tint rather than to the sampled texel — a
    ///     program that tests too early keeps glyphs a caller is fading out.
    /// </remarks>
    private static void TexturedSlotScene(Action<Tessellator> finish)
    {
        Ortho();
        GLManager.TextureEnabled = true;
        GLManager.GL.BindTexture(GLEnum.Texture2D, s_checkerboard);

        Tessellator tessellator = Tessellator.instance;

        GLManager.Color = new(1.0f, 1.0f, 1.0f, 1.0f);
        tessellator.startDrawingQuads();
        tessellator.addVertexWithUV(16, 112, 0.0, 0.0, 1.0);
        tessellator.addVertexWithUV(112, 112, 0.0, 1.0, 1.0);
        tessellator.addVertexWithUV(112, 16, 0.0, 1.0, 0.0);
        tessellator.addVertexWithUV(16, 16, 0.0, 0.0, 0.0);
        finish(tessellator);

        // Per-vertex tint, which binds an array to the colour attribute the quad above left at its
        // default value.
        tessellator.startDrawingQuads();
        tessellator.setColorRGBA_F(1.0f, 0.4f, 0.4f, 1.0f);
        tessellator.addVertexWithUV(144, 112, 0.0, 0.0, 1.0);
        tessellator.setColorRGBA_F(0.4f, 1.0f, 0.4f, 1.0f);
        tessellator.addVertexWithUV(240, 112, 0.0, 1.0, 1.0);
        tessellator.setColorRGBA_F(0.4f, 0.4f, 1.0f, 1.0f);
        tessellator.addVertexWithUV(240, 16, 0.0, 1.0, 0.0);
        tessellator.setColorRGBA_F(1.0f, 1.0f, 0.4f, 1.0f);
        tessellator.addVertexWithUV(144, 16, 0.0, 0.0, 0.0);
        finish(tessellator);

        GLManager.TextureMatrix.Translate(0.25f, 0.5f, 0.0f);
        GLManager.TextureMatrix.Scale(2.0f, 2.0f, 1.0f);

        // Half over the threshold and half under, so the test has to discard some of it.
        GLManager.AlphaTestEnabled = true;
        GLManager.AlphaThreshold = 0.5f;
        GLManager.Color = new(1.0f, 1.0f, 1.0f, 0.25f);
        tessellator.startDrawingQuads();
        tessellator.addVertexWithUV(16, 240, 0.0, 0.0, 1.0);
        tessellator.addVertexWithUV(112, 240, 0.0, 1.0, 1.0);
        tessellator.addVertexWithUV(112, 144, 0.0, 1.0, 0.0);
        tessellator.addVertexWithUV(16, 144, 0.0, 0.0, 0.0);
        finish(tessellator);

        GLManager.Color = new(1.0f, 1.0f, 1.0f, 1.0f);
        tessellator.startDrawingQuads();
        tessellator.addVertexWithUV(144, 240, 0.0, 0.0, 1.0);
        tessellator.addVertexWithUV(240, 240, 0.0, 1.0, 1.0);
        tessellator.addVertexWithUV(240, 144, 0.0, 1.0, 0.0);
        tessellator.addVertexWithUV(144, 144, 0.0, 0.0, 0.0);
        finish(tessellator);
    }

    private static void StaticMeshScene()
    {
        Ortho();
        Tessellator tessellator = Tessellator.instance;
        tessellator.startDrawingQuads();
        tessellator.addVertex(48, 208, 0.0);
        tessellator.addVertex(208, 208, 0.0);
        tessellator.addVertex(208, 48, 0.0);
        tessellator.addVertex(48, 48, 0.0);

        using StaticMesh mesh = tessellator.captureStatic();
        GLManager.Color = new(0.95f, 0.75f, 0.15f, 1.0f);
        mesh.Draw();
    }
}
