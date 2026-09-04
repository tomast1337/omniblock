using Brigadier.NET.Builder;
using Brigadier.NET.Context;
using Microsoft.Extensions.Logging;
using OmniBlock.Entities;
using OmniBlock.Network.Messages;

namespace OmniBlock.Server.Commands;

public class TellCommand : Command.Command
{
    private static readonly ILogger s_logger = Log.Instance.For(nameof(TellCommand));

    public override string Usage => "tell <player> <message>";
    public override string Description => "Whispers to a player";
    public override string[] Names => ["tell"];

    public override LiteralArgumentBuilder<CommandSource> Register(LiteralArgumentBuilder<CommandSource> argBuilder) =>
        argBuilder.Then(ArgumentPlayer("player").Then(ArgumentGreedy("message").Executes(Execute)));

    private static int Execute(CommandContext<CommandSource> context)
    {
        var target = context.GetArgument<ServerPlayerEntity>("player");
        var message = context.GetArgument<string>("message");
        s_logger.LogInformation("[" + context.Source.SenderName + "->" + target.Name + "] " + message);

        var whisper = "§7" + context.Source.SenderName + " whispers " + message;
        s_logger.LogInformation(whisper);

        if (target.NetworkHandler is { } handler)
        {
            handler.SendMessage(new ChatMessage
            {
                Text = whisper
            });
        }
        else
        {
            context.Source.Output.SendMessage("There's no player by that name online.");
        }

        return 1;
    }
}
