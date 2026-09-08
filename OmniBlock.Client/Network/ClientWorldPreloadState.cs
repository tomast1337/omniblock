using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using Silk.NET.Maths;

namespace OmniBlock.Client.Network;

/// <summary>A small, measurable playable area required before entering the world.</summary>
public sealed class ClientWorldPreloadState
{
    public const int ChunkRadius = 2;
    public const int MeshChunkRadius = 1;
    public const int VerticalSectionRadius = 1;

    private readonly HashSet<ChunkPos> _decodedChunks = [];
    private readonly HashSet<Vector3D<int>> _uploadedSections = [];
    private ChunkPos _centerChunk;
    private int _centerSectionY;

    public bool HasSpawn { get; private set; }
    public int RequiredChunks => 25;
    public int RequiredMeshes => (MeshChunkRadius * 2 + 1) * (MeshChunkRadius * 2 + 1);
    public int DecodedChunks => HasSpawn ? CountChunks() : 0;
    public int UploadedMeshes => HasSpawn ? CountSections() : 0;
    public bool IsReady => HasSpawn && DecodedChunks == RequiredChunks && UploadedMeshes == RequiredMeshes;

    public void SetSpawn(double x, double y, double z)
    {
        _centerChunk = new ChunkPos((int)Math.Floor(x / 16.0), (int)Math.Floor(z / 16.0));
        _centerSectionY = Math.Clamp((int)Math.Floor(y / 16.0), 0, ChuckFormat.WorldHeight / 16 - 1);
        HasSpawn = true;
    }

    public void MarkChunkDecoded(int x, int z) => _decodedChunks.Add(new ChunkPos(x, z));
    public void MarkChunkUnloaded(int x, int z) => _decodedChunks.Remove(new ChunkPos(x, z));
    public void MarkMeshUploaded(Vector3D<int> pos) =>
        _uploadedSections.Add(new Vector3D<int>(pos.X >> 4, pos.Y >> 4, pos.Z >> 4));

    public bool IsChunkDecoded(int offsetX, int offsetZ) =>
        HasSpawn && _decodedChunks.Contains(new ChunkPos(_centerChunk.X + offsetX, _centerChunk.Z + offsetZ));

    public bool HasMesh(int offsetX, int offsetZ)
    {
        if (!HasSpawn)
            return false;

        for (var y = Math.Max(0, _centerSectionY - VerticalSectionRadius);
             y <= Math.Min(ChuckFormat.WorldHeight / 16 - 1, _centerSectionY + VerticalSectionRadius); y++)
            if (_uploadedSections.Contains(new Vector3D<int>(_centerChunk.X + offsetX, y, _centerChunk.Z + offsetZ)))
                return true;

        return false;
    }

    public string DescribeMissingMeshes()
    {
        if (!HasSpawn)
            return "spawn-not-received";

        List<string> missing = [];
        for (var x = -MeshChunkRadius; x <= MeshChunkRadius; x++)
        for (var z = -MeshChunkRadius; z <= MeshChunkRadius; z++)
            if (!HasMesh(x, z))
                missing.Add($"{_centerChunk.X + x},{_centerChunk.Z + z}");

        return missing.Count == 0 ? "none" : string.Join(" ", missing);
    }

    /// <summary>
    ///     True while this section can satisfy the initial playable-area mesh requirement.
    ///     The renderer uses this to put startup work ahead of the rest of the render distance.
    /// </summary>
    public bool RequiresMesh(Vector3D<int> sectionWorldPos)
    {
        if (!HasSpawn || IsReady)
            return false;

        var section = new Vector3D<int>(
            sectionWorldPos.X >> 4,
            sectionWorldPos.Y >> 4,
            sectionWorldPos.Z >> 4);
        if (Math.Abs(section.X - _centerChunk.X) > MeshChunkRadius ||
            Math.Abs(section.Z - _centerChunk.Z) > MeshChunkRadius ||
            Math.Abs(section.Y - _centerSectionY) > VerticalSectionRadius)
            return false;

        // One nearby vertical section is enough for each column. Once one has uploaded, avoid
        // promoting the other two and spend the urgent queue on columns still blocking entry.
        for (var y = Math.Max(0, _centerSectionY - VerticalSectionRadius);
             y <= Math.Min(ChuckFormat.WorldHeight / 16 - 1, _centerSectionY + VerticalSectionRadius); y++)
            if (_uploadedSections.Contains(new Vector3D<int>(section.X, y, section.Z)))
                return false;

        return true;
    }

    public IEnumerable<Vector3D<int>> RequiredMeshSections()
    {
        if (!HasSpawn || IsReady)
            yield break;

        for (var x = -MeshChunkRadius; x <= MeshChunkRadius; x++)
        for (var z = -MeshChunkRadius; z <= MeshChunkRadius; z++)
        for (var y = Math.Max(0, _centerSectionY - VerticalSectionRadius);
             y <= Math.Min(ChuckFormat.WorldHeight / 16 - 1, _centerSectionY + VerticalSectionRadius); y++)
        {
            var section = new Vector3D<int>(_centerChunk.X + x, y, _centerChunk.Z + z);
            if (RequiresMesh(section * 16))
                yield return section * 16;
        }
    }

    public void Reset()
    {
        HasSpawn = false;
        _decodedChunks.Clear();
        _uploadedSections.Clear();
    }

    private int CountChunks()
    {
        var count = 0;
        for (var x = -ChunkRadius; x <= ChunkRadius; x++)
        for (var z = -ChunkRadius; z <= ChunkRadius; z++)
            if (_decodedChunks.Contains(new ChunkPos(_centerChunk.X + x, _centerChunk.Z + z))) count++;
        return count;
    }

    private int CountSections()
    {
        var count = 0;
        for (var x = -MeshChunkRadius; x <= MeshChunkRadius; x++)
        for (var z = -MeshChunkRadius; z <= MeshChunkRadius; z++)
        {
            for (var y = Math.Max(0, _centerSectionY - VerticalSectionRadius);
                 y <= Math.Min(ChuckFormat.WorldHeight / 16 - 1, _centerSectionY + VerticalSectionRadius); y++)
            {
                if (!_uploadedSections.Contains(new Vector3D<int>(_centerChunk.X + x, y, _centerChunk.Z + z))) continue;
                count++;
                break;
            }
        }
        return count;
    }
}
