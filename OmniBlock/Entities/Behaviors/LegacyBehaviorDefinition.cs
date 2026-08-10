using System.Text.Json;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     A behavior with no typed <see cref="EntityBehaviorDefinition" /> yet: it keeps its raw JSON
///     and hands it to the string-keyed <c>EntityBehaviorRegistry</c> at build time. Both this and
///     that registry go away once every behavior type appears in
///     <see cref="EntityBehaviorDefinitionRegistry" />.
/// </summary>
internal sealed class LegacyBehaviorDefinition(string typeName, JsonElement json) : EntityBehaviorDefinition
{
    public override object Build(in EntityBehaviorBuildContext context) =>
        EntityBehaviorRegistry.Build(new EntityBehaviorContext(json, context.Definition, context.Layout));

    public override string ToString() => $"legacy behavior '{typeName}'";
}
