using Silk.NET.GLFW;

namespace OmniBlock.Client.Input;

public class ControllerListener
{
    private readonly bool[] _snapshot = new bool[15];
    private Action<GamepadButton>? _callback;

    public bool IsListening { get; private set; }

    public void StartListening(Action<GamepadButton> callback)
    {
        _callback = callback;
        IsListening = true;

        // Take initial snapshot to ignore buttons already held down
        for (var i = 0; i < _snapshot.Length; i++)
        {
            _snapshot[i] = Controller.IsButtonDown((GamepadButton)i);
        }
    }

    public void StopListening()
    {
        IsListening = false;
        _callback = null;
    }

    public void Update()
    {
        if (!IsListening) return;

        for (var i = 0; i < _snapshot.Length; i++)
        {
            var isDown = Controller.IsButtonDown((GamepadButton)i);

            // Check for new press
            if (isDown && !_snapshot[i])
            {
                var pressed = (GamepadButton)i;

                // Allow callback to handle or ignore specific buttons
                IsListening = false;
                _callback?.Invoke(pressed);
                _callback = null;
                return;
            }

            // Update snapshot for released buttons
            if (!isDown)
            {
                _snapshot[i] = false;
            }
        }
    }
}
