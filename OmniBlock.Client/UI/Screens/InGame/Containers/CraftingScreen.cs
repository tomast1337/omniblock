using OmniBlock.Client.Entities;
using OmniBlock.Client.Input;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Layout.Flexbox;
using OmniBlock.Inventories;
using OmniBlock.Screens;
using OmniBlock.Worlds.Core.Systems;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Screens.InGame.Containers;

public class CraftingScreen(
    UIContext context,
    ClientPlayerEntity playerEntity,
    PlayerController playerController,
    InventoryPlayer playerInventory,
    IWorldContext world,
    int posX,
    int posY,
    int posZ) :
    ContainerScreen(context, playerEntity, playerController, new CraftingScreenHandler(playerInventory, world, posX, posY, posZ))
{
    protected override void Init()
    {
        base.Init();

        // Background Image
        Image background = new()
        {
            Texture = Renderer.TextureManager.GetTextureId("/gui/crafting.png"),
            U = 0,
            V = 0,
            UWidth = 176,
            VHeight = 166
        };
        background.Style.Width = _xSize;
        background.Style.Height = _ySize;
        background.Style.Position = PositionType.Absolute;
        _containerPanel.AddChild(background);

        // Labels
        Label lblCrafting = new()
        {
            Text = Translations.Get("gui.container.crafting"),
            HasShadow = false
        };
        lblCrafting.TextColor = Color.Gray40;
        lblCrafting.Style.Position = PositionType.Absolute;
        lblCrafting.Style.Left = 28;
        lblCrafting.Style.Top = 6;
        _containerPanel.AddChild(lblCrafting);

        Label lblInventory = new()
        {
            Text = Translations.Get("gui.container.inventory"),
            HasShadow = false
        };
        lblInventory.TextColor = Color.Gray40;
        lblInventory.Style.Position = PositionType.Absolute;
        lblInventory.Style.Left = 8;
        lblInventory.Style.Top = _ySize - 96 + 2;
        _containerPanel.AddChild(lblInventory);

        AddSlots();
    }
}
