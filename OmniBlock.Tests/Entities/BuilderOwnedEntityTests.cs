using System.Text.Json;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Registries;
using OmniBlock.Processes;

namespace OmniBlock.Tests.Entities;

[Collection("EntityTests")]
public sealed class BuilderOwnedEntityTests
{
    [Fact]
    public void Builder_compiles_definition_into_a_finalized_type_and_state_layout()
    {
        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddEntityDefinition(Definition("draft_entity", 20,
            """{"Type":"ignore_fall_damage","Slots":["Physics"]}"""));

        _ = builder.Build();

        EntityType type = builder.GetEntityType("omniblock:draft_entity");
        Assert.Equal(typeof(EntityObject), type.BaseType);
        Assert.IsType<IgnoreFallDamageBehavior>(type.Behaviors.Physics);
        Assert.Same(type.Behaviors.StateLayout, type.Behaviors.StateLayout);
        Assert.IsType<EntityObject>(type.Create(new FakeWorldContext()));
    }

    [Fact]
    public void Two_builders_create_independent_entity_types_behaviors_and_layouts()
    {
        ContentRuntimeBuilder first = ContentRuntimeBuilder.CreateBuiltIns();
        ContentRuntimeBuilder second = ContentRuntimeBuilder.CreateBuiltIns();
        first.AddEntityDefinition(Definition("isolated", 20,
            """{"Type":"ignore_fall_damage","Slots":["Physics"]}"""));
        second.AddEntityDefinition(Definition("isolated", 20,
            """{"Type":"ignore_fall_damage","Slots":["Physics"]}"""));

        _ = first.Build();
        _ = second.Build();
        EntityType firstType = first.GetEntityType("omniblock:isolated");
        EntityType secondType = second.GetEntityType("omniblock:isolated");

        Assert.NotSame(firstType, secondType);
        Assert.NotSame(firstType.Behaviors, secondType.Behaviors);
        Assert.NotSame(firstType.Behaviors.Physics, secondType.Behaviors.Physics);
        Assert.NotSame(firstType.Behaviors.StateLayout, secondType.Behaviors.StateLayout);
    }

    [Fact]
    public void Failed_entity_compilation_does_not_publish_a_partial_legacy_catalog()
    {
        ContentRuntime published = ContentRuntime.Current;
        int legacyCount = DefaultRegistries.EntityTypes.Count();
        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddEntityDefinition(Definition("broken", 20,
            """{"Type":"example:missing","Slots":["Physics"]}"""));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("omniblock:broken", error.Message);
        Assert.Contains("example:missing", error.Message);
        Assert.Same(published, ContentRuntime.Current);
        Assert.Equal(legacyCount, DefaultRegistries.EntityTypes.Count());
        Assert.False(DefaultRegistries.EntityTypes.ContainsKey("omniblock:broken"));
    }

    [Fact]
    public void Later_process_failure_does_not_publish_successfully_built_entities()
    {
        ContentRuntime published = ContentRuntime.Current;
        int legacyCount = DefaultRegistries.EntityTypes.Count();
        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddEntityDefinition(Definition("complete_but_unpublished", 20));
        builder.AddProcessDefinition(new ProcessDefinition
        {
            Name = "broken_process",
            Namespace = Namespace.OmniBlock,
            DeclaredId = "omniblock:broken_process",
            Type = "example:missing_provider"
        });

        ArgumentException error = Assert.Throws<ArgumentException>(() => builder.Build());

        Assert.Contains("missing_provider", error.Message);
        Assert.Same(published, ContentRuntime.Current);
        Assert.Equal(legacyCount, DefaultRegistries.EntityTypes.Count());
        Assert.False(DefaultRegistries.EntityTypes.ContainsKey("omniblock:complete_but_unpublished"));
    }

    private static EntityDefinition Definition(string name, int protocolId, params string[] behaviors) => new()
    {
        Name = name,
        Namespace = Namespace.OmniBlock,
        ProtocolId = protocolId,
        Behaviors = [.. behaviors.Select(json => JsonSerializer.Deserialize<JsonElement>(json))]
    };
}
