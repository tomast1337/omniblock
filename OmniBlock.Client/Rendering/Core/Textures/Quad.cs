using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Core.Textures;

internal struct Quad
{
    private PositionTextureVertex[] _vertexPositions;
    public int NVertices;
    private readonly bool _invertNormal;

    private Quad(PositionTextureVertex[] vertices)
    {
        NVertices = 0;
        _invertNormal = false;
        _vertexPositions = vertices;
        NVertices = vertices.Length;
    }

    public Quad(PositionTextureVertex[] vertices, int texMinU, int texMinV, int texMaxU, int texMaxV) : this(vertices)
    {
        var uMargin = 0.0015625F;
        var vMargin = 0.003125F;
        vertices[0] = vertices[0].setTexturePosition(texMaxU / 64.0F - uMargin, texMinV / 32.0F + vMargin);
        vertices[1] = vertices[1].setTexturePosition(texMinU / 64.0F + uMargin, texMinV / 32.0F + vMargin);
        vertices[2] = vertices[2].setTexturePosition(texMinU / 64.0F + uMargin, texMaxV / 32.0F - vMargin);
        vertices[3] = vertices[3].setTexturePosition(texMaxU / 64.0F - uMargin, texMaxV / 32.0F - vMargin);
    }

    public void flipFace()
    {
        var reversed = new PositionTextureVertex[_vertexPositions.Length];

        for (var i = 0; i < _vertexPositions.Length; ++i)
        {
            reversed[i] = _vertexPositions[_vertexPositions.Length - i - 1];
        }

        _vertexPositions = reversed;
    }

    /// <summary>
    ///     Writes this face as 2 local-space (unscaled) triangles (6 verts, matching the
    ///     A,B,C / C,D,A winding <see cref="Tessellator" />'s quad-to-triangle conversion uses)
    ///     into <paramref name="dest" />.
    /// </summary>
    public readonly void GetTriangles(Span<ModelVertexLocal> dest)
    {
        var edge1 = _vertexPositions[1].vector3D - _vertexPositions[0].vector3D;
        var edge2 = _vertexPositions[1].vector3D - _vertexPositions[2].vector3D;
        var faceNormal = Vector3D.Normalize(Vector3D.Cross(edge2, edge1));
        var normal = _invertNormal
            ? new Vector3D<float>(-(float)faceNormal.X, -(float)faceNormal.Y, -(float)faceNormal.Z)
            : new Vector3D<float>((float)faceNormal.X, (float)faceNormal.Y, (float)faceNormal.Z);

        ReadOnlySpan<int> order = [0, 1, 2, 2, 3, 0];
        for (var i = 0; i < 6; ++i)
        {
            var vertex = _vertexPositions[order[i]];
            dest[i] = new ModelVertexLocal
            {
                Position = new Vector3D<float>((float)vertex.vector3D.X, (float)vertex.vector3D.Y, (float)vertex.vector3D.Z),
                U = vertex.texturePositionX,
                V = vertex.texturePositionY,
                Normal = normal
            };
        }
    }
}

/// <summary>Local-space (unscaled, unlit) vertex baked once per <see cref="ModelPart" /> face, transformed and lit at submit time.</summary>
internal struct ModelVertexLocal
{
    public Vector3D<float> Position;
    public float U;
    public float V;
    public Vector3D<float> Normal;
}
