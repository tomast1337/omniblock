using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using OmniBlock.Registries;

namespace OmniBlock.Tests.Items;

public sealed class ItemRegistryTests
{
    [Fact]
    public void Apple_RegistersUnderExpectedResourceLocation()
    {
        Holder<ItemDefinition>? holder = DefaultRegistries.Items.Get(ResourceLocation.Parse("omniblock:apple"));

        Assert.NotNull(holder);
        Assert.Equal(ContentRuntime.Current.Items.Get("omniblock:apple").Id, holder.Value.ProtocolId);
        Assert.Equal("food", holder.Value.Behaviors.Single().GetProperty("Type").GetString());
    }

    [Fact]
    public void DuplicateTranslationKeys_DisambiguateByProtocolId()
    {
        // Both records share the translation key "record"; the first declared keeps the plain
        // name and the second is disambiguated with its protocol ID suffix.
        Holder<ItemDefinition>? thirteen = DefaultRegistries.Items.Get(ResourceLocation.Parse("omniblock:record"));
        Holder<ItemDefinition>? cat = DefaultRegistries.Items.Get(ResourceLocation.Parse($"omniblock:record_{ContentRuntime.Current.Items.Get("omniblock:record_2257").Id}"));

        Assert.NotNull(thirteen);
        Assert.NotNull(cat);
        Assert.Equal(ContentRuntime.Current.Items.Get("omniblock:record").Id, thirteen.Value.ProtocolId);
        Assert.Equal(ContentRuntime.Current.Items.Get("omniblock:record_2257").Id, cat.Value.ProtocolId);
    }

    [Fact]
    public void JsonLoadedItems_AreRegistered()
    {
        Assert.True(DefaultRegistries.Items.ContainsId(ContentRuntime.Current.Items.Get("omniblock:shovel_iron").Id));
        Assert.True(DefaultRegistries.Items.ContainsId(ContentRuntime.Current.Items.Get("omniblock:boots_diamond").Id));
        Assert.True(DefaultRegistries.Items.ContainsId(ContentRuntime.Current.Items.Get("omniblock:map").Id));
    }

    [Fact]
    public void CraftingReturnItems_AreWiredAfterBoot()
    {
        Item bucket = ContentRuntime.Current.Items.Get("omniblock:bucket");

        Assert.Same(bucket, ContentRuntime.Current.Items.Get("omniblock:bucket_water").GetContainerItem());
        Assert.Same(bucket, ContentRuntime.Current.Items.Get("omniblock:bucket_lava").GetContainerItem());
        Assert.Same(bucket, ContentRuntime.Current.Items.Get("omniblock:milk").GetContainerItem());
    }

    [Fact]
    public void ResolveCrossReferences_ResolvesReferenceToLaterCreatedItem()
    {
        // The loader enumerates definitions alphabetically by filename, so a definition can
        // reference an item whose file sorts after it. Mirror that: create the referencing
        // item first, its target second, then run the second pass.
        var referencing = new ItemDefinition
        {
            Name = "test_filled_container",
            ProtocolId = 31900,
            MaxStackSize = 1,
            CraftingReturnItemProtocolId = 31901,
        };
        var target = new ItemDefinition
        {
            Name = "test_empty_container",
            ProtocolId = 31901,
        };

        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddItemDefinition(referencing);
        builder.AddItemDefinition(target);
        ContentRuntime runtime = builder.Build();

        Assert.Same(runtime.Items.GetByProtocolId(31901), runtime.Items.GetByProtocolId(31900).GetContainerItem());
    }
}
