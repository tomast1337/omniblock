using System.Text.Json;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     A behavior that has not been given a typed <see cref="EntityBehaviorDefinition" /> yet: it
///     keeps its raw JSON and hands it to the old string-keyed factory table at build time.
///     <para>
///         Scaffolding for the conversion, and the only thing keeping
///         <c>EntityBehaviorRegistry</c> alive. Both go away once every behavior type appears in
///         <see cref="EntityBehaviorDefinitionRegistry" />.
///     </para>
/// </summary>
internal sealed class LegacyBehaviorDefinition(string typeName, JsonElement json) : EntityBehaviorDefinition
{
    public override object Build(in EntityBehaviorBuildContext context) =>
        EntityBehaviorRegistry.Build(new EntityBehaviorContext(json, context.Definition, context.Layout));

    public override string ToString() => $"legacy behavior '{typeName}'";
}
