using System.Diagnostics;
using BetaSharp.Client.Guis;
using BetaSharp.Client.Options;
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
            Text = "Credits",
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
        btnDone.Text = TranslationStorage.Instance.TranslateKey("gui.done");
        btnDone.Style.MarginBottom = 20;
        btnDone.OnClick += (e) => Context.Navigator.Navigate(parent);
        Root.AddChild(btnDone);
    }

    private void Content(ScrollView scroll)
    {
        void ColoredText(string text, Color color, float scale)
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

        void Text(string text, float scale = 1.0F)
            => ColoredText(text, Color.White, scale);

        void Header(string text)
            => ColoredText(text, Color.Yellow, 1f);

        void Link(string text, string url, float scale = 1.0F)
        {
            Label lbl = new Label()
            {
                Text = text,
                Scale = scale,
                Centered = true
            };

            lbl.Style.MarginBottom = 4;

            lbl.OnMouseEnter += (e) => {
                lbl.TextColor = Color.HoverYellow;
            };

            lbl.OnMouseLeave += (e) =>
            {
                lbl.TextColor = Color.White;
            };

            lbl.OnClick += (e) =>
            {
                if (e.Button == MouseButton.Left)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
            };

            scroll.AddContent(lbl);
        }

        Header("BetaSharp");
        Text("Port of Minecraft Beta 1.7.3 to C#");
        Link("On GitHub", "https://github.com/betasharp-official/betasharp/");
        Text("Version " + BetaSharp.Version);
        Text("");

        Header("Developers");
        Link("Made by Fazin85", "https://github.com/Fazin85");
        Link("With help by TheVeryStarlk", "https://github.com/TheVeryStarlk");
        Link("and many others!", "https://github.com/betasharp-official/betasharp/graphs/contributors");
        Text("");

        Header("Libraries");
        Link("Slik.NET - Graphics", "https://github.com/dotnet/Silk.NET");
        Link("ImGui - Debug UI", "https://github.com/ocornut/imgui");
        Link("SFML.NET - Audio", "https://github.com/SFML/SFML.Net");
        Link("SixLabors - Fonts, image processing", "https://github.com/sixlabors");
    }
}
