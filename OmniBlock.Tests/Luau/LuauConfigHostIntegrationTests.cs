using OmniBlock.Luau;
using OmniBlock.Luau.Host;

namespace OmniBlock.Tests.Luau;

[Collection(LuauHostCollection.Name)]
public sealed class LuauConfigHostIntegrationTests
{
    [SkippableFact]
    public void BootstrapProvidesTypedPersistentConfigProperties()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using LuauState state = new();
        state.ResetInstructionBudget(100_000);
        Dictionary<string, LuauConfigValue> values = new()
        {
            ["music"] = LuauConfigValue.From(1.0),
            ["vsync"] = LuauConfigValue.From(false),
            ["language"] = LuauConfigValue.From("en_us")
        };

        LuauConfigHost.Get = key => values.GetValueOrDefault(key);
        LuauConfigHost.Set = (key, value) =>
        {
            if (!values.ContainsKey(key)) return false;
            values[key] = value;
            return true;
        };
        LuauConfigHost.Options = key => key == "language"
            ? [LuauConfigValue.From("en_us"), LuauConfigValue.From("pt_br")]
            : null;

        try
        {
            // The UI bootstrap owns the top-level client environment; config augments it.
            Assert.True(state.TryExecute(LuauDomHost.Bootstrap, out string omniError), omniError);
            LuauConfigHost.Install(state.Handle);
            Assert.True(state.TryExecute(LuauConfigHost.Bootstrap, out string configError), configError);

            Assert.True(state.TryExecute("OMNI.config.music = 0.25", out string setMusic), setMusic);
            Assert.True(state.TryExecute("OMNI.config.vsync = true", out string setVsync), setVsync);
            Assert.True(state.TryExecute("OMNI.config.language = \"pt_br\"", out string setLanguage), setLanguage);
            Assert.True(state.TryExecute("OMNI.config.music", out string music), music);
            Assert.True(state.TryExecute("table.concat(OMNI.config.options(\"language\"), \",\")", out string languages), languages);

            Assert.Equal(0.25, values["music"].Number);
            Assert.True(values["vsync"].Boolean);
            Assert.Equal("pt_br", values["language"].String);
            Assert.Equal("0.25", music);
            Assert.Equal("en_us,pt_br", languages);
        }
        finally
        {
            LuauConfigHost.Get = null;
            LuauConfigHost.Set = null;
            LuauConfigHost.Options = null;
        }
    }
}
