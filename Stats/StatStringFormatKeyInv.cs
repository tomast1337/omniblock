using betareborn.Stats.Achievements;

namespace betareborn.Stats
{

    public class StatStringFormatKeyInv : AchievementStatFormatter
    {
        readonly Minecraft theGame;
        private static readonly StringTranslate localizedName = StringTranslate.getInstance();


        public StatStringFormatKeyInv(Minecraft game)
        {
            theGame = game;
        }

        public String formatString(String key)
        {
            return localizedName.translateKeyFormat(key, Keyboard.getKeyName(theGame.gameSettings.keyBindInventory.keyCode));
        }
    }

}