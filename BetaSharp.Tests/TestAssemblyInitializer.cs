using System.Runtime.CompilerServices;

namespace BetaSharp.Tests;

/// <summary>
/// Runs <see cref="Bootstrap.Initialize"/> exactly once for the whole test assembly, before any
/// test executes. Items are now loaded from <c>assets/item/betasharp/*.json</c> at startup (see
/// docs/item-data-driven-migration.md, Phase 4) rather than via static field initializers, so the
/// old per-fixture trick of touching a static field to force initialization no longer works —
/// callers need the real bootstrap to have run.
/// </summary>
internal static class TestAssemblyInitializer
{
    [ModuleInitializer]
    internal static void Initialize() => Bootstrap.Initialize();
}
