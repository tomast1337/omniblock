using betareborn.Client.Resource.Language;

namespace betareborn.Client.Guis
{
    public class GuiControls : GuiScreen
    {

        private GuiScreen parentScreen;
        protected string screenTitle = "Controls";
        private GameOptions options;
        private int buttonId = -1;

        public GuiControls(GuiScreen var1, GameOptions var2)
        {
            parentScreen = var1;
            options = var2;
        }

        private int func_20080_j()
        {
            return width / 2 - 155;
        }

        public override void initGui()
        {
            TranslationStorage var1 = TranslationStorage.getInstance();
            int var2 = func_20080_j();

            for (int var3 = 0; var3 < options.keyBindings.Length; ++var3)
            {
                controlList.add(new GuiSmallButton(var3, var2 + var3 % 2 * 160, height / 6 + 24 * (var3 >> 1), 70, 20, options.getOptionDisplayString(var3)));
            }

            controlList.add(new GuiButton(200, width / 2 - 100, height / 6 + 168, var1.translateKey("gui.done")));
            screenTitle = var1.translateKey("controls.title");
        }

        protected override void actionPerformed(GuiButton var1)
        {
            for (int var2 = 0; var2 < options.keyBindings.Length; ++var2)
            {
                ((GuiButton)controlList.get(var2)).displayString = options.getOptionDisplayString(var2);
            }

            if (var1.id == 200)
            {
                mc.displayGuiScreen(parentScreen);
            }
            else
            {
                buttonId = var1.id;
                var1.displayString = "> " + options.getOptionDisplayString(var1.id) + " <";
            }

        }

        protected override void keyTyped(char eventChar, int eventKey)
        {
            if (buttonId >= 0)
            {
                options.setKeyBinding(buttonId, eventKey);
                ((GuiButton)controlList.get(buttonId)).displayString = options.getOptionDisplayString(buttonId);
                buttonId = -1;
            }
            else
            {
                base.keyTyped(eventChar, eventKey);
            }

        }

        public override void render(int var1, int var2, float var3)
        {
            drawDefaultBackground();
            drawCenteredString(fontRenderer, screenTitle, width / 2, 20, 16777215);
            int var4 = func_20080_j();

            for (int var5 = 0; var5 < options.keyBindings.Length; ++var5)
            {
                drawString(fontRenderer, options.getKeyBindingDescription(var5), var4 + var5 % 2 * 160 + 70 + 6, height / 6 + 24 * (var5 >> 1) + 7, -1);
            }

            base.render(var1, var2, var3);
        }
    }
}