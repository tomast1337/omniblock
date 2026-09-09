using OmniBlock.Client.Entities;
using OmniBlock.Client.Input;
using OmniBlock.Client.UI.Controls;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Layout.Flexbox;
using OmniBlock.Screens;
using Silk.NET.GLFW;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Screens.InGame.Containers;

public abstract class ContainerScreen(
    UIContext context,
    ClientPlayerEntity player,
    PlayerController playerController,
    ScreenHandler inventorySlots) : UIScreen(context)
{
    protected Panel _containerPanel = null!;
    protected int _xSize = 176;
    protected int _ySize = 166;
    public ScreenHandler InventorySlots { get; } = inventorySlots;

    public override bool PausesGame => false;

    protected override void Init()
    {
        player.CurrentScreenHandler = InventorySlots;

        Root.Style.AlignItems = Align.Center;
        Root.Style.JustifyContent = Justify.Center;

        Background background = new(BackgroundType.World);
        background.OnMouseDown += e => OnSlotClick(null, e.Button);
        Root.AddChild(background);

        _containerPanel = new Panel();
        _containerPanel.Style.Width = _xSize;
        _containerPanel.Style.Height = _ySize;
        _containerPanel.Style.Position = PositionType.Relative;
        Root.AddChild(_containerPanel);
    }

    protected void AddSlots()
    {
        foreach (var slot in InventorySlots.Slots)
        {
            UISlot uiSlot = new(slot);
            uiSlot.Style.Position = PositionType.Absolute;
            uiSlot.Style.Left = slot.xDisplayPosition;
            uiSlot.Style.Top = slot.yDisplayPosition;
            uiSlot.OnMouseDown += e => OnSlotClick(uiSlot, e.Button);
            _containerPanel.AddChild(uiSlot);
        }
    }

    private void OnSlotClick(UISlot? uiSlot, MouseButton button)
    {
        var slotId = uiSlot == null ? ScreenHandler.NullSlot : uiSlot.Slot.id;
        var isShiftClick = Keyboard.isKeyDown(Keyboard.KEY_LSHIFT) || Keyboard.isKeyDown(Keyboard.KEY_RSHIFT);
        var mouseBtn = button == MouseButton.Right ? 1 : 0;

        playerController.OnSlotClick(InventorySlots.SyncId, slotId, mouseBtn, isShiftClick, player);
    }

    public override void Update(float partialTicks)
    {
        base.Update(partialTicks);
        if (!player.IsAlive || player.Dead)
        {
            player.CloseHandledScreen();
        }
    }

    public override void Render(int mouseX, int mouseY, float partialTicks)
    {
        base.Render(mouseX, mouseY, partialTicks);

        // Render held item on top of everything
        var cursorStack = player.Inventory.GetCursorStack();
        if (cursorStack != null)
        {
            Renderer.Begin();
            Renderer.ClearDepth();
            Renderer.DrawItem(cursorStack, mouseX - 8, mouseY - 8);
            Renderer.DrawItemOverlay(cursorStack, mouseX - 8, mouseY - 8);
            Renderer.End();
        }

        // Tooltip rendering
        if (Root.HitTest(MouseX, MouseY) is UISlot hoveredSlot && cursorStack == null)
        {
            var stack = hoveredSlot.Slot.getStack();
            if (stack != null)
            {
                var itemName = stack.GetDisplayName().Trim();
                if (itemName.Length > 0)
                {
                    var textWidth = Context.TextRenderer.GetStringWidth(itemName);
                    var tx = MouseX + 12;
                    var ty = MouseY - 12;

                    Renderer.Begin();
                    Renderer.DrawGradientRect(tx - 3, ty - 3, textWidth + 6, 14, Color.BlackAlphaC0, Color.BlackAlphaC0);
                    Renderer.DrawText(itemName, tx, ty, Color.White);
                    Renderer.End();
                }
            }
        }
    }

    public override void GetTooltips(List<ActionTip> tips)
    {
        var cursorStack = player.Inventory.GetCursorStack();

        if (Root.HitTest(MouseX, MouseY) is UISlot hoveredSlot)
        {
            var slotStack = hoveredSlot.Slot.getStack();

            if (cursorStack == null && slotStack != null)
            {
                tips.Add(new ActionTip(ControlIcon.A, "Move"));
                tips.Add(new ActionTip(ControlIcon.Y, "Quick Move"));
                if (slotStack.Count > 1)
                {
                    tips.Add(new ActionTip(ControlIcon.X, "Take Half"));
                }
            }
            else if (cursorStack != null)
            {
                tips.Add(new ActionTip(ControlIcon.A, "Place"));
                tips.Add(new ActionTip(ControlIcon.X, "Place One"));
            }
        }
    }

    public override void HandleControllerInput()
    {
        var button = (GamepadButton)Controller.GetEventButton();
        var isDown = Controller.GetEventButtonState();

        if (isDown && (button == GamepadButton.X || button == GamepadButton.Y))
        {
            if (GetElementUnderVirtualCursor() is UISlot uiSlot)
            {
                var slotId = uiSlot.Slot.id;
                if (button == GamepadButton.Y)
                {
                    playerController.OnSlotClick(InventorySlots.SyncId, slotId, 0, true, player);
                }
                else
                {
                    playerController.OnSlotClick(InventorySlots.SyncId, slotId, 1, false, player);
                }

                return;
            }
        }

        base.HandleControllerInput();
    }

    public override void KeyTyped(int key, char character)
    {
        if (key == Keyboard.KEY_ESCAPE || key == Context.Options.KeyBindInventory.ScanCode)
        {
            player.CloseHandledScreen();
        }
        else
        {
            base.KeyTyped(key, character);
        }
    }

    public override void Uninit()
    {
        base.Uninit();
        if (player != null)
        {
            playerController.OnGuiClosed(InventorySlots.SyncId, player);
        }
    }
}
