namespace betareborn.Client.Guis
{
    public class GuiYesNo : GuiScreen
    {

        private GuiScreen parentScreen;
        private string message1;
        private string message2;
        private string field_22106_k;
        private string field_22105_l;
        private int worldNumber;

        public GuiYesNo(GuiScreen var1, string var2, string var3, string var4, string var5, int var6)
        {
            parentScreen = var1;
            message1 = var2;
            message2 = var3;
            field_22106_k = var4;
            field_22105_l = var5;
            worldNumber = var6;
        }

        public override void initGui()
        {
            controlList.add(new GuiSmallButton(0, width / 2 - 155 + 0, height / 6 + 96, field_22106_k));
            controlList.add(new GuiSmallButton(1, width / 2 - 155 + 160, height / 6 + 96, field_22105_l));
        }

        protected override void actionPerformed(GuiButton var1)
        {
            parentScreen.deleteWorld(var1.id == 0, worldNumber);
        }

        public override void render(int var1, int var2, float var3)
        {
            drawDefaultBackground();
            drawCenteredString(fontRenderer, message1, width / 2, 70, 16777215);
            drawCenteredString(fontRenderer, message2, width / 2, 90, 16777215);
            base.render(var1, var2, var3);
        }
    }

}