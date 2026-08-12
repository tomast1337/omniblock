using OmniBlock.Client.UI;

namespace OmniBlock.Tests.UI;

/// <summary>
///     Covers <see cref="UiCommandRegistry" /> in isolation — pure C#, no native Luau library
///     involved, unlike everything under <c>OmniBlock.Tests/Luau</c>. Each test constructs its own
///     instance (mirrors <see cref="OmniBlock.Network.Messages.MessageRegistry" />'s own
///     testability — see <c>LoopbackMessageTests.cs</c>'s <c>new MessageRegistry()</c>) rather than
///     touching any shared/static state.
/// </summary>
public sealed class UiCommandRegistryTests
{
    [Fact]
    public void Register_assignsIdsInCallOrder_notSortedKeyOrder()
    {
        UiCommandRegistry registry = new();
        List<int> invoked = [];

        // "debug.toast" sorts before "inventory.open" alphabetically, but is registered second —
        // if IDs were sorted-key order (the pre-§0 behavior), invoking id 0 would fire this
        // handler, not inventory.open's. docs/luau-registry-phase-plan.md §0 requires call order.
        registry.Register("omniblock:inventory.open", (_, _, _) => invoked.Add(0));
        registry.Register("omniblock:debug.toast", (_, _, _) => invoked.Add(1));

        registry.Invoke(0, 0, 0, 0);
        registry.Invoke(1, 0, 0, 0);

        Assert.Equal([0, 1], invoked);
    }

    [Fact]
    public void ResolveOrCreate_calledTwiceForTheSameKey_returnsTheSameId()
    {
        UiCommandRegistry registry = new();

        int first = registry.ResolveOrCreate("omniblock:inventory.open");
        int second = registry.ResolveOrCreate("omniblock:inventory.open");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Freeze_doesNotChangeWhatAnAlreadyIssuedIdMapsTo()
    {
        UiCommandRegistry registry = new();
        int invokedWith = -1;

        // "zz.last" would sort after every other key a real mod set might register — the exact
        // shape that would get silently reassigned to a different ID under the old sorted-freeze
        // behavior docs/luau-registry-phase-plan.md §0 replaced. A script that captured this ID
        // before Freeze() (the only time it's legal to call ResolveOrCreate/Register at all) must
        // find it still means the same thing afterward.
        registry.Register("omniblock:zz.last", (a, _, _) => invokedWith = a);
        int id = registry.ResolveOrCreate("omniblock:zz.last");
        registry.Freeze();

        registry.Invoke(id, 42, 0, 0);

        Assert.Equal(42, invokedWith);
    }

    [Fact]
    public void ResolveOrCreate_afterRegisterForTheSameKey_returnsTheHandlersId()
    {
        UiCommandRegistry registry = new();
        registry.Register("omniblock:inventory.open", (_, _, _) => { });

        int id = registry.ResolveOrCreate("omniblock:inventory.open");

        Assert.Equal(0, id);
    }

    [Fact]
    public void Invoke_onAResolveOrCreateIdWithNoHandler_isNoOp()
    {
        UiCommandRegistry registry = new();
        int id = registry.ResolveOrCreate("omniblock:mymod.reserved");

        Exception? ex = Record.Exception(() => registry.Invoke(id, 0, 0, 0));

        Assert.Null(ex);
    }

    [Fact]
    public void Invoke_passesArgumentsThrough()
    {
        UiCommandRegistry registry = new();
        (int, int, int)? received = null;

        registry.Register("omniblock:inventory.open", (a, b, c) => received = (a, b, c));
        registry.Freeze();

        registry.Invoke(0, 7, 8, 9);

        Assert.Equal((7, 8, 9), received);
    }

    [Fact]
    public void Invoke_unknownId_isNoOp()
    {
        UiCommandRegistry registry = new();
        registry.Freeze();

        Exception? ex = Record.Exception(() => registry.Invoke(999, 0, 0, 0));

        Assert.Null(ex);
    }

    [Fact]
    public void Register_duplicateKey_throws()
    {
        UiCommandRegistry registry = new();
        registry.Register("omniblock:inventory.open", (_, _, _) => { });

        Assert.Throws<InvalidOperationException>(
            () => registry.Register("omniblock:inventory.open", (_, _, _) => { }));
    }

    [Fact]
    public void Register_afterFreeze_throws()
    {
        UiCommandRegistry registry = new();
        registry.Freeze();

        Assert.Throws<InvalidOperationException>(
            () => registry.Register("omniblock:inventory.open", (_, _, _) => { }));
    }

    [Fact]
    public void ResolveOrCreate_afterFreeze_throws()
    {
        UiCommandRegistry registry = new();
        registry.Freeze();

        Assert.Throws<InvalidOperationException>(
            () => registry.ResolveOrCreate("omniblock:inventory.open"));
    }

    [Fact]
    public void Freeze_isIdempotent()
    {
        UiCommandRegistry registry = new();
        registry.Register("omniblock:inventory.open", (_, _, _) => { });

        registry.Freeze();
        registry.Freeze();

        Assert.True(registry.Frozen);
    }
}
