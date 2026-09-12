using System.Security.Cryptography;
using System.Text;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Core.WebGPU;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>Portable color atlases, never GPU handles. All disk methods run on bounded worker jobs.</summary>
internal static class EntityImpostorCache
{
    public const int PixelBytes = EntityImpostorLayout.Width * EntityImpostorLayout.Height * 4;
    private const int HeaderBytes = 8 + 7 * 4 + 32 + 32;
    public const int FileBytes = HeaderBytes + PixelBytes;
    public const long DiskBudget = 256L * 1024 * 1024;
    private static readonly byte[] s_magic = "OMNIIMP1"u8.ToArray();
    internal sealed record Atlas(string Key, float Radius, byte[] Pixels);

    public static string Key(CowImpostorGeometry.Vertex[] geometry, Texture2D.CaptureSource skin, float radius)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("omniblock:standing_cow:canonical-v1:root24:inflate:RGBA8:cutout0.1:nearest:mip0");
        writer.Write(EntityImpostorLayout.Version); writer.Write(EntityImpostorLayout.Views);
        writer.Write(EntityImpostorLayout.Tile); writer.Write(EntityImpostorLayout.Padding);
        writer.Write(EntityImpostorLayout.Width); writer.Write(EntityImpostorLayout.Height); writer.Write(radius);
        for (var i = 0; i < 26; i++)
        {
            var d = EntityLodDirections.Get(i); var (r, u) = EntityImpostorLayout.Basis(d);
            foreach (var v in new[] { d, r, u }) { writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); }
        }
        writer.Write(EntityImpostorShaders.Capture); writer.Write(EntityImpostorShaders.Present);
        writer.Write(geometry.Length);
        foreach (var v in geometry)
        {
            writer.Write(v.Position.X); writer.Write(v.Position.Y); writer.Write(v.Position.Z);
            writer.Write(v.UV.X); writer.Write(v.UV.Y);
            writer.Write(v.Normal.X); writer.Write(v.Normal.Y); writer.Write(v.Normal.Z);
        }
        // Hash the exact uploaded RGBA bytes, including renderer resource fallback. Pack display
        // names/timestamps are irrelevant; two packs resolving identical inputs may share an atlas.
        writer.Write("/mob/cow.png"); writer.Write(skin.Width); writer.Write(skin.Height);
        writer.Write((int)skin.Sampler.Mag); writer.Write((int)skin.Sampler.Min); writer.Write((int)skin.Sampler.Mipmap);
        writer.Write((int)skin.Sampler.AddressU); writer.Write((int)skin.Sampler.AddressV);
        writer.Write(skin.Sampler.LodMaxClamp); writer.Write(skin.Sampler.MaxAnisotropy);
        writer.Write(skin.Pixels);
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    internal static string PathFor(string directory, string key)
    {
        if (key.Length != 64 || key.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("Invalid atlas digest.", nameof(key));
        return Path.Combine(directory, key + ".atlas");
    }

    public static Atlas? Read(string directory, string key, float radius, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var path = PathFor(directory, key);
        if (!File.Exists(path)) return null;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (file.Length != FileBytes) throw new InvalidDataException("Wrong impostor cache length.");
        using var reader = new BinaryReader(file);
        if (!reader.ReadBytes(8).SequenceEqual(s_magic) || reader.ReadInt32() != 1 ||
            reader.ReadInt32() != EntityImpostorLayout.Width || reader.ReadInt32() != EntityImpostorLayout.Height ||
            reader.ReadInt32() != 26 || reader.ReadInt32() != 1 || reader.ReadInt32() != PixelBytes ||
            reader.ReadSingle() != radius || !reader.ReadBytes(32).SequenceEqual(Convert.FromHexString(key)))
            throw new InvalidDataException("Incompatible impostor cache metadata.");
        var digest = reader.ReadBytes(32);
        var pixels = reader.ReadBytes(PixelBytes);
        cancellation.ThrowIfCancellationRequested();
        if (pixels.Length != PixelBytes || !SHA256.HashData(pixels).SequenceEqual(digest))
            throw new InvalidDataException("Corrupt impostor cache pixels.");
        try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return new Atlas(key, radius, pixels);
    }

    public static void Write(string directory, Atlas atlas, CancellationToken cancellation, long budget = DiskBudget)
    {
        if (atlas.Pixels.Length != PixelBytes || !float.IsFinite(atlas.Radius) || atlas.Radius <= 0)
            throw new InvalidDataException("Invalid atlas payload.");
        cancellation.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        var path = PathFor(directory, atlas.Key);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(file))
            {
                writer.Write(s_magic); writer.Write(1); writer.Write(EntityImpostorLayout.Width); writer.Write(EntityImpostorLayout.Height);
                writer.Write(26); writer.Write(1); writer.Write(PixelBytes); writer.Write(atlas.Radius);
                writer.Write(Convert.FromHexString(atlas.Key)); writer.Write(SHA256.HashData(atlas.Pixels)); writer.Write(atlas.Pixels);
                file.Flush(true);
            }
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, path, true); // atomic replacement in the same directory
            Trim(directory, budget, cancellation);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static void Trim(string directory, long budget, CancellationToken cancellation)
    {
        var files = new DirectoryInfo(directory).EnumerateFiles("*.atlas")
            .Where(f => f.Name.Length == 70 && f.Name[..64].All(Uri.IsHexDigit) && (f.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.Ordinal).ToArray();
        var bytes = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (bytes <= Math.Max(0, budget)) break;
            cancellation.ThrowIfCancellationRequested();
            var length = file.Length;
            file.Delete(); bytes -= length;
        }
    }
}

/// <summary>Bound both bytes and metadata entries; render-thread only, portable atlas storage.</summary>
internal sealed class EntityImpostorMemoryCache(long maxBytes = 64L * 1024 * 1024, int maxEntries = 512)
{
    private readonly LinkedList<EntityImpostorCache.Atlas> _lru = new();
    private readonly Dictionary<string, LinkedListNode<EntityImpostorCache.Atlas>> _entries = [];
    public long Bytes { get; private set; }
    public int Count => _entries.Count;
    public EntityImpostorCache.Atlas? Get(string key)
    {
        if (!_entries.TryGetValue(key, out var node)) return null;
        _lru.Remove(node); _lru.AddLast(node); return node.Value;
    }
    public void Put(EntityImpostorCache.Atlas atlas)
    {
        if (atlas.Pixels.LongLength > maxBytes || maxEntries <= 0) return;
        if (_entries.Remove(atlas.Key, out var previous)) { Bytes -= previous.Value.Pixels.Length; _lru.Remove(previous); }
        while (_lru.First is { } first && (Bytes + atlas.Pixels.Length > maxBytes || Count >= maxEntries))
        { _lru.RemoveFirst(); _entries.Remove(first.Value.Key); Bytes -= first.Value.Pixels.Length; }
        _entries.Add(atlas.Key, _lru.AddLast(atlas)); Bytes += atlas.Pixels.Length;
    }
    public void Clear() { _entries.Clear(); _lru.Clear(); Bytes = 0; }
}
