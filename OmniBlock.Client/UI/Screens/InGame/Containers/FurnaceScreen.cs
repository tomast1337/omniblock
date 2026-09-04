using OmniBlock.Blocks.Entities;
using OmniBlock.Client.Entities;
using OmniBlock.Client.Input;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Layout.Flexbox;
using OmniBlock.Client.UI.Rendering;
using OmniBlock.Inventories;
using OmniBlock.Screens;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Screens.InGame.Containers;

public class FurnaceScreen(
    UIContext context,
    ClientPlayerEntity playerEntity,
    PlayerController playerController,
    InventoryPlayer playerInventory,
    BlockEntityFurnace furnace) :
    ContainerScreen(
        context,
        playerEntity,
        playerController,
        new FurnaceScreenHandler(playerInventory, furnace))
{
    protected override void Init()
    {
        base.Init();

        // Background Image
        Image background = new()
        {
            Texture = Renderer.TextureManager.GetTextureId("/gui/furnace.png"),
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
        Label lblFurnace = new()
        {
            Text = Translations.Get("gui.container.furnace"),
            HasShadow = false,
            TextColor = Color.Gray40
        };
        lblFurnace.Style.Position = PositionType.Absolute;
        lblFurnace.Style.Left = 60;
        lblFurnace.Style.Top = 6;
        _containerPanel.AddChild(lblFurnace);

        Label lblInventory = new()
        {
            Text = Translations.Get("gui.container.inventory"),
            HasShadow = false,
            TextColor = Color.Gray40
        };
        lblInventory.Style.Position = PositionType.Absolute;
        lblInventory.Style.Left = 8;
        lblInventory.Style.Top = _ySize - 96 + 2;
        _containerPanel.AddChild(lblInventory);

        // Progress Indicators
        // Burning Fire
        FurnaceFireProgress fireProgress = new(furnace);
        fireProgress.Style.Position = PositionType.Absolute;
        fireProgress.Style.Left = 56;
        fireProgress.Style.Top = 36;
        _containerPanel.AddChild(fireProgress);

        // Smelting Arrow
        FurnaceSmeltProgress smeltProgress = new(furnace);
        smeltProgress.Style.Position = PositionType.Absolute;
        smeltProgress.Style.Left = 79;
        smeltProgress.Style.Top = 34;
        _containerPanel.AddChild(smeltProgress);

        AddSlots();
    }
}

public class FurnaceFireProgress(BlockEntityFurnace furnace) : UIElement
{
    public override void Render(UIRenderer renderer)
    {
        if (furnace.IsBurning)
        {
            var progress = furnace.GetFuelTimeDelta(12);
            var texture = renderer.TextureManager.GetTextureId("/gui/furnace.png");
            renderer.DrawTexturedModalRect(texture, 0, 12 - progress, 176, 12 - progress, 14, progress + 2);
        }

        base.Render(renderer);
    }
}

public class FurnaceSmeltProgress(BlockEntityFurnace furnace) : UIElement
{
    public override void Render(UIRenderer renderer)
    {
        var progress = furnace.GetCookTimeDelta(24);
        var texture = renderer.TextureManager.GetTextureId("/gui/furnace.png");
        renderer.DrawTexturedModalRect(texture, 0, 0, 176, 14, progress + 1, 16);
        base.Render(renderer);
    }
}
