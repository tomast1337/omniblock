using Brigadier.NET.Builder;
using Brigadier.NET.Context;
using Brigadier.NET.Tree;
using OmniBlock.Server.Command;
using OmniBlock.Server.Internal;
using StringReader = Brigadier.NET.StringReader;

namespace OmniBlock.Server.Commands;

public class HelpCommand : Command.Command
{
    private readonly List<Command.Command> _helpEntries = [];
    public override string Usage => "help <command>";
    public override string Description => "Lists commands";
    public override string[] Names => ["help", "h", "?"];
    public override byte PermissionLevel => 0;

    public override LiteralArgumentBuilder<CommandSource> Register(LiteralArgumentBuilder<CommandSource> argBuilder) =>
        argBuilder
            .Then(ArgumentGreedy("command").Executes(HelpTargeted))
            .Executes(HelpAll);

    private int HelpAll(CommandContext<CommandSource> context)
    {
        var c = context.Source;
        var inInternalServer = c.Server is InternalServer;
        var per = inInternalServer ? (byte)4 : c.Output.PermissionLevel;

        c.Output.SendMessage("Available commands:");
        foreach (var cmd in _helpEntries)
        {
            if (per < cmd.PermissionLevel)
            {
                continue;
            }

            if (inInternalServer && cmd.DisallowInternalServer)
            {
                continue;
            }

            foreach (var usage in cmd.Usages)
            {
                c.Output.SendMessage($"  {usage,-30} - {cmd.Description}");
            }
        }

        return 1;
    }

    private int HelpTargeted(CommandContext<CommandSource> context)
    {
        var c = context.Source;

        var arg = context.GetArgument<string>("command");
        var s = arg.Split(' ');
        var found = false;

        var node = c.Handler.Dispatcher.Root.GetChild(s[0]);
        var i = 0;
        if (node != null)
        {
            found = true;
            for (i = 1; i < s.Length; i++)
            {
                var a = node.GetChild(s[i]);
                if (a != null)
                {
                    node = a;
                    continue;
                }

                var relevantNodes = node.GetRelevantNodes(new StringReader(s[i])).ToArray();
                if (relevantNodes.Length == 1)
                {
                    node = relevantNodes[0];
                    continue;
                }

                foreach (var r2 in relevantNodes)
                {
                    var type = r2.GetType();
                    if (type.IsGenericType &&
                        type.GetGenericTypeDefinition() == typeof(ArgumentCommandNode<,>))
                    {
                        var args = type.GetGenericArguments();
                        var valueType = args[1];

                        if (valueType.IsEnum)
                        {
                            if (Enum.TryParse(valueType, s[i], true, out var _))
                            {
                                a = r2;
                                break;
                            }
                        }
                    }
                }

                if (a != null)
                {
                    node = a;
                    continue;
                }

                found = false;
                break;
            }
        }

        if (node != null)
        {
            if (found)
            {
                // full find.
                BuildHelp('/' + arg, c.Output, node);
            }
            else
            {
                // partial find.
                var arg2 = string.Join(' ', s, 0, i);
                // list relevant nodes.
                var relevantNodes = node.GetRelevantNodes(new StringReader(s[i])).ToArray();
                foreach (var n in relevantNodes)
                {
                    BuildHelp('/' + arg2 + ' ' + n.UsageText, c.Output, n);
                }
            }
        }
        else
        {
            c.Output.SendMessage($"Command \"{arg}\" not found, use /help to list all commands");
        }

        return 1;
    }

    private static void BuildHelp(string s, ICommandOutput o, CommandNode<CommandSource> context)
    {
        if (context.Children.Count == 0)
        {
            o.SendMessage("  " + s);
            return;
        }

        foreach (var node in context.Children)
        {
            BuildHelp(s + ' ' + node.UsageText, o, node);
        }
    }

    internal void Add(Command.Command command) => _helpEntries.Add(command);
}
