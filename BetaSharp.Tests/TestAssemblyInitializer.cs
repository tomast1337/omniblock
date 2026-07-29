using System.Runtime.CompilerServices;

namespace BetaSharp.Tests;

/// <summary>
/// Runs <see cref="Bootstrap.Initialize"/> exactly once for the whole test assembly, before any
/// test executes. Items load from <c>assets/item/betasharp/*.json</c> at startup rather than via
/// static field initializers, so touching a static field does not force initialization; the real
/// bootstrap has to run.
/// </summary>
internal static class TestAssemblyInitializer
{
    [ModuleInitializer]
    internal static void Initialize() => Bootstrap.Initialize();
}
