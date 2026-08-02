using System.Security.Cryptography;
using BetaSharp.Client.Rendering.Core;
using Microsoft.Extensions.Logging;
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

        uint checkerboard = CreateCheckerboard();
        List<string> lines = [];

        foreach ((string name, Action draw) in Scenes(checkerboard))
        {
            ResetState();

            GLManager.GL.Viewport(0, 0, Width, Height);
            GLManager.GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            GLManager.GL.ClearDepth(1.0);
            GLManager.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            draw();

            lines.Add($"{name} {HashPixels()}");
        }

        GLManager.GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GLManager.GL.DeleteFramebuffer(framebuffer);
        GLManager.GL.DeleteRenderbuffer(depth);
        GLManager.GL.DeleteTexture(texture);
        GLManager.GL.DeleteTexture(checkerboard);

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
        GLManager.ModelView.LoadIdentity();
        GLManager.Projection.LoadIdentity();
        GLManager.TextureMatrix.LoadIdentity();

        GLManager.GL.Disable(GLEnum.Blend);
        GLManager.GL.Disable(GLEnum.Fog);
        GLManager.GL.Disable(GLEnum.AlphaTest);
        GLManager.GL.Disable(GLEnum.Lighting);
        GLManager.GL.Disable(GLEnum.CullFace);
        GLManager.GL.Disable(GLEnum.Texture2D);
        GLManager.GL.Enable(GLEnum.DepthTest);
        GLManager.GL.DepthFunc(GLEnum.Lequal);
        GLManager.GL.DepthMask(true);
        GLManager.GL.ColorMask(true, true, true, true);
        GLManager.GL.Color4(1.0f, 1.0f, 1.0f, 1.0f);
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

    private static void Quad(double x, double y, double w, double h)
    {
        Tessellator tessellator = Tessellator.instance;
        tessellator.startDrawingQuads();
        tessellator.addVertex(x, y + h, 0.0);
        tessellator.addVertex(x + w, y + h, 0.0);
        tessellator.addVertex(x + w, y, 0.0);
        tessellator.addVertex(x, y, 0.0);
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
    ///     One scene per thing the fixed-function layer still does, so a hash that moves names the
    ///     group that broke.
    /// </summary>
    private static IEnumerable<(string Name, Action Draw)> Scenes(uint checkerboard)
    {
        yield return ("flat-quad", () =>
        {
            Ortho();
            GLManager.GL.Color4(0.9f, 0.2f, 0.3f, 1.0f);
            Quad(32, 32, 192, 192);
        });

        yield return ("modelview-translate", () =>
        {
            Ortho();
            GLManager.ModelView.Translate(40.0f, 20.0f, 0.0f);
            GLManager.GL.Color4(0.2f, 0.8f, 0.4f, 1.0f);
            Quad(0, 0, 128, 128);
        });

        yield return ("modelview-rotate-scale", () =>
        {
            Ortho();
            GLManager.ModelView.Translate(128.0f, 128.0f, 0.0f);
            GLManager.ModelView.Rotate(30.0f, 0.0f, 0.0f, 1.0f);
            GLManager.ModelView.Scale(1.5f, 0.5f, 1.0f);
            GLManager.GL.Color4(0.3f, 0.5f, 0.9f, 1.0f);
            Quad(-64, -64, 128, 128);
        });

        // Nesting is the property a stack exists for: the inner transform must not survive the pop.
        yield return ("modelview-push-pop-nesting", () =>
        {
            Ortho();
            GLManager.ModelView.Translate(64.0f, 64.0f, 0.0f);

            GLManager.ModelView.Push();
            GLManager.ModelView.Translate(64.0f, 0.0f, 0.0f);
            GLManager.ModelView.Rotate(45.0f, 0.0f, 0.0f, 1.0f);
            GLManager.GL.Color4(1.0f, 0.6f, 0.1f, 1.0f);
            Quad(-24, -24, 48, 48);
            GLManager.ModelView.Pop();

            GLManager.GL.Color4(0.1f, 0.6f, 1.0f, 1.0f);
            Quad(-24, -24, 48, 48);
        });

        yield return ("projection-frustum", () =>
        {
            GLManager.Projection.Frustum(-1.0, 1.0, -1.0, 1.0, 1.0, 100.0);
            GLManager.ModelView.Translate(0.0f, 0.0f, -4.0f);
            GLManager.ModelView.Rotate(35.0f, 1.0f, 1.0f, 0.0f);
            GLManager.GL.Color4(0.8f, 0.8f, 0.2f, 1.0f);
            Quad(-1, -1, 2, 2);
        });

        yield return ("texture", () =>
        {
            Ortho();
            GLManager.GL.Enable(GLEnum.Texture2D);
            GLManager.GL.BindTexture(GLEnum.Texture2D, checkerboard);
            TexturedQuad(16, 16, 224, 224);
        });

        // The least exercised of the three stacks, and the one the clouds animate through.
        yield return ("texture-matrix", () =>
        {
            Ortho();
            GLManager.GL.Enable(GLEnum.Texture2D);
            GLManager.GL.BindTexture(GLEnum.Texture2D, checkerboard);
            GLManager.TextureMatrix.Translate(0.25f, 0.5f, 0.0f);
            GLManager.TextureMatrix.Scale(2.0f, 2.0f, 1.0f);
            TexturedQuad(16, 16, 224, 224);
        });

        yield return ("blend-additive", () =>
        {
            Ortho();
            GLManager.GL.Enable(GLEnum.Blend);
            GLManager.GL.BlendFunc(GLEnum.One, GLEnum.One);
            GLManager.GL.Color4(0.5f, 0.1f, 0.1f, 1.0f);
            Quad(32, 32, 128, 128);
            GLManager.GL.Color4(0.1f, 0.1f, 0.5f, 1.0f);
            Quad(96, 96, 128, 128);
        });

        yield return ("blend-alpha", () =>
        {
            Ortho();
            GLManager.GL.Enable(GLEnum.Blend);
            GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
            GLManager.GL.Color4(1.0f, 0.0f, 0.0f, 1.0f);
            Quad(32, 32, 128, 128);
            GLManager.GL.Color4(0.0f, 0.0f, 1.0f, 0.5f);
            Quad(96, 96, 128, 128);
        });

        yield return ("alpha-test", () =>
        {
            Ortho();
            GLManager.GL.Enable(GLEnum.Texture2D);
            GLManager.GL.BindTexture(GLEnum.Texture2D, checkerboard);
            GLManager.GL.Enable(GLEnum.AlphaTest);
            GLManager.Legacy.AlphaFunc(GLEnum.Greater, 0.5f);
            GLManager.GL.Color4(1.0f, 1.0f, 1.0f, 0.75f);
            TexturedQuad(16, 16, 224, 224);
        });

        yield return ("depth-ordering", () =>
        {
            Ortho();
            GLManager.ModelView.Translate(0.0f, 0.0f, 10.0f);
            GLManager.GL.Color4(0.2f, 0.9f, 0.2f, 1.0f);
            Quad(32, 32, 128, 128);
            GLManager.ModelView.Translate(0.0f, 0.0f, -20.0f);
            GLManager.GL.Color4(0.9f, 0.2f, 0.9f, 1.0f);
            Quad(96, 96, 128, 128);
        });

        yield return ("fog-linear", () =>
        {
            GLManager.Projection.Frustum(-1.0, 1.0, -1.0, 1.0, 1.0, 100.0);
            GLManager.GL.Enable(GLEnum.Fog);
            GLManager.Legacy.Fog(GLEnum.FogMode, (float)GLEnum.Linear);
            GLManager.Legacy.Fog(GLEnum.FogStart, 2.0f);
            GLManager.Legacy.Fog(GLEnum.FogEnd, 12.0f);
            GLManager.Legacy.Fog(GLEnum.FogColor, [0.4f, 0.5f, 0.9f, 1.0f]);

            for (int i = 0; i < 5; i++)
            {
                GLManager.ModelView.LoadIdentity();
                GLManager.ModelView.Translate(0.0f, 0.0f, -2.0f - i * 2.0f);
                GLManager.GL.Color4(1.0f, 1.0f, 1.0f, 1.0f);
                Quad(-0.8, -0.8, 1.6, 1.6);
            }
        });

        yield return ("static-mesh", () =>
        {
            Ortho();
            Tessellator tessellator = Tessellator.instance;
            tessellator.startDrawingQuads();
            tessellator.addVertex(48, 208, 0.0);
            tessellator.addVertex(208, 208, 0.0);
            tessellator.addVertex(208, 48, 0.0);
            tessellator.addVertex(48, 48, 0.0);

            using StaticMesh mesh = tessellator.captureStatic();
            GLManager.GL.Color4(0.95f, 0.75f, 0.15f, 1.0f);
            mesh.Draw();
        });
    }
}
