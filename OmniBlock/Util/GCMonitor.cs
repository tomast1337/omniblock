using System.Diagnostics;

namespace OmniBlock.Util;

public sealed class GCMonitor : IDisposable
{
    private const int UpdateIntervalMs = 250;
    private readonly Process _process;

    private readonly Timer _timer;
    private bool _disposed;

    public GCMonitor()
    {
        _process = Process.GetCurrentProcess();
        MaxMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

        _timer = new Timer(
            _ => Update(),
            null,
            0,
            UpdateIntervalMs
        );
    }

    public long MaxMemoryBytes { get; private set; }
    public long UsedMemoryBytes { get; private set; }
    public long UsedHeapBytes { get; private set; }
    public bool AllowUpdating { get; set; } = true;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Dispose();
        _process.Dispose();
    }

    private void Update()
    {
        if (!AllowUpdating || _disposed) return;

        _process.Refresh();

        UsedMemoryBytes = _process.WorkingSet64;
        UsedHeapBytes = GC.GetTotalMemory(false);
    }
}
