namespace betareborn
{
    public class AxisAlignedBB : java.lang.Object
    {
        private static readonly List<AxisAlignedBB> boundingBoxes = [];
        private static int numBoundingBoxesInUse = 0;
        public double minX;
        public double minY;
        public double minZ;
        public double maxX;
        public double maxY;
        public double maxZ;

        public static AxisAlignedBB getBoundingBox(double var0, double var2, double var4, double var6, double var8, double var10)
        {
            return new AxisAlignedBB(var0, var2, var4, var6, var8, var10);
        }

        public static void cleanUp()
        {
            boundingBoxes.Clear();
            numBoundingBoxesInUse = 0;
        }

        public static void clearBoundingBoxPool()
        {
            numBoundingBoxesInUse = 0;
        }

        public static AxisAlignedBB getBoundingBoxFromPool(double var0, double var2, double var4, double var6, double var8, double var10)
        {
            if (numBoundingBoxesInUse >= boundingBoxes.Count)
            {
                boundingBoxes.Add(getBoundingBox(0.0D, 0.0D, 0.0D, 0.0D, 0.0D, 0.0D));
            }

            return boundingBoxes[numBoundingBoxesInUse++].setBounds(var0, var2, var4, var6, var8, var10);
        }

        private AxisAlignedBB(double var1, double var3, double var5, double var7, double var9, double var11)
        {
            minX = var1;
            minY = var3;
            minZ = var5;
            maxX = var7;
            maxY = var9;
            maxZ = var11;
        }

        public AxisAlignedBB setBounds(double var1, double var3, double var5, double var7, double var9, double var11)
        {
            minX = var1;
            minY = var3;
            minZ = var5;
            maxX = var7;
            maxY = var9;
            maxZ = var11;
            return this;
        }

        public AxisAlignedBB addCoord(double var1, double var3, double var5)
        {
            double var7 = minX;
            double var9 = minY;
            double var11 = minZ;
            double var13 = maxX;
            double var15 = maxY;
            double var17 = maxZ;
            if (var1 < 0.0D)
            {
                var7 += var1;
            }

            if (var1 > 0.0D)
            {
                var13 += var1;
            }

            if (var3 < 0.0D)
            {
                var9 += var3;
            }

            if (var3 > 0.0D)
            {
                var15 += var3;
            }

            if (var5 < 0.0D)
            {
                var11 += var5;
            }

            if (var5 > 0.0D)
            {
                var17 += var5;
            }

            return getBoundingBoxFromPool(var7, var9, var11, var13, var15, var17);
        }

        public AxisAlignedBB expand(double var1, double var3, double var5)
        {
            double var7 = minX - var1;
            double var9 = minY - var3;
            double var11 = minZ - var5;
            double var13 = maxX + var1;
            double var15 = maxY + var3;
            double var17 = maxZ + var5;
            return getBoundingBoxFromPool(var7, var9, var11, var13, var15, var17);
        }

        public AxisAlignedBB getOffsetBoundingBox(double var1, double var3, double var5)
        {
            return getBoundingBoxFromPool(minX + var1, minY + var3, minZ + var5, maxX + var1, maxY + var3, maxZ + var5);
        }

        public double calculateXOffset(AxisAlignedBB var1, double var2)
        {
            if (var1.maxY > minY && var1.minY < maxY)
            {
                if (var1.maxZ > minZ && var1.minZ < maxZ)
                {
                    double var4;
                    if (var2 > 0.0D && var1.maxX <= minX)
                    {
                        var4 = minX - var1.maxX;
                        if (var4 < var2)
                        {
                            var2 = var4;
                        }
                    }

                    if (var2 < 0.0D && var1.minX >= maxX)
                    {
                        var4 = maxX - var1.minX;
                        if (var4 > var2)
                        {
                            var2 = var4;
                        }
                    }

                    return var2;
                }
                else
                {
                    return var2;
                }
            }
            else
            {
                return var2;
            }
        }

        public double calculateYOffset(AxisAlignedBB var1, double var2)
        {
            if (var1.maxX > minX && var1.minX < maxX)
            {
                if (var1.maxZ > minZ && var1.minZ < maxZ)
                {
                    double var4;
                    if (var2 > 0.0D && var1.maxY <= minY)
                    {
                        var4 = minY - var1.maxY;
                        if (var4 < var2)
                        {
                            var2 = var4;
                        }
                    }

                    if (var2 < 0.0D && var1.minY >= maxY)
                    {
                        var4 = maxY - var1.minY;
                        if (var4 > var2)
                        {
                            var2 = var4;
                        }
                    }

                    return var2;
                }
                else
                {
                    return var2;
                }
            }
            else
            {
                return var2;
            }
        }

        public double calculateZOffset(AxisAlignedBB var1, double var2)
        {
            if (var1.maxX > minX && var1.minX < maxX)
            {
                if (var1.maxY > minY && var1.minY < maxY)
                {
                    double var4;
                    if (var2 > 0.0D && var1.maxZ <= minZ)
                    {
                        var4 = minZ - var1.maxZ;
                        if (var4 < var2)
                        {
                            var2 = var4;
                        }
                    }

                    if (var2 < 0.0D && var1.minZ >= maxZ)
                    {
                        var4 = maxZ - var1.minZ;
                        if (var4 > var2)
                        {
                            var2 = var4;
                        }
                    }

                    return var2;
                }
                else
                {
                    return var2;
                }
            }
            else
            {
                return var2;
            }
        }

        public bool intersectsWith(AxisAlignedBB var1)
        {
            return var1.maxX > minX && var1.minX < maxX ? (var1.maxY > minY && var1.minY < maxY ? var1.maxZ > minZ && var1.minZ < maxZ : false) : false;
        }

        public AxisAlignedBB offset(double var1, double var3, double var5)
        {
            minX += var1;
            minY += var3;
            minZ += var5;
            maxX += var1;
            maxY += var3;
            maxZ += var5;
            return this;
        }

        public bool isVecInside(Vec3D var1)
        {
            return var1.xCoord > minX && var1.xCoord < maxX ? (var1.yCoord > minY && var1.yCoord < maxY ? var1.zCoord > minZ && var1.zCoord < maxZ : false) : false;
        }

        public double getAverageEdgeLength()
        {
            double var1 = maxX - minX;
            double var3 = maxY - minY;
            double var5 = maxZ - minZ;
            return (var1 + var3 + var5) / 3.0D;
        }

        public AxisAlignedBB func_28195_e(double var1, double var3, double var5)
        {
            double var7 = minX + var1;
            double var9 = minY + var3;
            double var11 = minZ + var5;
            double var13 = maxX - var1;
            double var15 = maxY - var3;
            double var17 = maxZ - var5;
            return getBoundingBoxFromPool(var7, var9, var11, var13, var15, var17);
        }

        public AxisAlignedBB copy()
        {
            return getBoundingBoxFromPool(minX, minY, minZ, maxX, maxY, maxZ);
        }

        public MovingObjectPosition func_1169_a(Vec3D var1, Vec3D var2)
        {
            Vec3D var3 = var1.getIntermediateWithXValue(var2, minX);
            Vec3D var4 = var1.getIntermediateWithXValue(var2, maxX);
            Vec3D var5 = var1.getIntermediateWithYValue(var2, minY);
            Vec3D var6 = var1.getIntermediateWithYValue(var2, maxY);
            Vec3D var7 = var1.getIntermediateWithZValue(var2, minZ);
            Vec3D var8 = var1.getIntermediateWithZValue(var2, maxZ);
            if (!isVecInYZ(var3))
            {
                var3 = null;
            }

            if (!isVecInYZ(var4))
            {
                var4 = null;
            }

            if (!isVecInXZ(var5))
            {
                var5 = null;
            }

            if (!isVecInXZ(var6))
            {
                var6 = null;
            }

            if (!isVecInXY(var7))
            {
                var7 = null;
            }

            if (!isVecInXY(var8))
            {
                var8 = null;
            }

            Vec3D var9 = null;
            if (var3 != null && (var9 == null || var1.squareDistanceTo(var3) < var1.squareDistanceTo(var9)))
            {
                var9 = var3;
            }

            if (var4 != null && (var9 == null || var1.squareDistanceTo(var4) < var1.squareDistanceTo(var9)))
            {
                var9 = var4;
            }

            if (var5 != null && (var9 == null || var1.squareDistanceTo(var5) < var1.squareDistanceTo(var9)))
            {
                var9 = var5;
            }

            if (var6 != null && (var9 == null || var1.squareDistanceTo(var6) < var1.squareDistanceTo(var9)))
            {
                var9 = var6;
            }

            if (var7 != null && (var9 == null || var1.squareDistanceTo(var7) < var1.squareDistanceTo(var9)))
            {
                var9 = var7;
            }

            if (var8 != null && (var9 == null || var1.squareDistanceTo(var8) < var1.squareDistanceTo(var9)))
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

        private bool isVecInYZ(Vec3D var1)
        {
            return var1 == null ? false : var1.yCoord >= minY && var1.yCoord <= maxY && var1.zCoord >= minZ && var1.zCoord <= maxZ;
        }

        private bool isVecInXZ(Vec3D var1)
        {
            return var1 == null ? false : var1.xCoord >= minX && var1.xCoord <= maxX && var1.zCoord >= minZ && var1.zCoord <= maxZ;
        }

        private bool isVecInXY(Vec3D var1)
        {
            return var1 == null ? false : var1.xCoord >= minX && var1.xCoord <= maxX && var1.yCoord >= minY && var1.yCoord <= maxY;
        }

        public void setBB(AxisAlignedBB var1)
        {
            minX = var1.minX;
            minY = var1.minY;
            minZ = var1.minZ;
            maxX = var1.maxX;
            maxY = var1.maxY;
            maxZ = var1.maxZ;
        }

        public override string toString()
        {
            return "box[" + minX + ", " + minY + ", " + minZ + " -> " + maxX + ", " + maxY + ", " + maxZ + "]";
        }
    }

}