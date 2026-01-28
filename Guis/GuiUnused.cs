namespace betareborn.Guis
{
    public class GuiUnused : GuiScreen
    {

        private String message1;
        private String message2;

        public override void initGui()
        {
        }

        public override void drawScreen(int var1, int var2, float var3)
        {
            drawGradientRect(0, 0, width, height, -12574688, -11530224);
            drawCenteredString(fontRenderer, message1, width / 2, 90, 16777215);
            drawCenteredString(fontRenderer, message2, width / 2, 110, 16777215);
            base.drawScreen(var1, var2, var3);
        }

        protected override void keyTyped(char eventChar, int eventKey)
        {
        }
    }

}