using Brigadier.NET.Builder;
using Brigadier.NET.Context;
using OmniBlock.Entities;

namespace OmniBlock.Server.Commands;

public class KillSelfCommand : Command.Command
{
    public override string Usage => "kill <target>";
    public override string Description => "Kills player";
    public override string[] Names => ["kill"];

    public override LiteralArgumentBuilder<CommandSource> Register(LiteralArgumentBuilder<CommandSource> argBuilder) =>
        argBuilder.Executes(Execute).Then(ArgumentTargets("targets").Executes(ExecuteOther));

    private static int Execute(CommandContext<CommandSource> context)
    {
        var player = context.Source.Server.playerManager.getPlayer(context.Source.SenderName);
        if (player == null)
        {
            context.Source.Output.SendMessage("Could not find your player.");
            return 1;
        }

        player.Damage(null, 1000);
        return 1;
    }

    private static int ExecuteOther(CommandContext<CommandSource> context)
    {
        var entities = context.GetArgument<Entity[]>("targets");
        if (entities.Length == 0) context.Source.Output.SendMessage("Could not find target.");
        else
        {
            foreach (var entity in entities)
            {
                entity.Damage(null, 1000);
            }
        }

        return 1;
    }
}
