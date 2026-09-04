using Brigadier.NET;
using Brigadier.NET.ArgumentTypes;
using Brigadier.NET.Exceptions;
using OmniBlock.Registries;

namespace OmniBlock.Server.Command;

public abstract partial class Command
{
    private class ArgBlock(RuntimeItemRegistry items, RuntimeBlockRegistry blocks) : IArgumentType<(int id, int meta)>
    {
        private const string AirBlockAlias = "air";
        private static readonly DynamicCommandExceptionType s_blockNotFound = new(expected => new LiteralMessage($"Block \"{expected}\" not found."));

        public (int id, int meta) Parse(IStringReader reader)
        {
            var name = ArgItemStack.ParseString(reader);

            var separator = name.IndexOf(':');
            if (separator < 0)
            {
                // No meta data, resolve id.
                if (int.TryParse(name, out var id))
                {
                    if (id == 0 || blocks.TryGetByProtocolId(id, out _)) return (id, 0);
                    throw s_blockNotFound.Create(name);
                }

                if (name == AirBlockAlias)
                {
                    return (0, 0);
                }
            }
            else
            {
                var idPart = name.Substring(0, separator);
                var metaPart = name.Substring(separator + 1);

                // Resolve id and meta data.
                if (int.TryParse(idPart, out var id))
                {
                    if (id != 0 && !blocks.TryGetByProtocolId(id, out _)) throw s_blockNotFound.Create(name);
                    return (id, int.Parse(metaPart));
                }

                if (idPart == AirBlockAlias)
                {
                    return (0, int.Parse(metaPart));
                }
            }


            if (items.TryParse(name, out var result)
                && blocks.TryGetByProtocolId(result.ItemId, out _))
            {
                return (result.ItemId, result.GetDamage());
            }

            throw s_blockNotFound.Create(name);
        }
    }
}
