using OmniBlock.Client.Rendering.Chunks;

namespace OmniBlock.Tests.Rendering;

public sealed class MeshBuildCancellationTests
{
    [Fact]
    public void Cancellation_is_idempotent_and_retains_the_highest_replacement_priority()
    {
        using var control = new MeshBuildCancellation(MeshWorkPriority.Background);
        Assert.False(control.IsCancellationRequested);

        Assert.True(control.Cancel(MeshCancellationReason.Superseded, MeshWorkPriority.Foreground));
        Assert.False(control.Cancel(MeshCancellationReason.RendererDisposed, MeshWorkPriority.Critical));

        Assert.True(control.IsCancellationRequested);
        Assert.True(control.Token.IsCancellationRequested);
        Assert.Equal(MeshCancellationReason.Superseded, control.Reason);
        Assert.Equal(MeshWorkPriority.Critical, control.Priority);
    }

    [Fact]
    public void Cancellation_requires_an_explicit_reason()
    {
        using var control = new MeshBuildCancellation(MeshWorkPriority.Background);
        Assert.Throws<ArgumentException>(() =>
            control.Cancel(MeshCancellationReason.None, MeshWorkPriority.Background));
    }

    [Fact]
    public void Late_cancellation_after_owner_disposal_is_a_safe_no_op()
    {
        var control = new MeshBuildCancellation(MeshWorkPriority.Background);
        control.Dispose();

        Assert.False(control.Cancel(MeshCancellationReason.Superseded, MeshWorkPriority.Critical));
        Assert.False(control.IsCancellationRequested);
    }
}
