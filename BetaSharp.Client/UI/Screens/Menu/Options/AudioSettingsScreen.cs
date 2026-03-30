using BetaSharp.Client.Options;

namespace BetaSharp.Client.UI.Screens.Menu.Options;

public class AudioSettingsScreen : BaseOptionsScreen
{
    public AudioSettingsScreen(BetaSharp game, UIScreen? parent, GameOptions options)
        : base(game, parent, options, "options.audioTitle")
    {
        TitleText = "Audio Settings";
    }

    protected override IEnumerable<GameOption> GetOptions() => Options.AudioScreenOptions;
}
