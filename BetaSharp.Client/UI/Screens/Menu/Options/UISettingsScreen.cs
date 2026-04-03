using BetaSharp.Client.Options;

namespace BetaSharp.Client.UI.Screens.Menu.Options;

public class UISettingsScreen(UIContext context, UIScreen? parent) : BaseOptionsScreen(context, parent, "UI Settings")
{
    protected override IEnumerable<GameOption> GetOptions() => Options.UIScreenOptions;
}
