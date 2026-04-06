using BetaSharp.Entities;
using BetaSharp.Network.Packets.S2CPlay;
using BetaSharp.Registries;
using BetaSharp.Server.Command;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Server.Commands;

public class GameModeCommand : ICommand
{
    private static readonly ILogger s_logger = Log.Instance.For(nameof(GameModeCommand));

    // ReSharper disable once StringLiteralTypo
    public string Usage => "gamemode <player> <mode>";
    public string Description => "Broadcasts a message";

    // ReSharper disable once StringLiteralTypo
    public string[] Names => ["gamemode", "gm"];

    public void Execute(ICommand.CommandContext c)
    {
        if (c.Args.Length == 0)
        {
            var p = c.Server.playerManager.getPlayer(c.SenderName)!;
            c.Output.SendMessage(p.GameMode.Name);
            return;
        }

        if (c.Args.Length == 1)
        {
            if (c.Args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                ListGameModes(c);
                return;
            }

            var p = c.Server.playerManager.getPlayer(c.SenderName)!;
            SetGameMode(p, c.Args[0], c);
        }
        else
        {
            var p = c.Server.playerManager.getPlayer(c.Args[1]);
            if (p == null)
            {
                c.Output.SendMessage("Player not found.");
                return;
            }

            SetGameMode(p, c.Args[1], c);
        }
    }

    private static void ListGameModes(ICommand.CommandContext c)
    {
        var registry = c.Server.RegistryAccess.GetOrThrow(RegistryKeys.GameModes);
        foreach (var key in registry.Keys)
        {
            c.Output.SendMessage(key.ToString());
        }
    }

    private static void SetGameMode(ServerPlayerEntity p, string arg, ICommand.CommandContext c)
    {
        if (c.Server.RegistryAccess.GetOrThrow(RegistryKeys.GameModes).AsAssetLoader().TryGetHolderByPrefix(arg, out Holder<GameMode>? holder))
        {
            SetGameMode(p, holder, c);
            return;
        }

        c.Output.SendMessage("Gamemode not found.");
    }

    private static void SetGameMode(ServerPlayerEntity p, Holder<GameMode> holder, ICommand.CommandContext c)
    {
        p.GameModeHolder = holder;
        p.NetworkHandler.SendPacket(PlayerGameModeUpdateS2CPacket.Get(holder.Value));
        string s = $"{p.name} game mode set to {holder.Value.Name}.";
        s_logger.LogInformation(s);
        c.Output.SendMessage(s);
    }
}
