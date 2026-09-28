using System.Numerics;
using System.Text.Json;
using OmniBlock.Blocks;

namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>
/// Strict first subset of Java-style block model JSON: explicit elements, textures and face UVs.
/// No mutable JSON, world, GPU object or registry is retained by the compiled result.
/// </summary>
internal static class BlockModelCompiler
{
    public const int MaximumJsonCharacters = 262144;
    public const int MaximumElements = 64;
    public const int MaximumTextureVariables = 128;

    public static CompiledBlockModel Compile(ResourceLocation id, string json, Func<ResourceLocation, int> resolveTextureLayer)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(resolveTextureLayer);
        try
        {
            if (json.Length > MaximumJsonCharacters) throw Error("definition exceeds JSON size limit");
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            CheckProperties(root, "model", "textures", "elements", "ambientocclusion", "credit");
            var textures = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("textures", out var textureObject))
            {
                CheckObject(textureObject, "textures");
                foreach (var property in textureObject.EnumerateObject())
                {
                    if (textures.Count >= MaximumTextureVariables) throw Error("too many texture variables");
                    textures.Add(property.Name, ReadString(property.Value, "texture '" + property.Name + "'"));
                }
            }

            // Resolve even unused declarations: misspelled dependencies must not hide until a
            // different state happens to draw. Cache only within this candidate build.
            var materials = new Dictionary<string, (ResourceLocation Id, int Layer)>(StringComparer.Ordinal);
            foreach (var name in textures.Keys) Resolve("#" + name);

            var elements = Required(root, "elements");
            if (elements.ValueKind != JsonValueKind.Array || elements.GetArrayLength() > MaximumElements)
                throw Error($"elements must be an array of at most {MaximumElements} elements");
            var quads = new List<CompiledBlockQuad>();
            var index = 0;
            foreach (var element in elements.EnumerateArray())
            {
                var owner = $"element {index++}";
                CheckProperties(element, owner, "from", "to", "faces", "shade", "name");
                var from = ReadVector(Required(element, "from"), owner + " from");
                var to = ReadVector(Required(element, "to"), owner + " to");
                if (from.X > to.X || from.Y > to.Y || from.Z > to.Z)
                    throw Error(owner + ": from must not exceed to");
                if ((from.X == to.X ? 1 : 0) + (from.Y == to.Y ? 1 : 0) + (from.Z == to.Z ? 1 : 0) > 1)
                    throw Error(owner + ": line/point elements have no surface");

                var faces = Required(element, "faces");
                CheckProperties(faces, owner + " faces", "down", "up", "north", "south", "west", "east");
                // Canonical side order makes compilation independent of JSON property ordering.
                foreach (var side in Enum.GetValues<Side>())
                {
                    if (!faces.TryGetProperty(side.ToString().ToLowerInvariant(), out var face)) continue;
                    var faceOwner = owner + " face " + side;
                    CheckProperties(face, faceOwner, "uv", "texture", "rotation", "cullface", "tintindex");
                    var material = Resolve(ReadString(Required(face, "texture"), faceOwner + " texture"));
                    var uv = ReadNumbers(Required(face, "uv"), 4, faceOwner + " uv", 0, 16);
                    var rotation = face.TryGetProperty("rotation", out var rot) ? ReadInt(rot, faceOwner + " rotation") : 0;
                    if (rotation is not (0 or 90 or 180 or 270)) throw Error(faceOwner + ": rotation must be 0, 90, 180 or 270");
                    var tint = face.TryGetProperty("tintindex", out var tintValue) ? ReadInt(tintValue, faceOwner + " tintindex") : -1;
                    if (tint is < -1 or > 255) throw Error(faceOwner + ": tintindex must be -1..255");
                    Side? cull = null;
                    if (face.TryGetProperty("cullface", out var cullValue))
                    {
                        cull = ParseSide(ReadString(cullValue, faceOwner + " cullface"));
                        if (cull != side || !OnBoundary(side, from, to))
                            throw Error(faceOwner + ": cullface must match a face on the block boundary");
                    }

                    var corners = Corners(side, from, to);
                    if (Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]).LengthSquared() == 0)
                        throw Error(faceOwner + ": zero-area face");
                    var uvs = FaceUvs(side, uv);
                    ModelVertex Vertex(int i) => new(corners[i], uvs[(i + rotation / 90) % 4]);
                    quads.Add(new CompiledBlockQuad(Vertex(0), Vertex(1), Vertex(2), Vertex(3), material.Id,
                        material.Layer, side, cull, tint, ReadBool(element, "shade", true)));
                }
            }
            return new CompiledBlockModel(id, ReadBool(root, "ambientocclusion", true), quads);

            (ResourceLocation Id, int Layer) Resolve(string reference)
            {
                if (materials.TryGetValue(reference, out var cached)) return cached;
                var path = reference;
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (path.StartsWith('#'))
                {
                    if (!visited.Add(path)) throw Error($"texture '{reference}': cyclic variable '{path}'");
                    if (!textures.TryGetValue(path[1..], out var target)) throw Error($"texture '{reference}': unknown variable '{path}'");
                    path = target;
                }
                if (!path.Contains(':', StringComparison.Ordinal)) throw Error($"texture '{reference}': expected a namespaced texture, got '{path}'");
                var textureId = ResourceLocation.Parse(path);
                int layer;
                try { layer = resolveTextureLayer(textureId); }
                catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException or InvalidOperationException)
                {
                    throw new InvalidDataException($"texture '{reference}' ('{textureId}'): {ex.Message}", ex);
                }
                if (layer is < 1 or > 255) throw Error($"texture '{textureId}': layer {layer} is outside the current chunk vertex range 1..255");
                var result = (textureId, layer);
                materials.Add(reference, result);
                return result;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException or FormatException or InvalidOperationException)
        {
            throw new InvalidDataException($"Block model '{id}': {ex.Message}", ex);
        }
    }

    private static InvalidDataException Error(string message) => new(message);
    private static JsonElement Required(JsonElement obj, string key) => obj.TryGetProperty(key, out var value)
        ? value : throw Error($"missing required '{key}'");
    private static string ReadString(JsonElement value, string owner) => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
        ? value.GetString()! : throw Error(owner + " must be a nonempty string");
    private static int ReadInt(JsonElement value, string owner) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
        ? result : throw Error(owner + " must be an integer");
    private static bool ReadBool(JsonElement obj, string key, bool fallback)
    {
        if (!obj.TryGetProperty(key, out var value)) return fallback;
        return value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw Error(key + " must be boolean") };
    }

    private static void CheckObject(JsonElement value, string owner)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error(owner + " must be an object");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name)) throw Error(owner + ": duplicate property '" + property.Name + "'");
    }

    private static void CheckProperties(JsonElement value, string owner, params string[] allowed)
    {
        CheckObject(value, owner);
        foreach (var property in value.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw Error(owner + ": unsupported property '" + property.Name + "'");
    }

    private static float[] ReadNumbers(JsonElement value, int count, string owner, float minimum, float maximum)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != count) throw Error(owner + $" must contain {count} numbers");
        var result = new float[count];
        for (var i = 0; i < count; i++)
        {
            if (value[i].ValueKind != JsonValueKind.Number || !value[i].TryGetSingle(out var number) || !float.IsFinite(number) || number < minimum || number > maximum)
                throw Error(owner + $": coordinates must be finite and in {minimum}..{maximum}");
            result[i] = number / 16;
        }
        return result;
    }

    private static Vector3 ReadVector(JsonElement value, string owner)
    {
        var numbers = ReadNumbers(value, 3, owner, 0, 16);
        return new Vector3(numbers[0], numbers[1], numbers[2]);
    }

    private static Side ParseSide(string name) => name switch
    {
        "down" => Side.Down, "up" => Side.Up, "north" => Side.North,
        "south" => Side.South, "west" => Side.West, "east" => Side.East,
        _ => throw Error($"unknown cullface '{name}'")
    };

    private static bool OnBoundary(Side side, Vector3 min, Vector3 max) => side switch
    {
        Side.Down => min.Y == 0, Side.Up => max.Y == 1,
        Side.North => min.Z == 0, Side.South => max.Z == 1,
        Side.West => min.X == 0, _ => max.X == 1
    };

    private static Vector3[] Corners(Side side, Vector3 a, Vector3 b) => side switch
    {
        Side.Down => [new(a.X, a.Y, b.Z), new(a.X, a.Y, a.Z), new(b.X, a.Y, a.Z), new(b.X, a.Y, b.Z)],
        Side.Up => [new(b.X, b.Y, b.Z), new(b.X, b.Y, a.Z), new(a.X, b.Y, a.Z), new(a.X, b.Y, b.Z)],
        Side.North => [new(b.X, b.Y, a.Z), new(b.X, a.Y, a.Z), new(a.X, a.Y, a.Z), new(a.X, b.Y, a.Z)],
        Side.South => [new(a.X, b.Y, b.Z), new(a.X, a.Y, b.Z), new(b.X, a.Y, b.Z), new(b.X, b.Y, b.Z)],
        Side.West => [new(a.X, b.Y, a.Z), new(a.X, a.Y, a.Z), new(a.X, a.Y, b.Z), new(a.X, b.Y, b.Z)],
        _ => [new(b.X, b.Y, b.Z), new(b.X, a.Y, b.Z), new(b.X, a.Y, a.Z), new(b.X, b.Y, a.Z)]
    };

    private static Vector2[] FaceUvs(Side side, float[] uv) => side switch
    {
        Side.Down => [new(uv[0], uv[3]), new(uv[0], uv[1]), new(uv[2], uv[1]), new(uv[2], uv[3])],
        Side.Up => [new(uv[2], uv[3]), new(uv[2], uv[1]), new(uv[0], uv[1]), new(uv[0], uv[3])],
        _ => [new(uv[0], uv[1]), new(uv[0], uv[3]), new(uv[2], uv[3]), new(uv[2], uv[1])]
    };
}
