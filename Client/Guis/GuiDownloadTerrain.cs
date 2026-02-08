using betareborn.Client.Network;
using betareborn.Client.Resource.Language;
using betareborn.Packets;

namespace betareborn.Client.Guis
{

    public class GuiDownloadTerrain : GuiScreen
    {

        private ClientNetworkHandler netHandler;
        private int updateCounter = 0;

        public GuiDownloadTerrain(ClientNetworkHandler var1)
        {
            netHandler = var1;
        }

        protected override void keyTyped(char eventChar, int eventKey)
        {
        }

        public override void initGui()
        {
            controlList.clear();
        }

        public override void updateScreen()
        {
            ++updateCounter;
            if (updateCounter % 20 == 0)
            {
                netHandler.addToSendQueue(new Packet0KeepAlive());
            }

            if (netHandler != null)
            {
                netHandler.tick();
            }

        }

        protected override void actionPerformed(GuiButton var1)
        {
        }

        public override void render(int var1, int var2, float var3)
        {
            drawBackground(0);
            TranslationStorage var4 = TranslationStorage.getInstance();
            drawCenteredString(fontRenderer, var4.translateKey("multiplayer.downloadingTerrain"), width / 2, height / 2 - 50, 16777215);
            base.render(var1, var2, var3);
        }
    }

}