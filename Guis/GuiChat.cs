namespace betareborn.Guis {
    public class GuiChat : GuiScreen {

        protected String message = "";
        private int updateCounter = 0;
        private static readonly String allowedChars = ChatAllowedCharacters.allowedCharacters;

        public override void initGui() {
            Keyboard.enableRepeatEvents(true);
        }

        public override void onGuiClosed() {
            Keyboard.enableRepeatEvents(false);
        }

        public override void updateScreen() {
            ++updateCounter;
        }

        protected override void keyTyped(char eventChar, int eventKey) {
            switch (eventKey) {
                // Escape key
                case Keyboard.KEY_ESCAPE:
                    mc.displayGuiScreen((GuiScreen)null);
                    break;
                // Enter key
                case Keyboard.KEY_RETURN: {
                    String msg = message.Trim();
                    if (msg.Length > 0) {
                        if (!mc.lineIsCommand(msg)) {
                            mc.thePlayer.sendChatMessage(msg);
                        }
                    }

                    mc.displayGuiScreen((GuiScreen)null);
                    break;
                }
                // Backspace
                case Keyboard.KEY_BACK: {
                    if (message.Length > 0) {
                        message = message.Substring(0, message.Length - 1);
                    }

                    break;
                }
                case Keyboard.KEY_NONE: {
                    break;
                }
                // All other keys
                default: {
                    if (allowedChars.Contains(eventChar) && message.Length < 100) {
                        message += eventChar;
                    }

                    break;
                }
            }
        }

        public override void drawScreen(int var1, int var2, float var3) {
            drawRect(2, height - 14, width - 2, height - 2, java.lang.Integer.MIN_VALUE);
            drawString(fontRenderer, "> " + message + (updateCounter / 6 % 2 == 0 ? "_" : ""), 4, height - 12,
                14737632);
            base.drawScreen(var1, var2, var3);
        }

        protected override void mouseClicked(int var1, int var2, int var3) {
            if (var3 == 0) {
                if (mc.ingameGUI.field_933_a != null) {
                    if (message.Length > 0 && !message.EndsWith(" ")) {
                        message = message + " ";
                    }

                    message = message + mc.ingameGUI.field_933_a;
                    byte var4 = 100;
                    if (message.Length > var4) {
                        message = message.Substring(0, var4);
                    }
                }
                else {
                    base.mouseClicked(var1, var2, var3);
                }
            }
        }
    }

}