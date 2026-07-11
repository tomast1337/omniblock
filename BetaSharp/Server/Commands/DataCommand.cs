using BetaSharp.Blocks.Entities;
using BetaSharp.Entities;
using BetaSharp.Inventorys;
using BetaSharp.Server.Command;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;
using Brigadier.NET.Builder;
using Brigadier.NET.Context;

namespace BetaSharp.Server.Commands;

public class DataCommand : Command.Command
{
    public override string Usage => "data get <entity|player|global|block> [type|id] [first|close]";
    public override string Description => "Get debug info from target(s)";
    public override string[] Names => ["data"];

    public override LiteralArgumentBuilder<CommandSource> Register(LiteralArgumentBuilder<CommandSource> argBuilder) =>
        argBuilder
            .Then(Literal("get")
                .Then(ArgumentEnum<ListKindEntity>("target")
                    .Executes(c => DataGetCount(c, c.GetArgument<ListKindEntity>("target")))
                    .Then(ArgumentEnum<Selector>("selector").Executes(c => DataGetBySelector(c, c.GetArgument<ListKindEntity>("target"), c.GetArgument<Selector>("selector"))))
                    .Then(ArgumentInt("id").Executes(c => DataGetById(c, c.GetArgument<ListKindEntity>("target"), c.GetArgument<int>("id"))))
                    .Then(ArgumentString("type")
                        .Executes(c => DataGetByType(c, c.GetArgument<ListKindEntity>("target"), c.GetArgument<string>("type"), null))
                        .Then(ArgumentEnum<Selector>("selector").Executes(c => DataGetByType(c, c.GetArgument<ListKindEntity>("target"), c.GetArgument<string>("type"), c.GetArgument<Selector>("selector"))))
                    )
                )
            );

    private static IEnumerable<IEntity> GetEntityList(CommandContext<CommandSource> context, ListKindEntity kind, ServerPlayerEntity player)
    {
        EntityManager entities = context.Source.Server.getWorld(player.DimensionId).Entities;
        return kind switch
        {
            ListKindEntity.Player => entities.Players,
            ListKindEntity.Global => entities.GlobalEntities,
            ListKindEntity.Block => entities.BlockEntities,
            _ => entities.Entities
        };
    }

    private static string KindName(ListKindEntity kind) => kind.ToString();

    private static int DataGetCount(CommandContext<CommandSource> context, ListKindEntity kind)
    {
        ServerPlayerEntity? player = GetSenderPlayer(context);
        if (player == null)
        {
            return 1;
        }

        List<IEntity> items = GetEntityList(context, kind, player).ToList();
        string name = KindName(kind);
        if (items.Count != 1)
        {
            FormatPlural(ref name);
        }

        context.Source.Output.SendMessage($"Found {items.Count} {name}");
        return 1;
    }

    private static int DataGetBySelector(CommandContext<CommandSource> context, ListKindEntity kind, Selector? selector)
    {
        ServerPlayerEntity? player = GetSenderPlayer(context);
        if (player == null)
        {
            return 1;
        }

        LogEntitySub(selector, GetEntityList(context, kind, player), player, context.Source.Output, KindName(kind));
        return 1;
    }

    private static int DataGetById(CommandContext<CommandSource> context, ListKindEntity kind, int id)
    {
        ServerPlayerEntity? player = GetSenderPlayer(context);
        if (player == null)
        {
            return 1;
        }

        IEntity? entity = GetEntityList(context, kind, player).FirstOrDefault(e => e.GetId() == id);
        if (entity == null)
        {
            context.Source.Output.SendMessage($"{id} not found.");
            return 1;
        }

        LogEntity(entity, context.Source.Output);
        return 1;
    }

    private static int DataGetByType(CommandContext<CommandSource> context, ListKindEntity kind, string typeName, Selector? selector)
    {
        ServerPlayerEntity? player = GetSenderPlayer(context);
        if (player == null)
        {
            return 1;
        }

        IEnumerable<IEntity> items = GetEntityList(context, kind, player);
        string displayName;

        if (EntityRegistry.TryGetTypeFromName(typeName, out Type? type))
        {
            displayName = type!.Name;
            items = items.Where(e => e.GetType() == type);
        }
        else
        {
            displayName = typeName;
            items = items.Where(e => e.GetType().Name == typeName);
        }

        LogEntitySub(selector, items, player, context.Source.Output, displayName, selector == null);
        return 1;
    }

    private static void LogEntitySub(Selector? selector, IEnumerable<IEntity> items, ServerPlayerEntity player, ICommandOutput output, string displayName, bool listHits = false)
    {
        if (selector == Selector.First)
        {
            IEntity? item = items.FirstOrDefault();
            if (item == null)
            {
                output.SendMessage($"Found 0 instances of {displayName}");
                return;
            }

            LogEntity(item, output);
        }
        else if (selector == Selector.Close)
        {
            IEntity? closest = null;
            double distance = double.MaxValue;
            double distanceFast = double.MaxValue;

            foreach (IEntity entity in items)
            {
                // Tiered distance check for faster comparison
                double d = Math.Abs(entity.Position.x - player.X) + Math.Abs(entity.Position.z - player.Z);
                if (d * d * 1.15 > distanceFast)
                {
                    continue;
                }

                Vec3D pPos = player.Position;
                Vec3D ePos = entity.Position;
                d = pPos.squareDistance2DTo(ePos);
                if (d > distanceFast)
                {
                    continue;
                }

                double slowD = pPos.squareDistanceTo(ePos);
                if (slowD > distance)
                {
                    continue;
                }

                if (entity is EntityPlayer p && p.ID == player.ID)
                {
                    continue; // don't get self
                }

                distanceFast = d;
                distance = slowD;
                closest = entity;
            }

            if (closest == null)
            {
                output.SendMessage("0 matches");
                return;
            }

            LogEntity(closest, output);
        }
        else
        {
            List<IEntity> list = items.ToList();
            int count = list.Count;

            if (listHits && count > 0)
            {
                output.SendMessage(string.Join(", ", list.Select(e => e.GetId())));
            }

            output.SendMessage($"Found {count} {(count == 1 ? $"instance of {displayName}" : $"instances of {displayName}")}");
        }
    }

    private static void FormatPlural(ref string s)
    {
        if (s == "entity")
        {
            s = "entities";
        }
        else
        {
            s += "s";
        }
    }

    private static void LogEntity(IEntity e, ICommandOutput output)
    {
        if (e is Entity entity)
        {
            LogEntity(entity, output);
        }
        else if (e is BlockEntity blockEntity)
        {
            LogEntity(blockEntity, output);
        }
        else
        {
            output.SendMessage("Unknown entity type: " + e.GetType().Name);
        }
    }

    private static void LogEntity(Entity e, ICommandOutput output)
    {
        output.SendMessage("type: " + e.GetType().Name);
        output.SendMessage("id: " + e.ID);
        output.SendMessage("isPersistent: " + e.IsPersistent);
        output.SendMessage("pos: " + e.Position.ToString("F2"));
        output.SendMessage("age: " + e.Age);
        output.SendMessage("dead: " + e.Dead);
        if (e is EntityPlayer player)
        {
            output.SendMessage("deathTime: " + player.DeathTime);
            output.SendMessage("health: " + player.Health);
            output.SendMessage("name: " + player.Name);
        }
        else if (e is EntityLiving living)
        {
            output.SendMessage("deathTime: " + living.DeathTime);
            output.SendMessage("health: " + living.Health);
        }

        if (e.Passenger != null)
        {
            output.SendMessage("passenger: " + e.Passenger.ID);
        }

        if (e.Vehicle != null)
        {
            output.SendMessage("vehicle: " + e.Vehicle.ID);
        }
    }

    private static void LogEntity(BlockEntity e, ICommandOutput output)
    {
        output.SendMessage("type: " + e.GetType().Name);
        output.SendMessage("name: " + e.getBlock().getBlockName());
        output.SendMessage($"pos: {e.X} {e.Y} {e.Z}");
        output.SendMessage("removed: " + e.isRemoved());

        if (e is IInventory inventory)
        {
            output.SendMessage("size: " + inventory.Size);
        }

        if (e is BlockEntitySign sign)
        {
            output.SendMessage("text: " + string.Join(" | ", sign.Texts));
        }
        else if (e is BlockEntityMobSpawner spawner)
        {
            output.SendMessage("entitySpawned: " + spawner.GetSpawnedEntityId());
            output.SendMessage("spawnDelay: " + spawner.SpawnDelay);
        }
        else if (e is BlockEntityNote note)
        {
            output.SendMessage("note: " + note.note);
        }
        else if (e is BlockEntityRecordPlayer recordPlayer)
        {
            output.SendMessage("record: " + recordPlayer.recordId);
        }
    }

    private enum ListKindEntity
    {
        Entity = 0,
        E = 0,
        Player = 1,
        P = 1,
        Global = 2,
        G = 2,
        Block = 3,
        B = 3,
    }

    private enum Selector
    {
        First = 0,
        F = 0,
        Close = 1,
        C = 1
    }
}
