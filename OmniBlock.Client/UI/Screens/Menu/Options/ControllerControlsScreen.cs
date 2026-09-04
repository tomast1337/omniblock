namespace OmniBlock.Client.UI.Screens.Menu.Options;

public class ControllerControlsScreen : BaseOptionsScreen
{
    public ControllerControlsScreen(UIContext context, UIScreen? parent)
        : base(context, parent, "options.controllerSettings")
    {
    }

    protected override List<OptionSection> GetOptions() => [];

    protected override UIElement CreateContent()
    {
        var list = CreateTwoColumnList();

        // Sensitivity
        var sens = CreateControlForOption(Options.ControllerSensitivityOption);
        sens.Style.Width = ButtonSize;
        sens.Style.MarginLeft = ButtonPadding;
        sens.Style.MarginRight = ButtonPadding;
        sens.Style.MarginBottom = 4;
        list.AddChild(sens);

        // Controller Type
        var type = CreateControlForOption(Options.ControllerTypeOption);
        type.Style.Width = ButtonSize;
        type.Style.MarginLeft = ButtonPadding;
        type.Style.MarginRight = ButtonPadding;
        type.Style.MarginBottom = 4;
        list.AddChild(type);

        // Edit Bindings Button
        var btnBindings = CreateButton();
        btnBindings.Text = Translations.Get("options.editBindings");
        btnBindings.Style.Width = ButtonSize;
        btnBindings.Style.MarginLeft = ButtonPadding;
        btnBindings.Style.MarginRight = ButtonPadding;
        btnBindings.Style.MarginBottom = 4;
        btnBindings.OnClick += e => { Context.Navigator.Navigate(new ControllerBindingsScreen(Context, this)); };
        list.AddChild(btnBindings);

        // Reset Button
        var btnReset = CreateButton();
        btnReset.Style.Width = ButtonSize;
        btnReset.Style.MarginLeft = ButtonPadding;
        btnReset.Style.MarginRight = ButtonPadding;
        btnReset.Style.MarginBottom = 4;
        btnReset.Text = Translations.Get("options.resetBindings");
        btnReset.OnClick += e =>
        {
            foreach (var cb in Options.ControllerBindings)
            {
                cb.Button = cb.DefaultButton;
            }

            Options.SaveOptions();
        };
        list.AddChild(btnReset);

        return list;
    }
}
