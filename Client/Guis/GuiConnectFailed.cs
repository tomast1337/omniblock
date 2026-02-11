using betareborn.Client.Resource.Language;

namespace betareborn.Client.Guis
{
    public class GuiConnectFailed : GuiScreen
    {

        private string errorMessage;
        private string errorDetail;

        public GuiConnectFailed(string var1, string var2, params object[] var3)
        {
            TranslationStorage var4 = TranslationStorage.getInstance();
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
            mc.stopInternalServer();
            TranslationStorage var1 = TranslationStorage.getInstance();
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

        public override void render(int var1, int var2, float var3)
        {
            drawDefaultBackground();
            drawCenteredString(fontRenderer, errorMessage, width / 2, height / 2 - 50, 16777215);
            drawCenteredString(fontRenderer, errorDetail, width / 2, height / 2 - 10, 16777215);
            base.render(var1, var2, var3);
        }
    }

}