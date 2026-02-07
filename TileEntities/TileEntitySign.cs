using betareborn.NBT;
using betareborn.Packets;
using java.lang;

namespace betareborn.TileEntities
{
    public class TileEntitySign : TileEntity
    {
        public static readonly new Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(TileEntitySign).TypeHandle);
        public string[] texts = ["", "", "", ""];
        public int currentRow = -1;
        private bool editable = true;

        public override void writeNbt(NBTTagCompound nbt)
        {
            base.writeNbt(nbt);
            nbt.setString("Text1", texts[0]);
            nbt.setString("Text2", texts[1]);
            nbt.setString("Text3", texts[2]);
            nbt.setString("Text4", texts[3]);
        }

        public override void readNbt(NBTTagCompound nbt)
        {
            editable = false;
            base.readNbt(nbt);

            for (int var2 = 0; var2 < 4; ++var2)
            {
                texts[var2] = nbt.getString("Text" + (var2 + 1));
                if (texts[var2].Length > 15)
                {
                    texts[var2] = texts[var2].Substring(0, 15);
                }
            }

        }

        public override Packet createUpdatePacket()
        {
            string[] var1 = new string[4];

            for (int var2 = 0; var2 < 4; var2++)
            {
                var1[var2] = texts[var2];
            }

            return new UpdateSignPacket(x, y, z, var1);
        }

        public bool isEditable()
        {
            return editable;
        }

        public void setEditable(bool editable)
        {
            this.editable = editable;
        }
    }

}