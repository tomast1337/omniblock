using OmniBlock.Client.Options;
using OmniBlock.Client.UI;
using Silk.NET.GLFW;

namespace OmniBlock.Client.Input;

public sealed class VirtualCursor
{
    private bool _wasDpadDownDown;

    private bool _wasDpadLeftDown;
    private bool _wasDpadRightDown;
    private bool _wasDpadUpDown;
    private float _x;
    private float _y;

    public float X => _x;
    public float Y => _y;

    public void Reset(int displayWidth, int displayHeight)
    {
        _x = displayWidth / 2.0f;
        _y = displayHeight / 2.0f;
    }

    public void Update(UIScreen? currentScreen, GameOptions options, int displayWidth, int displayHeight, float deltaTime)
    {
        var lx = Controller.LeftStickX;
        var ly = Controller.LeftStickY;

        var dpadLeft = Controller.IsButtonDown(GamepadButton.DPadLeft);
        var dpadRight = Controller.IsButtonDown(GamepadButton.DPadRight);
        var dpadUp = Controller.IsButtonDown(GamepadButton.DPadUp);
        var dpadDown = Controller.IsButtonDown(GamepadButton.DPadDown);

        if (currentScreen != null)
        {
            int dpadX = 0, dpadY = 0;
            if (dpadLeft && !_wasDpadLeftDown) dpadX = -1;
            if (dpadRight && !_wasDpadRightDown) dpadX = 1;
            if (dpadUp && !_wasDpadUpDown) dpadY = -1;
            if (dpadDown && !_wasDpadDownDown) dpadY = 1;

            if (dpadX != 0 || dpadY != 0)
                currentScreen.HandleDPadNavigation(dpadX, dpadY, ref _x, ref _y);
        }

        _wasDpadLeftDown = dpadLeft;
        _wasDpadRightDown = dpadRight;
        _wasDpadUpDown = dpadUp;
        _wasDpadDownDown = dpadDown;

        if (currentScreen?.IsEditingSlider == true) return;

        ScaledResolution sr = new(options, displayWidth, displayHeight);
        var speed = 200f * sr.ScaleFactor;

        _x = Math.Clamp(_x + lx * speed * deltaTime, 0, displayWidth);
        _y = Math.Clamp(_y + ly * speed * deltaTime, 0, displayHeight);
    }
}
