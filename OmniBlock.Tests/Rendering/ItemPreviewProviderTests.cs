using OmniBlock.Blocks.Entities;
using OmniBlock.Client.Rendering.Items;
using OmniBlock.Items;

namespace OmniBlock.Tests.Rendering;

public sealed class ItemPreviewProviderTests
{
    [Fact]
    public void Spawner_preview_uses_the_entity_type_stored_on_the_stack()
    {
        LightTestWorld world = new();
        ItemStack stack = new(world.Content.Items.Get("omniblock:spawner"));
        stack.SetStringComponent(BlockEntityMobSpawner.SpawnedEntityComponent, "omniblock:skeleton");
        ClientItemPreviewRegistry previews = new(world.Content);

        Assert.True(previews.TryGetEntityPreview(stack, world, out var preview));
        Assert.Same(world.Content.EntityTypes.Get("omniblock:skeleton"), preview.Entity.Type);
        Assert.True(preview.Depth < 32.0F);
    }

    [Fact]
    public void Unconfigured_spawner_preview_uses_its_legacy_pig_default()
    {
        LightTestWorld world = new();
        ItemStack stack = new(world.Content.Items.Get("omniblock:spawner"));
        ClientItemPreviewRegistry previews = new(world.Content);

        Assert.True(previews.TryGetEntityPreview(stack, world, out var preview));
        Assert.Same(world.Content.EntityTypes.Get("omniblock:pig"), preview.Entity.Type);
    }
}
