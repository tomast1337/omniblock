using Microsoft.Extensions.Logging;
using OmniBlock.Stats;

namespace OmniBlock.Threading;

internal class ThreadStatSynchronizerSend
{
    private readonly ILogger<ThreadStatSynchronizerSend> _logger = Log.Instance.For<ThreadStatSynchronizerSend>();
    private readonly Dictionary<StatBase, int> _statsMap;
    private readonly StatsSynchronizer _synchronizer;

    public ThreadStatSynchronizerSend(StatsSynchronizer synchronizer, Dictionary<StatBase, int> statsMap)
    {
        _synchronizer = synchronizer;
        _statsMap = statsMap;
    }

    public void Start()
    {
        Task.Run(() =>
        {
            try
            {
                _synchronizer.SaveStatsToFile(_statsMap, _synchronizer.UnsentStatsFile, _synchronizer.TempUnsentStatsFile, _synchronizer.OldUnsentStatsFile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in Stat Synchronizer Send");
            }
            finally
            {
                _synchronizer.Busy = false;
            }
        });
    }
}
