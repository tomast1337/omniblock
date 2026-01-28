using System.Text;

namespace betareborn.Guis {
    public class GuiChat : GuiScreen {
        StringBuilder _message = new();
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
            if (eventKey == 1) {
                mc.displayGuiScreen((GuiScreen)null);
            }
            else if (eventKey == Keyboard.KEY_RETURN) {
                // Old Logic
                // String var3 = message.Trim();
                // if (var3.Length > 0) {
                //     String var4 = message.Trim();
                //     if (!mc.lineIsCommand(var4)) {
                //         mc.thePlayer.sendChatMessage(var4);
                //     }
                // }

                var msg = _message.ToString().Trim();
                if (msg.Length > 0 && !mc.lineIsCommand(msg)) {
                    if (!mc.lineIsCommand(msg)) {
                        mc.thePlayer.sendChatMessage(msg);
                    }
                }

                mc.displayGuiScreen((GuiScreen)null);
            }
            else {
                if (eventKey == Keyboard.KEY_BACK && _message.Length > 0) {
                    _message.Remove(message.Length - 1, 1);
                    // message = message.Substring(0, message.Length - 1);
                }

                if (allowedChars.IndexOf(eventChar) >= 0 && _message.Length < 100) {
                    // message += eventChar;
                    _message.Append(eventChar);
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
                    // if (message.Length > 0 && !message.EndsWith(" ")) {
                    //      message = message + " ";
                    // }

                    if (_message.Length > 0 && !message.EndsWith(" ")) {
                        _message.Append(" ");
                    }

                    // message = message + mc.ingameGUI.field_933_a;
                    _message.Append(mc.ingameGUI.field_933_a);

                    _message = new StringBuilder(_message.ToString(0, 100));
                    // byte var4 = 100;
                    // if (message.Length > var4) {
                    //     // message = message.Substring(0, var4);
                    // }
                }
                else {
                    base.mouseClicked(var1, var2, var3);
                }
            }
        }
    }

}