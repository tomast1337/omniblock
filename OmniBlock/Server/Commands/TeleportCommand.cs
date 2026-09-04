using Brigadier.NET.Builder;
using Brigadier.NET.Context;
using OmniBlock.Entities;
using OmniBlock.Util.Maths;

namespace OmniBlock.Server.Commands;

public class TeleportCommand : Command.Command
{
    public override string Usage => "tp <player> <position|target>";
    public override string Description => "Teleport";
    public override string[] Names => ["tp", "teleport"];

    public override LiteralArgumentBuilder<CommandSource> Register(LiteralArgumentBuilder<CommandSource> argBuilder) =>
        argBuilder
            .Then(ArgumentPos("position")
                .Executes(TpPos))
            .Then(ArgumentPlayer("player")
                .Then(ArgumentPos("position")
                    .Executes(TpPlayerPos)));

    private static int TpPos(CommandContext<CommandSource> context)
    {
        var pos = context.GetArgument<Vec3D>("position");

        var sender = context.Source.Server.playerManager.getPlayer(context.Source.SenderName);
        if (sender == null)
        {
            context.Source.Output.SendMessage("Could not find your player.");
            return 1;
        }

        sender.NetworkHandler.teleport(pos.X, pos.Y, pos.Z, sender.Yaw, sender.Pitch);
        context.Source.Output.SendMessage($"Teleported to {pos}");

        return 1;
    }

    private static int TpPlayerPos(CommandContext<CommandSource> context)
    {
        var a1 = context.GetArgument<ServerPlayerEntity>("player");
        var pos = context.GetArgument<Vec3D>("position");

        a1.NetworkHandler.teleport(pos.X, pos.Y, pos.Z, a1.Yaw, a1.Pitch);
        context.Source.Output.SendMessage($"Teleported to {pos}");

        return 1;
    }
}
