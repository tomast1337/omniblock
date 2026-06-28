using BetaSharp.Client.Guis;
using BetaSharp.Client.UI.Controls;
using BetaSharp.Client.UI.Controls.Core;
using BetaSharp.Client.UI.Layout.Flexbox;

namespace BetaSharp.Client.UI.Screens.Menu.Options;

public class CreditsScreen(UIContext context, UIScreen parent) : UIScreen(context)
{
    protected override void Init()
    {
        Root.Style.AlignItems = Align.Center;
        Root.Style.JustifyContent = Justify.FlexStart;

        Root.AddChild(new Background(Context.HasWorld ? BackgroundType.World : BackgroundType.Dirt));

        Label title = new()
        {
            Text = Translations.Get("menu.credits"),
            TextColor = Color.White,
            Centered = true
        };
        title.Style.MarginTop = 20;
        title.Style.MarginBottom = 8;
        Root.AddChild(title);
        AddTitleSpacer();

        ScrollView scroll = new();
        scroll.Style.Width = 300;
        scroll.Style.FlexGrow = 1;
        scroll.Style.MaxHeight = 200;
        scroll.Style.MarginBottom = 10;

        Content(scroll);

        Root.AddChild(scroll);

        Panel bottomButtons = new();
        bottomButtons.Style.FlexDirection = FlexDirection.Row;
        bottomButtons.Style.MarginBottom = 20;

        Button btnDone = CreateButton();
        btnDone.Text = Translations.Get("gui.done");
        btnDone.Style.MarginRight = 4;
        btnDone.OnClick += (e) => Context.Navigator.Navigate(parent);
        bottomButtons.AddChild(btnDone);

        ImageButton btnLang = CreateImageButton();
        btnLang.OnClick += (e) => Context.Navigator.Navigate(new TranslationsCreditsScreen(Context, this));
        btnLang.Texture = Renderer.TextureManager.GetTextureId("/gui/Globe.png");
        btnLang.U = 0;
        btnLang.V = 0;
        btnLang.UWidth = 24;
        btnLang.VHeight = 24;
        bottomButtons.AddChild(btnLang);

        Root.AddChild(bottomButtons);
    }

    private void Content(ScrollView scroll)
    {
        void ColoredTextUntranslated(string text, Color color, float scale)
        {
            Label lbl = new Label()
            {
                Text = text,
                Scale = scale,
                TextColor = color,
                Centered = true,
            };

            lbl.Style.MarginBottom = 4;
            scroll.AddContent(lbl);
        }

        void ColoredText(string key, Color color, float scale)
            => ColoredTextUntranslated(Translations.Get(key), color, scale);

        void Text(string key, float scale = 1.0F)
            => ColoredText(key, Color.White, scale);

        void HeaderFormat(string key)
            => ColoredText(Translations.GetFormat(key), Color.Yellow, 1f);
        void Header(string key)
            => ColoredText(key, Color.Yellow, 1f);

        void Link(string key, string url, float scale = 1.0F)
        {
            Link lbl = new Link()
            {
                Text = Translations.Get(key),
                Scale = scale,
                Centered = true,
                URL = url
            };

            lbl.Style.MarginBottom = 4;
            scroll.AddContent(lbl);
        }

        void Seperator() {
            var ele = new UIElement();
            ele.Style.Height = 10;
            scroll.AddContent(ele);
        }

        const int scale = 20;
        const int imageWidth = 1000 / scale;
        const int imageHeight = 675 / scale;

        var image = new Image();
        image.Texture = Context.TextureManager.GetTextureId("gui/Logo.png");
        image.Style.Width = imageWidth;
        image.Style.Height = imageHeight;
        image.Style.AlignSelf = Align.Center;
        image.Style.MarginBottom = 10;
        scroll.AddContent(image);

        ColoredTextUntranslated(Translations.GetFormat("credits.version", BetaSharp.Version), Color.Yellow, 1f);
        Text("credits.description");
        Link("credits.github", "https://git.gay/betasharp-official/betasharp");
        Link("credits.author", "https://git.gay/Fazin85");
        Seperator();

        Header("credits.libs");
        Link("credits.slik", "https://github.com/dotnet/Silk.NET");
        Link("credits.imgui", "https://github.com/ocornut/imgui");
        Link("credits.sfml", "https://github.com/SFML/SFML.Net");
        Link("credits.sixlabors", "https://github.com/sixlabors");
    }
}
