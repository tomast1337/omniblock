using BetaSharp.Client.Input;
using BetaSharp.Client.Options;
using BetaSharp.Client.UI.Controls.Core;

namespace BetaSharp.Client.UI.Screens.Menu.Options;

public class ControllerControlsScreen : BaseOptionsScreen
{
    public ControllerControlsScreen(BetaSharp game, UIScreen? parent, GameOptions options)
        : base(game, parent, options, "Controller Settings")
    {
        TitleText = "Controller Settings";
    }

    protected override IEnumerable<GameOption> GetOptions() => [];

    protected override UIElement CreateContent()
    {
        Panel list = CreateVerticalList();

        // Sensitivity
        UIElement sens = CreateControlForOption(Options.ControllerSensitivityOption);
        sens.Style.Width = 310;
        sens.Style.MarginBottom = 4;
        list.AddChild(sens);

        // Controller Type
        UIElement type = CreateControlForOption(Options.ControllerTypeOption);
        type.Style.Width = 310;
        type.Style.MarginBottom = 10;
        list.AddChild(type);

        // Edit Bindings Button
        Button btnBindings = CreateButton();
        btnBindings.Text = "Edit Bindings...";
        btnBindings.Style.Width = 310;
        btnBindings.Style.MarginBottom = 4;
        btnBindings.OnClick += (e) =>
        {
            Navigator.Navigate(new ControllerBindingsScreen(Game, this, Options));
        };
        list.AddChild(btnBindings);

        // Reset Button
        Button btnReset = CreateButton();
        btnReset.Text = "Reset Bindings";
        btnReset.Style.Width = 310;
        btnReset.OnClick += (e) =>
        {
            foreach (ControllerBinding cb in Options.ControllerBindings)
                cb.Button = cb.DefaultButton;
            Options.SaveOptions();
        };
        list.AddChild(btnReset);

        return list;
    }
}
