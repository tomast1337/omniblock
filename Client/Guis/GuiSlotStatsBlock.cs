using betareborn.Client.Rendering.Core;
using betareborn.Stats;
using java.util;

namespace betareborn.Client.Guis
{
    public class GuiSlotStatsBlock : GuiSlotStats
    {
        public readonly GuiStats field_27274_a;


        public GuiSlotStatsBlock(GuiStats var1) : base(var1)
        {
            field_27274_a = var1;
            field_27273_c = new ArrayList();
            Iterator var2 = Stats.Stats.BLOCKS_MINED_STATS.iterator();

            while (var2.hasNext())
            {
                StatCrafting var3 = (StatCrafting)var2.next();
                bool var4 = false;
                int var5 = var3.func_25072_b();
                if (GuiStats.func_27142_c(var1).writeStat(var3) > 0)
                {
                    var4 = true;
                }
                else if (Stats.Stats.USED[var5] != null && GuiStats.func_27142_c(var1).writeStat(Stats.Stats.USED[var5]) > 0)
                {
                    var4 = true;
                }
                else if (Stats.Stats.CRAFTED[var5] != null && GuiStats.func_27142_c(var1).writeStat(Stats.Stats.CRAFTED[var5]) > 0)
                {
                    var4 = true;
                }

                if (var4)
                {
                    field_27273_c.add(var3);
                }
            }

            field_27272_d = new SorterStatsBlock(this, var1);
        }

        protected override void func_27260_a(int var1, int var2, Tessellator var3)
        {
            base.func_27260_a(var1, var2, var3);
            if (field_27268_b == 0)
            {
                GuiStats.func_27128_a(field_27274_a, var1 + 115 - 18 + 1, var2 + 1 + 1, 18, 18);
            }
            else
            {
                GuiStats.func_27128_a(field_27274_a, var1 + 115 - 18, var2 + 1, 18, 18);
            }

            if (field_27268_b == 1)
            {
                GuiStats.func_27128_a(field_27274_a, var1 + 165 - 18 + 1, var2 + 1 + 1, 36, 18);
            }
            else
            {
                GuiStats.func_27128_a(field_27274_a, var1 + 165 - 18, var2 + 1, 36, 18);
            }

            if (field_27268_b == 2)
            {
                GuiStats.func_27128_a(field_27274_a, var1 + 215 - 18 + 1, var2 + 1 + 1, 54, 18);
            }
            else
            {
                GuiStats.func_27128_a(field_27274_a, var1 + 215 - 18, var2 + 1, 54, 18);
            }

        }

        protected override void drawSlot(int var1, int var2, int var3, int var4, Tessellator var5)
        {
            StatCrafting var6 = func_27264_b(var1);
            int var7 = var6.func_25072_b();
            GuiStats.func_27148_a(field_27274_a, var2 + 40, var3, var7);
            func_27265_a((StatCrafting)Stats.Stats.CRAFTED[var7], var2 + 115, var3, var1 % 2 == 0);
            func_27265_a((StatCrafting)Stats.Stats.USED[var7], var2 + 165, var3, var1 % 2 == 0);
            func_27265_a(var6, var2 + 215, var3, var1 % 2 == 0);
        }

        protected override string func_27263_a(int var1)
        {
            return var1 == 0 ? "stat.crafted" : var1 == 1 ? "stat.used" : "stat.mined";
        }
    }

}