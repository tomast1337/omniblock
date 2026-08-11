using OmniBlock.Luau;

namespace OmniBlock.Tests.Luau;

/// <summary>
///     Covers <see cref="LuauQuickRun" />, the public ephemeral-execution surface the debug-menu
///     Luau console (<c>OmniBlock.Client/Diagnostics/Windows/LuauConsoleWindow.cs</c>) calls.
///     Same native-library requirement as <see cref="LuauExecutionTests" /> and
///     <see cref="LuauInterruptIntegrationTests" />: requires native/luau/build-local.sh to have
///     been run for this checkout. Unlike those two, this exercises the public wrapper end to
///     end rather than raw <see cref="LuauNative" /> calls — in particular the value-formatting
///     paths (nil/boolean/string/number/error/multi-return) added for the console's output.
/// </summary>
public sealed class LuauQuickRunTests
{
    [SkippableFact]
    public void TryExecute_arithmetic_returnsFormattedInteger()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(),
            "native/luau/build-local.sh hasn't been run for this checkout — omniblock_luau isn't resolvable.");

        bool success = LuauQuickRun.TryExecute("return 10 + 32", out string output);

        Assert.True(success);
        Assert.Equal("42", output);
    }

    [SkippableFact]
    public void TryExecute_string_returnsRawText()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");

        bool success = LuauQuickRun.TryExecute("return 'hello'", out string output);

        Assert.True(success);
        Assert.Equal("hello", output);
    }

    [SkippableFact]
    public void TryExecute_boolean_returnsTrueOrFalse()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");

        bool success = LuauQuickRun.TryExecute("return 1 == 1", out string output);

        Assert.True(success);
        Assert.Equal("true", output);
    }

    [SkippableFact]
    public void TryExecute_nilReturn_returnsNilLiteral()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");

        bool success = LuauQuickRun.TryExecute("return nil", out string output);

        Assert.True(success);
        Assert.Equal("nil", output);
    }

    [SkippableFact]
    public void TryExecute_noReturnValue_reportsNoReturnValue()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");

        bool success = LuauQuickRun.TryExecute("local x = 1", out string output);

        Assert.True(success);
        Assert.Equal("(no return value)", output);
    }

    [SkippableFact]
    public void TryExecute_multiReturn_joinsValuesWithComma()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");

        bool success = LuauQuickRun.TryExecute("return 1, 'two', false", out string output);

        Assert.True(success);
        Assert.Equal("1, two, false", output);
    }

    [SkippableFact]
    public void TryExecute_tableReturn_describesTypeWithoutInvokingMetamethods()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");

        bool success = LuauQuickRun.TryExecute("return {}", out string output);

        Assert.True(success);
        Assert.Equal("<table>", output);
    }

    [SkippableFact]
    public void TryExecute_compileError_failsWithMessage()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");

        bool success = LuauQuickRun.TryExecute("this is not valid luau (((", out string output);

        Assert.False(success);
        Assert.False(string.IsNullOrWhiteSpace(output));
    }

    [SkippableFact]
    public void TryExecute_runtimeError_failsWithMessage()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");

        bool success = LuauQuickRun.TryExecute("error('boom')", out string output);

        Assert.False(success);
        Assert.Contains("boom", output);
    }

    [SkippableFact]
    public void TryExecute_callingNilValue_failsWithMessage()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");

        bool success = LuauQuickRun.TryExecute("local f = nil; f()", out string output);

        Assert.False(success);
        Assert.False(string.IsNullOrWhiteSpace(output));
    }
}
