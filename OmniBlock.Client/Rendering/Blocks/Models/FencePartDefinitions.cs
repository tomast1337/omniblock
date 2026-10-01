using System.Text;
using System.Text.Json;
using OmniBlock.Blocks;

namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>Fence model parts selected by validated named neighbor inputs.</summary>
internal sealed class FencePartDefinitions
{
    internal const string Path = "assets/omniblock/blockstates/fence.json";
    internal readonly record struct Rule(string Element, BlockModelCondition Condition);
    private static readonly IReadOnlyDictionary<string, int> s_conditionInputs = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["west"] = FenceShape.West, ["east"] = FenceShape.East,
        ["north"] = FenceShape.North, ["south"] = FenceShape.South,
        ["neighbor.west"] = FenceShape.West, ["neighbor.east"] = FenceShape.East,
        ["neighbor.north"] = FenceShape.North, ["neighbor.south"] = FenceShape.South
    };
    private readonly Rule[] _rules;

    private FencePartDefinitions(RenderResourceId model, Rule[] rules)
    {
        Model = model;
        _rules = rules;
    }

    internal RenderResourceId Model { get; }
    internal ReadOnlySpan<Rule> Rules => _rules;

    internal static FencePartDefinitions Load(Func<string, Stream?> openOverride, Func<string, Stream?> openInstalled)
    {
        try
        {
            using var stream = openOverride(Path) ?? openInstalled(Path)
                ?? throw new InvalidDataException("required definition is missing");
            using var bytes = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = stream.Read(buffer, 0, Math.Min(buffer.Length, 65536 - (int)bytes.Length + 1))) != 0)
            {
                bytes.Write(buffer, 0, count);
                if (bytes.Length > 65536) throw new InvalidDataException("encoded byte budget exceeded");
            }
            using var document = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(bytes.ToArray()),
                new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            Check(root, "model", "parts");
            var model = RenderResourceId.Parse(root.GetProperty("model").GetString()!);
            var parts = root.GetProperty("parts");
            if (parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() is < 1 or > 64)
                throw new InvalidDataException("parts must be an array of 1..64 entries");
            var rules = new List<Rule>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var hasPost = false;
            foreach (var part in parts.EnumerateArray())
            {
                Check(part, "element", "when");
                var element = part.GetProperty("element").GetString();
                if (string.IsNullOrWhiteSpace(element) || !names.Add(element))
                    throw new InvalidDataException($"duplicate or empty part '{element}'");
                BlockModelCondition condition;
                try { condition = BlockModelCondition.Parse(part.GetProperty("when"), s_conditionInputs); }
                catch (InvalidDataException ex)
                {
                    throw new InvalidDataException($"part '{element}': {ex.Message}", ex);
                }
                hasPost |= element == "post" && condition.IsUnconditional;
                rules.Add(new Rule(element, condition));
            }
            if (!hasPost) throw new InvalidDataException("missing unconditional post part");
            return new FencePartDefinitions(model, rules.ToArray());
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"Fence parts 'omniblock:fence' ({Path}): {ex.Message}", ex);
        }
    }

    private static void Check(JsonElement element, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("expected an object");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !names.Add(property.Name))
                throw new InvalidDataException($"unknown or duplicate property '{property.Name}'");
    }
}
