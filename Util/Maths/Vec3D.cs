namespace betareborn.Util.Maths
{
    public class Vec3D : java.lang.Object
    {
        private class Pool
        {
            private readonly List<Vec3D> vectorList = [];
            private int index = 0;

            public Vec3D create(double x, double y, double z)
            {
                if (index >= vectorList.Count)
                {
                    vectorList.Add(new Vec3D(0.0D, 0.0D, 0.0D));
                }

                return vectorList[index++].setComponents(x, y, z);
            }

            public void cleanUp()
            {
                vectorList.Clear();
                index = 0;
            }
        }

        private static readonly ThreadLocal<Pool> pool = new(() => new());
        public double xCoord;
        public double yCoord;
        public double zCoord;

        public static void cleanUp()
        {
            Pool? p = pool.Value ?? throw new Exception("Vec3D pool was not created!");
            p.cleanUp();
        }

        public static Vec3D createVector(double x, double y, double z)
        {
            Pool? p = pool.Value;

            return p == null ? throw new Exception("Vec3D pool was not created!") : p.create(x, y, z);
        }

        private Vec3D(double var1, double var3, double var5)
        {
            if (var1 == -0.0D)
            {
                var1 = 0.0D;
            }

            if (var3 == -0.0D)
            {
                var3 = 0.0D;
            }

            if (var5 == -0.0D)
            {
                var5 = 0.0D;
            }

            xCoord = var1;
            yCoord = var3;
            zCoord = var5;
        }

        private Vec3D setComponents(double var1, double var3, double var5)
        {
            xCoord = var1;
            yCoord = var3;
            zCoord = var5;
            return this;
        }

        public Vec3D subtract(Vec3D var1)
        {
            return createVector(var1.xCoord - xCoord, var1.yCoord - yCoord, var1.zCoord - zCoord);
        }

        public Vec3D normalize()
        {
            double var1 = (double)MathHelper.sqrt_double(xCoord * xCoord + yCoord * yCoord + zCoord * zCoord);
            return var1 < 1.0E-4D ? createVector(0.0D, 0.0D, 0.0D) : createVector(xCoord / var1, yCoord / var1, zCoord / var1);
        }

        public Vec3D crossProduct(Vec3D var1)
        {
            return createVector(yCoord * var1.zCoord - zCoord * var1.yCoord, zCoord * var1.xCoord - xCoord * var1.zCoord, xCoord * var1.yCoord - yCoord * var1.xCoord);
        }

        public Vec3D addVector(double var1, double var3, double var5)
        {
            return createVector(xCoord + var1, yCoord + var3, zCoord + var5);
        }

        public double distanceTo(Vec3D var1)
        {
            double var2 = var1.xCoord - xCoord;
            double var4 = var1.yCoord - yCoord;
            double var6 = var1.zCoord - zCoord;
            return (double)MathHelper.sqrt_double(var2 * var2 + var4 * var4 + var6 * var6);
        }

        public double squareDistanceTo(Vec3D var1)
        {
            double var2 = var1.xCoord - xCoord;
            double var4 = var1.yCoord - yCoord;
            double var6 = var1.zCoord - zCoord;
            return var2 * var2 + var4 * var4 + var6 * var6;
        }

        public double squareDistanceTo(double var1, double var3, double var5)
        {
            double var7 = var1 - xCoord;
            double var9 = var3 - yCoord;
            double var11 = var5 - zCoord;
            return var7 * var7 + var9 * var9 + var11 * var11;
        }

        public double lengthVector()
        {
            return (double)MathHelper.sqrt_double(xCoord * xCoord + yCoord * yCoord + zCoord * zCoord);
        }

        public Vec3D getIntermediateWithXValue(Vec3D var1, double var2)
        {
            double var4 = var1.xCoord - xCoord;
            double var6 = var1.yCoord - yCoord;
            double var8 = var1.zCoord - zCoord;
            if (var4 * var4 < (double)1.0E-7F)
            {
                return null;
            }
            else
            {
                double var10 = (var2 - xCoord) / var4;
                return var10 >= 0.0D && var10 <= 1.0D ? createVector(xCoord + var4 * var10, yCoord + var6 * var10, zCoord + var8 * var10) : null;
            }
        }

        public Vec3D getIntermediateWithYValue(Vec3D var1, double var2)
        {
            double var4 = var1.xCoord - xCoord;
            double var6 = var1.yCoord - yCoord;
            double var8 = var1.zCoord - zCoord;
            if (var6 * var6 < (double)1.0E-7F)
            {
                return null;
            }
            else
            {
                double var10 = (var2 - yCoord) / var6;
                return var10 >= 0.0D && var10 <= 1.0D ? createVector(xCoord + var4 * var10, yCoord + var6 * var10, zCoord + var8 * var10) : null;
            }
        }

        public Vec3D getIntermediateWithZValue(Vec3D var1, double var2)
        {
            double var4 = var1.xCoord - xCoord;
            double var6 = var1.yCoord - yCoord;
            double var8 = var1.zCoord - zCoord;
            if (var8 * var8 < (double)1.0E-7F)
            {
                return null;
            }
            else
            {
                double var10 = (var2 - zCoord) / var8;
                return var10 >= 0.0D && var10 <= 1.0D ? createVector(xCoord + var4 * var10, yCoord + var6 * var10, zCoord + var8 * var10) : null;
            }
        }

        public override string toString()
        {
            return "(" + xCoord + ", " + yCoord + ", " + zCoord + ")";
        }

        public void rotateAroundX(float var1)
        {
            float var2 = MathHelper.cos(var1);
            float var3 = MathHelper.sin(var1);
            double var4 = xCoord;
            double var6 = yCoord * (double)var2 + zCoord * (double)var3;
            double var8 = zCoord * (double)var2 - yCoord * (double)var3;
            xCoord = var4;
            yCoord = var6;
            zCoord = var8;
        }

        public void rotateAroundY(float var1)
        {
            float var2 = MathHelper.cos(var1);
            float var3 = MathHelper.sin(var1);
            double var4 = xCoord * (double)var2 + zCoord * (double)var3;
            double var6 = yCoord;
            double var8 = zCoord * (double)var2 - xCoord * (double)var3;
            xCoord = var4;
            yCoord = var6;
            zCoord = var8;
        }
    }
}