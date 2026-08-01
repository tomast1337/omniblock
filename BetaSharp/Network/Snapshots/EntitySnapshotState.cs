namespace BetaSharp.Network.Snapshots;

/// <summary>
///     One entity's replicated position and facing, in the units the wire uses.
///     <para>
///         Fixed point at 1/32 of a block and 1/256 of a turn, which is what Beta's position packets
///         have always carried. Kept in those units rather than converted to doubles because this is
///         the quantity the delta is taken over: two <see cref="double" /> positions differ in the
///         low bits of the mantissa every tick an entity is nominally still, and a delta encoder
///         given that has nothing to compress.
///     </para>
/// </summary>
/// <param name="X">Position along X, in 1/32 blocks.</param>
/// <param name="Y">Position along Y, in 1/32 blocks.</param>
/// <param name="Z">Position along Z, in 1/32 blocks.</param>
/// <param name="Yaw">Facing, in 1/256 of a turn.</param>
/// <param name="Pitch">Pitch, in 1/256 of a turn.</param>
public readonly record struct EntitySnapshotState(int X, int Y, int Z, byte Yaw, byte Pitch);
