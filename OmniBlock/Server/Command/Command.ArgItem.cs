using OmniBlock.Items;
using Brigadier.NET;
using Brigadier.NET.ArgumentTypes;
using Brigadier.NET.Exceptions;
using OmniBlock.Registries;

namespace OmniBlock.Server.Command;

public abstract partial class Command
{
    private class ArgItemStack(RuntimeItemRegistry items) : IArgumentType<ItemStack>
    {
        private static readonly DynamicCommandExceptionType s_itemNotFound = new(expected => new LiteralMessage($"Item \"{expected}\" not found."));

        public ItemStack Parse(IStringReader reader)
        {
            string name = ParseString(reader);
            if (items.TryParse(name, out ItemStack? result))
            {
                return result;
            }

            throw s_itemNotFound.Create(name);
        }

        public static string ParseString(IStringReader reader)
        {
            int cursor = reader.Cursor;
            while (reader.CanRead() && IsAllowedInUnquotedString(reader.Peek()))
                reader.Skip();
            return reader.String.AsSpan(cursor, reader.Cursor - cursor).ToString();
        }

        private static bool IsAllowedInUnquotedString(char c)
        {
            return c >= '0' && c <= '9' || c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c == '_' || c == '-' || c == ':';
        }
    }
}
