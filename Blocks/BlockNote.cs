using betareborn.Entities;
using betareborn.Worlds;
using betareborn.Blocks.BlockEntities;
using betareborn.Blocks.Materials;

namespace betareborn.Blocks
{
    public class BlockNote : BlockWithEntity
    {
        public BlockNote(int id) : base(id, 74, Material.WOOD)
        {
        }

        public override int getTexture(int side)
        {
            return textureId;
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            if (id > 0 && Block.BLOCKS[id].canEmitRedstonePower())
            {
                bool var6 = world.isBlockGettingPowered(x, y, z);
                BlockEntityNote var7 = (BlockEntityNote)world.getBlockEntity(x, y, z);
                if (var7.powered != var6)
                {
                    if (var6)
                    {
                        var7.playNote(world, x, y, z);
                    }

                    var7.powered = var6;
                }
            }

        }

        public override bool onUse(World world, int x, int y, int z, EntityPlayer player)
        {
            if (world.isRemote)
            {
                return true;
            }
            else
            {
                BlockEntityNote var6 = (BlockEntityNote)world.getBlockEntity(x, y, z);
                var6.cycleNote();
                var6.playNote(world, x, y, z);
                return true;
            }
        }

        public override void onBlockBreakStart(World world, int x, int y, int z, EntityPlayer player)
        {
            if (!world.isRemote)
            {
                BlockEntityNote var6 = (BlockEntityNote)world.getBlockEntity(x, y, z);
                var6.playNote(world, x, y, z);
            }
        }

        protected override BlockEntity getBlockEntity()
        {
            return new BlockEntityNote();
        }

        public override void onBlockAction(World world, int x, int y, int z, int data1, int data2)
        {
            float var7 = (float)java.lang.Math.pow(2.0D, (double)(data2 - 12) / 12.0D);
            string var8 = "harp";
            if (data1 == 1)
            {
                var8 = "bd";
            }

            if (data1 == 2)
            {
                var8 = "snare";
            }

            if (data1 == 3)
            {
                var8 = "hat";
            }

            if (data1 == 4)
            {
                var8 = "bassattack";
            }

            world.playSound((double)x + 0.5D, (double)y + 0.5D, (double)z + 0.5D, "note." + var8, 3.0F, var7);
            world.addParticle("note", (double)x + 0.5D, (double)y + 1.2D, (double)z + 0.5D, (double)data2 / 24.0D, 0.0D, 0.0D);
        }
    }

}