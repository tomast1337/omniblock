using BetaSharp.NBT;
using BetaSharp.Util;

namespace BetaSharp.Entities.State;

/// <summary>
///     Creates an entity's declared synced properties on its <see cref="DataSynchronizer" />, and
///     resolves names to wire ids for the behaviors that read them.
/// </summary>
public static class SyncedPropertyFactory
{
    /// <summary>Reserved for <see cref="Entity" />'s shared flags byte, so declarations may not use it.</summary>
    private const int FlagsId = 0;

    private const int MaxId = 31;

    public static void Declare(DataSynchronizer synchronizer, IReadOnlyList<SyncedPropertyDefinition> definitions, string owner)
    {
        foreach (SyncedPropertyDefinition definition in definitions)
        {
            if (definition.Id is FlagsId or < 0 or > MaxId)
            {
                throw new ArgumentException(
                    $"Synced property '{definition.Name}' on '{owner}' declares id {definition.Id}; " +
                    $"ids must be 1..{MaxId} ({FlagsId} is reserved for the shared entity flags byte).");
            }

            switch (definition.Kind)
            {
                case SyncedValueKind.Bool:
                    synchronizer.MakeProperty(definition.Id, definition.Default != 0.0D);
                    break;
                case SyncedValueKind.Byte:
                    synchronizer.MakeProperty(definition.Id, (byte)definition.Default);
                    break;
                case SyncedValueKind.Short:
                    synchronizer.MakeProperty(definition.Id, (short)definition.Default);
                    break;
                case SyncedValueKind.Int:
                    synchronizer.MakeProperty(definition.Id, (int)definition.Default);
                    break;
                case SyncedValueKind.Float:
                    synchronizer.MakeProperty(definition.Id, (float)definition.Default);
                    break;
                case SyncedValueKind.String:
                    synchronizer.MakeProperty<string?>(definition.Id, definition.DefaultString ?? "");
                    break;
                default:
                    throw new ArgumentException($"Unsupported synced property kind '{definition.Kind}' on '{owner}'.");
            }
        }
    }

    /// <summary>Saves every declared property that names an NBT key.</summary>
    public static void Write(DataSynchronizer synchronizer, IReadOnlyList<SyncedPropertyDefinition> definitions, NBTTagCompound nbt)
    {
        foreach (SyncedPropertyDefinition definition in definitions)
        {
            if (definition.Nbt is not { } key)
            {
                continue;
            }

            switch (definition.Kind)
            {
                case SyncedValueKind.Bool: nbt.SetBoolean(key, synchronizer.Get<bool>(definition.Id).Value); break;
                case SyncedValueKind.Byte: nbt.SetByte(key, (sbyte)synchronizer.Get<byte>(definition.Id).Value); break;
                case SyncedValueKind.Short: nbt.SetShort(key, synchronizer.Get<short>(definition.Id).Value); break;
                case SyncedValueKind.Int: nbt.SetInteger(key, synchronizer.Get<int>(definition.Id).Value); break;
                case SyncedValueKind.Float: nbt.SetFloat(key, synchronizer.Get<float>(definition.Id).Value); break;
                case SyncedValueKind.String: nbt.SetString(key, synchronizer.Get<string?>(definition.Id).Value ?? ""); break;
                default: throw new ArgumentException($"Cannot persist synced property kind '{definition.Kind}'.");
            }
        }
    }

    /// <summary>Restores every declared property that names an NBT key.</summary>
    public static void Read(DataSynchronizer synchronizer, IReadOnlyList<SyncedPropertyDefinition> definitions, NBTTagCompound nbt)
    {
        foreach (SyncedPropertyDefinition definition in definitions)
        {
            if (definition.Nbt is not { } key)
            {
                continue;
            }

            switch (definition.Kind)
            {
                case SyncedValueKind.Bool: synchronizer.Get<bool>(definition.Id).Value = nbt.GetBoolean(key); break;
                case SyncedValueKind.Byte: synchronizer.Get<byte>(definition.Id).Value = (byte)nbt.GetByte(key); break;
                case SyncedValueKind.Short: synchronizer.Get<short>(definition.Id).Value = nbt.GetShort(key); break;
                case SyncedValueKind.Int: synchronizer.Get<int>(definition.Id).Value = nbt.GetInteger(key); break;
                case SyncedValueKind.Float: synchronizer.Get<float>(definition.Id).Value = nbt.GetFloat(key); break;
                case SyncedValueKind.String: synchronizer.Get<string?>(definition.Id).Value = nbt.GetString(key); break;
                default: throw new ArgumentException($"Cannot restore synced property kind '{definition.Kind}'.");
            }
        }
    }

    /// <summary>
    ///     Resolves a declared property to a typed handle. Called once per entity type at load, so
    ///     behaviors hold the id instead of looking it up by name at runtime.
    /// </summary>
    public static SyncedHandle<T> Resolve<T>(EntityDefinition definition, string name)
    {
        foreach (SyncedPropertyDefinition property in definition.SyncedProperties)
        {
            if (property.Name != name)
            {
                continue;
            }

            if (!Matches<T>(property.Kind))
            {
                throw new ArgumentException(
                    $"Synced property '{name}' on '{definition.Name}' is declared as {property.Kind}, not {typeof(T).Name}.");
            }

            return new SyncedHandle<T>(property.Id);
        }

        throw new ArgumentException($"'{definition.Name}' declares no synced property named '{name}'.");
    }

    private static bool Matches<T>(SyncedValueKind kind) => kind switch
    {
        SyncedValueKind.Bool => typeof(T) == typeof(bool),
        SyncedValueKind.Byte => typeof(T) == typeof(byte),
        SyncedValueKind.Short => typeof(T) == typeof(short),
        SyncedValueKind.Int => typeof(T) == typeof(int),
        SyncedValueKind.Float => typeof(T) == typeof(float),
        SyncedValueKind.String => typeof(T) == typeof(string),
        _ => false
    };
}
