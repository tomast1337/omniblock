using betareborn.NBT;
using betareborn.Worlds;
using java.lang;
using java.util;
using Exception = java.lang.Exception;

namespace betareborn.Entities {
    public class EntityList {
        private static Map stringToClassMapping = new HashMap();
        private static Map classToStringMapping = new HashMap();
        private static Map IDtoClassMapping = new HashMap();
        private static Map classToIDMapping = new HashMap();
        public static Dictionary<string, int> namesToId = new();

        private static void addMapping(Class var0, string var1, int var2) {
            stringToClassMapping.put(var1, var0);
            classToStringMapping.put(var0, var1);
            IDtoClassMapping.put(Integer.valueOf(var2), var0);
            classToIDMapping.put(var0, Integer.valueOf(var2));
            namesToId.TryAdd(var1.ToLower(), var2);
        }

        public static Entity createEntityInWorld(string var0, World var1) {
            Entity var2 = null;

            try {
                Class var3 = (Class)stringToClassMapping.get(var0);
                if (var3 != null) {
                    var2 = (Entity)var3.getConstructor([World.Class]).newInstance([
                        var1
                    ]);
                }
            }
            catch (java.lang.Exception var4) {
                var4.printStackTrace();
            }

            return var2;
        }

        public static Entity createEntityFromNBT(NBTTagCompound var0, World var1) {
            Entity var2 = null;

            try {
                Class var3 = (Class)stringToClassMapping.get(var0.getString("id"));
                if (var3 != null) {
                    var2 = (Entity)var3.getConstructor([World.Class]).newInstance([var1]);
                }
            }
            catch (java.lang.Exception var4) {
                var4.printStackTrace();
            }

            if (var2 != null) {
                var2.readFromNBT(var0);
            }
            else {
                java.lang.System.@out.println("Skipping Entity with id " + var0.getString("id"));
            }

            return var2;
        }

        public static Entity? createEntityAt(string name, World world, float x, float y, float z) {
            name = name.ToLower();
            try {
                if (namesToId.TryGetValue(name, out int id)) {
                    Class cls = (Class)IDtoClassMapping.get(Integer.valueOf(id));
                    if (cls != null) {
                        var ent = (Entity)cls.getConstructor(World.Class).newInstance(world);

                        if (ent != null) {
                            ent.setPosition(x, y, z);
                            ent.setPositionAndRotation(x, y, z, 0, 0);
                            if (!world.entityJoinedWorld(ent)) {
                                Console.Error.WriteLine($"Entity `{name}` with ID:`{id}` failed to join world.");
                            }
                        }

                        return ent;
                    }
                    else {
                        Console.Error.WriteLine($"Failed to convert entity of name `{name}` and id `{id}` to a class.");
                    }
                }
                else {
                    Console.Error.WriteLine($"Unable to find entity of name `{name}`.");
                }
            }
            catch (Exception ex) {
                Console.Error.WriteLine(ex);
            }

            return null;
        }

        public static Entity createEntity(int var0, World var1) {
            Entity var2 = null;

            try {
                Class var3 = (Class)IDtoClassMapping.get(Integer.valueOf(var0));
                if (var3 != null) {
                    var2 = (Entity)var3.getConstructor([World.Class]).newInstance([var1]);
                }
            }
            catch (java.lang.Exception var4) {
                var4.printStackTrace();
            }

            if (var2 == null) {
                java.lang.System.@out.println("Skipping Entity with id " + var0);
            }

            return var2;
        }

        public static int getEntityID(Entity var0) {
            return ((Integer)classToIDMapping.get(var0.getClass())).intValue();
        }

        public static string getEntityString(Entity var0) {
            return (string)classToStringMapping.get(var0.getClass());
        }

        static EntityList() {
            addMapping(EntityArrow.Class, "Arrow", 10);
            addMapping(EntitySnowball.Class, "Snowball", 11);
            addMapping(EntityItem.Class, "Item", 1);
            addMapping(EntityPainting.Class, "Painting", 9);
            addMapping(EntityLiving.Class, "Mob", 48);
            addMapping(EntityMob.Class, "Monster", 49);
            addMapping(EntityCreeper.Class, "Creeper", 50);
            addMapping(EntitySkeleton.Class, "Skeleton", 51);
            addMapping(EntitySpider.Class, "Spider", 52);
            addMapping(EntityGiantZombie.Class, "Giant", 53);
            addMapping(EntityZombie.Class, "Zombie", 54);
            addMapping(EntitySlime.Class, "Slime", 55);
            addMapping(EntityGhast.Class, "Ghast", 56);
            addMapping(EntityPigZombie.Class, "PigZombie", 57);
            addMapping(EntityPig.Class, "Pig", 90);
            addMapping(EntitySheep.Class, "Sheep", 91);
            addMapping(EntityCow.Class, "Cow", 92);
            addMapping(EntityChicken.Class, "Chicken", 93);
            addMapping(EntitySquid.Class, "Squid", 94);
            addMapping(EntityWolf.Class, "Wolf", 95);
            addMapping(EntityTNTPrimed.Class, "PrimedTnt", 20);
            addMapping(EntityFallingSand.Class, "FallingSand", 21);
            addMapping(EntityMinecart.Class, "Minecart", 40);
            addMapping(EntityBoat.Class, "Boat", 41);
        }
    }

}