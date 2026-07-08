using System;
using System.Collections.Generic;

namespace BetaSharp.Blocks.Entities;

/// <summary>
///     Maps JSON-friendly tile-entity keys to the <see cref="BlockEntityType" /> instances
///     <see cref="BlockEntity" /> already registers into <c>DefaultRegistries.BlockEntityTypes</c>.
///     Deliberately reuses those existing factories rather than constructing a second, competing
///     set — note their internal registry ids are the legacy vanilla NBT type strings ("Trap" for
///     the dispenser, "Music" for the note block), not the friendly keys below, so this mapping
///     also absorbs that naming mismatch in one place.
/// </summary>
internal static class BlockEntityFactoryRegistry
{
    private static readonly Dictionary<string, Func<BlockEntity>> s_factories = new()
    {
        ["furnace"] = BlockEntity.Furnace.Create,
        ["chest"] = BlockEntity.Chest.Create,
        ["record_player"] = BlockEntity.RecordPlayer.Create,
        ["dispenser"] = BlockEntity.Dispenser.Create,
        ["sign"] = BlockEntity.Sign.Create,
        ["mob_spawner"] = BlockEntity.MobSpawner.Create,
        ["note"] = BlockEntity.Note.Create,
        ["piston"] = BlockEntity.Piston.Create,
    };

    public static Func<BlockEntity> Get(string key) =>
        s_factories.TryGetValue(key, out Func<BlockEntity>? factory)
            ? factory
            : throw new ArgumentException($"Unknown tile entity type: '{key}'");
}
