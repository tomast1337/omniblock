using System.Numerics;
using System.Text;
using System.Text.Json;
using OmniBlock.Blocks;

namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>
/// Client-only crossed-plant shape catalog. A pack may replace the shared default or individual
/// blocks; atlas texture selection and gameplay collision remain in the content runtime.
/// </summary>
internal sealed class CrossedPlantModelCatalog
{
    internal const string Path = "assets/omniblock/models/crossed_plants.json";
    private const int MaximumBytes = 64 * 1024;
    private readonly CompiledCrossedPlantGeometry?[] _byBlockId;

    private CrossedPlantModelCatalog(CompiledCrossedPlantGeometry?[] byBlockId) =>
        _byBlockId = byBlockId;

    internal CompiledCrossedPlantGeometry? Get(int blockId) =>
        (uint)blockId < _byBlockId.Length ? _byBlockId[blockId] : null;

    internal bool ContentEquals(CrossedPlantModelCatalog? other)
    {
        if (other is null) return false;
        for (var id = 0; id < _byBlockId.Length; id++)
        {
            var a = _byBlockId[id];
            var b = other._byBlockId[id];
            if (a is null != (b is null) || a is not null && b is not null && !a.ContentEquals(b))
                return false;
        }
        return true;
    }

    internal static CrossedPlantModelCatalog Load(IBlockRuntimeView blocks,
        Func<string, Stream?> openOverride, Func<string, Stream?> openBuiltin)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(openOverride);
        ArgumentNullException.ThrowIfNull(openBuiltin);
        try
        {
            using var installed = Read(openBuiltin(Path), required: true)!;
            using var replacement = Read(openOverride(Path), required: false);
            var overrides = new Dictionary<int, CompiledCrossedPlantGeometry>();
            var defaultGeometry = ParseDocument(installed.RootElement, blocks, requireDefault: true,
                CrossedPlantGeometry.Builtin, overrides);
            if (replacement is not null)
                defaultGeometry = ParseDocument(replacement.RootElement, blocks, requireDefault: false,
                    defaultGeometry, overrides);

            var byId = new CompiledCrossedPlantGeometry?[256];
            for (var id = 0; id < byId.Length; id++)
                if (blocks.TryGetByProtocolId(id, out var block) &&
                    block.RenderType == BlockRendererType.Reed)
                    byId[id] = overrides.GetValueOrDefault(id, defaultGeometry);
            return new CrossedPlantModelCatalog(byId);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException or
            FormatException or IOException or UnauthorizedAccessException or OverflowException)
        {
            throw new InvalidDataException($"Crossed-plant model '{Path}': {ex.Message}", ex);
        }
    }

    private static CompiledCrossedPlantGeometry ParseDocument(JsonElement root,
        IBlockRuntimeView blocks, bool requireDefault, CompiledCrossedPlantGeometry inherited,
        Dictionary<int, CompiledCrossedPlantGeometry> overrides)
    {
        CheckObject(root, "catalog", "default", "overrides");
        if (root.TryGetProperty("default", out var defaultValue)) inherited = ParseGeometry(defaultValue);
        else if (requireDefault) throw new InvalidDataException("missing required 'default' geometry");
        if (root.TryGetProperty("overrides", out var entries))
        {
            CheckObject(entries, "overrides");
            foreach (var entry in entries.EnumerateObject())
            {
                var id = RenderResourceId.Parse(entry.Name);
                if (!blocks.TryGet(ResourceLocation.Parse(id.ToString()), out var block))
                    throw new InvalidDataException($"block '{id}': unknown block");
                if (block.RenderType != BlockRendererType.Reed)
                    throw new InvalidDataException($"block '{id}': expected Reed render type");
                overrides[block.Id] = ParseGeometry(entry.Value);
            }
        }
        return inherited;
    }

    private static CompiledCrossedPlantGeometry ParseGeometry(JsonElement value)
    {
        CheckObject(value, "geometry", "inset", "quads");
        var hasInset = value.TryGetProperty("inset", out var inset);
        var hasQuads = value.TryGetProperty("quads", out var quads);
        if (hasInset == hasQuads)
            throw new InvalidDataException("geometry requires exactly one of 'inset' or 'quads'");
        if (hasInset) return CrossedPlantGeometry.FromInset(Number(inset, "inset"));
        if (quads.ValueKind != JsonValueKind.Array || quads.GetArrayLength() is < 1 or > 16)
            throw new InvalidDataException("quads must be an array of 1..16 quads");
        var compiled = new List<CrossedPlantGeometry.Quad>();
        foreach (var quad in quads.EnumerateArray())
        {
            if (quad.ValueKind != JsonValueKind.Array || quad.GetArrayLength() != 4)
                throw new InvalidDataException("each quad must have four vertices");
            var a = Vertex(quad[0]); var b = Vertex(quad[1]);
            var c = Vertex(quad[2]); var d = Vertex(quad[3]);
            var ab = new Vector3(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
            var ac = new Vector3(c.X - a.X, c.Y - a.Y, c.Z - a.Z);
            if (Vector3.Cross(ab, ac).LengthSquared() < 1e-8f)
                throw new InvalidDataException("quad has zero area");
            compiled.Add(new(a, b, c, d));
        }
        return new CompiledCrossedPlantGeometry(compiled);

        static CrossedPlantGeometry.Vertex Vertex(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 5)
                throw new InvalidDataException("vertex must be [x,y,z,u,v]");
            return new(Number(value[0], "x"), Number(value[1], "y"),
                Number(value[2], "z"), Number(value[3], "u"), Number(value[4], "v"));
        }
    }

    private static float Number(JsonElement value, string owner)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var number) ||
            !float.IsFinite(number) || number is < 0 or > 1)
            throw new InvalidDataException($"{owner} must be finite and in 0..1");
        return number;
    }

    private static void CheckObject(JsonElement value, string owner, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{owner} must be an object");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new InvalidDataException($"{owner}: duplicate '{property.Name}'");
            if (allowed.Length != 0 && !allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new InvalidDataException($"{owner}: unsupported '{property.Name}'");
        }
    }

    private static JsonDocument? Read(Stream? stream, bool required)
    {
        if (stream is null)
            return required ? throw new InvalidDataException("required built-in model is missing") : null;
        using (stream)
        using (var bytes = new MemoryStream())
        {
            var buffer = new byte[4096];
            int read;
            while ((read = stream.Read(buffer, 0, Math.Min(buffer.Length,
                       MaximumBytes - (int)bytes.Length + 1))) != 0)
            {
                if (bytes.Length + read > MaximumBytes)
                    throw new InvalidDataException("model exceeds encoded byte limit");
                bytes.Write(buffer, 0, read);
            }
            var json = new UTF8Encoding(false, true).GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
            return JsonDocument.Parse(json.TrimStart('\uFEFF'), new JsonDocumentOptions { MaxDepth = 12 });
        }
    }
}
