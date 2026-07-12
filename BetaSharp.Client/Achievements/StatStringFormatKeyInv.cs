using BetaSharp.Client.Input;

namespace BetaSharp.Client.Achievements;

public class StatStringFormatKeyInv(BetaSharp game) : AchievementStatFormatter
{
    private readonly BetaSharp _game = game;

    public string formatString(string key)
    {
        return Translations.GetFormat(key, Keyboard.getKeyName(_game.Options.KeyBindInventory.ScanCode));
    }
}
