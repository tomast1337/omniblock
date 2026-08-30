using OmniBlock.Client.Network;
using OmniBlock.Client.Resource.Pack;
using OmniBlock.Client.UI.Controls;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Controls.MainMenu;
using OmniBlock.Client.UI.Layout.Flexbox;
using OmniBlock.Client.UI.Screens.Menu.Options;
using OmniBlock.Client.UI.Screens.Menu.World;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Screens.Menu;

public class MainMenuScreen(
    UIContext context,
    Session? session,
    ISingleplayerHost singleplayerHost,
    ClientNetworkContext networkContext,
    TexturePacks texturePackList,
    Action shutdown) : UIScreen(context)
{
    private const float LogoTopPadding = 30f;

    protected override void Init()
    {
        Root.AutomationId = "main";
        Root.Style.AlignItems = Align.Center;
        Root.Style.JustifyContent = Justify.FlexStart;
        Root.Style.PaddingTop = LogoTopPadding;

        Root.AddChild(new Background());

        // --- Logo and Splash ---
        Panel headerPanel = new();
        headerPanel.Style.FlexDirection = FlexDirection.Row;
        headerPanel.Style.JustifyContent = Justify.Center;
        headerPanel.Style.AlignItems = Align.FlexStart;
        headerPanel.Style.MarginBottom = 20;

        MainMenuLogo logo = new();
        headerPanel.AddChild(logo);

        MainMenuSplash splash = new();

        splash.Style.Left = 227;
        splash.Style.Top = 40;
        headerPanel.AddChild(splash);

        Root.AddChild(headerPanel);

        AddTitleSpacer(LogoTopPadding + 64f, 48f);

        // --- Buttons ---
        Button btnSingleplayer = CreateButton();
        btnSingleplayer.AutomationId = "main.singleplayer";
        btnSingleplayer.Text = Translations.Get("menu.singleplayer");
        btnSingleplayer.OnClick += e => Context.Navigator.Navigate(new WorldScreen(Context, singleplayerHost));
        btnSingleplayer.Style.MarginBottom = 4;
        Root.AddChild(btnSingleplayer);

        Button btnMultiplayer = CreateButton();
        btnMultiplayer.AutomationId = "main.multiplayer";
        btnMultiplayer.Text = Translations.Get("menu.multiplayer");
        btnMultiplayer.OnClick += e => Context.Navigator.Navigate(new MultiplayerScreen(Context, networkContext));
        btnMultiplayer.Style.MarginBottom = 4;

        if (session == null || session.sessionId == "-")
        {
            btnMultiplayer.Enabled = false;
        }

        Root.AddChild(btnMultiplayer);

        // Options and Quit side-by-side
        Panel footerButtons = new();
        footerButtons.Style.FlexDirection = FlexDirection.Row;
        footerButtons.Style.JustifyContent = Justify.SpaceBetween;
        footerButtons.Style.Width = 224;
        footerButtons.Style.MarginLeft = -26;

        ImageButton btnLang = CreateImageButton();
        btnLang.AutomationId = "main.language";
        btnLang.OnClick += e => Context.Navigator.Navigate(new LanguageSelectionScreen(Context, this));
        btnLang.Texture = Renderer.TextureManager.GetTextureId("/gui/Globe.png");
        btnLang.U = 0;
        btnLang.V = 0;
        btnLang.UWidth = 24;
        btnLang.VHeight = 24;

        Button btnOptions = CreateButton();
        btnOptions.AutomationId = "main.options";
        btnOptions.Text = Translations.Get("menu.options");
        btnOptions.Style.Width = 98;
        btnOptions.OnClick += e => Context.Navigator.Navigate(new OptionsScreen(Context, this, texturePackList));

        Button btnQuit = CreateButton();
        btnQuit.AutomationId = "main.quit";
        btnQuit.Text = Translations.Get("menu.quit");
        btnQuit.Style.Width = 98;
        btnQuit.OnClick += e => shutdown();

        footerButtons.AddChild(btnLang);
        footerButtons.AddChild(btnOptions);
        footerButtons.AddChild(btnQuit);
        Root.AddChild(footerButtons);

        AddBottomLabels();
    }

    private void AddBottomLabels()
    {
        // Version info
        Link versionLabel = new()
        {
            Text = "OmniBlock " + OmniBlock.Version,
            TextColor = Color.White,
            URL = "https://git.gay/omniblock-official/omniblock"
        };
        versionLabel.Style.Position = PositionType.Absolute;
        versionLabel.Style.Left = 2;
        versionLabel.Style.Top = 2;

        Root.AddChild(versionLabel);

        // Copyright info
        Panel copyrightPanel = new();
        copyrightPanel.Style.Position = PositionType.Absolute;
        copyrightPanel.Style.Bottom = 2;
        copyrightPanel.Style.Right = 2;
        copyrightPanel.Style.AlignItems = Align.FlexEnd;

        copyrightPanel.AddChild(new Label
        {
            Text = "Copyright Mojang Studios. Not an official Minecraft product.",
            TextColor = Color.White
        });
        copyrightPanel.AddChild(new Label
        {
            Text = "Not approved by or associated with Mojang Studios or Microsoft.",
            TextColor = Color.White
        });

        Root.AddChild(copyrightPanel);
    }

    public override void KeyTyped(int key, char character)
    {
    }
}
