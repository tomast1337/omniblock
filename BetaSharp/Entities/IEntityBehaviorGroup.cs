namespace BetaSharp.Entities;

/// <summary>
///     Implemented by a behavior that holds other behaviors — a composite running several in one
///     slot, or a decorator wrapping one. It exists so <see cref="EntityBehaviorSet.Find{T}" /> can
///     reach a nested behavior, letting a caller ask for the capability it wants without knowing how
///     the definition happened to nest it.
/// </summary>
public interface IEntityBehaviorGroup
{
    IEnumerable<object> Children { get; }
}
