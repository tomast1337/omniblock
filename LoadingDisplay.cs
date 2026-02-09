namespace betareborn
{
    public interface LoadingDisplay
    {
        void progressStartNoAbort(string var1);

        void progressStage(string var1);

        void setLoadingProgress(int var1);
    }
}