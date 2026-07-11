using BetaSharp.Entities;
using BetaSharp.Items;
using Brigadier.NET.Builder;
using Brigadier.NET.Context;

namespace BetaSharp.Server.Commands;

public class GiveCommand : Command.Command
{
    public override string Usage => "give <player> <item> <count>";
    public override string Description => "Gives yourself an item";
    public override string[] Names => ["give"];

    public override LiteralArgumentBuilder<CommandSource> Register(LiteralArgumentBuilder<CommandSource> argBuilder) =>
        argBuilder
            .Then(ArgumentItemStack("item")
                .Executes(GiveItem)
                .Then(ArgumentInt("count")
                    .Executes(GiveItemCount)))
            .Then(ArgumentPlayer("player")
                .Then(ArgumentItemStack("item")
                    .Executes(GivePlayerItem)
                    .Then(ArgumentInt("count")
                        .Executes(GivePlayerItemCount))));

    // give <item> -> give 1 of item to self
    private static int GiveItem(CommandContext<CommandSource> context)
    {
        ItemStack item = context.GetArgument<ItemStack>("item");

        GiveTo(context.Source, item, 1);
        return 1;
    }

    private static int GiveItemCount(CommandContext<CommandSource> context)
    {
        ItemStack item = context.GetArgument<ItemStack>("item");
        int count = context.GetArgument<int>("count");

        GiveTo(context.Source, item, count);
        return 1;
    }


    private static int GivePlayerItem(CommandContext<CommandSource> context)
    {
        ItemStack item = context.GetArgument<ItemStack>("item");
        ServerPlayerEntity player = context.GetArgument<ServerPlayerEntity>("player");

        GiveTo(context.Source, player, item, 1);
        return 1;
    }

    private static int GivePlayerItemCount(CommandContext<CommandSource> context)
    {
        ItemStack item = context.GetArgument<ItemStack>("item");
        ServerPlayerEntity player = context.GetArgument<ServerPlayerEntity>("player");
        int count = context.GetArgument<int>("count");

        GiveTo(context.Source, player, item, count);
        return 1;
    }

    private static void GiveTo(CommandSource source, ItemStack item, int count)
    {
        ServerPlayerEntity? sender = source.Server.playerManager.getPlayer(source.SenderName);
        if (sender == null)
        {
            source.Output.SendMessage("Could not find your player.");
            return;
        }

        GiveTo(source, sender, item, count);
    }

    private static void GiveTo(CommandSource source, ServerPlayerEntity target, ItemStack item, int count)
    {
        target.Inventory.AddItemStackToInventoryOrDrop(item);
        string msg = $"Gave {count} [{ItemLookup.ResolveItemName(item)}] to {target.Name}";
        source.LogOp($"{target.Name} {msg}");
        source.Output.SendMessage(msg);
    }
}
