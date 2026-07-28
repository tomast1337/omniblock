using BetaSharp.NBT;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

/// <summary>
///     The shared body for non-living entities that are nothing but their definition and behaviors —
///     what <see cref="EntityLiving" /> is to mobs, this is to objects like primed TNT. It applies
///     the definition's box and flags and owns no state of its own: anything mutable lives in
///     <see cref="Entity.State" /> and persists through composed <see cref="IEntityPersistence" />.
/// </summary>
public class EntityObject : Entity
{
    public EntityObject(IWorldContext world, EntityType type) : base(world, type)
    {
        Definition = type.RequireDefinition();
        PreventEntitySpawning = Definition.PreventEntitySpawning;
        SetBoundingBoxSpacing(Definition.Width, Definition.Height);
        StandingEyeHeight = Height * Definition.EyeHeightScale;
    }

    protected EntityDefinition Definition { get; }

    public override bool HasCollision => Definition.Collidable && !Dead;

    protected override bool BypassesSteppingEffects() => Definition.MakesStepSounds;

    /// <summary>Zero for every non-living entity — the vertical shadow offset is a mob thing.</summary>
    public override float GetShadowRadius() => 0.0F;

    public override bool ShouldRender(Vec3D vec) => Physics?.ShouldRender(this) ?? base.ShouldRender(vec);

    protected override void ReadNbt(NBTTagCompound nbt) { }

    protected override void WriteNbt(NBTTagCompound nbt) { }
}
