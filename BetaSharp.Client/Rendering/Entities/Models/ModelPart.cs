using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Client.Rendering.Core.Textures;

namespace BetaSharp.Client.Rendering.Entities.Models;

public class ModelPart
{
    private PositionTextureVertex[] Corners;
    private Quad[] Faces;
    private readonly int TextureOffsetX;
    private readonly int TextureOffsetY;
    public float RotationPointX;
    public float RotationPointY;
    public float RotationPointZ;
    public float RotateAngleX;
    public float RotateAngleY;
    public float RotateAngleZ;
    private bool Compiled;
    private uint DisplayList;
    public bool Mirror = false;
    public bool Visible = true;
    public bool Hidden = false;

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

        if (!Mirror) return;

        for (int faceIndex = 0; faceIndex < Faces.Length; ++faceIndex)
        {
            Faces[faceIndex].flipFace();
        }
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

        if (!Compiled) CompileDisplayList(scale);

        if (RotateAngleX == 0.0F && RotateAngleY == 0.0F && RotateAngleZ == 0.0F)
        {
            if (RotationPointX == 0.0F && RotationPointY == 0.0F && RotationPointZ == 0.0F)
            {
                GLManager.GL.CallList(DisplayList);
            }
            else
            {
                GLManager.GL.Translate(RotationPointX * scale, RotationPointY * scale, RotationPointZ * scale);
                GLManager.GL.CallList(DisplayList);
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

            GLManager.GL.CallList(DisplayList);
            GLManager.GL.PopMatrix();
        }
    }

    public void Transform(float scale)
    {
        if (Hidden) return;

        if (!Visible) return;

        if (!Compiled)
        {
            CompileDisplayList(scale);
        }

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

    private void CompileDisplayList(float scale)
    {
        DisplayList = (uint)GLAllocation.generateDisplayLists(1);
        GLManager.GL.NewList(DisplayList, GLEnum.Compile);
        Tessellator tessellator = Tessellator.instance;

        for (int faceIndex = 0; faceIndex < Faces.Length; ++faceIndex)
        {
            Faces[faceIndex].draw(tessellator, scale);
        }

        GLManager.GL.EndList();
        Compiled = true;
    }
}
