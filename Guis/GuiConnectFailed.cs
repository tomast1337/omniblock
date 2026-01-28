namespace betareborn.Guis
{
    public class GuiConnectFailed : GuiScreen
    {

        private String errorMessage;
        private String errorDetail;

        public GuiConnectFailed(String var1, String var2, params object[] var3)
        {
            StringTranslate var4 = StringTranslate.getInstance();
            errorMessage = var4.translateKey(var1);
            if (var3 != null)
            {
                errorDetail = var4.translateKeyFormat(var2, var3);
            }
            else
            {
                errorDetail = var4.translateKey(var2);
            }

        }

        public override void updateScreen()
        {
        }

        protected override void keyTyped(char eventChar, int eventKey)
        {
        }

        public override void initGui()
        {
            StringTranslate var1 = StringTranslate.getInstance();
            controlList.clear();
            controlList.add(new GuiButton(0, width / 2 - 100, height / 4 + 120 + 12, var1.translateKey("gui.toMenu")));
        }

        protected override void actionPerformed(GuiButton var1)
        {
            if (var1.id == 0)
            {
                mc.displayGuiScreen(new GuiMainMenu());
            }

        }

        public override void drawScreen(int var1, int var2, float var3)
        {
            drawDefaultBackground();
            drawCenteredString(fontRenderer, errorMessage, width / 2, height / 2 - 50, 16777215);
            drawCenteredString(fontRenderer, errorDetail, width / 2, height / 2 - 10, 16777215);
            base.drawScreen(var1, var2, var3);
        }
    }

}