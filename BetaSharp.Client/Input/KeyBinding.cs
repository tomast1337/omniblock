namespace BetaSharp.Client.Input;

public class KeyBinding
{
    public string KeyDescription { get; }
    public int ScanCode { get; set; }
    public int DefaultLogicalKey { get; }

    public KeyBinding(string desc, int logicalDefault)
    {
        KeyDescription = desc;
        ScanCode = DefaultLogicalKey = logicalDefault;
    }

    public bool IsBound => ScanCode != Keyboard.KEY_NONE;
    public bool IsDefault => ScanCode == DefaultLogicalKey;
}
