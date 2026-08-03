using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Client.Rendering.Core.Textures;
using Silk.NET.Maths;
using Color = BetaSharp.Client.UI.Colors.Color;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;

namespace BetaSharp.Client.Rendering.Entities.Models;

public class ModelPart
{
    /// <summary>Upper bound on <see cref="LocalSlot"/> per model. Spider uses 11, the current max.</summary>
    public const int MaxPartsPerModel = 16;

    private static int s_nextStaticVertexOffset;

    private PositionTextureVertex[] Corners;
    private Quad[] Faces;
    private ModelVertexLocal[] _bakedVertices;
    private readonly int TextureOffsetX;
    private readonly int TextureOffsetY;

    /// <summary>Vertex offset of this part's baked geometry in the shared static GPU buffer. -1 until baked.</summary>
    public int StaticVertexOffset { get; private set; } = -1;

    /// <summary>Baked vertex count (36 for a single box; 0 until baked).</summary>
    public int BakedVertexCount => _bakedVertices?.Length ?? 0;

    /// <summary>
    /// Pose-matrix slot within the owning model's per-instance data. Distinct from the
    /// symbolic <see cref="Name"/>-derived part id: that one's shared across e.g. every leg of a
    /// quadruped for the fragment-shader effect hook, but each leg still needs its own pose. -1
    /// until assigned by <see cref="BbModelEntityModel"/>.
    /// </summary>
    public int LocalSlot { get; set; } = -1;

    /// <summary>The <see cref="Name"/>-derived <see cref="EntityShaderIds.ForPart"/> id.</summary>
    public uint SymbolicPartId => _partId;
    public float RotationPointX;
    public float RotationPointY;
    public float RotationPointZ;
    public float RotateAngleX;
    public float RotateAngleY;
    public float RotateAngleZ;
    public bool Mirror = false;
    public bool Visible = true;
    public bool Hidden = false;

    private string? _name;
    private uint _partId;

    /// <summary>
    /// The bbmodel bone this part was built from. Setting it resolves the symbolic id the shader
    /// branches on; parts built by hand rather than loaded from a model leave it null and render
    /// with id 0.
    /// </summary>
    public string? Name
    {
        get => _name;
        set
        {
            _name = value;
            _partId = EntityShaderIds.ForPart(value);
        }
    }

    public ModelPart(int textureOffsetX, int textureOffsetY)
    {
        TextureOffsetX = textureOffsetX;
        TextureOffsetY = textureOffsetY;
    }

    public void AddBox(float x, float y, float z, int width, int height, int depth, float inflation)
    {
        Corners = new PositionTextureVertex[8];
        Faces = new Quad[6];

        float minX = x - inflation;
        float minY = y - inflation;
        float minZ = z - inflation;
        float maxX = x + width + inflation;
        float maxY = y + height + inflation;
        float maxZ = z + depth + inflation;

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
            this.TextureOffsetX + depth + width,
            this.TextureOffsetY + depth,
            this.TextureOffsetX + depth + width + depth,
            this.TextureOffsetY + depth + height);
        Faces[1] = new Quad(
            [frontTopLeft, backTopLeft, backBottomLeft, frontBottomLeft],
            this.TextureOffsetX,
            this.TextureOffsetY + depth,
            this.TextureOffsetX + depth,
            this.TextureOffsetY + depth + height);
        Faces[2] = new Quad(
            [backTopRight, backTopLeft, frontTopLeft, frontTopRight],
            this.TextureOffsetX + depth,
            this.TextureOffsetY,
            this.TextureOffsetX + depth + width,
            this.TextureOffsetY + depth);
        Faces[3] = new Quad(
            [backBottomRight, backBottomLeft, frontBottomLeft, frontBottomRight],
            this.TextureOffsetX + depth + width,
            this.TextureOffsetY,
            this.TextureOffsetX + depth + width + width,
            this.TextureOffsetY + depth);
        Faces[4] = new Quad(
            [frontTopRight, frontTopLeft, frontBottomLeft, frontBottomRight],
            this.TextureOffsetX + depth,
            this.TextureOffsetY + depth,
            this.TextureOffsetX + depth + width,
            this.TextureOffsetY + depth + height);
        Faces[5] = new Quad(
            [backTopLeft, backTopRight, backBottomRight, backBottomLeft],
            this.TextureOffsetX + depth + width + depth,
            this.TextureOffsetY + depth,
            this.TextureOffsetX + depth + width + depth + width,
            this.TextureOffsetY + depth + height);

        if (Mirror)
        {
            for (int faceIndex = 0; faceIndex < Faces.Length; ++faceIndex)
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

    // Collapses to a point at the view-space origin instead of a zero matrix, which would leave
    // an undefined w=0 clip-space position.
    private static readonly System.Numerics.Matrix4x4 s_hiddenPose = new(
        0, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 1);

    /// <summary>Pose matrix captured by the most recent <see cref="CapturePose"/> call.</summary>
    internal System.Numerics.Matrix4x4 CapturedPose { get; private set; }

    /// <summary>Instanced-path counterpart to <see cref="Render"/>: same matrix stack walk, but captures the pose instead of transforming vertices.</summary>
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
                GLManager.GL.Translate(RotationPointX * scale, RotationPointY * scale, RotationPointZ * scale);
                CaptureCurrentMatrix(scale);
                GLManager.GL.Translate(-RotationPointX * scale, -RotationPointY * scale, -RotationPointZ * scale);
            }
        }
        else
        {
            GLManager.GL.PushMatrix();
            GLManager.GL.Translate(RotationPointX * scale, RotationPointY * scale, RotationPointZ * scale);
            if (RotateAngleZ != 0.0F)
            {
                GLManager.GL.Rotate(RotateAngleZ * (180.0F / (float)Math.PI), 0.0F, 0.0F, 1.0F);
            }

            if (RotateAngleY != 0.0F)
            {
                GLManager.GL.Rotate(RotateAngleY * (180.0F / (float)Math.PI), 0.0F, 1.0F, 0.0F);
            }

            if (RotateAngleX != 0.0F)
            {
                GLManager.GL.Rotate(RotateAngleX * (180.0F / (float)Math.PI), 1.0F, 0.0F, 0.0F);
            }

            CaptureCurrentMatrix(scale);
            GLManager.GL.PopMatrix();
        }
    }

    private unsafe void CaptureCurrentMatrix(float scale)
    {
        Span<float> matrixData = stackalloc float[16];
        GLManager.GL.GetFloat(GLEnum.ModelviewMatrix, matrixData);
        System.Numerics.Matrix4x4 modelView = new(
            matrixData[0], matrixData[1], matrixData[2], matrixData[3],
            matrixData[4], matrixData[5], matrixData[6], matrixData[7],
            matrixData[8], matrixData[9], matrixData[10], matrixData[11],
            matrixData[12], matrixData[13], matrixData[14], matrixData[15]);

        // Fold scale in here so the shader never needs its own scale uniform.
        CapturedPose = System.Numerics.Matrix4x4.CreateScale(scale) * modelView;
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
                GLManager.GL.Translate(RotationPointX * scale, RotationPointY * scale, RotationPointZ * scale);
                SubmitBakedVertices(scale);
                GLManager.GL.Translate(-RotationPointX * scale, -RotationPointY * scale, -RotationPointZ * scale);
            }
        }
        else
        {
            GLManager.GL.PushMatrix();
            GLManager.GL.Translate(RotationPointX * scale, RotationPointY * scale, RotationPointZ * scale);
            if (RotateAngleZ != 0.0F)
            {
                GLManager.GL.Rotate(RotateAngleZ * (180.0F / (float)Math.PI), 0.0F, 0.0F, 1.0F);
            }

            if (RotateAngleY != 0.0F)
            {
                GLManager.GL.Rotate(RotateAngleY * (180.0F / (float)Math.PI), 0.0F, 1.0F, 0.0F);
            }

            if (RotateAngleX != 0.0F)
            {
                GLManager.GL.Rotate(RotateAngleX * (180.0F / (float)Math.PI), 1.0F, 0.0F, 0.0F);
            }

            SubmitBakedVertices(scale);
            GLManager.GL.PopMatrix();
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
                GLManager.GL.Translate(RotationPointX * scale, RotationPointY * scale, RotationPointZ * scale);
            }
        }
        else
        {
            GLManager.GL.Translate(RotationPointX * scale, RotationPointY * scale, RotationPointZ * scale);
            if (RotateAngleZ != 0.0F)
            {
                GLManager.GL.Rotate(RotateAngleZ * (180.0F / (float)Math.PI), 0.0F, 0.0F, 1.0F);
            }

            if (RotateAngleY != 0.0F)
            {
                GLManager.GL.Rotate(RotateAngleY * (180.0F / (float)Math.PI), 0.0F, 1.0F, 0.0F);
            }

            if (RotateAngleX != 0.0F)
            {
                GLManager.GL.Rotate(RotateAngleX * (180.0F / (float)Math.PI), 1.0F, 0.0F, 0.0F);
            }
        }
    }

    private void BakeLocalVertices()
    {
        _bakedVertices = new ModelVertexLocal[Faces.Length * 6];
        for (int faceIndex = 0; faceIndex < Faces.Length; ++faceIndex)
        {
            Faces[faceIndex].GetTriangles(_bakedVertices.AsSpan(faceIndex * 6, 6));
        }

        if (StaticVertexOffset < 0)
        {
            StaticVertexOffset = s_nextStaticVertexOffset;
            s_nextStaticVertexOffset += _bakedVertices.Length;
        }
    }

    // System.Numerics gets JIT SIMD here, Silk.NET.Maths doesn't.
    private unsafe void SubmitBakedVertices(float scale)
    {
        if (_bakedVertices == null || _bakedVertices.Length == 0) return;

        Span<float> matrixData = stackalloc float[16];
        GLManager.GL.GetFloat(GLEnum.ModelviewMatrix, matrixData);
        System.Numerics.Matrix4x4 modelView = new(
            matrixData[0], matrixData[1], matrixData[2], matrixData[3],
            matrixData[4], matrixData[5], matrixData[6], matrixData[7],
            matrixData[8], matrixData[9], matrixData[10], matrixData[11],
            matrixData[12], matrixData[13], matrixData[14], matrixData[15]);

        System.Numerics.Matrix4x4 normalMatrix = ComputeNormalMatrix(modelView);

        EmulatedGL emuGl = (EmulatedGL)GLManager.GL;
        Vector4D<float> tintSrc = emuGl.GetCurrentColorTint();
        System.Numerics.Vector4 tint = new(tintSrc.X, tintSrc.Y, tintSrc.Z, tintSrc.W);
        EntityLightingSnapshot lightingSrc = emuGl.GetLightingState();
        System.Numerics.Vector3 light0Dir = new(lightingSrc.Light0Dir.X, lightingSrc.Light0Dir.Y, lightingSrc.Light0Dir.Z);
        System.Numerics.Vector3 light0Diffuse = new(lightingSrc.Light0Diffuse.X, lightingSrc.Light0Diffuse.Y, lightingSrc.Light0Diffuse.Z);
        System.Numerics.Vector3 light1Dir = new(lightingSrc.Light1Dir.X, lightingSrc.Light1Dir.Y, lightingSrc.Light1Dir.Z);
        System.Numerics.Vector3 light1Diffuse = new(lightingSrc.Light1Diffuse.X, lightingSrc.Light1Diffuse.Y, lightingSrc.Light1Diffuse.Z);
        System.Numerics.Vector3 ambient = new(lightingSrc.Ambient.X, lightingSrc.Ambient.Y, lightingSrc.Ambient.Z);
        bool lightingEnabled = lightingSrc.Enabled;

        float a = Math.Clamp(tint.W, 0f, 1f);

        Span<EntityVertex> outVerts = stackalloc EntityVertex[_bakedVertices.Length];

        // Faces are flat-shaded: each block of 6 shares one normal, so lighting runs once per face.
        for (int faceStart = 0; faceStart < _bakedVertices.Length; faceStart += 6)
        {
            System.Numerics.Vector3 localNormal = new(
                _bakedVertices[faceStart].Normal.X, _bakedVertices[faceStart].Normal.Y, _bakedVertices[faceStart].Normal.Z);
            System.Numerics.Vector3 normal = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.TransformNormal(localNormal, normalMatrix));

            System.Numerics.Vector3 lit = lightingEnabled
                ? ambient
                    + light0Diffuse * MathF.Max(System.Numerics.Vector3.Dot(normal, light0Dir), 0f)
                    + light1Diffuse * MathF.Max(System.Numerics.Vector3.Dot(normal, light1Dir), 0f)
                : System.Numerics.Vector3.One;

            float r = Math.Clamp(lit.X * tint.X, 0f, 1f);
            float g = Math.Clamp(lit.Y * tint.Y, 0f, 1f);
            float b = Math.Clamp(lit.Z * tint.Z, 0f, 1f);
            uint color = (uint)new Color(r, g, b, a);

            // Order [0,1,2,2,3,0]: slots 3 and 5 duplicate 2 and 0, so only 4 need transforming.
            int i0 = faceStart, i1 = faceStart + 1, i2 = faceStart + 2, i3 = faceStart + 3, i4 = faceStart + 4, i5 = faceStart + 5;
            TransformVertex(ref outVerts[i0], in _bakedVertices[i0], scale, modelView, color, _partId);
            TransformVertex(ref outVerts[i1], in _bakedVertices[i1], scale, modelView, color, _partId);
            TransformVertex(ref outVerts[i2], in _bakedVertices[i2], scale, modelView, color, _partId);
            TransformVertex(ref outVerts[i4], in _bakedVertices[i4], scale, modelView, color, _partId);
            outVerts[i3] = outVerts[i2];
            outVerts[i5] = outVerts[i0];
        }

        EntityBatchRenderer.Instance.SubmitTriangles(outVerts);

        static void TransformVertex(ref EntityVertex dest, in ModelVertexLocal local, float scale, System.Numerics.Matrix4x4 modelView, uint color, uint partId)
        {
            System.Numerics.Vector3 scaledPos = new System.Numerics.Vector3(local.Position.X, local.Position.Y, local.Position.Z) * scale;
            System.Numerics.Vector4 worldPos = System.Numerics.Vector4.Transform(scaledPos, modelView);

            dest.X = worldPos.X;
            dest.Y = worldPos.Y;
            dest.Z = worldPos.Z;
            dest.U = local.U;
            dest.V = local.V;
            dest.Color = color;
            dest.PartId = partId;
        }
    }

    private static System.Numerics.Matrix4x4 ComputeNormalMatrix(System.Numerics.Matrix4x4 modelView)
    {
        if (!System.Numerics.Matrix4x4.Invert(modelView, out System.Numerics.Matrix4x4 inverted))
        {
            return System.Numerics.Matrix4x4.Identity;
        }

        return System.Numerics.Matrix4x4.Transpose(inverted);
    }
}
