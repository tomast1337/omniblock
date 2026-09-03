using OmniBlock.Entities;
using OmniBlock.Worlds.Core;
using Brigadier.NET.Builder;
using Brigadier.NET.Context;

namespace OmniBlock.Server.Commands;

public class SummonCommand : Command.Command
{
    public override string Usage => "summon <entity> <count>";
    public override string Description => "Spawns an entity at your location";
    public override string[] Names => ["summon", "spawn"];

    public override LiteralArgumentBuilder<CommandSource> Register(LiteralArgumentBuilder<CommandSource> argBuilder) =>
        argBuilder
            .Then(ArgumentString("entity")
                .Executes(ctx => Summon(ctx, 1))
                .Then(ArgumentInt("count").Executes(ctx => Summon(ctx, ctx.GetArgument<int>("count")))));

    private static int Summon(CommandContext<CommandSource> context, int count)
    {
        ServerPlayerEntity? player = context.Source.Server.playerManager.getPlayer(context.Source.SenderName);
        if (player == null)
        {
            context.Source.Output.SendMessage("Could not find your player.");
            return 0;
        }

        string entityName = context.GetArgument<string>("entity");

        ServerWorld world = context.Source.Server.getWorld(player.DimensionId);

        // Resolved once: a misspelled name is one message, not one failed spawn per requested count.
        if (!world.Content.EntityTypes.TryGet(entityName, out EntityType? entityType))
        {
            context.Source.Output.SendMessage($"Unknown entity: {entityName}");
            return 0;
        }

        int summoned = 0;

        for (int i = 0; i < count; i++)
        {
            Entity entity = entityType.Create(world);
            entity.SetPositionAndAngles((float)player.X, (float)player.Y, (float)player.Z, 0, 0);
            if (world.SpawnEntity(entity))
            {
                summoned++;
            }
        }

        context.Source.Output.SendMessage(
            summoned > 0
                ? $"Summoned {summoned}x {entityName}"
                : $"Could not place any {entityName} here.");

        return 1;
    }
}
