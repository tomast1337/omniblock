namespace BetaSharp.Entities;

/// <summary>
///     Implemented by a behavior that holds other behaviors: a composite running several in one
///     slot, or a decorator wrapping one. Lets <see cref="EntityBehaviorSet.Find{T}" /> reach a
///     nested behavior, so a caller can ask for a capability without knowing how the definition
///     nested it.
/// </summary>
public interface IEntityBehaviorGroup
{
    IEnumerable<object> Children { get; }
}
