using OmniBlock.Client.Rendering.Core.WebGPU;
using Silk.NET.WebGPU;

namespace OmniBlock.Tests.Rendering;

public sealed class WebGpuPresentModeTests
{
    [Fact]
    public void Vsync_prefers_fifo()
    {
        PresentMode[] supported = [PresentMode.Immediate, PresentMode.Fifo, PresentMode.Mailbox];

        Assert.Equal(PresentMode.Fifo, WebGpuDevice.ChoosePresentMode(supported, true));
    }

    [Fact]
    public void Uncapped_presentation_prefers_mailbox_then_immediate()
    {
        Assert.Equal(PresentMode.Mailbox, WebGpuDevice.ChoosePresentMode(
            [PresentMode.Fifo, PresentMode.Immediate, PresentMode.Mailbox], false));
        Assert.Equal(PresentMode.Immediate, WebGpuDevice.ChoosePresentMode(
            [PresentMode.Fifo, PresentMode.Immediate], false));
    }

    [Fact]
    public void Fifo_remains_the_portable_fallback()
    {
        Assert.Equal(PresentMode.Fifo, WebGpuDevice.ChoosePresentMode([], false));
        Assert.Equal(PresentMode.Fifo, WebGpuDevice.ChoosePresentMode([PresentMode.Fifo], false));
    }
}
