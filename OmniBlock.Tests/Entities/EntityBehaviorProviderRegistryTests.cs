using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Entities.State;

namespace OmniBlock.Tests.Entities;

[Collection("EntityTests")]
public sealed class EntityBehaviorProviderRegistryTests
{
    [Fact]
    public void Namespaced_provider_receives_its_type_json_and_build_dependencies()
    {
        var provider = new CapturingProvider();
        var providers = new EntityBehaviorProviderRegistry();
        providers.Register("example:custom", provider);
        EntityDefinition owner = new()
        {
            ProtocolId = 20,
            Name = "owner"
        };
        var json = JsonSerializer.Deserialize<JsonElement>(
            """{"Type":"example:custom","value":42}""");
        var context = new EntityBehaviorBuildContext(
            owner,
            new EntityStateLayout(),
            ContentRuntime.Current.Blocks,
            ContentRuntime.Current.Items,
            new RegistryEntityTypeView(),
            providers);

        var result = context.Build(json);

        Assert.Same(provider.Result, result);
        Assert.Equal(ResourceLocation.Parse("example:custom"), provider.Type);
        Assert.Equal(42, provider.Definition.GetProperty("value").GetInt32());
        Assert.Same(owner, provider.Context.Definition);
        Assert.Same(ContentRuntime.Current.Blocks, provider.Context.Blocks);
        Assert.Same(ContentRuntime.Current.Items, provider.Context.Items);
        Assert.Same(providers, provider.Context.Providers);
    }

    [Fact]
    public void Built_in_short_name_resolves_through_the_omniblock_namespace()
    {
        var providers = new EntityBehaviorProviderRegistry();
        var context = Context(providers);

        Assert.IsType<MeleeAttackBehavior>(context.Build(
            JsonSerializer.Deserialize<JsonElement>("""{"Type":"melee"}""")));
        Assert.IsType<MeleeAttackBehavior>(context.Build(
            JsonSerializer.Deserialize<JsonElement>("""{"Type":"omniblock:melee"}""")));
    }

    [Fact]
    public void Duplicate_and_unknown_providers_fail_with_the_namespaced_key()
    {
        var providers = new EntityBehaviorProviderRegistry();
        var provider = new CapturingProvider();
        providers.Register("example:custom", provider);

        var duplicate = Assert.Throws<ArgumentException>(() => providers.Register("example:custom", provider));
        var unknown = Assert.Throws<ArgumentException>(() => Context(providers).Build(
            JsonSerializer.Deserialize<JsonElement>("""{"Type":"example:missing"}""")));

        Assert.Contains("example:custom", duplicate.Message);
        Assert.Contains("example:missing", unknown.Message);
    }

    private static EntityBehaviorBuildContext Context(IEntityBehaviorProviderRegistry providers) => new(
        new EntityDefinition
        {
            ProtocolId = 20,
            Name = "owner"
        },
        new EntityStateLayout(),
        ContentRuntime.Current.Blocks,
        ContentRuntime.Current.Items,
        new RegistryEntityTypeView(),
        providers);

    private sealed class CapturingProvider : IEntityBehaviorProvider
    {
        public object Result { get; } = new();
        public ResourceLocation? Type { get; private set; }
        public JsonElement Definition { get; private set; }
        public EntityBehaviorBuildContext Context { get; private set; }

        public object Build(ResourceLocation type, JsonElement definition, in EntityBehaviorBuildContext context)
        {
            Type = type;
            Definition = definition;
            Context = context;
            return Result;
        }
    }

    private sealed class RegistryEntityTypeView : IEntityTypeBuildView
    {
        public EntityType Get(ResourceLocation key) => ContentRuntime.Current.EntityTypes.Get(key);
        public bool TryGet(ResourceLocation key, [NotNullWhen(true)] out EntityType? type) => ContentRuntime.Current.EntityTypes.TryGet(key, out type);
    }
}
