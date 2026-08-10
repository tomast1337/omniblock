namespace OmniBlock.Client.Rendering.Core;

/// <summary>
///     Captures whatever geometry is drawn between <see cref="Begin" /> and <see cref="End" /> into
///     a separate buffer, blurs it, and composites it back over the frame that was in progress. The
///     one caller is the Soft Clouds bracket in <c>GameRenderer.DrawWorld</c>, kept backend-agnostic
///     so that call site does not have to name <c>FramebufferManager</c> or any <c>Wgpu*</c> type.
/// </summary>
public interface ICloudBlurPass
{
    void Begin();
    void End();
}
