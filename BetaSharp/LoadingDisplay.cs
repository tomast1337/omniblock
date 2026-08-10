namespace OmniBlock;

public interface LoadingDisplay
{
    void BeginLoadingPersistent(string message);
    void SetStage(string message);
    void SetProgress(int progress);
}
