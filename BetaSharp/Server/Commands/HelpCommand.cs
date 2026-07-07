using BetaSharp.Server.Command;
using BetaSharp.Server.Internal;
using Brigadier.NET.Builder;
using Brigadier.NET.Context;
using Brigadier.NET.Tree;
using StringReader = Brigadier.NET.StringReader;

namespace BetaSharp.Server.Commands;

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
        CommandSource c = context.Source;
        bool inInternalServer = c.Server is InternalServer;
        byte per = inInternalServer ? (byte)4 : c.Output.PermissionLevel;

        c.Output.SendMessage("Available commands:");
        foreach (Command.Command cmd in _helpEntries)
        {
            if (per < cmd.PermissionLevel)
            {
                continue;
            }

            if (inInternalServer && cmd.DisallowInternalServer)
            {
                continue;
            }

            foreach (string usage in cmd.Usages)
            {
                c.Output.SendMessage($"  {usage,-30} - {cmd.Description}");
            }
        }

        return 1;
    }

    private int HelpTargeted(CommandContext<CommandSource> context)
    {
        CommandSource c = context.Source;

        string arg = context.GetArgument<string>("command");
        string[] s = arg.Split(' ');
        bool found = false;

        CommandNode<CommandSource>? node = c.Handler.Dispatcher.Root.GetChild(s[0]);
        int i = 0;
        if (node != null)
        {
            found = true;
            for (i = 1; i < s.Length; i++)
            {
                CommandNode<CommandSource>? a = node.GetChild(s[i]);
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
                    Type type = r2.GetType();
                    if (type.IsGenericType &&
                        type.GetGenericTypeDefinition() == typeof(ArgumentCommandNode<,>))
                    {
                        Type[] args = type.GetGenericArguments();
                        Type valueType = args[1];

                        if (valueType.IsEnum)
                        {
                            if (Enum.TryParse(valueType, s[i], true, out object? _))
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
                string arg2 = string.Join(' ', s, 0, i);
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

        foreach (CommandNode<CommandSource> node in context.Children)
        {
            BuildHelp(s + ' ' + node.UsageText, o, node);
        }
    }

    internal void Add(Command.Command command) => _helpEntries.Add(command);
}
