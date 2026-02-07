namespace betareborn
{
    public interface ICamera
    {
        bool isBoundingBoxInFrustum(Box var1);

        void setPosition(double var1, double var3, double var5);
    }

}