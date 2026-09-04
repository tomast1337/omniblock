using OmniBlock.Items;

namespace OmniBlock.Tests.Items;

public sealed class ItemRegistryTests
{
    [Fact]
    public void Apple_RegistersUnderExpectedResourceLocation()
    {
        var definition = Assert.Single(TestItemCatalog.LoadDefinitions(),
            static definition => definition.Name == "apple");

        Assert.Equal(ContentRuntime.Current.Items.Get("omniblock:apple").Id, definition.ProtocolId);
        Assert.Equal("food", definition.Behaviors.Single().GetProperty("Type").GetString());
    }

    [Fact]
    public void DuplicateTranslationKeys_DisambiguateByProtocolId()
    {
        // Both records share the translation key "record"; the first declared keeps the plain
        // name and the second is disambiguated with its protocol ID suffix.
        var definitions = TestItemCatalog.LoadDefinitions();
        var thirteen = Assert.Single(definitions, static definition => definition.Name == "record");
        var cat = Assert.Single(definitions, static definition => definition.Name == "record_2257");

        Assert.Equal(ContentRuntime.Current.Items.Get("omniblock:record").Id, thirteen.ProtocolId);
        Assert.Equal(ContentRuntime.Current.Items.Get("omniblock:record_2257").Id, cat.ProtocolId);
    }

    [Fact]
    public void JsonLoadedItems_AreRegistered()
    {
        Assert.NotNull(ContentRuntime.Current.Items.Get("omniblock:shovel_iron"));
        Assert.NotNull(ContentRuntime.Current.Items.Get("omniblock:boots_diamond"));
        Assert.NotNull(ContentRuntime.Current.Items.Get("omniblock:map"));
    }

    [Fact]
    public void CraftingReturnItems_AreWiredAfterBoot()
    {
        var bucket = ContentRuntime.Current.Items.Get("omniblock:bucket");

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
            CraftingReturnItemProtocolId = 31901
        };
        var target = new ItemDefinition
        {
            Name = "test_empty_container",
            ProtocolId = 31901
        };

        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddItemDefinition(referencing);
        builder.AddItemDefinition(target);
        var runtime = builder.Build();

        Assert.Same(runtime.Items.GetByProtocolId(31901), runtime.Items.GetByProtocolId(31900).GetContainerItem());
    }
}
