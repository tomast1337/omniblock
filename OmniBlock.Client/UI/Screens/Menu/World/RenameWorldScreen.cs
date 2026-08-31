using OmniBlock.Client.UI.Controls;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Layout.Flexbox;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Storage;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Screens.Menu.World;

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
        Root.AutomationId = "world.rename.screen";
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
            AutomationId = "world.rename.name",
            Text = currentWorldName
        };
        _txfName.Style.MarginBottom = 20;
        Root.AddChild(_txfName);

        Panel buttonPanel = new();
        buttonPanel.Style.FlexDirection = FlexDirection.Row;

        Button btnRename = CreateButton();
        btnRename.AutomationId = "world.rename.submit";
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
        btnCancel.AutomationId = "world.rename.cancel";
        btnCancel.Text = Translations.Get("gui.cancel");
        btnCancel.Style.Width = 100;
        btnCancel.Style.SetMargin(2);
        btnCancel.OnClick += e => Context.Navigator.Navigate(parent);
        buttonPanel.AddChild(btnCancel);

        Root.AddChild(buttonPanel);
    }
}
