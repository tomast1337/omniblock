using BetaSharp.Client.Options;
using BetaSharp.Client.UI.Controls.Core;
using BetaSharp.Client.UI.Screens.InGame;

namespace BetaSharp.Client.UI.Screens.Menu.Options;

public class DebugOptionsScreen : BaseOptionsScreen
{
    public DebugOptionsScreen(BetaSharp game, UIScreen? parent, GameOptions options)
        : base(game, parent, options, "options.debugTitle")
    {
        TitleText = "Debug Options";
    }

    protected override IEnumerable<GameOption> GetOptions() => Options.DebugScreenOptions;

    protected override UIElement CreateContent()
    {
        Panel list = CreateVerticalList();

        foreach (GameOption option in GetOptions())
        {
            UIElement control = CreateControlForOption(option);
            control.Style.MarginTop = 2;
            control.Style.MarginBottom = 2;
            control.Style.Width = 310;
            list.AddChild(control);
        }

        Button editorBtn = CreateButton();
        editorBtn.Text = "Edit Debug Components...";
        editorBtn.Style.MarginTop = 2;
        editorBtn.Style.MarginBottom = 2;
        editorBtn.Style.Width = 310;
        editorBtn.OnClick += (e) =>
        {
            Options.SaveOptions();
            Navigator.Navigate(new DebugEditorScreen(Game, this));
        };
        list.AddChild(editorBtn);

        return list;
    }
}
