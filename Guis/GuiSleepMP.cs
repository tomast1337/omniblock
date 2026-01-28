using betareborn.Entities;
using betareborn.Packets;

namespace betareborn.Guis
{
    public class GuiSleepMP : GuiChat
    {

        public override void initGui()
        {
            Keyboard.enableRepeatEvents(true);
            StringTranslate var1 = StringTranslate.getInstance();
            controlList.add(new GuiButton(1, width / 2 - 100, height - 40, var1.translateKey("multiplayer.stopSleeping")));
        }

        public override void onGuiClosed()
        {
            Keyboard.enableRepeatEvents(false);
        }

        protected override void keyTyped(char eventChar, int eventKey)
        {
            if (eventKey == 1)
            {
                func_22115_j();
            }
            else if (eventKey == 28)
            {
                String var3 = message.Trim();
                if (var3.Length > 0)
                {
                    mc.thePlayer.sendChatMessage(message.Trim());
                }

                message = "";
            }
            else
            {
                base.keyTyped(eventChar, eventKey);
            }

        }

        public override void drawScreen(int var1, int var2, float var3)
        {
            base.drawScreen(var1, var2, var3);
        }

        protected override void actionPerformed(GuiButton var1)
        {
            if (var1.id == 1)
            {
                func_22115_j();
            }
            else
            {
                base.actionPerformed(var1);
            }

        }

        private void func_22115_j()
        {
            if (mc.thePlayer is EntityClientPlayerMP)
            {
                NetClientHandler var1 = ((EntityClientPlayerMP)mc.thePlayer).sendQueue;
                var1.addToSendQueue(new Packet19EntityAction(mc.thePlayer, 3));
            }

        }
    }

}