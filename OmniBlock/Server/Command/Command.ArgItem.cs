using Brigadier.NET;
using Brigadier.NET.ArgumentTypes;
using Brigadier.NET.Exceptions;
using OmniBlock.Items;
using OmniBlock.Registries;

namespace OmniBlock.Server.Command;

public abstract partial class Command
{
    private class ArgItemStack(RuntimeItemRegistry items) : IArgumentType<ItemStack>
    {
        private static readonly DynamicCommandExceptionType s_itemNotFound = new(expected => new LiteralMessage($"Item \"{expected}\" not found."));

        public ItemStack Parse(IStringReader reader)
        {
            var name = ParseString(reader);
            if (items.TryParse(name, out var result))
            {
                return result;
            }

            throw s_itemNotFound.Create(name);
        }

        public static string ParseString(IStringReader reader)
        {
            var cursor = reader.Cursor;
            while (reader.CanRead() && IsAllowedInUnquotedString(reader.Peek()))
                reader.Skip();
            return reader.String.AsSpan(cursor, reader.Cursor - cursor).ToString();
        }

        private static bool IsAllowedInUnquotedString(char c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_' || c == '-' || c == ':';
    }
}
