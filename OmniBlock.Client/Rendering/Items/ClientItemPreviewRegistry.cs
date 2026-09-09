using OmniBlock.Blocks.Entities;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Registries;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Client.Rendering.Items;

/// <summary>A client-only extension point for item previews composed from more than atlas/block geometry.</summary>
public sealed class ClientItemPreviewRegistry
{
    private readonly ContentRuntime _content;
    private readonly Dictionary<Item, IItemPreviewProvider> _providers = [];

    public ClientItemPreviewRegistry(ContentRuntime content)
    {
        _content = content;
        Register(new ResourceLocation(Namespace.OmniBlock, "spawner"), new MobSpawnerItemPreviewProvider());
    }

    public void Register(ResourceLocation itemId, IItemPreviewProvider provider)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(provider);
        var item = _content.Items.Get(itemId);
        if (!_providers.TryAdd(item, provider))
            throw new InvalidOperationException($"An item preview provider is already registered for '{itemId}'.");
    }

    public bool TryGetEntityPreview(ItemStack stack, World world, out ItemEntityPreview preview)
    {
        if (_providers.TryGetValue(stack.GetItem(), out var provider))
            return provider.TryGetEntityPreview(stack, world, out preview);
        preview = default;
        return false;
    }
}

public interface IItemPreviewProvider
{
    bool TryGetEntityPreview(ItemStack stack, World world, out ItemEntityPreview preview);
}

public readonly record struct ItemEntityPreview(
    Entity Entity,
    float Scale,
    float CenterX,
    float BaselineY,
    float Depth);

internal sealed class MobSpawnerItemPreviewProvider : IItemPreviewProvider
{
    private readonly Dictionary<ResourceLocation, Entity> _entities = [];
    private World? _world;

    public bool TryGetEntityPreview(ItemStack stack, World world, out ItemEntityPreview preview)
    {
        var targetName = stack.GetStringComponent(BlockEntityMobSpawner.SpawnedEntityComponent);
        if (string.IsNullOrWhiteSpace(targetName)) targetName = "omniblock:pig";
        if (!ResourceLocation.TryParse(targetName.ToLowerInvariant(), out var target) || target is null)
        {
            preview = default;
            return false;
        }

        if (!ReferenceEquals(_world, world))
        {
            _entities.Clear();
            _world = world;
        }

        if (!_entities.TryGetValue(target, out var entity))
        {
            if (!world.Content.EntityTypes.TryGet(target, out var type) || type is null)
            {
                preview = default;
                return false;
            }

            entity = type.Create(world);
            _entities.Add(target, entity);
        }
        else
        {
            entity.SetWorld(world);
        }

        // Item blocks render at depth 32. Keeping the contents behind that shell lets its bars and
        // other solid details occlude the entity instead of painting the entity over the cage.
        preview = new ItemEntityPreview(entity, 5.5F, 8.0F, 13.0F, 24.0F);
        return true;
    }
}
