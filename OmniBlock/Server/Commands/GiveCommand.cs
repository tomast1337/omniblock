using OmniBlock.Entities;
using OmniBlock.Items;
using Brigadier.NET.Builder;
using Brigadier.NET.Context;
using OmniBlock.Registries;

namespace OmniBlock.Server.Commands;

public class GiveCommand(RuntimeItemRegistry items) : Command.Command
{
    public override string Usage => "give <player> <item> <count>";
    public override string Description => "Gives yourself an item";
    public override string[] Names => ["give"];

    public override LiteralArgumentBuilder<CommandSource> Register(LiteralArgumentBuilder<CommandSource> argBuilder) =>
        argBuilder
            .Then(ArgumentItemStack("item", items)
                .Executes(GiveItem)
                .Then(ArgumentInt("count")
                    .Executes(GiveItemCount)))
            .Then(ArgumentPlayer("player")
                .Then(ArgumentItemStack("item", items)
                    .Executes(GivePlayerItem)
                    .Then(ArgumentInt("count")
                        .Executes(GivePlayerItemCount))));

    // give <item> -> give 1 of item to self
    private int GiveItem(CommandContext<CommandSource> context)
    {
        ItemStack item = context.GetArgument<ItemStack>("item");

        GiveTo(context.Source, item, 1);
        return 1;
    }

    private int GiveItemCount(CommandContext<CommandSource> context)
    {
        ItemStack item = context.GetArgument<ItemStack>("item");
        int count = context.GetArgument<int>("count");

        GiveTo(context.Source, item, count);
        return 1;
    }


    private int GivePlayerItem(CommandContext<CommandSource> context)
    {
        ItemStack item = context.GetArgument<ItemStack>("item");
        ServerPlayerEntity player = context.GetArgument<ServerPlayerEntity>("player");

        GiveTo(context.Source, player, item, 1);
        return 1;
    }

    private int GivePlayerItemCount(CommandContext<CommandSource> context)
    {
        ItemStack item = context.GetArgument<ItemStack>("item");
        ServerPlayerEntity player = context.GetArgument<ServerPlayerEntity>("player");
        int count = context.GetArgument<int>("count");

        GiveTo(context.Source, player, item, count);
        return 1;
    }

    private void GiveTo(CommandSource source, ItemStack item, int count)
    {
        ServerPlayerEntity? sender = source.Server.playerManager.getPlayer(source.SenderName);
        if (sender == null)
        {
            source.Output.SendMessage("Could not find your player.");
            return;
        }

        GiveTo(source, sender, item, count);
    }

    private void GiveTo(CommandSource source, ServerPlayerEntity target, ItemStack item, int count)
    {
        item.Count = count;
        target.Inventory.AddItemStackToInventoryOrDrop(item);
        string msg = $"Gave {count} [{items.GetName(item)}] to {target.Name}";
        source.LogOp($"{target.Name} {msg}");
        source.Output.SendMessage(msg);
    }
}
