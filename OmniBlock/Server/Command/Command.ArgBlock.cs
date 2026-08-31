using OmniBlock.Blocks;
using OmniBlock.Items;
using Brigadier.NET;
using Brigadier.NET.ArgumentTypes;
using Brigadier.NET.Exceptions;

namespace OmniBlock.Server.Command;

public abstract partial class Command
{
    private class ArgBlock : IArgumentType<(int id, int meta)>
    {
        private const string AirBlockAlias = "air";
        private static readonly DynamicCommandExceptionType s_blockNotFound = new(expected => new LiteralMessage($"Block \"{expected}\" not found."));

        public (int id, int meta) Parse(IStringReader reader)
        {
            string name = ArgItemStack.ParseString(reader);

            int separator = name.IndexOf(':');
            if (separator < 0)
            {
                // No meta data, resolve id.
                if (int.TryParse(name, out int id))
                {
                    if (id == 0 || BlockRegistry.TryGetByProtocolId(id, out _)) return (id, 0);
                    throw s_blockNotFound.Create(name);
                }

                if (name == AirBlockAlias)
                {
                    return (0, 0);
                }
            }
            else
            {
                string idPart = name.Substring(0, separator);
                string metaPart = name.Substring(separator + 1);

                // Resolve id and meta data.
                if (int.TryParse(idPart, out int id))
                {
                    if (id != 0 && !BlockRegistry.TryGetByProtocolId(id, out _)) throw s_blockNotFound.Create(name);
                    return (id, int.Parse(metaPart));
                }

                if (idPart == AirBlockAlias)
                {
                    return (0, int.Parse(metaPart));
                }
            }


            if (ItemLookup.TryGetItem(name, out ItemStack? result)
                && BlockRegistry.TryGetByProtocolId(result.ItemId, out _))
            {
                return (result.ItemId, result.GetDamage());
            }

            throw s_blockNotFound.Create(name);
        }
    }
}
