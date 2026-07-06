using System.Runtime.CompilerServices;

namespace BetaSharp.Tests.TestSupport;

/// <summary>
/// Runs <see cref="Bootstrap.Initialize"/> once when the test assembly loads, mirroring the
/// client and dedicated-server entry points. Materials and sound groups are loaded from JSON
/// at bootstrap, and <see cref="Blocks.Block"/>'s static fields consume them — so any test
/// touching a block needs this to have run first.
/// </summary>
internal static class AssemblyBootstrap
{
    [ModuleInitializer]
    internal static void Initialize() => Bootstrap.Initialize();
}
