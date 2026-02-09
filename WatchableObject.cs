namespace betareborn
{

    public class WatchableObject : java.lang.Object
    {
        private readonly int objectType;
        private readonly int dataValueId;
        private java.lang.Object watchedObject;
        private bool isWatching;

        public WatchableObject(int var1, int var2, java.lang.Object var3)
        {
            dataValueId = var2;
            watchedObject = var3;
            objectType = var1;
            isWatching = true;
        }

        public int getDataValueId()
        {
            return dataValueId;
        }

        public void setObject(java.lang.Object var1)
        {
            watchedObject = var1;
        }

        public java.lang.Object getObject()
        {
            return watchedObject;
        }

        public int getObjectType()
        {
            return objectType;
        }

        public bool getWatching()
        {
            return isWatching;
        }

        public void setWatching(bool var1)
        {
            isWatching = var1;
        }
    }
}