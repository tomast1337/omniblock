using betareborn.Worlds.Storage;
using java.util;

namespace betareborn.Guis
{
    public class GuiWorldSlot : GuiSlot
    {
        readonly GuiSelectWorld parentWorldGui;


        public GuiWorldSlot(GuiSelectWorld var1) : base(var1.mc, var1.width, var1.height, 32, var1.height - 64, 36)
        {
            parentWorldGui = var1;
        }

        public override int getSize()
        {
            return GuiSelectWorld.getSize(parentWorldGui).size();
        }

        protected override void elementClicked(int var1, bool var2)
        {
            GuiSelectWorld.onElementSelected(parentWorldGui, var1);
            bool var3 = GuiSelectWorld.getSelectedWorld(parentWorldGui) >= 0 && GuiSelectWorld.getSelectedWorld(parentWorldGui) < getSize();
            GuiSelectWorld.getSelectButton(parentWorldGui).enabled = var3;
            GuiSelectWorld.getRenameButton(parentWorldGui).enabled = var3;
            GuiSelectWorld.getDeleteButton(parentWorldGui).enabled = var3;
            if (var2 && var3)
            {
                parentWorldGui.selectWorld(var1);
            }

        }

        protected override bool isSelected(int var1)
        {
            return var1 == GuiSelectWorld.getSelectedWorld(parentWorldGui);
        }

        protected override int getContentHeight()
        {
            return GuiSelectWorld.getSize(parentWorldGui).size() * 36;
        }

        protected override void drawBackground()
        {
            parentWorldGui.drawDefaultBackground();
        }

        protected override void drawSlot(int var1, int var2, int var3, int var4, Tessellator var5)
        {
            WorldSaveInfo var6 = (WorldSaveInfo)GuiSelectWorld.getSize(parentWorldGui).get(var1);
            String var7 = var6.getDisplayName();
            if (var7 == null || MathHelper.stringNullOrLengthZero(var7))
            {
                var7 = GuiSelectWorld.func_22087_f(parentWorldGui) + " " + (var1 + 1);
            }

            String var8 = var6.getFileName();
            var8 = var8 + " (" + GuiSelectWorld.getDateFormatter(parentWorldGui).format(new Date(var6.getLastPlayed()));
            long var9 = var6.getSize();
            var8 = var8 + ", " + (float)(var9 / 1024L * 100L / 1024L) / 100.0F + " MB)";
            String var11 = "";
            if (var6.isSameVersion())
            {
                var11 = GuiSelectWorld.func_22088_h(parentWorldGui) + " " + var11;
            }

            parentWorldGui.drawString(parentWorldGui.fontRenderer, var7, var2 + 2, var3 + 1, 16777215);
            parentWorldGui.drawString(parentWorldGui.fontRenderer, var8, var2 + 2, var3 + 12, 8421504);
            parentWorldGui.drawString(parentWorldGui.fontRenderer, var11, var2 + 2, var3 + 12 + 10, 8421504);
        }
    }

}