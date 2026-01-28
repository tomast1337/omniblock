using betareborn.Threading;
using betareborn.Worlds;

namespace betareborn.Guis
{
    public class GuiConnecting : GuiScreen
    {

        private NetClientHandler clientHandler;
        private bool cancelled = false;

        public GuiConnecting(Minecraft var1, String var2, int var3)
        {
            java.lang.System.@out.println("Connecting to " + var2 + ", " + var3);
            var1.changeWorld1((World)null);
            (new ThreadConnectToServer(this, var1, var2, var3)).start();
        }

        public override void updateScreen()
        {
            if (clientHandler != null)
            {
                clientHandler.processReadPackets();
            }

        }

        protected override void keyTyped(char eventChar, int eventKey)
        {
        }

        public override void initGui()
        {
            StringTranslate var1 = StringTranslate.getInstance();
            controlList.clear();
            controlList.add(new GuiButton(0, width / 2 - 100, height / 4 + 120 + 12, var1.translateKey("gui.cancel")));
        }

        protected override void actionPerformed(GuiButton var1)
        {
            if (var1.id == 0)
            {
                cancelled = true;
                if (clientHandler != null)
                {
                    clientHandler.disconnect();
                }

                mc.displayGuiScreen(new GuiMainMenu());
            }

        }

        public override void drawScreen(int var1, int var2, float var3)
        {
            drawDefaultBackground();
            StringTranslate var4 = StringTranslate.getInstance();
            if (clientHandler == null)
            {
                drawCenteredString(fontRenderer, var4.translateKey("connect.connecting"), width / 2, height / 2 - 50, 16777215);
                drawCenteredString(fontRenderer, "", width / 2, height / 2 - 10, 16777215);
            }
            else
            {
                drawCenteredString(fontRenderer, var4.translateKey("connect.authorizing"), width / 2, height / 2 - 50, 16777215);
                drawCenteredString(fontRenderer, clientHandler.field_1209_a, width / 2, height / 2 - 10, 16777215);
            }

            base.drawScreen(var1, var2, var3);
        }

        public static NetClientHandler setNetClientHandler(GuiConnecting var0, NetClientHandler var1)
        {
            return var0.clientHandler = var1;
        }

        public static bool isCancelled(GuiConnecting var0)
        {
            return var0.cancelled;
        }

        public static NetClientHandler getNetClientHandler(GuiConnecting var0)
        {
            return var0.clientHandler;
        }
    }

}