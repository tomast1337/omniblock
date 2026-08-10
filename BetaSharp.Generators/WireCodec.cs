using Microsoft.CodeAnalysis;

namespace OmniBlock.Generators;

/// <summary>The read expression, write statement and size expression for one field.</summary>
internal readonly record struct Snippets(string Read, string Write, string Size);

/// <summary>
///     Maps a property's declared type onto the read, write and size snippets for it.
///     <para>
///         All three come from this one table, which is the property worth having: the class of bug
///         the generator exists to remove is a <c>Size()</c> that disagrees with <c>Write()</c>, and
///         they cannot disagree if a single entry produces both.
///     </para>
/// </summary>
internal static class WireCodec
{
    /// <summary>Matches the injected <c>WireEncoding</c> enum's ordering.</summary>
    public const int EncodingFixed = 0;
    public const int EncodingVarInt = 1;
    public const int EncodingZigZag = 2;

    private const string Extensions = "global::OmniBlock.StreamExtensions";

    /// <summary>
    ///     Produces the snippets for <paramref name="type" />, or null when the type has no
    ///     encoding — which the caller turns into a diagnostic rather than emitting broken code.
    /// </summary>
    /// <param name="access">How the value is referred to in the generated body, already cast if needed.</param>
    public static Snippets? Describe(ITypeSymbol type, int encoding, int maxLength, string access)
    {
        // An enum travels as its underlying integer. Casting on both sides keeps the property
        // strongly typed while the wire stays a number, so adding an enum member costs nothing.
        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol { EnumUnderlyingType: { } underlying })
        {
            string enumName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string underlyingName = underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (Describe(underlying, encoding, maxLength, $"(({underlyingName}){access})") is not { } inner)
            {
                return null;
            }

            return inner with { Read = $"({enumName})({inner.Read})" };
        }

        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte, Rank: 1 })
        {
            return new Snippets(
                maxLength > 0 ? $"stream.ReadByteArray({maxLength})" : "stream.ReadByteArray()",
                $"stream.WriteByteArray({access});",
                $"{Extensions}.ByteArraySize({access})");
        }

        if (type is IArrayTypeSymbol { Rank: 1 } array
            && array.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                is "global::OmniBlock.Items.ItemStack" or "global::OmniBlock.Items.ItemStack?")
        {
            return new Snippets(
                maxLength > 0 ? $"stream.ReadItemStacks({maxLength})" : "stream.ReadItemStacks()",
                $"stream.WriteItemStacks({access});",
                $"{Extensions}.ItemStacksSize({access})");
        }

        string fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // An inventory slot, with a negative item ID standing in for an empty one. Here rather than
        // left to hand-written readers because it is the most-repeated shape in the legacy packets
        // and the one they most often mis-sized: the two variants differ by three bytes, and a
        // constant Size() cannot be right about both.
        if (fullName is "global::OmniBlock.Items.ItemStack" or "global::OmniBlock.Items.ItemStack?")
        {
            return new Snippets(
                "stream.ReadItemStack()",
                $"stream.WriteItemStack({access});",
                $"{Extensions}.ItemStackSize({access})");
        }

        if (fullName == "global::OmniBlock.ResourceLocation")
        {
            return new Snippets(
                "stream.ReadResourceLocation()",
                $"stream.WriteResourceLocation({access});",
                $"{Extensions}.ResourceLocationSize({access})");
        }

        return type.SpecialType switch
        {
            SpecialType.System_Boolean =>
                new Snippets("stream.ReadBoolean()", $"stream.WriteBoolean({access});", "1"),

            SpecialType.System_Byte =>
                new Snippets("(byte)stream.ReadByte()", $"stream.WriteByte({access});", "1"),

            SpecialType.System_SByte =>
                new Snippets("(sbyte)stream.ReadByte()", $"stream.WriteByte((byte){access});", "1"),

            SpecialType.System_Int16 =>
                new Snippets("stream.ReadShort()", $"stream.WriteShort({access});", "2"),

            SpecialType.System_UInt16 =>
                new Snippets("stream.ReadUShort()", $"stream.WriteUShort({access});", "2"),

            SpecialType.System_Int32 => encoding switch
            {
                EncodingVarInt => new Snippets(
                    "stream.ReadVarInt()", $"stream.WriteVarInt({access});", $"{Extensions}.VarIntSize({access})"),
                EncodingZigZag => new Snippets(
                    "stream.ReadZigZag()", $"stream.WriteZigZag({access});", $"{Extensions}.ZigZagSize({access})"),
                _ => new Snippets("stream.ReadInt()", $"stream.WriteInt({access});", "4"),
            },

            // uint is the sequence-number type throughout the snapshot layer and it wraps
            // deliberately, so it travels as the same bits an int would rather than as a widened
            // value that would move the wrap point.
            SpecialType.System_UInt32 => encoding switch
            {
                EncodingVarInt => new Snippets(
                    "(uint)stream.ReadVarInt()",
                    $"stream.WriteVarInt((int){access});",
                    $"{Extensions}.VarIntSize((int){access})"),
                EncodingZigZag => new Snippets(
                    "(uint)stream.ReadZigZag()",
                    $"stream.WriteZigZag((int){access});",
                    $"{Extensions}.ZigZagSize((int){access})"),
                _ => new Snippets("(uint)stream.ReadInt()", $"stream.WriteInt((int){access});", "4"),
            },

            SpecialType.System_Int64 =>
                new Snippets("stream.ReadLong()", $"stream.WriteLong({access});", "8"),

            SpecialType.System_UInt64 =>
                new Snippets("(ulong)stream.ReadLong()", $"stream.WriteLong((long){access});", "8"),

            SpecialType.System_Single =>
                new Snippets("stream.ReadFloat()", $"stream.WriteFloat({access});", "4"),

            SpecialType.System_Double =>
                new Snippets("stream.ReadDouble()", $"stream.WriteDouble({access});", "8"),

            // The size counts encoded bytes rather than characters. Every hand-written Size() in the
            // legacy packets counts characters, which is correct only while the text stays ASCII.
            SpecialType.System_String => new Snippets(
                maxLength > 0 ? $"stream.ReadString({maxLength})" : "stream.ReadString()",
                $"stream.WriteString({access});",
                $"(2 + global::OmniBlock.Util.ModifiedUtf8.GetByteCount({access}))"),

            _ => null,
        };
    }
}
