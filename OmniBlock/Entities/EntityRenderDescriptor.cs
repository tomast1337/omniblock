using System.Text.Json;

namespace OmniBlock.Entities;

/// <summary>
///     Immutable, presentation-agnostic description retained by the shared catalog. The provider and
///     its schema are interpreted only by a client renderer registry.
/// </summary>
public sealed class EntityRenderDescriptor
{
    public EntityRenderDescriptor(ResourceLocation providerType, JsonElement definition)
    {
        ProviderType = providerType ?? throw new ArgumentNullException(nameof(providerType));
        Definition = definition.Clone();
    }

    public ResourceLocation ProviderType { get; }
    public JsonElement Definition { get; }

    public static EntityRenderDescriptor Compile(JsonElement definition)
    {
        var providerName = definition.TryGetProperty("Type", out var type)
            ? type.GetString() ?? throw new ArgumentException("Entity renderer has a null 'Type'.")
            : "living";
        var providerType = ResourceLocation.Parse(providerName);
        if (!providerName.Contains(':'))
            providerType = new ResourceLocation(Namespace.OmniBlock, providerType.Path);
        return new EntityRenderDescriptor(providerType, definition);
    }
}
