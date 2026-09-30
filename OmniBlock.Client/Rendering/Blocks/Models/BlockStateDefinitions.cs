using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OmniBlock.Client.Rendering.Blocks.Models;

internal enum CuboidStateShape { Cube, LowerSlab, UpperSlab }

internal readonly record struct BlockStateModelDefinition(
    ResourceLocation Block, int Metadata, RenderResourceId Model, CuboidStateShape Shape);
internal readonly record struct StairStateModelDefinition(ResourceLocation Block, RenderResourceId Model);

/// <summary>
/// Build-local metadata adapter, not gameplay state or a save-format change. The installed catalog
/// owns coverage and shape contracts. Packs may replace model choices, but cannot silently drop a
/// state or change the bounds expected by the procedural lighting/culling path during this stage.
/// </summary>
internal sealed class BlockStateDefinitions
{
    internal const string CatalogPath = "assets/omniblock/blockstates/catalog.json";
    internal const int MaximumFileBytes = 64 * 1024;
    internal const int MaximumCatalogBytes = 4 * 1024 * 1024;
    private readonly BlockStateModelDefinition[] _states;
    private readonly StairStateModelDefinition[] _stairs;
    private BlockStateDefinitions(List<BlockStateModelDefinition> states, List<StairStateModelDefinition> stairs)
    {
        _states = states.ToArray();
        _stairs = stairs.ToArray();
    }
    internal ReadOnlySpan<BlockStateModelDefinition> States => _states;
    internal ReadOnlySpan<StairStateModelDefinition> Stairs => _stairs;
    internal IEnumerable<RenderResourceId> ModelRoots => _states.Select(s => s.Model).Concat(_stairs.Select(s => s.Model)).Distinct()
        .OrderBy(id => id.ToString(), StringComparer.Ordinal);
    internal static string PathFor(ResourceLocation block) => $"assets/{block.Namespace}/blockstates/{block.Path}.json";

    internal static BlockStateDefinitions Load(Func<string, Stream?> openOverride, Func<string, Stream?> openInstalled)
    {
        try { return LoadCore(openOverride, openInstalled); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException or
            FormatException or KeyNotFoundException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Block state catalog '{CatalogPath}': {ex.Message}", ex);
        }
    }

    private static BlockStateDefinitions LoadCore(Func<string, Stream?> openOverride, Func<string, Stream?> openInstalled)
    {
        var totalBytes = 0;
        var states = new List<BlockStateModelDefinition>();
        var stairs = new List<StairStateModelDefinition>();
        var blocks = new HashSet<ResourceLocation>();
        using var catalog = Read(CatalogPath, openInstalled);
        CheckProperties(catalog.RootElement, "blocks", "stairs");
        var entries = catalog.RootElement.GetProperty("blocks");
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > BlockModelCatalog.MaximumModels)
            throw new InvalidDataException($"Block states '{CatalogPath}': expected at most {BlockModelCatalog.MaximumModels} blocks.");
        foreach (var entry in entries.EnumerateArray())
        {
            var renderId = RenderResourceId.Parse(entry.GetString()!);
            var block = ResourceLocation.Parse(renderId.ToString());
            if (!blocks.Add(block)) throw new InvalidDataException($"Block states '{CatalogPath}': duplicate block '{block}'.");
            var path = PathFor(block);
            try
            {
                using var installed = Read(path, openInstalled);
                var baseline = Parse(block, installed.RootElement, null);
                using var replacement = ReadDocument(path, openOverride, optional: true);
                var selected = replacement is null ? baseline : Parse(block, replacement.RootElement, baseline);
                for (var meta = 0; meta < 16; meta++)
                {
                    if (baseline[meta].Shape != selected[meta].Shape)
                        throw new InvalidDataException($"state {meta}: pack must preserve installed shape '{baseline[meta].Shape}'");
                    states.Add(selected[meta]);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException or
                FormatException or KeyNotFoundException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                throw new InvalidDataException($"Block states '{block}' ({path}): {ex.Message}", ex);
            }
        }
        if (catalog.RootElement.TryGetProperty("stairs", out var stairEntries))
        {
            if (stairEntries.ValueKind != JsonValueKind.Array ||
                stairEntries.GetArrayLength() + blocks.Count > BlockModelCatalog.MaximumModels)
                throw new InvalidDataException($"Block states '{CatalogPath}': too many stair models.");
            foreach (var entry in stairEntries.EnumerateArray())
            {
                var block = ResourceLocation.Parse(RenderResourceId.Parse(entry.GetString()!).ToString());
                if (!blocks.Add(block)) throw new InvalidDataException($"Block states '{CatalogPath}': duplicate block '{block}'.");
                var path = PathFor(block);
                try
                {
                    using var installed = Read(path, openInstalled);
                    var baseline = ParseStairModel(installed.RootElement);
                    using var replacement = ReadDocument(path, openOverride, optional: true);
                    stairs.Add(new StairStateModelDefinition(block,
                        replacement is null ? baseline : ParseStairModel(replacement.RootElement)));
                }
                catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException or
                    FormatException or KeyNotFoundException or InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    throw new InvalidDataException($"Block states '{block}' ({path}): {ex.Message}", ex);
                }
            }
        }
        return new BlockStateDefinitions(states, stairs);

        JsonDocument Read(string path, Func<string, Stream?> open) => ReadDocument(path, open, false)!;
        JsonDocument? ReadDocument(string path, Func<string, Stream?> open, bool optional)
        {
            using var stream = open(path);
            if (stream is null)
                return optional ? null : throw new InvalidDataException($"Required block states '{path}' are missing.");
            using var bytes = new MemoryStream();
            var buffer = new byte[4096];
            while (true)
            {
                var allowance = Math.Min(MaximumFileBytes - (int)bytes.Length, MaximumCatalogBytes - totalBytes);
                var count = stream.Read(buffer, 0, Math.Min(buffer.Length, allowance + 1));
                if (count == 0) break;
                totalBytes += count;
                if (totalBytes > MaximumCatalogBytes || bytes.Length + count > MaximumFileBytes)
                    throw new InvalidDataException($"Block states '{path}': encoded byte budget exceeded.");
                bytes.Write(buffer, 0, count);
            }
            var json = new UTF8Encoding(false, true).GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
            return JsonDocument.Parse(json.TrimStart('\uFEFF'), new JsonDocumentOptions { MaxDepth = 8 });
        }
    }

    private static RenderResourceId ParseStairModel(JsonElement root)
    {
        CheckProperties(root, "model");
        return RenderResourceId.Parse(root.GetProperty("model").GetString()!);
    }

    private static BlockStateModelDefinition[] Parse(ResourceLocation block, JsonElement root, BlockStateModelDefinition[]? baseline)
    {
        CheckProperties(root, "default", "variants");
        var states = new BlockStateModelDefinition[16];
        var seen = new bool[16];
        if (baseline is not null) Array.Copy(baseline, states, 16);
        if (root.TryGetProperty("default", out var defaultValue))
        {
            for (var meta = 0; meta < 16; meta++)
            {
                states[meta] = Apply(meta, defaultValue, baseline is null ? null : states[meta]);
                seen[meta] = true;
            }
        }
        if (root.TryGetProperty("variants", out var variants))
        {
            if (variants.ValueKind != JsonValueKind.Object) throw new InvalidDataException("variants must be an object");
            var selected = new bool[16];
            foreach (var variant in variants.EnumerateObject())
            {
                foreach (var meta in ExpandSelector(variant.Name))
                {
                    if (selected[meta]) throw new InvalidDataException($"duplicate metadata selector '{meta}'");
                    selected[meta] = true;
                    states[meta] = Apply(meta, variant.Value, seen[meta] || baseline is not null ? states[meta] : null);
                    seen[meta] = true;
                }
            }
        }
        for (var meta = 0; meta < 16; meta++)
            if (!seen[meta] && baseline is null) throw new InvalidDataException($"missing metadata selector '{meta}'; provide a default or cover all 16 states");
        return states;

        BlockStateModelDefinition Apply(int meta, JsonElement value, BlockStateModelDefinition? inherited)
        {
            try
            {
                CheckProperties(value, "model", "shape");
                var model = value.TryGetProperty("model", out var modelValue)
                    ? RenderResourceId.Parse(modelValue.GetString()!)
                    : inherited?.Model ?? throw new InvalidDataException("missing model");
                var shape = value.TryGetProperty("shape", out var shapeValue)
                    ? shapeValue.GetString() switch
                    {
                        "cube" => CuboidStateShape.Cube,
                        "lower_slab" => CuboidStateShape.LowerSlab,
                        "upper_slab" => CuboidStateShape.UpperSlab,
                        var unknown => throw new InvalidDataException($"unsupported shape '{unknown}'")
                    }
                    : inherited?.Shape ?? throw new InvalidDataException("missing shape");
                return new(block, meta, model, shape);
            }
            catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException)
            {
                throw new InvalidDataException($"state {meta}: {ex.Message}", ex);
            }
        }
    }

    private static IEnumerable<int> ExpandSelector(string selector)
    {
        var parts = selector.Split("..", StringSplitOptions.None);
        if (parts.Length is < 1 or > 2 || !TryIndex(parts[0], out var first) ||
            (parts.Length == 2 && (!TryIndex(parts[1], out var last) || last < first)))
            throw new InvalidDataException($"invalid metadata selector '{selector}'; expected 0..15 or a bounded range");
        var end = parts.Length == 1 ? first : int.Parse(parts[1], CultureInfo.InvariantCulture);
        return Enumerable.Range(first, end - first + 1);

        static bool TryIndex(string text, out int value) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
            value is >= 0 and <= 15 && text == value.ToString(CultureInfo.InvariantCulture);
    }

    private static void CheckProperties(JsonElement element, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("expected an object");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw new InvalidDataException($"unknown or duplicate property '{property.Name}'");
    }
}
