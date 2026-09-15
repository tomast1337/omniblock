using System.Text.Json;

namespace OmniBlock.Client.Rendering.Entities;

internal sealed record ClientEntityImpostorLayer(
    string Model,
    string Texture,
    ResourceLocation PoseProvider)
{
    public ClientEntityImpostorLayer(string model, string texture)
        : this(model, texture, EntityImpostorGeometry.QuadrupedPoseProvider) { }
}

/// <summary>Validated client-only impostor schema compiled from an entity renderer definition.</summary>
internal sealed record ClientEntityImpostorDescriptor(
    ResourceLocation Id,
    ResourceLocation ProviderType,
    double VisualDiameter,
    ClientEntityImpostorLayer[] Layers,
    bool OmitHeldItem = false,
    string? UnsupportedWhenTrue = null,
    string? ScaleProperty = null)
{
    public static ClientEntityImpostorDescriptor? Compile(JsonElement renderer)
    {
        if (!renderer.TryGetProperty("Impostor", out var value)) return null;
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Renderer 'Impostor' must be an object.");
        var id = Resource(value, "Id");
        var provider = Resource(value, "Provider");
        var diameter = value.GetProperty("VisualDiameter").GetDouble();
        if (!double.IsFinite(diameter) || diameter <= 0 || diameter > 64)
            throw new InvalidDataException("Impostor 'VisualDiameter' must be in (0, 64].");
        if (!value.TryGetProperty("Layers", out var layerJson) || layerJson.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Impostor 'Layers' must be an array.");
        var layers = layerJson.EnumerateArray().Select(layer =>
        {
            var model = layer.GetProperty("Model").GetString();
            var texture = layer.GetProperty("Texture").GetString();
            if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(texture) ||
                !texture.StartsWith('/'))
                throw new InvalidDataException("Every impostor layer requires a model and absolute texture path.");
            if (!layer.TryGetProperty("PoseProvider", out var pose))
                throw new InvalidDataException("Every impostor layer requires a namespaced 'PoseProvider'.");
            var poseProvider = ResourceLocation.Parse(pose.GetString()
                ?? throw new InvalidDataException("Impostor layer 'PoseProvider' cannot be null."));
            return new ClientEntityImpostorLayer(model, texture, poseProvider);
        }).ToArray();
        if (layers.Length is < 1 or > 2)
            throw new InvalidDataException("This impostor format supports one or two layers.");
        if (layers.Select(layer => layer.Texture).Distinct(StringComparer.Ordinal).Count() != layers.Length)
            throw new InvalidDataException("Impostor layer texture paths must be distinct.");
        var omitHeldItem = value.TryGetProperty("OmitHeldItem", out var omit) && omit.GetBoolean();
        var unsupportedWhenTrue = OptionalPropertyName(value, "UnsupportedWhenTrue");
        var scaleProperty = OptionalPropertyName(value, "ScaleProperty");
        return new ClientEntityImpostorDescriptor(
            id, provider, diameter, layers, omitHeldItem, unsupportedWhenTrue, scaleProperty);
    }

    private static string? OptionalPropertyName(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property)) return null;
        var result = property.GetString();
        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidDataException($"Impostor '{name}' must be a non-empty property name.");
        return result;
    }

    private static ResourceLocation Resource(JsonElement value, string name)
    {
        var text = value.GetProperty(name).GetString()
            ?? throw new InvalidDataException($"Impostor '{name}' cannot be null.");
        var result = ResourceLocation.Parse(text);
        return text.Contains(':') ? result : new ResourceLocation(Namespace.OmniBlock, result.Path);
    }
}
