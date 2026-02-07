namespace betareborn
{
    public class Box : java.lang.Object
    {
        private static readonly List<Box> cache = [];
        private static int cacheCount = 0;
        public double minX;
        public double minY;
        public double minZ;
        public double maxX;
        public double maxY;
        public double maxZ;

        public static Box create(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            return new Box(x1, y1, z1, x2, y2, z2);
        }

        public static void clearCache()
        {
            cache.Clear();
            cacheCount = 0;
        }

        public static void resetCacheCount()
        {
            cacheCount = 0;
        }

        public static Box createCached(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            if (cacheCount >= cache.Count)
            {
                cache.Add(create(0.0D, 0.0D, 0.0D, 0.0D, 0.0D, 0.0D));
            }

            return cache[cacheCount++].set(x1, y1, z1, x2, y2, z2);
        }

        private Box(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            minX = x1;
            minY = y1;
            minZ = z1;
            maxX = x2;
            maxY = y2;
            maxZ = z2;
        }

        public Box set(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            minX = x1;
            minY = y1;
            minZ = z1;
            maxX = x2;
            maxY = y2;
            maxZ = z2;
            return this;
        }

        public Box stretch(double x, double y, double z)
        {
            double var7 = minX;
            double var9 = minY;
            double var11 = minZ;
            double var13 = maxX;
            double var15 = maxY;
            double var17 = maxZ;
            if (x < 0.0D)
            {
                var7 += x;
            }

            if (x > 0.0D)
            {
                var13 += x;
            }

            if (y < 0.0D)
            {
                var9 += y;
            }

            if (y > 0.0D)
            {
                var15 += y;
            }

            if (z < 0.0D)
            {
                var11 += z;
            }

            if (z > 0.0D)
            {
                var17 += z;
            }

            return createCached(var7, var9, var11, var13, var15, var17);
        }

        public Box expand(double x, double y, double z)
        {
            double var7 = minX - x;
            double var9 = minY - y;
            double var11 = minZ - z;
            double var13 = maxX + x;
            double var15 = maxY + y;
            double var17 = maxZ + z;
            return createCached(var7, var9, var11, var13, var15, var17);
        }

        public Box offset(double x, double y, double z)
        {
            return createCached(minX + x, minY + y, minZ + z, maxX + x, maxY + y, maxZ + z);
        }

        public double getXOffset(Box box, double x)
        {
            if (box.maxY > minY && box.minY < maxY)
            {
                if (box.maxZ > minZ && box.minZ < maxZ)
                {
                    double var4;
                    if (x > 0.0D && box.maxX <= minX)
                    {
                        var4 = minX - box.maxX;
                        if (var4 < x)
                        {
                            x = var4;
                        }
                    }

                    if (x < 0.0D && box.minX >= maxX)
                    {
                        var4 = maxX - box.minX;
                        if (var4 > x)
                        {
                            x = var4;
                        }
                    }

                    return x;
                }
                else
                {
                    return x;
                }
            }
            else
            {
                return x;
            }
        }

        public double getYOffset(Box box, double y)
        {
            if (box.maxX > minX && box.minX < maxX)
            {
                if (box.maxZ > minZ && box.minZ < maxZ)
                {
                    double var4;
                    if (y > 0.0D && box.maxY <= minY)
                    {
                        var4 = minY - box.maxY;
                        if (var4 < y)
                        {
                            y = var4;
                        }
                    }

                    if (y < 0.0D && box.minY >= maxY)
                    {
                        var4 = maxY - box.minY;
                        if (var4 > y)
                        {
                            y = var4;
                        }
                    }

                    return y;
                }
                else
                {
                    return y;
                }
            }
            else
            {
                return y;
            }
        }

        public double getZOffset(Box box, double z)
        {
            if (box.maxX > minX && box.minX < maxX)
            {
                if (box.maxY > minY && box.minY < maxY)
                {
                    double var4;
                    if (z > 0.0D && box.maxZ <= minZ)
                    {
                        var4 = minZ - box.maxZ;
                        if (var4 < z)
                        {
                            z = var4;
                        }
                    }

                    if (z < 0.0D && box.minZ >= maxZ)
                    {
                        var4 = maxZ - box.minZ;
                        if (var4 > z)
                        {
                            z = var4;
                        }
                    }

                    return z;
                }
                else
                {
                    return z;
                }
            }
            else
            {
                return z;
            }
        }

        public bool intersects(Box box)
        {
            return box.maxX > minX && box.minX < maxX ? (box.maxY > minY && box.minY < maxY ? box.maxZ > minZ && box.minZ < maxZ : false) : false;
        }

        public Box translate(double x, double y, double z)
        {
            minX += x;
            minY += y;
            minZ += z;
            maxX += x;
            maxY += y;
            maxZ += z;
            return this;
        }

        public bool contains(Vec3D pos)
        {
            return pos.xCoord > minX && pos.xCoord < maxX ? (pos.yCoord > minY && pos.yCoord < maxY ? pos.zCoord > minZ && pos.zCoord < maxZ : false) : false;
        }

        public double getAverageSizeLength()
        {
            double var1 = maxX - minX;
            double var3 = maxY - minY;
            double var5 = maxZ - minZ;
            return (var1 + var3 + var5) / 3.0D;
        }

        public Box contract(double x, double y, double z)
        {
            double var7 = minX + x;
            double var9 = minY + y;
            double var11 = minZ + z;
            double var13 = maxX - x;
            double var15 = maxY - y;
            double var17 = maxZ - z;
            return createCached(var7, var9, var11, var13, var15, var17);
        }

        public Box copy()
        {
            return createCached(minX, minY, minZ, maxX, maxY, maxZ);
        }

        public MovingObjectPosition raycast(Vec3D min, Vec3D max)
        {
            Vec3D var3 = min.getIntermediateWithXValue(max, minX);
            Vec3D var4 = min.getIntermediateWithXValue(max, maxX);
            Vec3D var5 = min.getIntermediateWithYValue(max, minY);
            Vec3D var6 = min.getIntermediateWithYValue(max, maxY);
            Vec3D var7 = min.getIntermediateWithZValue(max, minZ);
            Vec3D var8 = min.getIntermediateWithZValue(max, maxZ);
            if (!containsInYZPlane(var3))
            {
                var3 = null;
            }

            if (!containsInYZPlane(var4))
            {
                var4 = null;
            }

            if (!containsInXZPlane(var5))
            {
                var5 = null;
            }

            if (!containsInXZPlane(var6))
            {
                var6 = null;
            }

            if (!containsInXYPlane(var7))
            {
                var7 = null;
            }

            if (!containsInXYPlane(var8))
            {
                var8 = null;
            }

            Vec3D var9 = null;
            if (var3 != null && (var9 == null || min.squareDistanceTo(var3) < min.squareDistanceTo(var9)))
            {
                var9 = var3;
            }

            if (var4 != null && (var9 == null || min.squareDistanceTo(var4) < min.squareDistanceTo(var9)))
            {
                var9 = var4;
            }

            if (var5 != null && (var9 == null || min.squareDistanceTo(var5) < min.squareDistanceTo(var9)))
            {
                var9 = var5;
            }

            if (var6 != null && (var9 == null || min.squareDistanceTo(var6) < min.squareDistanceTo(var9)))
            {
                var9 = var6;
            }

            if (var7 != null && (var9 == null || min.squareDistanceTo(var7) < min.squareDistanceTo(var9)))
            {
                var9 = var7;
            }

            if (var8 != null && (var9 == null || min.squareDistanceTo(var8) < min.squareDistanceTo(var9)))
            {
                var9 = var8;
            }

            if (var9 == null)
            {
                return null;
            }
            else
            {
                int var10 = -1;
                if (var9 == var3)
                {
                    var10 = 4;
                }

                if (var9 == var4)
                {
                    var10 = 5;
                }

                if (var9 == var5)
                {
                    var10 = 0;
                }

                if (var9 == var6)
                {
                    var10 = 1;
                }

                if (var9 == var7)
                {
                    var10 = 2;
                }

                if (var9 == var8)
                {
                    var10 = 3;
                }

                return new MovingObjectPosition(0, 0, 0, var10, var9);
            }
        }

        private bool containsInYZPlane(Vec3D pos)
        {
            return pos == null ? false : pos.yCoord >= minY && pos.yCoord <= maxY && pos.zCoord >= minZ && pos.zCoord <= maxZ;
        }

        private bool containsInXZPlane(Vec3D pos)
        {
            return pos == null ? false : pos.xCoord >= minX && pos.xCoord <= maxX && pos.zCoord >= minZ && pos.zCoord <= maxZ;
        }

        private bool containsInXYPlane(Vec3D pos)
        {
            return pos == null ? false : pos.xCoord >= minX && pos.xCoord <= maxX && pos.yCoord >= minY && pos.yCoord <= maxY;
        }

        public void clone(Box other)
        {
            minX = other.minX;
            minY = other.minY;
            minZ = other.minZ;
            maxX = other.maxX;
            maxY = other.maxY;
            maxZ = other.maxZ;
        }

        public override string toString()
        {
            return "box[" + minX + ", " + minY + ", " + minZ + " -> " + maxX + ", " + maxY + ", " + maxZ + "]";
        }
    }

}