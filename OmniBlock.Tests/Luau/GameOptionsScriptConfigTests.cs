using OmniBlock.Client.Options;
using OmniBlock.Luau.Host;

namespace OmniBlock.Tests.Luau;

public sealed class GameOptionsScriptConfigTests
{
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
}
