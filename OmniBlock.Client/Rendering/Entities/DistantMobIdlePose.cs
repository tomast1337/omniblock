using OmniBlock.Entities;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>
///     A deterministic, client-only sign of life for mobs whose authoritative simulation is
///     paused. It changes presentation angles only; entity state, networking and collision remain
///     untouched.
/// </summary>
internal static class DistantMobIdlePose
{
    private const int LoopTicks = 320;
    private static readonly float[] s_bodyYaw = [0, 0, 0, 7, 7, 0, -5, 0, 0];
    private static readonly float[] s_headYaw = [0, 0, 24, 24, 6, -18, -18, 0, 0];
    private static readonly float[] s_pitch = [0, 0, 3, 1, 0, 4, 2, 0, 0];

    public static EntityPresentationPose? Sample(
        Entity entity,
        EntityPlayer? player,
        int simulationDistance,
        long worldTime,
        float partialTicks)
    {
        if (entity is not EntityLiving living || entity is EntityPlayer || player == null || entity.Dead)
            return null;

        var entityChunkX = MathHelper.Floor(entity.X / 16.0);
        var entityChunkZ = MathHelper.Floor(entity.Z / 16.0);
        var playerChunkX = MathHelper.Floor(player.X / 16.0);
        var playerChunkZ = MathHelper.Floor(player.Z / 16.0);
        var chunkDistance = Math.Max(Math.Abs(entityChunkX - playerChunkX), Math.Abs(entityChunkZ - playerChunkZ));
        if (chunkDistance <= simulationDistance) return null;

        // Fade in across the first paused chunk so crossing the activation boundary does not snap.
        var distanceInChunks = Math.Max(Math.Abs(entity.X - player.X), Math.Abs(entity.Z - player.Z)) / 16.0;
        var strength = (float)Math.Clamp(distanceInChunks - simulationDistance, 0, 1);
        var baseBody = EntityLodDirections.InterpolateYaw(living.LastBodyYaw, living.BodyYaw, partialTicks);
        var baseHead = EntityLodDirections.InterpolateYaw(entity.PrevYaw, entity.Yaw, partialTicks);
        var basePitch = entity.PrevPitch + (entity.Pitch - entity.PrevPitch) * partialTicks;
        var phaseOffset = SpatialPhase(MathHelper.Floor(entity.X), MathHelper.Floor(entity.Z));
        var phase = PositiveModulo(worldTime + partialTicks + phaseOffset, LoopTicks) / LoopTicks;

        return new EntityPresentationPose(
            baseBody + SampleCurve(s_bodyYaw, phase) * strength,
            baseHead + SampleCurve(s_headYaw, phase) * strength,
            basePitch + SampleCurve(s_pitch, phase) * strength);
    }

    private static float SampleCurve(float[] points, double phase)
    {
        var scaled = phase * (points.Length - 1);
        var index = Math.Min((int)scaled, points.Length - 2);
        var amount = (float)(scaled - index);
        amount = amount * amount * (3 - 2 * amount);
        return points[index] + (points[index + 1] - points[index]) * amount;
    }

    private static int SpatialPhase(int x, int z)
    {
        unchecked
        {
            var hash = (uint)x * 0x9E3779B1u ^ (uint)z * 0x85EBCA77u;
            hash ^= hash >> 16;
            return (int)(hash % LoopTicks);
        }
    }

    private static double PositiveModulo(double value, double divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}

internal readonly record struct EntityPresentationPose(float BodyYaw, float HeadYaw, float Pitch);
