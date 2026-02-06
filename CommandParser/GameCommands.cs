using betareborn.Blocks;
using betareborn.Entities;
using betareborn.Items;
using System.Reflection;

namespace betareborn.CommandParser;

public class GameCommands {
    public static Dictionary<string, int> allItems = new();
    public static Dictionary<int, Block> blocks = new();
    public static Dictionary<int, Item> items = new();

    public GameCommands() {
        { // Get blocks
            var fields = typeof(Block).GetFields(BindingFlags.Static | BindingFlags.Public)
                .Where(f => f.FieldType == typeof(Block))
                .ToList();
            fields.ForEach(x => {
                var block = ((Block)x.GetValue(null));
                var id = block.blockID;
                allItems.TryAdd(x.Name.ToLower(), id);
                blocks.TryAdd(id, block);
            });
        }


        { // Get items
            var fields = typeof(Item).GetFields(BindingFlags.Static | BindingFlags.Public)
                .Where(f => f.FieldType == typeof(Item))
                .ToList();
            fields.ForEach(x => {
                var item = ((Item)x.GetValue(null));
                var id = item.shiftedIndex;
                allItems.TryAdd(x.Name.ToLower(), id);
                items.TryAdd(id, item);
            });
        }
    }

    [MinecraftCommand("clear")]
    public void ClearInventory(CommandContext ctx) {
        var inventory = ctx.Game.thePlayer.inventory.mainInventory;
        for (int i = 0; i < inventory.Length; i++) {
            inventory[i] = null;
        }

        ctx.Reply("Inventory cleared.");
    }

    [MinecraftCommand("give")]
    public void GiveItem(CommandContext ctx, string itemName, int count = -1) {
        itemName = itemName.ToLower();
        if (allItems.TryGetValue(itemName, out var itemId)) {
            int finalCount = count;

            if (count == -1) finalCount = items.TryGetValue(itemId, out var item) ? item.maxStackSize : 64;

            ctx.Game.thePlayer.inventory.addItemStackToInventory(new ItemStack(id: itemId, count: finalCount));
            ctx.Reply($"Gave {finalCount} of {itemName}");
        }
        else {
            ctx.Reply($"Item '{itemName}' not found.");
        }
    }


    [MinecraftCommand("heal")]
    public void Heal(CommandContext ctx, int amount = 20) {
        ctx.Game.thePlayer.heal(amount);
    }

    [MinecraftCommand("settime")]
    public void SetTime(CommandContext ctx, string timeValue) {
        long? timeToSet = timeValue.ToLower() switch {
            "sunrise" or "dawn" => 0,
            "morning" => 1000,
            "noon" or "day" => 6000,
            "sunset" or "dusk" => 12000,
            "night" => 13000,
            "midnight" => 18000,
            _ => long.TryParse(timeValue, out long t) ? t : null
        };

        if (timeToSet.HasValue) {
            ctx.Game.theWorld.setWorldTime(timeToSet.Value);
            ctx.Reply($"Time set to {timeValue} ({timeToSet.Value})");
        }
        else {
            ctx.Reply($"Invalid time value: {timeValue}");
        }
    }

    [MinecraftCommand("teleport", aliases: "tp")]
    public void Teleport(CommandContext ctx, float x, float y, float z) {
        ctx.Game.thePlayer.setPosition(x, y, z);
    }

    [MinecraftCommand("summon", aliases: "spawn")]
    public void Summon(CommandContext ctx, string name) {
        var p = ctx.Game.thePlayer;
        var ent = EntityList.createEntityAt(name, ctx.Game.theWorld, (float)p.posX, (float)p.posY, (float)p.posZ);

        if (ent == null) {
            Console.Error.WriteLine($"Entity created by createEntityInWorld is null `{name}`");
        }
    }

    [MinecraftCommand("weather")]
    public void Weather(CommandContext ctx, string command) {
        command = command.ToLower();
        switch (command) {
            case "clear": {
                ctx.Game.theWorld.weatherEffects.clear();
                ctx.Game.theWorld.getWorldInfo().setRaining(false);
                ctx.Game.theWorld.getWorldInfo().setThundering(false);
                Console.WriteLine("Clear Weather");
                break;
            }
            case "rain": {
                ctx.Game.theWorld.getWorldInfo().setRaining(true);
                ctx.Game.theWorld.getWorldInfo().setThundering(false);
                break;
            }
            case "storm": {
                ctx.Game.theWorld.getWorldInfo().setRaining(true);
                ctx.Game.theWorld.getWorldInfo().setThundering(true);
                break;
            }
        }
    }

    [MinecraftCommand("killall")]
    public void KillAll(CommandContext ctx, string filter = "all") {
        var world = ctx.Game.theWorld;
        var entities = new List<Entity>(world.loadedEntityList);
        int count = 0;
        filter = filter.ToLower();

        foreach (var ent in entities) {
            if (ent is EntityPlayer) continue;

            bool shouldKill = filter switch {
                "all" => true,
                "living" or "mob" => ent is EntityLiving,
                "monster" => ent is EntityMob,
                "animal" => ent is EntityAnimal,
                "item" => ent is EntityItem,
                "tnt" => ent is EntityTNTPrimed,
                _ => EntityList.getEntityString(ent)?.Equals(filter, StringComparison.OrdinalIgnoreCase) ?? false
            };

            if (shouldKill) {
                world.setEntityDead(ent);
                count++;
            }
        }

        ctx.Reply($"Killed {count} entities (filter: {filter}).");
    }

    [MinecraftCommand("dis")]
    public void Distance(CommandContext ctx, int dist) {
        ctx.Game.gameSettings.renderDistance = dist;
    }

    [MinecraftCommand("help", description: "this command", usage: "/help <command>")]
    public void Help(CommandContext ctx, String command = "") {
        if (command == "") {
            string str = "Avaiable commands:\n";
            foreach (var item in CommandService._commands) {
                var attribute = item.Value.GetCustomAttribute<MinecraftCommandAttribute>();
                if (attribute is null) {
                    continue;
                }
                if (attribute.Name == item.Key) {
                    str += $"/{item.Key}" + (attribute.Description != "" ? $" - {attribute.Description}" : "") + "\n";
                } else {
                    str += $"/{item.Key} (alias of {attribute.Name})\n";
                }
            }

            ctx.Reply(str);
            return;
        } 
        
        if (!CommandService._commands.ContainsKey(command)) {
            ctx.Reply($"Cannot find command `{command}`");
            return;
        }


        var attr = CommandService._commands[command].GetCustomAttribute<MinecraftCommandAttribute>();
        if (attr is null) {
            ctx.Reply("Unknown error");
            return;
        }

        ctx.Reply($"Usage:\n{attr.Usage}");
    }   
}