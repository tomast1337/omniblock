namespace BetaSharp;

public interface LoadingDisplay
{
    void progressStartNoAbort(string message);

    void progressStage(string message);

    void setLoadingProgress(int progress);
}
