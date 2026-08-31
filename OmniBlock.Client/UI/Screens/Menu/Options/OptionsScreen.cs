using OmniBlock.Client.Options;
using OmniBlock.Client.Resource.Pack;
using OmniBlock.Client.UI.Controls.Core;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Screens.Menu.Options;

public class OptionsScreen(
    UIContext context,
    UIScreen? parent,
    TexturePacks texturePacks) : BaseOptionsScreen(context, parent, "options.title")
{
    protected override List<OptionSection> GetOptions() => [];

    protected override UIElement CreateContent()
    {
        Panel list = CreateTwoColumnList();

        // Main options list
        foreach (GameOption option in Options.MainScreenOptions)
        {
            UIElement control = CreateControlForOption(option);
            control.Style.MarginTop = 2;
            control.Style.MarginBottom = 2;
            control.Style.MarginLeft = 4;
            control.Style.MarginRight = 4;
            control.Style.Width = ButtonSize;
            list.AddChild(control);
        }

        // Separator
        Panel separator = new();
        separator.Style.Width = ScrollContentSize;
        separator.Style.Height = 1;
        separator.Style.BackgroundColor = Color.Gray70;
        separator.Style.MarginTop = 6;
        separator.Style.MarginBottom = 6;
        list.AddChild(separator);

        // Sub-menu buttons
        void AddSubButton(string id, string key, Action onClick)
        {
            Button btn = CreateButton();
            btn.AutomationId = $"options.{id}";
            btn.Text = Translations.Get(key);
            btn.Style.MarginTop = 2;
            btn.Style.MarginBottom = 2;
            btn.Style.MarginLeft = 4;
            btn.Style.MarginRight = 4;
            btn.Style.Width = ButtonSize;
            btn.OnClick += e =>
            {
                Options.SaveOptions();
                onClick();
            };
            list.AddChild(btn);
        }

        AddSubButton("video", "options.video.text", () => Context.Navigator.Navigate(new VideoSettingsScreen(Context, this)));
        AddSubButton("ui", "options.uiSettings", () => Context.Navigator.Navigate(new UISettingsScreen(Context, this)));
        AddSubButton("audio", "options.audioSettings", () => Context.Navigator.Navigate(new AudioSettingsScreen(Context, this)));
        AddSubButton("controls", "options.controls", () => Context.Navigator.Navigate(new AllControlsScreen(Context, this)));
        AddSubButton("texturePacks", "menu.texturePacks", () => Context.Navigator.Navigate(new TexturePacksScreen(Context, this, texturePacks)));
        AddSubButton("credits", "menu.credits", () => Context.Navigator.Navigate(new CreditsScreen(Context, this)));

        return list;
    }
}
