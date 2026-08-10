using System;
using System.Collections.Generic;

namespace OmniBlock.Blocks.Entities;

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
