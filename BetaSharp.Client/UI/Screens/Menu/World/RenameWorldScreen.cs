using BetaSharp.Client.UI.Controls;
using BetaSharp.Client.UI.Controls.Core;
using BetaSharp.Client.UI.Layout.Flexbox;
using BetaSharp.Worlds.Core.Systems;
using BetaSharp.Worlds.Storage;
using Color = BetaSharp.Client.UI.Colors.Color;

namespace BetaSharp.Client.UI.Screens.Menu.World;

public class RenameWorldScreen(
    UIContext context,
    WorldScreen parent,
    string worldFolderName,
    IWorldStorageSource saveLoader) : UIScreen(context)
{
    private readonly string _worldFolderName = worldFolderName;
    private TextField _txfName = null!;

    protected override void Init()
    {
        Root.AddChild(new Background());
        Root.Style.AlignItems = Align.Center;
        Root.Style.JustifyContent = Justify.Center;
        Root.Style.SetPadding(20);

        Label title = new()
        {
            Text = Translations.Get("selectWorld.renameTitle"),
            TextColor = Color.White
        };
        title.Style.MarginBottom = 10;
        Root.AddChild(title);

        Label lName = new()
        {
            Text = Translations.Get("selectWorld.enterName"),
            TextColor = Color.GrayA0
        };
        lName.Style.MarginBottom = 4;
        Root.AddChild(lName);

        IWorldStorageSource worldStorage = saveLoader;
        WorldProperties? worldProperties = worldStorage.GetProperties(_worldFolderName);
        string currentWorldName = worldProperties?.LevelName ?? string.Empty;

        _txfName = new TextField
        {
            Text = currentWorldName
        };
        _txfName.Style.MarginBottom = 20;
        Root.AddChild(_txfName);

        Panel buttonPanel = new();
        buttonPanel.Style.FlexDirection = FlexDirection.Row;

        Button btnRename = CreateButton();
        btnRename.Text = Translations.Get("gui.rename");
        btnRename.Style.Width = 100;
        btnRename.Style.SetMargin(2);
        btnRename.OnClick += e =>
        {
            if (_txfName.Text.Trim().Length > 0)
            {
                worldStorage.Rename(_worldFolderName, _txfName.Text.Trim());
                Context.Navigator.Navigate(parent);
            }
        };
        buttonPanel.AddChild(btnRename);

        Button btnCancel = CreateButton();
        btnCancel.Text = Translations.Get("gui.cancel");
        btnCancel.Style.Width = 100;
        btnCancel.Style.SetMargin(2);
        btnCancel.OnClick += e => Context.Navigator.Navigate(parent);
        buttonPanel.AddChild(btnCancel);

        Root.AddChild(buttonPanel);
    }
}
