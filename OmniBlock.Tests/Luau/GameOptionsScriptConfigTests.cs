using OmniBlock.Client.Options;
using OmniBlock.Luau.Host;

namespace OmniBlock.Tests.Luau;

public sealed class GameOptionsScriptConfigTests
{
    [Theory]
    [InlineData("pauseOnFocusLoss")]
    [InlineData("captureMouse")]
    public void FocusOptionsAreEnabledByDefaultAndScriptChoicePersists(string key)
    {
        var directory = Directory.CreateTempSubdirectory("omniblock-focus-options-");
        try
        {
            var path = Path.Combine(directory.FullName, "options.txt");
            File.WriteAllText(path, "music:0.5\n");
            var options = new GameOptions(null!, directory.FullName);

            Assert.True(options.GetScriptConfig(key).Boolean);
            Assert.Contains(options.UIScreenOptions, option => option.SaveKey == key);
            Assert.False(options.SetScriptConfig(key, LuauConfigValue.From("false")));
            Assert.True(options.SetScriptConfig(key, LuauConfigValue.From(false)));
            Assert.False(options.GetScriptConfig(key).Boolean);

            var reloaded = new GameOptions(null!, directory.FullName);
            Assert.False(reloaded.GetScriptConfig(key).Boolean);
            Assert.Contains(key + ":false", File.ReadAllText(path));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void TypedScriptWritesUseNormalOptionSettersAndCallbacks()
    {
        float observed = -1;
        FloatOption option = new("test", "volume")
        {
            Steps = 10,
            OnChanged = value => observed = value
        };

        var changed = GameOptions.SetOptionValue(option, LuauConfigValue.From(1.7));

        Assert.True(changed);
        Assert.Equal(1, option.Value);
        Assert.Equal(1, observed);
    }

    [Fact]
    public void RejectsWrongTypesAndOutOfRangeCycleValues()
    {
        BoolOption boolean = new("test", "enabled");
        CycleOption cycle = new("test", "mode", ["one", "two"]);

        Assert.False(GameOptions.SetOptionValue(boolean, LuauConfigValue.From("true")));
        Assert.False(GameOptions.SetOptionValue(cycle, LuauConfigValue.From(2.0)));
        Assert.False(GameOptions.SetOptionValue(cycle, LuauConfigValue.From(0.5)));
    }

    [Fact]
    public void Presentation_quality_is_scriptable_and_persistent()
    {
        var directory = Directory.CreateTempSubdirectory("omniblock-presentation-options-");
        try
        {
            var options = new GameOptions(null!, directory.FullName);

            Assert.Equal(1, options.PresentationQuality);
            Assert.True(options.SetScriptConfig(
                "presentationQuality", LuauConfigValue.From(0.0)));
            Assert.Equal(0, options.PresentationQuality);
            Assert.Equal(3, options.GetScriptConfigOptions("presentationQuality")!.Count);

            var reloaded = new GameOptions(null!, directory.FullName);
            Assert.Equal(0, reloaded.PresentationQuality);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Entity_impostor_distance_is_aggressive_scriptable_persistent_and_migrates_boolean_gate()
    {
        var directory = Directory.CreateTempSubdirectory("omniblock-impostor-option-");
        try
        {
            var options = new GameOptions(null!, directory.FullName);
            Assert.Equal(0, GameOptions.DecodeEntityImpostorDistance(0));
            Assert.Equal(32, GameOptions.DecodeEntityImpostorDistance(1f / 15));
            Assert.Equal(256, GameOptions.DecodeEntityImpostorDistance(1));
            Assert.True(options.EntityImpostors);
            Assert.Equal(48, options.EntityImpostorDistance);
            Assert.True(options.SetScriptConfig("entityImpostorDistance", LuauConfigValue.From(0.0)));
            Assert.False(options.EntityImpostors);

            var reloaded = new GameOptions(null!, directory.FullName);
            Assert.False(reloaded.EntityImpostors);

            File.WriteAllText(Path.Combine(directory.FullName, "options.txt"), "entityImpostors:true\n");
            var migrated = new GameOptions(null!, directory.FullName);
            Assert.Equal(48, migrated.EntityImpostorDistance);

            Assert.True(migrated.SetScriptConfig("entityImpostors", LuauConfigValue.From(false)));
            Assert.False(migrated.GetScriptConfig("entityImpostors").Boolean);
            Assert.Contains("entityImpostorDistance:0", File.ReadAllText(Path.Combine(directory.FullName, "options.txt")));
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
