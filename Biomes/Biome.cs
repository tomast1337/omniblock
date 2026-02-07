using betareborn.Blocks;
using betareborn.Entities;
using betareborn.Worlds;
using java.awt;
using java.util;

namespace betareborn.Biomes
{
    public class Biome
    {
        public static readonly Biome rainforest = (new BiomeGenRainforest()).setColor(588342).setBiomeName("Rainforest").func_4124_a(2094168);
        public static readonly Biome swampland = (new BiomeGenSwamp()).setColor(522674).setBiomeName("Swampland").func_4124_a(9154376);
        public static readonly Biome seasonalForest = (new Biome()).setColor(10215459).setBiomeName("Seasonal Forest");
        public static readonly Biome forest = (new BiomeGenForest()).setColor(353825).setBiomeName("Forest").func_4124_a(5159473);
        public static readonly Biome savanna = (new BiomeGenDesert()).setColor(14278691).setBiomeName("Savanna");
        public static readonly Biome shrubland = (new Biome()).setColor(10595616).setBiomeName("Shrubland");
        public static readonly Biome taiga = (new BiomeGenTaiga()).setColor(3060051).setBiomeName("Taiga").setEnableSnow().func_4124_a(8107825);
        public static readonly Biome desert = (new BiomeGenDesert()).setColor(16421912).setBiomeName("Desert").setDisableRain();
        public static readonly Biome plains = (new BiomeGenDesert()).setColor(16767248).setBiomeName("Plains");
        public static readonly Biome iceDesert = (new BiomeGenDesert()).setColor(16772499).setBiomeName("Ice Desert").setEnableSnow().setDisableRain().func_4124_a(12899129);
        public static readonly Biome tundra = (new Biome()).setColor(5762041).setBiomeName("Tundra").setEnableSnow().func_4124_a(12899129);
        public static readonly Biome hell = (new BiomeGenHell()).setColor(16711680).setBiomeName("Hell").setDisableRain();
        public static readonly Biome sky = (new BiomeGenSky()).setColor(8421631).setBiomeName("Sky").setDisableRain();
        public String biomeName;
        public int color;
        public byte topBlock = (byte)Block.GRASS_BLOCK.id;
        public byte fillerBlock = (byte)Block.DIRT.id;
        public int field_6502_q = 5169201;
        protected java.util.List spawnableMonsterList = new ArrayList();
        protected java.util.List spawnableCreatureList = new ArrayList();
        protected java.util.List spawnableWaterCreatureList = new ArrayList();
        private bool enableSnow;
        private bool enableRain = true;
        private static Biome[] biomeLookupTable = new Biome[4096];

        protected Biome()
        {
            spawnableMonsterList.add(new SpawnListEntry(EntitySpider.Class, 10));
            spawnableMonsterList.add(new SpawnListEntry(EntityZombie.Class, 10));
            spawnableMonsterList.add(new SpawnListEntry(EntitySkeleton.Class, 10));
            spawnableMonsterList.add(new SpawnListEntry(EntityCreeper.Class, 10));
            spawnableMonsterList.add(new SpawnListEntry(EntitySlime.Class, 10));
            spawnableCreatureList.add(new SpawnListEntry(EntitySheep.Class, 12));
            spawnableCreatureList.add(new SpawnListEntry(EntityPig.Class, 10));
            spawnableCreatureList.add(new SpawnListEntry(EntityChicken.Class, 10));
            spawnableCreatureList.add(new SpawnListEntry(EntityCow.Class, 8));
            spawnableWaterCreatureList.add(new SpawnListEntry(EntitySquid.Class, 10));
        }

        private Biome setDisableRain()
        {
            enableRain = false;
            return this;
        }

        public static void generateBiomeLookup()
        {
            for (int var0 = 0; var0 < 64; ++var0)
            {
                for (int var1 = 0; var1 < 64; ++var1)
                {
                    biomeLookupTable[var0 + var1 * 64] = getBiome((float)var0 / 63.0F, (float)var1 / 63.0F);
                }
            }

            desert.topBlock = desert.fillerBlock = (byte)Block.SAND.id;
            iceDesert.topBlock = iceDesert.fillerBlock = (byte)Block.SAND.id;
        }

        public virtual WorldGenerator getRandomWorldGenForTrees(java.util.Random var1)
        {
            return (WorldGenerator)(var1.nextInt(10) == 0 ? new WorldGenBigTree() : new WorldGenTrees());
        }

        protected Biome setEnableSnow()
        {
            enableSnow = true;
            return this;
        }

        protected Biome setBiomeName(String var1)
        {
            biomeName = var1;
            return this;
        }

        protected Biome func_4124_a(int var1)
        {
            field_6502_q = var1;
            return this;
        }

        protected Biome setColor(int var1)
        {
            color = var1;
            return this;
        }

        public static Biome getBiome(double var0, double var2)
        {
            int var4 = (int)(var0 * 63.0D);
            int var5 = (int)(var2 * 63.0D);
            return biomeLookupTable[var4 + var5 * 64];
        }

        public static Biome getBiome(float var0, float var1)
        {
            var1 *= var0;
            return var0 < 0.1F ? tundra : (var1 < 0.2F ? (var0 < 0.5F ? tundra : (var0 < 0.95F ? savanna : desert)) : (var1 > 0.5F && var0 < 0.7F ? swampland : (var0 < 0.5F ? taiga : (var0 < 0.97F ? (var1 < 0.35F ? shrubland : forest) : (var1 < 0.45F ? plains : (var1 < 0.9F ? seasonalForest : rainforest))))));
        }

        public virtual int getSkyColorByTemp(float var1)
        {
            var1 /= 3.0F;
            if (var1 < -1.0F)
            {
                var1 = -1.0F;
            }

            if (var1 > 1.0F)
            {
                var1 = 1.0F;
            }

            return Color.getHSBColor(224.0F / 360.0F - var1 * 0.05F, 0.5F + var1 * 0.1F, 1.0F).getRGB();
        }

        public java.util.List getSpawnableList(EnumCreatureType var1)
        {
            return var1 == EnumCreatureType.monster ? spawnableMonsterList : (var1 == EnumCreatureType.creature ? spawnableCreatureList : (var1 == EnumCreatureType.waterCreature ? spawnableWaterCreatureList : null));
        }

        public bool getEnableSnow()
        {
            return enableSnow;
        }

        public bool canSpawnLightningBolt()
        {
            return enableSnow ? false : enableRain;
        }


        static Biome()
        {
            generateBiomeLookup();
        }
    }

}