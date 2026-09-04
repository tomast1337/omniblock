using Brigadier.NET;
using Brigadier.NET.ArgumentTypes;
using Brigadier.NET.Context;
using Brigadier.NET.Exceptions;
using Brigadier.NET.Suggestion;
using OmniBlock.Entities;
using OmniBlock.Util.Maths;
using StringReader = Brigadier.NET.StringReader;

namespace OmniBlock.Server.Command;

public abstract partial class Command
{
    private class ArgTargets : IArgumentType<Entity[]>
    {
        public static readonly SimpleCommandExceptionType PlayerNotFound = new(new LiteralMessage("Player not found."));
        public static readonly SimpleCommandExceptionType TargetNotFound = new(new LiteralMessage("Target not found."));

        private static readonly DynamicCommandExceptionType s_givenPlayerNotFound = new(expected => new LiteralMessage($"Player \"{expected}\" not found."));
        private static readonly SimpleCommandExceptionType s_executorNotFound = new(new LiteralMessage("Sender not found."));

        private static readonly SimpleCommandExceptionType s_noTargetNotFound = new(new LiteralMessage("No target was not found."));

        private static readonly Dynamic2CommandExceptionType s_selectorUnexpectedChar = new((c, e) => new LiteralMessage($"Expected \"{e}\" but found \"{c}\" found in selector."));
        private static readonly SimpleCommandExceptionType s_selectorError = new(new LiteralMessage("Selector failed to prase arguments."));
        private static readonly SimpleCommandExceptionType s_selectorInvalid = new(new LiteralMessage("Selector invalid."));

        public static IEnumerable<string> StaticExamples => ["@p", "player", "@a[x=0,y=65,z=0,range=100,sort=near]"];


        public Entity[] Parse(IStringReader reader) => throw new Exception("Unsupported invocation.");

        public Entity[] Parse<T>(StringReader reader, T source) => Parse<T>(reader, source, false, -1);

        public Task<Suggestions> ListSuggestions<T>(
            CommandContext<T> context,
            SuggestionsBuilder builder) =>
            context is not CommandContext<CommandSource> c ? Suggestions.Empty() : ListSuggestionsAsync(c, builder);

        public IEnumerable<string> Examples => StaticExamples;

        public static Entity[] Parse<T>(StringReader reader, T source, bool playerOnly, int maxSelection)
        {
            if (source is not CommandSource c)
            {
                throw new Exception("Unsupported source.");
            }

            if (reader.Peek() == '@' && (reader.RemainingLength == 2 || reader.Peek(2) == ' '))
            {
                reader.Cursor++;
                var ch = reader.Next();
                if (ch == 'p' || ch == 'P')
                {
                    return [c.Server.playerManager.getPlayer(c.SenderName) ?? throw PlayerNotFound.CreateWithContext(reader)];
                }

                if (ch == 'r' || ch == 'R')
                {
                    return [c.Server.playerManager.GetRandomPlayer() ?? throw PlayerNotFound.CreateWithContext(reader)];
                }

                if (ch == 'a' || ch == 'A')
                {
                    var selector = new SelectorArgs('a', reader);
                    Vec3D? pos = null;
                    // merge position form selector and senders position.
                    if (selector.Range <= 0 && selector.Selector != Selector.Nearest && selector.Selector != Selector.Furthest)
                    {
                        if (selector is { X: not null, Y: not null, Z: not null })
                        {
                            pos = new Vec3D(selector.X.Value, selector.Y.Value, selector.Z.Value);
                        }
                        else
                        {
                            var p = c.Server.playerManager.getPlayer(c.SenderName)?.Position ?? throw s_executorNotFound.CreateWithContext(reader);
                            if (selector.X.HasValue) p.X = selector.X.Value;
                            if (selector.Y.HasValue) p.Y = selector.Y.Value;
                            if (selector.Z.HasValue) p.Z = selector.Z.Value;
                            pos = p;
                        }
                    }

                    var l = maxSelection == -1 ? selector.Limit : Math.Min(maxSelection, selector.Limit);
                    return c.Server.playerManager.GetPlayers(pos, selector.Range, selector.Selector, l, selector.Dimension);
                }
                //if (ch == 'e' || ch == 'E')
                //{
                // TODO: Add entity target support
                //}

                throw s_selectorInvalid.CreateWithContext(reader);
            }

            var input = reader.ReadUnquotedString();
            return [c.Server.playerManager.getPlayer(input) ?? throw s_givenPlayerNotFound.CreateWithContext(reader, input)];
        }

        public static async Task<Suggestions> ListSuggestionsAsync(
            CommandContext<CommandSource> context,
            SuggestionsBuilder builder)
        {
            var s = builder.RemainingLowerCase;

            if (s.Length > 0 && s[0] == '@')
            {
                if (s.Length == 1)
                {
                    builder.Suggest("@p");
                    builder.Suggest("@r");
                    builder.Suggest("@a");
                    //builder.Suggest("@e");
                }
            }
            else
            {
                foreach (var p in context.Source.Server.playerManager.players)
                {
                    if (p.Name.StartsWith(s, StringComparison.OrdinalIgnoreCase))
                    {
                        builder.Suggest(p.Name);
                    }
                }
            }

            return await builder.BuildAsync();
        }

        private class SelectorArgs
        {
            public SelectorArgs(char type, StringReader reader)
            {
                if (reader.RemainingLength == 0 || reader.Peek() != '[') return;
                reader.Cursor++;
                while (reader.RemainingLength != 0 && reader.Peek() != ']')
                {
                    var name = reader.ReadUnquotedString();
                    var c = reader.Next();
                    if (c != '=') throw s_selectorUnexpectedChar.CreateWithContext(reader, c, '=');
                    var value = reader.ReadUnquotedString();
                    c = reader.Next();
                    if (c != ',') throw s_selectorUnexpectedChar.CreateWithContext(reader, c, ',');

                    switch (name.ToLowerInvariant())
                    {
                        case "sort":
                        case "s":
                            if (Enum.TryParse(value, true, out SelectorS selector)) Selector = (Selector)(byte)selector;
                            else throw s_selectorError.CreateWithContext(reader);
                            break;
                        case "dimension":
                        case "d":
                            if (int.TryParse(value, out var dim)) Dimension = dim;
                            else throw s_selectorError.CreateWithContext(reader);
                            break;
                        case "limit":
                        case "l":
                            if (int.TryParse(value, out var limit)) Limit = limit;
                            else throw s_selectorError.CreateWithContext(reader);
                            break;
                        case "range":
                        case "r":
                            if (double.TryParse(value, out var range)) Range = range;
                            else throw s_selectorError.CreateWithContext(reader);
                            break;
                        case "x":
                            if (double.TryParse(value, out var x)) X = x;
                            else throw s_selectorError.CreateWithContext(reader);
                            break;
                        case "y":
                            if (double.TryParse(value, out var y)) Y = y;
                            else throw s_selectorError.CreateWithContext(reader);
                            break;
                        case "z":
                            if (double.TryParse(value, out var z)) Z = z;
                            else throw s_selectorError.CreateWithContext(reader);
                            break;
                    }
                }
            }

            public double? X { get; }
            public double? Y { get; }
            public double? Z { get; }
            public Selector Selector { get; } = Selector.Arbitrary;
            public int Limit { get; } = -1;
            public double Range { get; } = -1;
            public int? Dimension { get; }

            private enum SelectorS : byte
            {
                Arbitrary = Selector.Arbitrary,
                A = Selector.Arbitrary,
                Nearest = Selector.Nearest,
                Near = Selector.Nearest,
                N = Selector.Arbitrary,
                Furthest = Selector.Furthest,
                Far = Selector.Furthest,
                F = Selector.Arbitrary,
                Random = Selector.Random,
                R = Selector.Arbitrary
            }
        }
    }
}
