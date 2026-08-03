using System.Reflection;
using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Items;
using BetaSharp.Util.Hit;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Client.UI;

public enum ControlIcon
{
    A, B, X, Y,
    Lt, Rt, Lb, Rb,
    Ls, Rs,
    LsClick, RsClick,
    DPadUp, DPadDown, DPadLeft, DPadRight,
    Start, Back,
    TouchPad
}

public record ActionTip(ControlIcon Icon, string Action);

public record InGameTipContext(HitResult ObjectMouseOver, IBlockReader WorldReader, ItemStack HeldItem);

public static class ControlTooltip
{
    public static ControllerType ControllerType = ControllerType.XboxOne;

    private static readonly Dictionary<int, bool> s_usabilityCache = [];

    internal static void PopulateInGameTips(InGameTipContext context, List<ActionTip> tips)
    {
        tips.Add(new ActionTip(ControlIcon.Y, "Inventory"));

        string? useAction = null;
        ItemStack held = context.HeldItem;
        HitResult hit = context.ObjectMouseOver;

        if (hit.Type == HitResultType.Tile)
        {
            int blockX = hit.BlockX;
            int blockY = hit.BlockY;
            int blockZ = hit.BlockZ;
            int blockId = context.WorldReader.GetBlockId(blockX, blockY, blockZ);

            if (blockId == BlockRegistry.Get("chest").Id || blockId == BlockRegistry.Get("furnace").Id || blockId == BlockRegistry.Get("lit_furnace").Id || blockId == BlockRegistry.Get("crafting_table").Id || blockId == BlockRegistry.Get("dispenser").Id)
                useAction = "Interact";
            else if (blockId == BlockRegistry.Get("door").Id || blockId == BlockRegistry.Get("iron_door").Id || blockId == BlockRegistry.Get("trapdoor").Id)
                useAction = "Open/Close";
            else if (blockId == BlockRegistry.Get("lever").Id || blockId == BlockRegistry.Get("button").Id || blockId == BlockRegistry.Get("repeater").Id || blockId == BlockRegistry.Get("powered_repeater").Id)
                useAction = "Use";
            else if (blockId == BlockRegistry.Get("bed").Id)
                useAction = "Sleep";
            else if (blockId == BlockRegistry.Get("cake").Id)
                useAction = "Eat";
            else if (blockId == BlockRegistry.Get("jukebox").Id)
                useAction = "Use";
            else if (IsItemUsable(held))
            {
                useAction = GetItemActionLabel(held);
            }
        }
        else if (hit.Type == HitResultType.Entity)
        {
            if (MinecartBehavior.IsMinecart(hit.Entity) || hit.Entity.Behaviors.Find<BoatBehavior>() is not null)
                useAction = "Enter";
            else if (hit.Entity?.Synced<bool>("saddled") is { Value: true })
                useAction = "Ride";
            else if (IsItemUsable(held))
            {
                string label = GetItemActionLabel(held);
                if (label != "Place") useAction = label;
            }
        }
        else if (IsItemUsable(held))
        {
            string label = GetItemActionLabel(held);
            if (label != "Place") useAction = label;
        }

        if (useAction != null)
            tips.Add(new ActionTip(ControlIcon.Lt, useAction));

        if (hit.Type != HitResultType.Miss)
        {
            string attackAction = hit.Type == HitResultType.Entity ? "Attack" : "Mine";
            tips.Add(new ActionTip(ControlIcon.Rt, attackAction));
        }

        if (held != null)
            tips.Add(new ActionTip(ControlIcon.B, "Drop"));
    }

    internal static void PopulateGuiTips(UIScreen screen, List<ActionTip> tips)
    {
        tips.Add(new ActionTip(ControlIcon.B, "Back"));

        screen.GetTooltips(tips);

        if (tips.All(t => t.Icon != ControlIcon.A) && screen.HasInteractiveElementUnderCursor())
            tips.Add(new ActionTip(ControlIcon.A, "Select"));
    }

    internal static string? GetAssetPath(ControlIcon icon)
    {
        string iconName = icon switch
        {
            ControlIcon.A => "down_button",
            ControlIcon.B => "right_button",
            ControlIcon.X => "left_button",
            ControlIcon.Y => "up_button",
            ControlIcon.Lt => "left_trigger",
            ControlIcon.Rt => "right_trigger",
            ControlIcon.Lb => "left_bumper",
            ControlIcon.Rb => "right_bumper",
            ControlIcon.Ls => "left_stick",
            ControlIcon.Rs => "right_stick",
            ControlIcon.LsClick => "left_stick_button",
            ControlIcon.RsClick => "right_stick_button",
            ControlIcon.DPadUp => "dpad_up",
            ControlIcon.DPadDown => "dpad_down",
            ControlIcon.DPadLeft => "dpad_left",
            ControlIcon.DPadRight => "dpad_right",
            ControlIcon.Start => "start_button",
            ControlIcon.Back => "back_button",
            ControlIcon.TouchPad => "touchpad",
            _ => "unknown"
        };

        return $"/gui/controls/{ControllerType.Key}/{iconName}.png";
    }

    private static bool IsItemUsable(ItemStack stack)
    {
        if (stack == null) return false;
        if (stack.ItemId < 256) return true;

        if (s_usabilityCache.TryGetValue(stack.ItemId, out bool usable))
            return usable;

        Item item = stack.GetItem();
        if (item == null) return false;

        Type type = item.GetType();
        MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (MethodInfo method in methods)
        {
            if ((method.Name == "use" || method.Name == "useOnBlock") && method.DeclaringType != typeof(Item))
            {
                usable = true;
                break;
            }
        }

        s_usabilityCache[stack.ItemId] = usable;
        return usable;
    }

    private static string GetItemActionLabel(ItemStack stack)
    {
        if (stack == null) return "Use";
        if (stack.ItemId < 256) return "Place";

        Item item = stack.GetItem();
        if (item == null) return "Use";

        string typeName = item.GetType().Name;
        if (typeName.Contains("Food") || typeName.Contains("Soup") || typeName.Contains("MushroomStew")) return "Eat";
        if (typeName.Contains("Egg") || typeName.Contains("Snowball")) return "Throw";
        if (typeName.Contains("Bow")) return "Shoot";

        return "Use";
    }
}
