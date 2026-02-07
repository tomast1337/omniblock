using betareborn.Entities;

namespace betareborn.Biomes
{
    public class BiomeGenSky : Biome
    {

        public BiomeGenSky()
        {
            spawnableMonsterList.clear();
            spawnableCreatureList.clear();
            spawnableWaterCreatureList.clear();
            spawnableCreatureList.add(new SpawnListEntry(EntityChicken.Class, 10));
        }

        public override int getSkyColorByTemp(float var1)
        {
            return 12632319;
        }
    }

}