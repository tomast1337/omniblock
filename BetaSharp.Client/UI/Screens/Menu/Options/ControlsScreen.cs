using BetaSharp.Client.Input;
using BetaSharp.Client.Options;
using BetaSharp.Client.UI.Controls.Core;
using BetaSharp.Client.UI.Layout.Flexbox;
using Button = BetaSharp.Client.UI.Controls.Core.Button;

namespace BetaSharp.Client.UI.Screens.Menu.Options;

public class ControlsScreen : BaseOptionsScreen
{
    private (KeyBinding key, Button button)? _selectedKey = null;

    protected override int MaxWidth { get; } = 300;

    public ControlsScreen(UIContext context, UIScreen? parent)
        : base(context, parent, "controls.title")
    {
        TitleText = "Controls";
    }

    protected override List<OptionSection> GetOptions() => [];

    protected override UIElement CreateContent()
    {
        Panel list = CreateTwoColumnList();

        // Mouse Settings at top
        UIElement sensitivity = CreateControlForOption(Options.MouseSensitivityOption);
        sensitivity.Style.Width = ButtonSize;
        sensitivity.Style.MarginLeft = ButtonPadding;
        sensitivity.Style.MarginRight = ButtonPadding;
        sensitivity.Style.MarginBottom = 10;
        list.AddChild(sensitivity);

        UIElement invert = CreateControlForOption(Options.InvertMouseOption);
        invert.Style.Width = ButtonSize;
        invert.Style.MarginLeft = ButtonPadding;
        invert.Style.MarginRight = ButtonPadding;
        invert.Style.MarginBottom = 10;
        list.AddChild(invert);

        // Keybinds List
        bool first = true;
        foreach (GameOptions.KeyBindingGroup group in Options.KeyBindingGroups)
        {
            list.AddChild(CreateSectionHeader(group.Title, first));

            foreach (var bind in group.Bindings)
            {
                Panel row = new()
                {
                    Style =
                    {
                        FlexDirection = FlexDirection.Row,
                        AlignItems = Align.Center,
                        Width = TwoButtonSize
                    }
                };
                row.Style.SetMargin(2);

                Label label = new()
                {
                    Text = Options.GetKeyBindingDescription(bind),
                    Style =
                    {
                        FlexGrow = 1
                    }
                };
                row.AddChild(label);

                Button btn = CreateButton();
                btn.Text = Options.GetOptionDisplayString(bind);
                btn.Style.Width = 80;
                var bind1 = bind;
                btn.OnClick += (e) =>
                {
                    Button button = (e.Target as Button)!;
                    // If seek key is down, reset.
                    if (Keyboard.isKeyDown(Options.KeyBindSneak.ScanCode))
                    {
                        bind1.ScanCode = bind1.DefaultLogicalKey;
                        button.Text = Options.GetOptionDisplayString(bind1);
                    }
                    else
                    {
                        _selectedKey = (bind1, button);
                        button.Text = "> ??? <";
                    }
                };
                row.AddChild(btn);

                list.AddChild(row);
            }

            first = false;
        }


        return list;
    }

    public override void KeyTyped(int key, char character)
    {
        if (_selectedKey.HasValue)
        {
            // If escape is pressed, set the key to none.
            int keyToSet = key;
            if (key == Keyboard.KEY_ESCAPE) keyToSet = Keyboard.KEY_NONE;

            Options.SetKeyBinding(_selectedKey.Value.key, keyToSet);
            _selectedKey.Value.button.Text = Options.GetOptionDisplayString(_selectedKey.Value.key);
            _selectedKey = null;
        }
        else
        {
            base.KeyTyped(key, character);
        }
    }
}
