using System.Globalization;
using System.Text;
using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Registries;
using OmniBlock.Registries.Data;

namespace OmniBlock.Tests.Catalog;

/// <summary>
/// Characterizes the catalog produced by the shipped JSON assets. This deliberately records the
/// merged definitions and their runtime composition rather than duplicating every source file.
/// An intentional content change should update the readable snapshot in the same commit.
/// </summary>
public sealed class CatalogSnapshotTests
{
    private const string SnapshotFileName = "catalog.snapshot.txt";

    [Fact]
    public void Shipped_catalog_matches_snapshot()
    {
        string actual = CatalogSnapshot.Build();
        string expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Catalog", SnapshotFileName));

        Assert.Equal(Normalize(expected), Normalize(actual));
    }

    private static string Normalize(string value) => value.ReplaceLineEndings("\n").TrimEnd();
}

internal static class CatalogSnapshot
{
    internal static string Build()
    {
        StringBuilder snapshot = new();
        AppendBlocks(snapshot);
        AppendItems(snapshot);
        AppendEntities(snapshot);
        return snapshot.ToString();
    }

    private static void AppendBlocks(StringBuilder snapshot)
    {
        BlockDefinitionJsonLoader loader = new(RegistryDefinitions.Blocks.AssetPath, LoadLocations.Assets);
        loader.LoadFromPaths(null, null, null);
        AssertLoaderSucceeded(loader);

        snapshot.AppendLine("[blocks]");
        foreach (BlockDefinition definition in loader.OrderBy(static d => d.ProtocolId))
        {
            Block block = TestBlocks.Get(definition.Name);
            snapshot.Append(definition.ProtocolId.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(Key(definition.Namespace, definition.Name))
                .Append(" material=").Append(definition.Material)
                .Append(" sound=").Append(definition.SoundGroup ?? "-")
                .Append(" render=").Append(definition.RenderType)
                .Append(" randomTick=").Append(Bool(definition.TickRandomly))
                .Append(" tileEntity=").Append(definition.TileEntity ?? "-")
                .Append(" behaviors=").Append(BlockBehaviors(definition))
                .Append(" runtimeSlots=").Append(RuntimeBlockSlots(block))
                .AppendLine();
        }

        snapshot.AppendLine();
    }

    private static void AppendItems(StringBuilder snapshot)
    {
        snapshot.AppendLine("[items]");
        foreach (ItemDefinition definition in TestItemCatalog.LoadDefinitions().OrderBy(static d => d.ProtocolId))
        {
            Item item = ContentRuntime.Current.Items.GetByProtocolId(definition.ProtocolId);
            snapshot.Append(definition.ProtocolId.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(Key(definition.Namespace, definition.Name))
                .Append(" stack=").Append(item.GetMaxCount().ToString(CultureInfo.InvariantCulture))
                .Append(" durability=").Append(item.GetMaxDamage().ToString(CultureInfo.InvariantCulture))
                .Append(" behaviors=").Append(string.Join(',', definition.Behaviors.Select(static behavior => behavior.GetProperty("Type").GetString())))
                .AppendLine();
        }

        snapshot.AppendLine();
    }

    private static void AppendEntities(StringBuilder snapshot)
    {
        snapshot.AppendLine("[entities]");
        foreach (EntityType type in ContentRuntime.Current.EntityTypes.Values
                     .Where(static type => type.Definition is not null)
                     .OrderBy(type => type.Definition!.ProtocolId))
        {
            EntityDefinition definition = type.RequireDefinition();
            snapshot.Append(definition.ProtocolId.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(Key(definition.Namespace, definition.Name))
                .Append(" runtime=").Append(type.BaseType.Name)
                .Append(" health=").Append(definition.Health.ToString(CultureInfo.InvariantCulture))
                .Append(" speed=").Append(definition.MovementSpeed.ToString("R", CultureInfo.InvariantCulture))
                .Append(" spawnObject=").Append(definition.SpawnObjectId.ToString(CultureInfo.InvariantCulture))
                .Append(" globalSpawn=").Append(definition.GlobalSpawnId.ToString(CultureInfo.InvariantCulture))
                .Append(" behaviors=").Append(EntityBehaviors(definition))
                .AppendLine();
        }
    }

    private static string BlockBehaviors(BlockDefinition definition) =>
        Join(definition.Behaviors.Select(static behavior =>
        {
            string type = behavior.GetProperty("Type").GetString() ?? "?";
            string slots = Join(behavior.GetProperty("Slots").EnumerateArray().Select(static slot => slot.GetString() ?? "?"));
            return $"{type}[{slots}]";
        }));

    private static string EntityBehaviors(EntityDefinition definition) =>
        Join(definition.Behaviors.Select(static behavior =>
            $"{behavior.GetProperty("Type").GetString()}[{Join(behavior.GetProperty("Slots").EnumerateArray().Select(static slot => slot.GetString() ?? "?"))}]"));

    private static string RuntimeBlockSlots(Block block)
    {
        List<string> slots = [];
        Add(block.Ticker, "Ticker");
        Add(block.Physics, "Physics");
        Add(block.Lifecycle, "Lifecycle");
        Add(block.Visuals, "Visuals");
        Add(block.Interactable, "Interactable");
        Add(block.Redstone, "Redstone");
        return Join(slots);

        void Add(object? behavior, string slot)
        {
            if (behavior is not null) slots.Add($"{slot}:{behavior.GetType().Name}");
        }
    }

    private static string Key(Namespace @namespace, string name) => $"{@namespace}:{name}";
    private static string Bool(bool value) => value ? "true" : "false";
    private static string Join(IEnumerable<string> values) => string.Join(',', values);

    private static void AssertLoaderSucceeded(DataAssetLoader loader)
    {
        if (loader.HasErrors)
        {
            throw new InvalidOperationException(loader.FirstErrorMessage ?? "Catalog loader failed.");
        }
    }
}
