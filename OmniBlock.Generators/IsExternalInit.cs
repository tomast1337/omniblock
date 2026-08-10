namespace System.Runtime.CompilerServices;

/// <summary>
///     Records and <c>init</c> accessors are a compiler feature that needs this marker type to
///     exist. It ships in .NET 5 and later; this project targets netstandard2.0 because that is what
///     the Roslyn host runs on, so it has to be declared here.
/// </summary>
internal static class IsExternalInit;
