using betareborn.NBT;
using java.lang;

namespace betareborn.TileEntities
{
    public class TileEntitySign : TileEntity
    {
        public static readonly new Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(TileEntitySign).TypeHandle);
        public string[] signText = ["", "", "", ""];
        public int lineBeingEdited = -1;
        private bool field_25062_c = true;

        public override void writeNbt(NBTTagCompound var1)
        {
            base.writeNbt(var1);
            var1.setString("Text1", signText[0]);
            var1.setString("Text2", signText[1]);
            var1.setString("Text3", signText[2]);
            var1.setString("Text4", signText[3]);
        }

        public override void readNbt(NBTTagCompound var1)
        {
            field_25062_c = false;
            base.readNbt(var1);

            for (int var2 = 0; var2 < 4; ++var2)
            {
                signText[var2] = var1.getString("Text" + (var2 + 1));
                if (signText[var2].Length > 15)
                {
                    signText[var2] = signText[var2].Substring(0, 15);
                }
            }

        }
    }

}