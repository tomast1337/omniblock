namespace betareborn.Guis
{
    public class GuiVideoSettings : GuiScreen
    {

        private GuiScreen field_22110_h;
        protected String field_22107_a = "Video Settings";
        private GameSettings guiGameSettings;
        private static EnumOptions[] field_22108_k = new EnumOptions[] { EnumOptions.RENDER_DISTANCE, EnumOptions.FRAMERATE_LIMIT, EnumOptions.VIEW_BOBBING, EnumOptions.GUI_SCALE, EnumOptions.ANISOTROPIC, EnumOptions.MIPMAPS, EnumOptions.DEBUG_MODE };

        public GuiVideoSettings(GuiScreen var1, GameSettings var2)
        {
            field_22110_h = var1;
            guiGameSettings = var2;
        }

        public override void initGui()
        {
            StringTranslate var1 = StringTranslate.getInstance();
            field_22107_a = var1.translateKey("options.videoTitle");
            int var2 = 0;
            EnumOptions[] var3 = field_22108_k;
            int var4 = var3.Length;

            for (int var5 = 0; var5 < var4; ++var5)
            {
                EnumOptions var6 = var3[var5];
                if (!var6.getEnumFloat())
                {
                    controlList.add(new GuiSmallButton(var6.returnEnumOrdinal(), width / 2 - 155 + var2 % 2 * 160, height / 6 + 24 * (var2 >> 1), var6, guiGameSettings.getKeyBinding(var6)));
                }
                else
                {
                    controlList.add(new GuiSlider(var6.returnEnumOrdinal(), width / 2 - 155 + var2 % 2 * 160, height / 6 + 24 * (var2 >> 1), var6, guiGameSettings.getKeyBinding(var6), guiGameSettings.getOptionFloatValue(var6)));
                }

                ++var2;
            }

            controlList.add(new GuiButton(200, width / 2 - 100, height / 6 + 168, var1.translateKey("gui.done")));
        }

        protected override void actionPerformed(GuiButton var1)
        {
            if (var1.enabled)
            {
                if (var1.id < 100 && var1 is GuiSmallButton)
                {
                    guiGameSettings.setOptionValue(((GuiSmallButton)var1).returnEnumOptions(), 1);
                    var1.displayString = guiGameSettings.getKeyBinding(EnumOptions.getEnumOptions(var1.id));
                }

                if (var1.id == 200)
                {
                    mc.gameSettings.saveOptions();
                    mc.displayGuiScreen(field_22110_h);
                }

                ScaledResolution var2 = new ScaledResolution(mc.gameSettings, mc.displayWidth, mc.displayHeight);
                int var3 = var2.getScaledWidth();
                int var4 = var2.getScaledHeight();
                setWorldAndResolution(mc, var3, var4);
            }
        }

        public override void drawScreen(int var1, int var2, float var3)
        {
            drawDefaultBackground();
            drawCenteredString(fontRenderer, field_22107_a, width / 2, 20, 16777215);
            base.drawScreen(var1, var2, var3);
        }
    }

}