namespace betareborn.Guis
{
    public class GuiSmallButton : GuiButton
    {

        private readonly EnumOptions enumOptions;

        public GuiSmallButton(int x, int y, int width, String var4) : this(x, y, width, null, var4)
        {
        }

        public GuiSmallButton(int x, int y, int width, int wid, int hei, String displayStr) : base(x, y, width, wid, hei, displayStr)
        {
            enumOptions = null;
        }

        public GuiSmallButton(int x, int y, int width, EnumOptions var4, String var5) : base(x, y, width, 150, 20, var5)
        {
            enumOptions = var4;
        }

        public EnumOptions returnEnumOptions()
        {
            return enumOptions;
        }
    }

}