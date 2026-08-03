using BetaSharp.Entities;
using BetaSharp.Util.Maths;
using Brigadier.NET;
using Brigadier.NET.ArgumentTypes;
using Brigadier.NET.Exceptions;
using StringReader = Brigadier.NET.StringReader;

namespace BetaSharp.Server.Command;

public abstract partial class Command
{
    private class ArgPosition : IArgumentType<Vec3D>
    {
        public Vec3D Parse(IStringReader reader) => new(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());

        public Vec3D Parse<T>(StringReader reader, T source)
        {
            if (source is not CommandSource c)
            {
                return Parse(reader);
            }

            if (!reader.CanRead()) throw CommandSyntaxException.BuiltInExceptions.DispatcherUnknownArgument().CreateWithContext(reader);

            // Get pos of target
            char p = reader.Peek();
            if ((p < '0' || p > '9') && p != '~' && p != '-')
            {
                return ArgTarget.ParseStatic(reader, source).Position;
            }

            Vec3D pos = new();
            Vec3D? player = null;

            if (p == '~')
            {
                player ??= SenderPosition(c);
                reader.Cursor++;
                pos.X = player.Value.X;
                if (reader.Peek() != ' ')
                {
                    pos.X += reader.ReadDouble();
                }
            }
            else
            {
                pos.X = reader.ReadDouble();
            }

            reader.Cursor++;

            if (reader.Peek() == '~')
            {
                player ??= SenderPosition(c);
                reader.Cursor++;
                pos.Y = player.Value.Y;
                if (reader.Peek() != ' ')
                {
                    pos.Y += reader.ReadDouble();
                }
            }
            else
            {
                pos.Y = reader.ReadDouble();
            }

            reader.Cursor++;

            if (reader.Peek() == '~')
            {
                player ??= SenderPosition(c);
                reader.Cursor++;
                pos.Z = player.Value.Z;
                if (reader.RemainingLength != 0 && reader.Peek() != ' ')
                {
                    pos.Z += reader.ReadDouble();
                }
            }
            else
            {
                pos.Z = reader.ReadDouble();
            }

            return pos;
        }

        public IEnumerable<string> Examples => ["~ ~ ~", "19 -5.2, 109", "~12 ~-4 ~8.2"];

        private static Vec3D SenderPosition(CommandSource s) => s.Server.playerManager.getPlayer(s.SenderName)?.GetPosition() ?? throw new Exception("Player not found.");
    }
}
