using BetaSharp.Entities;

namespace BetaSharp.Server.Commands;

public static class DataCommands
{
    public static void Data(MinecraftServer server, string senderName, string[] args, CommandOutput output)
    {
        if (args.Length < 2)
        {
            output.SendMessage("Usage: data [get] ...");
            return;
        }

        ServerPlayerEntity player = server.playerManager.getPlayer(senderName);
        if (player == null)
        {
            output.SendMessage("Could not find your player.");
            return;
        }


        if (args[0].ToLower() == "get")
        {
            if (args.Length < 2)
            {
                output.SendMessage("Usage: data get [entity|player|global] <target?>");
                return;
            }

            DataGet(server, player, args, output);
        }
        else
        {
            // TODO: Add a set here
            output.SendMessage(args[0] + " is not supported.");
        }
    }

    private static void DataGet(MinecraftServer server, ServerPlayerEntity player, string[] args, CommandOutput output)
    {
        // convert short to the full string.
        switch (args[1] = args[1].ToLower())
        {
            case "e":
                args[1] = "entity";
                break;
            case "p":
                args[1] = "player";
                break;
            case "g":
                args[1] = "global";
                break;
        }

        switch (args[1])
        {
            case "entity":
                LogEntity(server.getWorld(player.dimensionId).entities, player, args, output);
                return;
            case "player":
                LogEntity(server.getWorld(player.dimensionId).players, player, args, output);
                return;
            case "global":
                LogEntity(server.getWorld(player.dimensionId).globalEntities, player, args, output);
                return;
            default:
                output.SendMessage("Usage: data get [entity|player|global] <target?>");
                return;
        }
    }

    private static void LogEntity<T>(List<T> items, ServerPlayerEntity player, string[] args, CommandOutput output) where T : Entity
    {
        var sel = new Selector(args);

        if (args.Length < 3)
        {
            if (items.Count == 1) FormatPlural(ref args[1]);
            output.SendMessage("Found " + items.Count + " " + args[1]);
            return;
        }

        if (args.Length < 4 && sel.HaveSelector)
        {
            if (LogEntitySub(sel, items, player, args, output)) return;

            if (items.Count == 1) FormatPlural(ref args[1]);
            output.SendMessage("Found " + items.Count + " " + args[1]);
            return;
        }

        if (!int.TryParse(args[2], out int id))
        {
            if (LogEntitySub(sel, items.Where(entity => entity.GetType().Name == args[2]), player, args, output)) return;

            int count = 0;
            foreach (T item in items)
            {
                if (item.GetType().Name == args[2])
                {
                    count++;
                    output.SendMessage(" - " + item.id);
                }
            }

            if (count == 1) FormatPlural(ref args[1]);
            output.SendMessage("Found " + count + " instances of " + args[1]);
            return;
        }

        T? e = items.Find(entity => entity.id == id);

        if (e == null)
        {
            output.SendMessage(id + " not found.");
            return;
        }

        LogEntity(e, output);
    }

    private class Selector
    {
        public bool HaveSelector;
        public bool first = false;
        public bool close = false;

        public Selector(string[] args, int start = 2)
        {
            HaveSelector = true;
            for (int i = start, l = args.Length; i < l; i++)
            {
                string low = args[i].ToLower();
                if (low == "first")
                {
                    first = true;
                    return;
                }

                if (low == "close")
                {
                    close = true;
                    return;
                }
            }

            HaveSelector = false;
        }
    }

    private static bool LogEntitySub<T>(Selector sel, IEnumerable<T> items, ServerPlayerEntity player, string[] args, CommandOutput output) where T : Entity
    {
        if (sel.first)
        {
            T? item = items.FirstOrDefault();
            if (item == null)
            {
                FormatPlural(ref args[1]);
                output.SendMessage("Found 0 instances of " + args[1]);
                return true;
            }

            LogEntity(item, output);
        }
        else if (sel.close)
        {
            T? item = null;
            double distance = double.MaxValue;
            double distanceFast = double.MaxValue;

            foreach (T entity in items)
            {
                // Tiered distance check for faster comparison
                double d = Math.Abs(entity.x - player.x) + Math.Abs(entity.z - player.z);
                if (d * d * 1.15 > distanceFast) continue;
                var pPos = player.Position;
                var ePos = entity.Position;
                d = pPos.squareDistance2DTo(ePos);
                if (d > distanceFast) continue;
                double slowD = pPos.squareDistanceTo(ePos);
                if (slowD > distance) continue;
                if (entity.id == player.id) continue; // dont get self

                distanceFast = d;
                distance = slowD;
                item = entity;
            }

            if (item == null)
            {
                FormatPlural(ref args[1]);
                output.SendMessage("0 matches");
                return true;
            }

            LogEntity(item, output);
        }
        else
        {
            return false;
        }

        return true;
    }

    private static void FormatPlural(ref string s)
    {
        if (s == "entity") s = "entities";
        else s += "s";
    }

    private static void LogEntity(Entity e, CommandOutput output)
    {
        output.SendMessage("type: " + e.GetType().Name);
        output.SendMessage("id: " + e.id);
        output.SendMessage("alive: " + !e.dead);
        output.SendMessage("isPersistent: " + e.isPersistent);
        output.SendMessage("age: " + e.age);
        output.SendMessage("pos: " + e.Position.ToString("F2"));
        if (e.passenger != null)
            output.SendMessage("Passenger: " + e.passenger.id);
        if (e.vehicle != null)
            output.SendMessage("Vehicle: " + e.vehicle.id);
        if (e is EntityPlayer player)
        {
            output.SendMessage("health: " + player.health);
            output.SendMessage("name: " + player.name);
        }
        else if (e is EntityLiving living)
        {
            output.SendMessage("health: " + living.health);
        }
    }
}
