using System.Xml.Linq;
using BetaSharp.Client.Guis;
using BetaSharp.Client.Options;
using BetaSharp.Client.UI.Controls;
using BetaSharp.Client.UI.Controls.Core;
using BetaSharp.Client.UI.Layout.Flexbox;

namespace BetaSharp.Client.UI.Screens.Menu.Options;

public abstract class BaseOptionsScreen(
    UIContext context,
    UIScreen? parent,
    string titleKey) : UIScreen(context)
{
    protected readonly UIScreen? Parent = parent;
    protected GameOptions Options => Context.Options;
    protected string TitleText = TranslationStorage.Instance.TranslateKey(titleKey);

    private const int WIDTH = 320;

    protected override void Init()
    {
        Root.Style.AlignItems = Align.Center;
        Root.Style.JustifyContent = Justify.FlexStart;

        Root.AddChild(new Background(Context.HasWorld ? BackgroundType.World : BackgroundType.Dirt));

        Label title = new()
        {
            Text = TitleText,
            TextColor = Color.White,
            Centered = true
        };
        title.Style.MarginTop = 20;
        title.Style.MarginBottom = 8;
        Root.AddChild(title);
        AddTitleSpacer();

        ScrollView scroll = new();
        scroll.Style.Width = 340;
        scroll.Style.FlexGrow = 1;
        scroll.Style.MaxHeight = 200;
        scroll.Style.MarginBottom = 10;

        UIElement content = CreateContent();
        scroll.AddContent(content);
        Root.AddChild(scroll);

        Button btnDone = CreateButton();
        btnDone.Text = TranslationStorage.Instance.TranslateKey("gui.done");
        btnDone.Style.MarginBottom = 20;
        btnDone.OnClick += (e) => OnDone();
        Root.AddChild(btnDone);
    }

    protected virtual UIElement CreateContent()
    {
        Panel root = new();
        root.Style.FlexDirection = FlexDirection.Column;
        root.Style.AlignItems = Align.Center;
        root.Style.Width = WIDTH;

        var options = GetOptions();
        foreach (OptionSection section in options)
        {
            if (section.Name is not null) root.AddChild(CreateSectionHeader(section.Name));

            Panel grid = CreateTwoColumnList();
            foreach (GameOption option in section.Options)
            {
                UIElement control = CreateControlForOption(option);
                control.Style.Width = 150;
                control.Style.MarginTop = 2;
                control.Style.MarginBottom = 2;
                control.Style.MarginLeft = 4;
                control.Style.MarginRight = 4;
                grid.AddChild(control);
            }
            root.AddChild(grid);
        }

        return root;
    }

    protected static Panel CreateTwoColumnList()
    {
        Panel list = new();
        list.Style.FlexDirection = FlexDirection.Row;
        list.Style.FlexWrap = Wrap.Wrap;
        list.Style.JustifyContent = Justify.FlexStart;
        list.Style.Width = WIDTH;
        return list;
    }

    protected static UIElement CreateSectionHeader(string text)
    {
        Panel header = new();
        header.Style.FlexDirection = FlexDirection.Row;
        header.Style.AlignItems = Align.Center;
        header.Style.Width = WIDTH;
        header.Style.MarginTop = 10;
        header.Style.MarginBottom = 4;
        header.IsHitTestVisible = false;

        Panel leftLine = new();
        leftLine.Style.FlexGrow = 1;
        leftLine.Style.Height = 1;
        leftLine.Style.BackgroundColor = Color.Gray70;
        leftLine.Style.MarginLeft = 8;

        Label label = new()
        {
            Text = text,
            TextColor = Color.GrayAA,
            Centered = true
        };
        label.Style.MarginLeft = 8;
        label.Style.MarginRight = 8;

        Panel rightLine = new();
        rightLine.Style.FlexGrow = 1;
        rightLine.Style.Height = 1;
        rightLine.Style.BackgroundColor = Color.Gray70;
        rightLine.Style.MarginRight = 8;

        header.AddChild(leftLine);
        header.AddChild(label);
        header.AddChild(rightLine);

        return header;
    }

    protected struct OptionSection
    {
        public string? Name;
        public IEnumerable<GameOption> Options;

        public OptionSection(string name, IEnumerable<GameOption> options)
        {
            Name = name;
            Options = options;
        }

        public OptionSection(IEnumerable<GameOption> options)
        {
            Name = null;
            Options = options;
        }
    }

    protected abstract List<OptionSection> GetOptions();

    protected virtual void OnDone()
    {
        Options.SaveOptions();
        if (Parent != null)
        {
            Context.Navigator.Navigate(Parent);
        }
        else
        {
            Context.Navigator.Navigate(null);
        }
    }

    protected virtual UIElement CreateControlForOption(GameOption option)
    {
        TranslationStorage translations = TranslationStorage.Instance;

        if (option is FloatOption floatOpt)
        {
            Slider slider = CreateSlider();
            slider.Value = floatOpt.Value;
            slider.Text = option.GetDisplayString(translations);
            slider.OnValueChanged += (v) =>
            {
                floatOpt.Value = v;
                slider.Text = option.GetDisplayString(translations);
            };
            return slider;
        }
        else
        {
            Button btn = CreateButton();
            btn.Text = option.GetDisplayString(translations);
            btn.OnClick += (e) =>
            {
                if (option is BoolOption boolOpt) boolOpt.Toggle();
                else if (option is CycleOption cycleOpt) cycleOpt.Cycle();

                btn.Text = option.GetDisplayString(translations);
            };
            return btn;
        }
    }
}
