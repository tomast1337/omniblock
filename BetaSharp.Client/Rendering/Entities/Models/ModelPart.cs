using BetaSharp.Client.Guis;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Client.Rendering.Core.Textures;
using Silk.NET.Maths;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;

namespace BetaSharp.Client.Rendering.Entities.Models;

public class ModelPart
{
    private PositionTextureVertex[] Corners;
    private Quad[] Faces;
    private ModelVertexLocal[] _bakedVertices;
    private readonly int TextureOffsetX;
    private readonly int TextureOffsetY;
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
    }

    private unsafe void SubmitBakedVertices(float scale)
    {
        if (_bakedVertices == null || _bakedVertices.Length == 0) return;

        Span<float> matrixData = stackalloc float[16];
        GLManager.GL.GetFloat(GLEnum.ModelviewMatrix, matrixData);
        Matrix4X4<float> modelView = new(
            matrixData[0], matrixData[1], matrixData[2], matrixData[3],
            matrixData[4], matrixData[5], matrixData[6], matrixData[7],
            matrixData[8], matrixData[9], matrixData[10], matrixData[11],
            matrixData[12], matrixData[13], matrixData[14], matrixData[15]);

        Matrix3X3<float> normalMatrix = ComputeNormalMatrix(modelView);

        EmulatedGL emuGl = (EmulatedGL)GLManager.GL;
        Vector4D<float> tint = emuGl.GetCurrentColorTint();
        EntityLightingSnapshot lighting = emuGl.GetLightingState();

        Span<EntityVertex> outVerts = stackalloc EntityVertex[_bakedVertices.Length];
        for (int i = 0; i < _bakedVertices.Length; ++i)
        {
            ModelVertexLocal local = _bakedVertices[i];

            Vector3D<float> scaledPos = local.Position * scale;
            Vector4D<float> worldPos = Vector4D.Transform(new Vector4D<float>(scaledPos, 1f), modelView);

            Vector3D<float> normal = Vector3D.Normalize(TransformDirection(local.Normal, normalMatrix));

            Vector3D<float> lit = lighting.Enabled
                ? lighting.Ambient
                    + lighting.Light0Diffuse * MathF.Max(Vector3D.Dot(normal, lighting.Light0Dir), 0f)
                    + lighting.Light1Diffuse * MathF.Max(Vector3D.Dot(normal, lighting.Light1Dir), 0f)
                : Vector3D<float>.One;

            float r = Math.Clamp(lit.X * tint.X, 0f, 1f);
            float g = Math.Clamp(lit.Y * tint.Y, 0f, 1f);
            float b = Math.Clamp(lit.Z * tint.Z, 0f, 1f);
            float a = Math.Clamp(tint.W, 0f, 1f);

            outVerts[i] = new EntityVertex
            {
                X = worldPos.X,
                Y = worldPos.Y,
                Z = worldPos.Z,
                U = local.U,
                V = local.V,
                Color = (uint)new Color(r, g, b, a),
                PartId = _partId
            };
        }

        EntityBatchRenderer.Instance.SubmitTriangles(outVerts);
    }

    private static Matrix3X3<float> ComputeNormalMatrix(Matrix4X4<float> modelView)
    {
        if (!Matrix4X4.Invert(modelView, out Matrix4X4<float> inverted))
        {
            return Matrix3X3<float>.Identity;
        }

        Matrix4X4<float> t = Matrix4X4.Transpose(inverted);
        return new Matrix3X3<float>(
            t.M11, t.M12, t.M13,
            t.M21, t.M22, t.M23,
            t.M31, t.M32, t.M33);
    }

    private static Vector3D<float> TransformDirection(Vector3D<float> v, Matrix3X3<float> m)
    {
        return new Vector3D<float>(
            v.X * m.M11 + v.Y * m.M21 + v.Z * m.M31,
            v.X * m.M12 + v.Y * m.M22 + v.Z * m.M32,
            v.X * m.M13 + v.Y * m.M23 + v.Z * m.M33);
    }
}
