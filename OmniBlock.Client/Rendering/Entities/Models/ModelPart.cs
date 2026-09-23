using System.Numerics;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.Rendering.Entities.Models;

public class ModelPart
{
    /// <summary>Upper bound on <see cref="LocalSlot" /> per model. Spider uses 11, the current max.</summary>
    public const int MaxPartsPerModel = 16;

    private static int s_nextStaticVertexOffset;
    private readonly bool _reserveStaticGeometry;

    // Collapses to a point at the view-space origin instead of a zero matrix, which would leave
    // an undefined w=0 clip-space position.
    private static readonly Matrix4x4 s_hiddenPose = new(
        0, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 1);

    private readonly int TextureOffsetX;
    private readonly int TextureOffsetY;
    private ModelVertexLocal[] _bakedVertices = [];

    private string? _name;

    private PositionTextureVertex[] Corners = [];
    private Quad[] Faces = [];
    public bool Hidden = false;
    public bool Mirror = false;
    public float RotateAngleX;
    public float RotateAngleY;
    public float RotateAngleZ;
    public float RotationPointX;
    public float RotationPointY;
    public float RotationPointZ;
    public bool Visible = true;

    public ModelPart(int textureOffsetX, int textureOffsetY) : this(textureOffsetX, textureOffsetY, true) { }

    internal ModelPart(int textureOffsetX, int textureOffsetY, bool reserveStaticGeometry)
    {
        _reserveStaticGeometry = reserveStaticGeometry;
        TextureOffsetX = textureOffsetX;
        TextureOffsetY = textureOffsetY;
    }

    /// <summary>Vertex offset of this part's baked geometry in the shared static GPU buffer. -1 until baked.</summary>
    public int StaticVertexOffset { get; private set; } = -1;

    /// <summary>Baked vertex count (36 for a single box; 0 until baked).</summary>
    public int BakedVertexCount => _bakedVertices?.Length ?? 0;

    /// <summary>
    ///     Pose-matrix slot within the owning model's per-instance data. Distinct from the
    ///     symbolic <see cref="Name" />-derived part id: that one's shared across e.g. every leg of a
    ///     quadruped for the fragment-shader effect hook, but each leg still needs its own pose. -1
    ///     until assigned by <see cref="BbModelEntityModel" />.
    /// </summary>
    public int LocalSlot { get; set; } = -1;

    /// <summary>The <see cref="Name" />-derived <see cref="EntityShaderIds.ForPart" /> id.</summary>
    public uint SymbolicPartId { get; private set; }

    /// <summary>
    ///     The bbmodel bone this part was built from. Setting it resolves the symbolic id the shader
    ///     branches on; parts built by hand rather than loaded from a model leave it null and render
    ///     with id 0.
    /// </summary>
    public string? Name
    {
        get => _name;
        set
        {
            _name = value;
            SymbolicPartId = EntityShaderIds.ForPart(value);
        }
    }

    /// <summary>Pose matrix captured by the most recent <see cref="CapturePose" /> call.</summary>
    internal Matrix4x4 CapturedPose { get; private set; }

    public void AddBox(float x, float y, float z, int width, int height, int depth, float inflation)
    {
        Corners = new PositionTextureVertex[8];
        Faces = new Quad[6];

        var minX = x - inflation;
        var minY = y - inflation;
        var minZ = z - inflation;
        var maxX = x + width + inflation;
        var maxY = y + height + inflation;
        var maxZ = z + depth + inflation;

        if (Mirror)
        {
            (maxX, minX) = (minX, maxX);
        }


        PositionTextureVertex frontTopLeft = new(minX, minY, minZ, 0.0F, 0.0F);
        PositionTextureVertex frontTopRight = new(maxX, minY, minZ, 0.0F, 8.0F);
        PositionTextureVertex frontBottomRight = new(maxX, maxY, minZ, 8.0F, 8.0F);
        PositionTextureVertex frontBottomLeft = new(minX, maxY, minZ, 8.0F, 0.0F);
        PositionTextureVertex backTopLeft = new(minX, minY, maxZ, 0.0F, 0.0F);
        PositionTextureVertex backTopRight = new(maxX, minY, maxZ, 0.0F, 8.0F);
        PositionTextureVertex backBottomRight = new(maxX, maxY, maxZ, 8.0F, 8.0F);
        PositionTextureVertex backBottomLeft = new(minX, maxY, maxZ, 8.0F, 0.0F);

        Corners[0] = frontTopLeft;
        Corners[1] = frontTopRight;
        Corners[2] = frontBottomRight;
        Corners[3] = frontBottomLeft;
        Corners[4] = backTopLeft;
        Corners[5] = backTopRight;
        Corners[6] = backBottomRight;
        Corners[7] = backBottomLeft;

        Faces[0] = new Quad(
            [backTopRight, frontTopRight, frontBottomRight, backBottomRight],
            TextureOffsetX + depth + width,
            TextureOffsetY + depth,
            TextureOffsetX + depth + width + depth,
            TextureOffsetY + depth + height);
        Faces[1] = new Quad(
            [frontTopLeft, backTopLeft, backBottomLeft, frontBottomLeft],
            TextureOffsetX,
            TextureOffsetY + depth,
            TextureOffsetX + depth,
            TextureOffsetY + depth + height);
        Faces[2] = new Quad(
            [backTopRight, backTopLeft, frontTopLeft, frontTopRight],
            TextureOffsetX + depth,
            TextureOffsetY,
            TextureOffsetX + depth + width,
            TextureOffsetY + depth);
        Faces[3] = new Quad(
            [backBottomRight, backBottomLeft, frontBottomLeft, frontBottomRight],
            TextureOffsetX + depth + width,
            TextureOffsetY,
            TextureOffsetX + depth + width + width,
            TextureOffsetY + depth);
        Faces[4] = new Quad(
            [frontTopRight, frontTopLeft, frontBottomLeft, frontBottomRight],
            TextureOffsetX + depth,
            TextureOffsetY + depth,
            TextureOffsetX + depth + width,
            TextureOffsetY + depth + height);
        Faces[5] = new Quad(
            [backTopLeft, backTopRight, backBottomRight, backBottomLeft],
            TextureOffsetX + depth + width + depth,
            TextureOffsetY + depth,
            TextureOffsetX + depth + width + depth + width,
            TextureOffsetY + depth + height);

        if (Mirror)
        {
            for (var faceIndex = 0; faceIndex < Faces.Length; ++faceIndex)
            {
                Faces[faceIndex].flipFace();
            }
        }

        BakeLocalVertices();
    }

    public void SetRotationPoint(float x, float y, float z)
    {
        RotationPointX = x;
        RotationPointY = y;
        RotationPointZ = z;
    }

    /// <summary>Local baked geometry, for the static buffer upload.</summary>
    internal ReadOnlySpan<ModelVertexLocal> GetBakedVertices() => _bakedVertices;

    /// <summary>Instanced-path counterpart to <see cref="Render" />: same matrix stack walk, but captures the pose instead of transforming vertices.</summary>
    public void CapturePose(float scale)
    {
        if (Hidden || !Visible)
        {
            CapturedPose = s_hiddenPose;
            return;
        }

        if (RotateAngleX == 0.0F && RotateAngleY == 0.0F && RotateAngleZ == 0.0F)
        {
            if (RotationPointX == 0.0F && RotationPointY == 0.0F && RotationPointZ == 0.0F)
            {
                CaptureCurrentMatrix(scale);
            }
            else
            {
                RenderSystem.ModelView.Translate(RotationPointX * scale, RotationPointY * scale, RotationPointZ * scale);
                CaptureCurrentMatrix(scale);
                RenderSystem.ModelView.Translate(-RotationPointX * scale, -RotationPointY * scale, -RotationPointZ * scale);
            }
        }
        else
        {
            RenderSystem.ModelView.Push();
            RenderSystem.ModelView.Translate(RotationPointX * scale, RotationPointY * scale, RotationPointZ * scale);
            if (RotateAngleZ != 0.0F)
            {
                RenderSystem.ModelView.Rotate(RotateAngleZ * (180.0F / (float)Math.PI), 0.0F, 0.0F, 1.0F);
            }

            if (RotateAngleY != 0.0F)
            {
                RenderSystem.ModelView.Rotate(RotateAngleY * (180.0F / (float)Math.PI), 0.0F, 1.0F, 0.0F);
            }

            if (RotateAngleX != 0.0F)
            {
                RenderSystem.ModelView.Rotate(RotateAngleX * (180.0F / (float)Math.PI), 1.0F, 0.0F, 0.0F);
            }

            CaptureCurrentMatrix(scale);
            RenderSystem.ModelView.Pop();
        }
    }

    private void CaptureCurrentMatrix(float scale)
    {
        var mv = RenderSystem.ModelView.Top;
        Matrix4x4 modelView = new(
            mv.M11, mv.M12, mv.M13, mv.M14,
            mv.M21, mv.M22, mv.M23, mv.M24,
            mv.M31, mv.M32, mv.M33, mv.M34,
            mv.M41, mv.M42, mv.M43, mv.M44);

        // Fold scale in here so the shader never needs its own scale uniform.
        CapturedPose = Matrix4x4.CreateScale(scale) * modelView;
    }

    public void Render(float scale)
    {
        if (Hidden) return;

        if (!Visible) return;

        if (RotateAngleX == 0.0F && RotateAngleY == 0.0F && RotateAngleZ == 0.0F)
        {
            if (RotationPointX == 0.0F && RotationPointY == 0.0F && RotationPointZ == 0.0F)
            {
                SubmitBakedVertices(scale);
            }
            else
            {
                RenderSystem.ModelView.Translate(RotationPointX * scale, RotationPointY * scale, RotationPointZ * scale);
                SubmitBakedVertices(scale);
                RenderSystem.ModelView.Translate(-RotationPointX * scale, -RotationPointY * scale, -RotationPointZ * scale);
            }
        }
        else
        {
            RenderSystem.ModelView.Push();
            RenderSystem.ModelView.Translate(RotationPointX * scale, RotationPointY * scale, RotationPointZ * scale);
            if (RotateAngleZ != 0.0F)
            {
                RenderSystem.ModelView.Rotate(RotateAngleZ * (180.0F / (float)Math.PI), 0.0F, 0.0F, 1.0F);
            }

            if (RotateAngleY != 0.0F)
            {
                RenderSystem.ModelView.Rotate(RotateAngleY * (180.0F / (float)Math.PI), 0.0F, 1.0F, 0.0F);
            }

            if (RotateAngleX != 0.0F)
            {
                RenderSystem.ModelView.Rotate(RotateAngleX * (180.0F / (float)Math.PI), 1.0F, 0.0F, 0.0F);
            }

            SubmitBakedVertices(scale);
            RenderSystem.ModelView.Pop();
        }
    }

    public void Transform(float scale)
    {
        if (Hidden) return;

        if (!Visible) return;

        if (RotateAngleX == 0.0F && RotateAngleY == 0.0F && RotateAngleZ == 0.0F)
        {
            if (RotationPointX != 0.0F || RotationPointY != 0.0F || RotationPointZ != 0.0F)
            {
                RenderSystem.ModelView.Translate(RotationPointX * scale, RotationPointY * scale, RotationPointZ * scale);
            }
        }
        else
        {
            RenderSystem.ModelView.Translate(RotationPointX * scale, RotationPointY * scale, RotationPointZ * scale);
            if (RotateAngleZ != 0.0F)
            {
                RenderSystem.ModelView.Rotate(RotateAngleZ * (180.0F / (float)Math.PI), 0.0F, 0.0F, 1.0F);
            }

            if (RotateAngleY != 0.0F)
            {
                RenderSystem.ModelView.Rotate(RotateAngleY * (180.0F / (float)Math.PI), 0.0F, 1.0F, 0.0F);
            }

            if (RotateAngleX != 0.0F)
            {
                RenderSystem.ModelView.Rotate(RotateAngleX * (180.0F / (float)Math.PI), 1.0F, 0.0F, 0.0F);
            }
        }
    }

    private void BakeLocalVertices()
    {
        _bakedVertices = new ModelVertexLocal[Faces.Length * 6];
        for (var faceIndex = 0; faceIndex < Faces.Length; ++faceIndex)
        {
            Faces[faceIndex].GetTriangles(_bakedVertices.AsSpan(faceIndex * 6, 6));
        }

        if (_reserveStaticGeometry && StaticVertexOffset < 0)
        {
            StaticVertexOffset = s_nextStaticVertexOffset;
            s_nextStaticVertexOffset += _bakedVertices.Length;
        }
    }

    // System.Numerics gets JIT SIMD here, Silk.NET.Maths doesn't.
    private unsafe void SubmitBakedVertices(float scale)
    {
        if (_bakedVertices == null || _bakedVertices.Length == 0) return;

        var mv = RenderSystem.ModelView.Top;
        Matrix4x4 modelView = new(
            mv.M11, mv.M12, mv.M13, mv.M14,
            mv.M21, mv.M22, mv.M23, mv.M24,
            mv.M31, mv.M32, mv.M33, mv.M34,
            mv.M41, mv.M42, mv.M43, mv.M44);

        var normalMatrix = ComputeNormalMatrix(modelView);

        var tintSrc = RenderSystem.Color;
        Vector4 tint = new(tintSrc.X, tintSrc.Y, tintSrc.Z, tintSrc.W);
        var lightingSrc = RenderSystem.Lighting;
        Vector3 light0Dir = new(lightingSrc.Light0Direction.X, lightingSrc.Light0Direction.Y, lightingSrc.Light0Direction.Z);
        Vector3 light0Diffuse = new(lightingSrc.Light0Diffuse.X, lightingSrc.Light0Diffuse.Y, lightingSrc.Light0Diffuse.Z);
        Vector3 light1Dir = new(lightingSrc.Light1Direction.X, lightingSrc.Light1Direction.Y, lightingSrc.Light1Direction.Z);
        Vector3 light1Diffuse = new(lightingSrc.Light1Diffuse.X, lightingSrc.Light1Diffuse.Y, lightingSrc.Light1Diffuse.Z);
        Vector3 ambient = new(lightingSrc.Ambient.X, lightingSrc.Ambient.Y, lightingSrc.Ambient.Z);
        var lightingEnabled = RenderSystem.LightingEnabled;

        var a = Math.Clamp(tint.W, 0f, 1f);

        Span<EntityVertex> outVerts = stackalloc EntityVertex[_bakedVertices.Length];

        // Faces are flat-shaded: each block of 6 shares one normal, so lighting runs once per face.
        for (var faceStart = 0; faceStart < _bakedVertices.Length; faceStart += 6)
        {
            Vector3 localNormal = new(
                _bakedVertices[faceStart].Normal.X, _bakedVertices[faceStart].Normal.Y, _bakedVertices[faceStart].Normal.Z);
            var normal = Vector3.Normalize(Vector3.TransformNormal(localNormal, normalMatrix));

            var lit = lightingEnabled
                ? ambient
                  + light0Diffuse * MathF.Max(Vector3.Dot(normal, light0Dir), 0f)
                  + light1Diffuse * MathF.Max(Vector3.Dot(normal, light1Dir), 0f)
                : Vector3.One;

            var r = Math.Clamp(lit.X * tint.X, 0f, 1f);
            var g = Math.Clamp(lit.Y * tint.Y, 0f, 1f);
            var b = Math.Clamp(lit.Z * tint.Z, 0f, 1f);
            var color = (uint)new Color(r, g, b, a);

            // Order [0,1,2,2,3,0]: slots 3 and 5 duplicate 2 and 0, so only 4 need transforming.
            int i0 = faceStart, i1 = faceStart + 1, i2 = faceStart + 2, i3 = faceStart + 3, i4 = faceStart + 4, i5 = faceStart + 5;
            TransformVertex(ref outVerts[i0], in _bakedVertices[i0], scale, modelView, color, SymbolicPartId);
            TransformVertex(ref outVerts[i1], in _bakedVertices[i1], scale, modelView, color, SymbolicPartId);
            TransformVertex(ref outVerts[i2], in _bakedVertices[i2], scale, modelView, color, SymbolicPartId);
            TransformVertex(ref outVerts[i4], in _bakedVertices[i4], scale, modelView, color, SymbolicPartId);
            outVerts[i3] = outVerts[i2];
            outVerts[i5] = outVerts[i0];
        }

        EntityBatchRenderer.Instance.SubmitTriangles(outVerts);

        static void TransformVertex(ref EntityVertex dest, in ModelVertexLocal local, float scale, Matrix4x4 modelView, uint color, uint partId)
        {
            var scaledPos = new Vector3(local.Position.X, local.Position.Y, local.Position.Z) * scale;
            var worldPos = Vector4.Transform(scaledPos, modelView);

            dest.X = worldPos.X;
            dest.Y = worldPos.Y;
            dest.Z = worldPos.Z;
            dest.U = local.U;
            dest.V = local.V;
            dest.Color = color;
            dest.PartId = partId;
        }
    }

    private static Matrix4x4 ComputeNormalMatrix(Matrix4x4 modelView)
    {
        if (!Matrix4x4.Invert(modelView, out var inverted))
        {
            return Matrix4x4.Identity;
        }

        return Matrix4x4.Transpose(inverted);
    }
}
