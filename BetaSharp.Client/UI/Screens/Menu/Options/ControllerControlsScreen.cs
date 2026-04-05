using BetaSharp.Client.Input;
using BetaSharp.Client.Options;
using BetaSharp.Client.UI.Controls.Core;

namespace BetaSharp.Client.UI.Screens.Menu.Options;

public class ControllerControlsScreen : BaseOptionsScreen
{
    public ControllerControlsScreen(UIContext context, UIScreen? parent)
        : base(context, parent, "Controller Settings")
    {
        TitleText = "Controller Settings";
    }

    protected override List<OptionSection> GetOptions() => [];

    protected override UIElement CreateContent()
    {
        Panel list = CreateTwoColumnList();

        // Sensitivity
        UIElement sens = CreateControlForOption(Options.ControllerSensitivityOption);
        sens.Style.Width = BUTTONSIZE;
        sens.Style.MarginLeft = BUTTONPADDING;
        sens.Style.MarginRight = BUTTONPADDING;
        sens.Style.MarginBottom = 4;
        list.AddChild(sens);

        // Controller Type
        UIElement type = CreateControlForOption(Options.ControllerTypeOption);
        type.Style.Width = BUTTONSIZE;
        type.Style.MarginLeft = BUTTONPADDING;
        type.Style.MarginRight = BUTTONPADDING;
        type.Style.MarginBottom = 4;
        list.AddChild(type);

        // Edit Bindings Button
        Button btnBindings = CreateButton();
        btnBindings.Text = "Edit Bindings...";
        btnBindings.Style.Width = BUTTONSIZE;
        btnBindings.Style.MarginLeft = BUTTONPADDING;
        btnBindings.Style.MarginRight = BUTTONPADDING;
        btnBindings.Style.MarginBottom = 4;
        btnBindings.OnClick += (e) =>
        {
            Context.Navigator.Navigate(new ControllerBindingsScreen(Context, this));
        };
        list.AddChild(btnBindings);

        // Reset Button
        Button btnReset = CreateButton();
        btnReset.Style.Width = BUTTONSIZE;
        btnReset.Style.MarginLeft = BUTTONPADDING;
        btnReset.Style.MarginRight = BUTTONPADDING;
        btnReset.Style.MarginBottom = 4;
        btnReset.Text = "Reset Bindings...";
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
