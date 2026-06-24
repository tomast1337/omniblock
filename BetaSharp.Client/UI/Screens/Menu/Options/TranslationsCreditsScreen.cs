using BetaSharp.Client.Guis;
using BetaSharp.Client.UI.Controls;
using BetaSharp.Client.UI.Controls.Core;
using BetaSharp.Client.UI.Layout.Flexbox;

namespace BetaSharp.Client.UI.Screens.Menu.Options;

public class TranslationsCreditsScreen(UIContext context, UIScreen parent) : UIScreen(context)
{
    protected override void Init()
    {
        Root.Style.AlignItems = Align.Center;
        Root.Style.JustifyContent = Justify.FlexStart;

        Root.AddChild(new Background(Context.HasWorld ? BackgroundType.World : BackgroundType.Dirt));

        Label title = new()
        {
            Text = Translations.Get("credits.translations"),
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

        Button btnDone = CreateButton();
        btnDone.Text = Translations.Get("gui.done");
        btnDone.Style.MarginBottom = 20;
        btnDone.OnClick += (e) => Context.Navigator.Navigate(parent);
        Root.AddChild(btnDone);
    }

    private void Content(ScrollView scroll)
    {
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

        foreach (var lang in Translations.Instance.Languages.Values) {
            Label lbl = new Label()
            {
                Text = lang.Name + ": " + lang.Author,
                Centered = true,
            };

            lbl.Style.MarginBottom = 4;
            scroll.AddContent(lbl);
        }
    }
}
