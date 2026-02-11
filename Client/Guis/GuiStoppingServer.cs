using betareborn.Client.Resource.Language;

namespace betareborn.Client.Guis
{
    public class GuiStoppingServer : GuiScreen
    {
        private int updateCounter = 0;

        public override void initGui()
        {
            controlList.clear();
        }

        public override void updateScreen()
        {
            updateCounter++;
            if (mc.internalServer != null)
            {
                if (updateCounter == 1)
                {
                    mc.internalServer.stop();
                }

                if (mc.internalServer.stopped)
                {
                    mc.internalServer = null;
                    mc.displayGuiScreen(new GuiMainMenu());
                }
            }
            else
            {
                mc.displayGuiScreen(new GuiMainMenu());
            }
        }

        public override void render(int var1, int var2, float var3)
        {
            drawDefaultBackground();
            TranslationStorage var4 = TranslationStorage.getInstance();
            drawCenteredString(fontRenderer, "Saving level..", width / 2, height / 2 - 50, 16777215);
            drawCenteredString(fontRenderer, "Stopping internal server", width / 2, height / 2 - 10, 16777215);
            base.render(var1, var2, var3);
        }
    }
}
