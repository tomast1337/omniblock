using System.Text.Json;
using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Entities;

namespace BetaSharp.Client.Rendering.Entities;

/// <summary>
///     Builds an <see cref="EntityRenderer" /> from the <c>"Renderer"</c> block of an entity
///     definition, mirroring <c>EntityBehaviorRegistry</c> on the server side: a <c>"Type"</c> key
///     selects a factory, and the factory reads whatever else it needs from the same object.
///     <para>
///         A renderer that is nothing but a model and a shadow radius needs no class at all — it is
///         <c>"living"</c> with two values. Renderers that genuinely draw something extra (a held
///         item, a second pass) keep their class and appear here as their own type.
///     </para>
/// </summary>
internal static class EntityRendererRegistry
{
    private delegate EntityRenderer RendererFactory(in JsonElement json);

    private static readonly Dictionary<string, RendererFactory> s_factories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["living"] = (in JsonElement json) => new LivingEntityRenderer(Model(json), Shadow(json)),
        ["flapping"] = (in JsonElement json) => new FlappingEntityRenderer(Model(json), Shadow(json)),
        ["glowing_eyes"] = (in JsonElement json) => new GlowingEyesEntityRenderer(
            Model(json),
            EntityModelRegistry.Create(json.GetProperty("OverlayModel").GetString()!),
            Shadow(json),
            json.GetProperty("OverlayTexture").GetString()!,
            json.TryGetProperty("DeathRotation", out JsonElement d) ? d.GetSingle() : 90.0F),
        ["scaled"] = (in JsonElement json) => new ScaledEntityRenderer(
            Model(json), Shadow(json), json.GetProperty("Scale").GetSingle()),
        ["undead"] = (in JsonElement json) => new UndeadEntityRenderer((ModelBiped)Model(json), Shadow(json)),
        ["creeper"] = (in JsonElement json) => new FuseEntityRenderer(
            Model(json),
            EntityModelRegistry.Create(json.GetProperty("OverlayModel").GetString()!),
            Shadow(json),
            json.GetProperty("OverlayTexture").GetString()!,
            json.GetProperty("OverlayProperty").GetString()!),
        ["fleece"] = (in JsonElement json) => new FleeceEntityRenderer(
            Model(json),
            EntityModelRegistry.Create(json.GetProperty("OverlayModel").GetString()!),
            Shadow(json),
            json.GetProperty("OverlayTexture").GetString()!),
        ["overlay"] = (in JsonElement json) => new OverlayEntityRenderer(
            Model(json),
            EntityModelRegistry.Create(json.GetProperty("OverlayModel").GetString()!),
            Shadow(json),
            json.GetProperty("OverlayProperty").GetString()!,
            json.GetProperty("OverlayTexture").GetString()!)
    };

    public static EntityRenderer Create(JsonElement json)
    {
        string type = json.TryGetProperty("Type", out JsonElement typeJson)
            ? typeJson.GetString() ?? "living"
            : "living";

        return s_factories.TryGetValue(type, out RendererFactory? factory)
            ? factory(json)
            : throw new ArgumentException($"Unknown entity renderer type '{type}'.");
    }

    private static ModelBase Model(in JsonElement json) =>
        EntityModelRegistry.Create(json.GetProperty("Model").GetString()
            ?? throw new ArgumentException("Entity renderer is missing its 'Model' name."));

    private static float Shadow(in JsonElement json) =>
        json.TryGetProperty("Shadow", out JsonElement shadow) ? shadow.GetSingle() : 0.0F;
}
