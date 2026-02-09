using betareborn.Client.Rendering.Core;
using betareborn.Stats;

namespace betareborn.Client.Guis
{
    public class GuiSlotStatsGeneral : GuiSlot
    {
        readonly GuiStats field_27276_a;


        public GuiSlotStatsGeneral(GuiStats var1) : base(GuiStats.func_27141_a(var1), var1.width, var1.height, 32, var1.height - 64, 10)
        {
            field_27276_a = var1;
            func_27258_a(false);
        }

        public override int getSize()
        {
            return Stats.Stats.GENERAL_STATS.size();
        }

        protected override void elementClicked(int var1, bool var2)
        {
        }

        protected override bool isSelected(int var1)
        {
            return false;
        }

        protected override int getContentHeight()
        {
            return getSize() * 10;
        }

        protected override void drawBackground()
        {
            field_27276_a.drawDefaultBackground();
        }

        protected override void drawSlot(int var1, int var2, int var3, int var4, Tessellator var5)
        {
            StatBase var6 = (StatBase)Stats.Stats.GENERAL_STATS.get(var1);
            field_27276_a.drawString(GuiStats.func_27145_b(field_27276_a), var6.statName, var2 + 2, var3 + 1, var1 % 2 == 0 ? 16777215 : 9474192);
            string var7 = var6.format(GuiStats.func_27142_c(field_27276_a).writeStat(var6));
            field_27276_a.drawString(GuiStats.func_27140_d(field_27276_a), var7, var2 + 2 + 213 - GuiStats.func_27146_e(field_27276_a).getStringWidth(var7), var3 + 1, var1 % 2 == 0 ? 16777215 : 9474192);
        }
    }

}