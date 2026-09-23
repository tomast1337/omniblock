using OmniBlock.Entities.Behaviors;

namespace OmniBlock.Loot;

/// <summary>
///     Where a dropped stack's metadata comes from. A small closed set rather than an open
///     expression language: only one vanilla drop reads mob state for its meta.
/// </summary>
public enum LootMetaSource
{
    /// <summary>Use the literal <c>Meta</c> declared on the entry (0 when absent).</summary>
    Literal,

    /// <summary>Read the dropping sheep's fleece colour.</summary>
    FleeceColor
}

internal static class LootMetaSourceExtensions
{
    public static int Resolve(this LootMetaSource source, in LootContext context, int literal)
    {
        switch (source)
        {
            case LootMetaSource.FleeceColor:
                return context.Self?.Behaviors.Find<WoolBehavior>() is { } wool ? wool.ColorOf(context.Self) : literal;
            default:
                return literal;
        }
    }
}
