using OmniBlock.Client;

namespace OmniBlock.Tests;

public sealed class ClientReadySignalTests
{
    [Fact]
    public void Signal_marksReadyAndRunsRegisteredCallbacksOnce()
    {
        ClientReadySignal signal = new();
        int calls = 0;
        signal.WhenReady(() => calls++);

        signal.Signal();
        signal.Signal();

        Assert.True(signal.IsReady);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void WhenReady_runsLateRegistrationsImmediately()
    {
        ClientReadySignal signal = new();
        signal.Signal();
        bool called = false;

        signal.WhenReady(() => called = true);

        Assert.True(called);
    }

    [Fact]
    public void WhenReady_rejectsNullCallbacks()
    {
        ClientReadySignal signal = new();

        Assert.Throws<ArgumentNullException>(() => signal.WhenReady(null!));
    }

    [Fact]
    public void Remove_preventsARegisteredCallbackFromRunning()
    {
        ClientReadySignal signal = new();
        int calls = 0;
        void Callback() => calls++;
        signal.WhenReady(Callback);

        signal.Remove(Callback);
        signal.Signal();

        Assert.Equal(0, calls);
    }
}
