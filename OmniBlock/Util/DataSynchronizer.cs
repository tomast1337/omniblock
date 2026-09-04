using OmniBlock.Items;
using OmniBlock.Registries;
using OmniBlock.Util.Maths;

namespace OmniBlock.Util;

public sealed class DataSynchronizer
{
    public static readonly Dictionary<Type, SyncedDataType> TypeIds = [];
    private readonly IItemRuntimeView _items;

    private readonly Dictionary<int, ISyncedProperty> _syncedProperties = new();

    static DataSynchronizer()
    {
        TypeIds[typeof(byte)] = SyncedDataType.Byte;
        TypeIds[typeof(short)] = SyncedDataType.Short;
        TypeIds[typeof(int)] = SyncedDataType.Int;
        TypeIds[typeof(float)] = SyncedDataType.Float;
        TypeIds[typeof(string)] = SyncedDataType.String;
        TypeIds[typeof(ItemStack)] = SyncedDataType.ItemStack;
        TypeIds[typeof(Vec3I)] = SyncedDataType.Vec3i;

        TypeIds[typeof(bool)] = SyncedDataType.Byte; // Serialize bools as bytes
    }

    public DataSynchronizer(IItemRuntimeView items) => _items = items ?? throw new ArgumentNullException(nameof(items));
    public bool Dirty { get; internal set; }

    public SyncedProperty<T> MakeProperty<T>(int dataValueId, T initialValue)
    {
        if (!TypeIds.TryGetValue(typeof(T), out var dataType))
        {
            throw new ArgumentException("Unknown data type: " + typeof(T));
        }

        if (dataValueId > 31)
        {
            throw new ArgumentException("Data value id is too big with " + dataValueId + "! (Max is " + 31 + ")");
        }

        if (_syncedProperties.ContainsKey(dataValueId))
        {
            throw new ArgumentException("Duplicate id value for " + dataValueId + "!");
        }

        var prop = new SyncedProperty<T>(this, dataValueId, dataType, initialValue);
        _syncedProperties[dataValueId] = prop;
        return prop;
    }

    /// <summary>
    ///     Resolves a property declared by <see cref="MakeProperty{T}" />. Behaviors are shared per
    ///     entity type, so they hold the id and look the instance up here rather than holding the
    ///     property itself.
    /// </summary>
    public SyncedProperty<T> Get<T>(int dataValueId) =>
        _syncedProperties.TryGetValue(dataValueId, out var prop) && prop is SyncedProperty<T> typed
            ? typed
            : throw new ArgumentException($"No synced property of type {typeof(T).Name} declared with id {dataValueId}.", nameof(dataValueId));

    public bool Has(int dataValueId) => _syncedProperties.ContainsKey(dataValueId);

    private static void SerializeProperty(Stream stream, ISyncedProperty obj)
    {
        var header = (byte)(((int)obj.DataType << 5) | (obj.DataValueId & 31));
        stream.WriteByte(header);

        switch (obj)
        {
            case SyncedProperty<bool>(var b):
                stream.WriteBoolean(b);
                break;
            case SyncedProperty<byte>(var b):
                stream.WriteByte(b);
                break;
            case SyncedProperty<short>(var s):
                stream.WriteShort(s);
                break;
            case SyncedProperty<int>(var i):
                stream.WriteInt(i);
                break;
            case SyncedProperty<float>(var f):
                stream.WriteFloat(f);
                break;
            case SyncedProperty<string>(var str):
                stream.WriteLongString(str);
                break;
            case SyncedProperty<ItemStack>(var item):
                stream.WriteShort((short)item.GetItem().Id);
                stream.WriteByte((byte)item.Count);
                stream.WriteShort((short)item.GetDamage());
                break;
            case SyncedProperty<Vec3I>(var vec):
                stream.WriteInt(vec.X);
                stream.WriteInt(vec.Y);
                stream.WriteInt(vec.Z);
                break;
            default: throw new ArgumentException("Unsupported data type: " + obj.DataType);
        }
    }

    private void DeserializeProperty(Stream stream, SyncedDataType objectType, int dataValueId)
    {
        if (!_syncedProperties.TryGetValue(dataValueId, out var prop))
        {
            SkipProperty(stream, objectType);
            return;
        }

        if (prop.DataType != objectType)
        {
            SkipProperty(stream, objectType);
            return;
        }

        switch (objectType)
        {
            case SyncedDataType.Byte:
                if (prop is SyncedProperty<bool> property)
                {
                    property.Value = stream.ReadByte() != 0;
                    break;
                }

                ((SyncedProperty<byte>)prop).Value = (byte)stream.ReadByte();
                break;
            case SyncedDataType.Short:
                ((SyncedProperty<short>)prop).Value = stream.ReadShort();
                break;
            case SyncedDataType.Int:
                ((SyncedProperty<int>)prop).Value = stream.ReadInt();
                break;
            case SyncedDataType.Float:
                ((SyncedProperty<float>)prop).Value = stream.ReadFloat();
                break;
            case SyncedDataType.String:
                ((SyncedProperty<string>)prop).Value = stream.ReadLongString();
                break;
            case SyncedDataType.ItemStack:
                ((SyncedProperty<ItemStack>)prop).Value = new ItemStack(_items, stream.ReadShort(), (sbyte)stream.ReadByte(), stream.ReadShort());
                break;
            case SyncedDataType.Vec3i:
                ((SyncedProperty<Vec3I>)prop).Value = new Vec3I(stream.ReadInt(), stream.ReadInt(), stream.ReadInt());
                break;
            default:
                throw new ArgumentException("Unsupported data type: " + objectType);
        }
    }

    private static void SkipProperty(Stream stream, SyncedDataType objectType)
    {
        switch (objectType)
        {
            case SyncedDataType.Byte: stream.ReadByte(); break;
            case SyncedDataType.Short: stream.ReadShort(); break;
            case SyncedDataType.Int: stream.ReadInt(); break;
            case SyncedDataType.Float: stream.ReadFloat(); break;
            case SyncedDataType.String: stream.ReadLongString(); break;
            case SyncedDataType.ItemStack:
                stream.ReadShort();
                stream.ReadByte();
                stream.ReadShort();
                break;
            case SyncedDataType.Vec3i:
                stream.ReadInt();
                stream.ReadInt();
                stream.ReadInt();
                break;
        }
    }

    public void WriteAll(Stream stream)
    {
        foreach (var obj in _syncedProperties.Values)
        {
            SerializeProperty(stream, obj);
        }
    }

    public void WriteChanges(Stream stream)
    {
        if (Dirty)
        {
            foreach (var obj in _syncedProperties.Values)
            {
                if (obj.Dirty)
                {
                    SerializeProperty(stream, obj);
                    obj.Dirty = false;
                }
            }
        }

        Dirty = false;
    }

    public void ApplyChanges(Stream stream)
    {
        while (true)
        {
            var b = stream.ReadByte();
            if (b == -1) break;
            var objectType = (SyncedDataType)((b & 224) >> 5);
            var dataValueId = b & 31;
            DeserializeProperty(stream, objectType, dataValueId);
        }
    }
}
