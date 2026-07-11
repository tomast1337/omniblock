using BetaSharp.Blocks;
using BetaSharp.Items;
using Brigadier.NET;
using Brigadier.NET.ArgumentTypes;
using Brigadier.NET.Exceptions;

namespace BetaSharp.Server.Command;

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
                    if (id == 0 || Block.Blocks.Length > id && Block.Blocks[id] != null) return (id, 0);
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
                    if (id != 0 && (Block.Blocks.Length <= id || Block.Blocks[id] == null)) throw s_blockNotFound.Create(name);
                    return (id, int.Parse(metaPart));
                }

                if (idPart == AirBlockAlias)
                {
                    return (0, int.Parse(metaPart));
                }
            }


            if (ItemLookup.TryGetItem(name, out ItemStack? result) && Block.Blocks.Length > result.ItemId && Block.Blocks[result.ItemId] != null)
            {
                return (result.ItemId, result.getDamage());
            }

            throw s_blockNotFound.Create(name);
        }
    }
}
