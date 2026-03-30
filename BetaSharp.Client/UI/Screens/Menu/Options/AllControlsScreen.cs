using BetaSharp.Client.Options;
using BetaSharp.Client.UI.Controls.Core;

namespace BetaSharp.Client.UI.Screens.Menu.Options;

public class AllControlsScreen : BaseOptionsScreen
{
    public AllControlsScreen(BetaSharp game, UIScreen? parent, GameOptions options)
        : base(game, parent, options, "options.controls")
    {
        TitleText = "Controls";
    }

    protected override IEnumerable<GameOption> GetOptions() => [];

    protected override UIElement CreateContent()
    {
        Panel list = CreateVerticalList();

        Button btnKeyboard = CreateButton();
        btnKeyboard.Text = "Keyboard Controls...";
        btnKeyboard.Style.Width = 310;
        btnKeyboard.Style.MarginBottom = 4;
        btnKeyboard.OnClick += (e) =>
        {
            Game.DisplayUIScreen(new ControlsScreen(Game, this, Options));
        };
        list.AddChild(btnKeyboard);

        Button btnController = CreateButton();
        btnController.Text = "Controller Settings...";
        btnController.Style.Width = 310;
        btnController.OnClick += (e) =>
        {
            Game.DisplayUIScreen(new ControllerControlsScreen(Game, this, Options));
        };
        list.AddChild(btnController);

        return list;
    }
}
