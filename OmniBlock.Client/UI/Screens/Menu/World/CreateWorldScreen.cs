using OmniBlock.Client.UI.Controls;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Layout.Flexbox;
using OmniBlock.Util;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Storage;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Screens.Menu.World;

public class CreateWorldScreen(
    UIContext context,
    ISingleplayerHost singleplayerHost) : UIScreen(context)
{
    private Button _btnCustomize = null!;
    private Button _btnWorldType = null!;
    private bool _moreOptions;
    private string _seed = "";
    private WorldType _selectedWorldType = context.Content.WorldTypes.Get("omniblock:default");
    private TextField _txfSeed = null!;

    private TextField _txfWorldName = null!;
    private string _worldName = Translations.Get("selectWorld.newWorld");
    public string GeneratorOptions { get; set; } = "";

    protected override void Init() => BuildUI();

    private void BuildUI()
    {
        Root.Children.Clear();
        Root.AutomationId = "world.create.screen";
        Root.AddChild(new Background());
        Root.Style.AlignItems = Align.Center;
        Root.Style.JustifyContent = Justify.Center;
        Root.Style.SetPadding(20);

        Label title = new()
        {
            Text = Translations.Get("selectWorld.create"),
            TextColor = Color.White
        };
        title.Style.MarginBottom = 20;
        Root.AddChild(title);

        if (!_moreOptions)
        {
            // --- Default View ---
            Label lName = new()
            {
                Text = Translations.Get("selectWorld.enterName"),
                TextColor = Color.GrayA0
            };
            lName.Style.MarginBottom = 4;
            Root.AddChild(lName);

            _txfWorldName = new TextField
            {
                AutomationId = "world.create.name",
                Text = _worldName
            };
            _txfWorldName.Style.MarginBottom = 10;
            _txfWorldName.OnTextChanged += text => _worldName = text;
            Root.AddChild(_txfWorldName);
        }
        else
        {
            // --- More Options View ---
            Label lSeed = new()
            {
                Text = Translations.Get("selectWorld.enterSeed"),
                TextColor = Color.GrayA0
            };
            lSeed.Style.MarginBottom = 4;
            Root.AddChild(lSeed);

            _txfSeed = new TextField
            {
                AutomationId = "world.create.seed",
                Text = _seed
            };
            _txfSeed.Style.MarginBottom = 10;
            _txfSeed.OnTextChanged += text => _seed = text;
            Root.AddChild(_txfSeed);

            _btnWorldType = CreateButton();
            _btnWorldType.AutomationId = "world.create.type";
            _btnWorldType.Text = Translations.Get("selectWorld.worldType") + ": " + Translations.Get($"selectWorld.type.{_selectedWorldType.Name.ToLowerInvariant()}.title");
            _btnWorldType.Style.MarginBottom = 4;
            _btnWorldType.OnClick += e => Context.Navigator.Navigate(new SelectWorldTypeScreen(Context, this, _selectedWorldType));
            Root.AddChild(_btnWorldType);

            _btnCustomize = CreateButton();
            _btnCustomize.AutomationId = "world.create.customize";
            _btnCustomize.Text = Translations.Get("gui.customize");
            _btnCustomize.Style.MarginBottom = 10;
            _btnCustomize.Enabled = _selectedWorldType.Key == WorldType.Flat.Key;
            _btnCustomize.OnClick += e => Context.Navigator.Navigate(new CreateFlatWorldScreen(Context, this, GeneratorOptions));
            Root.AddChild(_btnCustomize);
        }

        Panel buttonPanel = new();
        buttonPanel.Style.FlexDirection = FlexDirection.Row;
        buttonPanel.Style.FlexWrap = Wrap.Wrap;
        buttonPanel.Style.JustifyContent = Justify.Center;
        buttonPanel.Style.Width = 310;
        buttonPanel.Style.MarginTop = 10;

        var btnCreate = CreateButton();
        btnCreate.AutomationId = "world.create.submit";
        btnCreate.Text = Translations.Get("gui.create");
        btnCreate.Style.Width = 150;
        btnCreate.Style.SetMargin(2);
        btnCreate.OnClick += e => DoCreateWorld();
        buttonPanel.AddChild(btnCreate);

        var moreOptionsText = _moreOptions ? Translations.Get("gui.done") : Translations.Get("selectWorld.moreWorldOptions");
        var btnToggleMore = CreateButton();
        btnToggleMore.AutomationId = "world.create.more";
        btnToggleMore.Text = moreOptionsText;
        btnToggleMore.Style.Width = 150;
        btnToggleMore.Style.SetMargin(2);
        btnToggleMore.OnClick += e =>
        {
            _moreOptions = !_moreOptions;
            BuildUI();
        };
        buttonPanel.AddChild(btnToggleMore);

        var btnCancel = CreateButton();
        btnCancel.AutomationId = "world.create.cancel";
        btnCancel.Text = Translations.Get("gui.cancel");
        btnCancel.Style.Width = 150;
        btnCancel.Style.SetMargin(2);
        btnCancel.OnClick += e => Context.Navigator.Navigate(new WorldScreen(Context, singleplayerHost));
        buttonPanel.AddChild(btnCancel);

        Root.AddChild(buttonPanel);
    }

    public void SetWorldType(WorldType type)
    {
        _selectedWorldType = type;
        BuildUI();
    }

    private void DoCreateWorld()
    {
        var worldSeed = new JavaRandom().NextLong();
        if (!string.IsNullOrEmpty(_seed))
        {
            try
            {
                if (long.TryParse(_seed, out var parsedSeed) && parsedSeed != 0L)
                {
                    worldSeed = parsedSeed;
                }
                else
                {
                    worldSeed = CalculateJavaHash(_seed);
                }
            }
            catch
            {
                worldSeed = CalculateJavaHash(_seed);
            }
        }

        var folderName = _worldName.Trim();
        var invalidCharacters = ChatAllowedCharacters.InvalidFileNameChars;
        foreach (var c in invalidCharacters)
        {
            folderName = folderName.Replace(c, '_');
        }

        if (string.IsNullOrEmpty(folderName))
        {
            folderName = "World";
        }

        folderName = GenerateUnusedFolderName(singleplayerHost.SaveLoader, folderName);

        WorldSettings settings = new(worldSeed, _selectedWorldType, GeneratorOptions);
        singleplayerHost.LoadWorld(folderName, _worldName, settings);
    }


    private static long CalculateJavaHash(string input)
    {
        var hash = 0;
        foreach (var c in input)
        {
            hash = 31 * hash + c;
        }

        return hash;
    }

    private static string GenerateUnusedFolderName(IWorldStorageSource worldStorage, string baseFolderName)
    {
        while (worldStorage.GetProperties(baseFolderName) != null)
        {
            baseFolderName += "-";
        }

        return baseFolderName;
    }
}
