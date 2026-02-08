using betareborn.Client.Resource.Language;

namespace betareborn.Client.Guis
{
    public class GuiOptions : GuiScreen
    {

        private GuiScreen parentScreen;
        protected string screenTitle = "Options";
        private GameOptions options;
        private static EnumOptions[] field_22135_k = new EnumOptions[] { EnumOptions.MUSIC, EnumOptions.SOUND, EnumOptions.INVERT_MOUSE, EnumOptions.SENSITIVITY, EnumOptions.DIFFICULTY };

        public GuiOptions(GuiScreen var1, GameOptions var2)
        {
            parentScreen = var1;
            options = var2;
        }

        public override void initGui()
        {
            TranslationStorage var1 = TranslationStorage.getInstance();
            screenTitle = var1.translateKey("options.title");
            int var2 = 0;
            EnumOptions[] var3 = field_22135_k;
            int var4 = var3.Length;

            for (int var5 = 0; var5 < var4; ++var5)
            {
                EnumOptions var6 = var3[var5];
                if (!var6.getEnumFloat())
                {
                    controlList.add(new GuiSmallButton(var6.returnEnumOrdinal(), width / 2 - 155 + var2 % 2 * 160, height / 6 + 24 * (var2 >> 1), var6, options.getKeyBinding(var6)));
                }
                else
                {
                    controlList.add(new GuiSlider(var6.returnEnumOrdinal(), width / 2 - 155 + var2 % 2 * 160, height / 6 + 24 * (var2 >> 1), var6, options.getKeyBinding(var6), options.getOptionFloatValue(var6)));
                }

                ++var2;
            }

            controlList.add(new GuiButton(101, width / 2 - 100, height / 6 + 96 + 12, var1.translateKey("options.video")));
            controlList.add(new GuiButton(100, width / 2 - 100, height / 6 + 120 + 12, var1.translateKey("options.controls")));
            controlList.add(new GuiButton(200, width / 2 - 100, height / 6 + 168, var1.translateKey("gui.done")));
        }

        protected override void actionPerformed(GuiButton var1)
        {
            if (var1.enabled)
            {
                if (var1.id < 100 && var1 is GuiSmallButton)
                {
                    options.setOptionValue(((GuiSmallButton)var1).returnEnumOptions(), 1);
                    var1.displayString = options.getKeyBinding(EnumOptions.getEnumOptions(var1.id));
                }

                if (var1.id == 101)
                {
                    mc.options.saveOptions();
                    mc.displayGuiScreen(new GuiVideoSettings(this, options));
                }

                if (var1.id == 100)
                {
                    mc.options.saveOptions();
                    mc.displayGuiScreen(new GuiControls(this, options));
                }

                if (var1.id == 200)
                {
                    mc.options.saveOptions();
                    mc.displayGuiScreen(parentScreen);
                }

            }
        }

        public override void render(int var1, int var2, float var3)
        {
            drawDefaultBackground();
            drawCenteredString(fontRenderer, screenTitle, width / 2, 20, 16777215);
            base.render(var1, var2, var3);
        }
    }

}