using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OmniBlock.Stats;

public class StatFileWriter
{
    private static readonly ILogger<StatFileWriter> s_logger = Log.Instance.For<StatFileWriter>();

    private readonly Dictionary<StatBase, int> _statsData = new();
    private readonly Dictionary<StatBase, int> _statsSyncedData = new();
    private readonly StatsSynchronizer _statsSyncer;
    private bool _statsExist;

    public StatFileWriter(Session session, string mcDataDir)
    {
        var statsFolder = Path.Combine(mcDataDir, "stats");
        if (!Directory.Exists(statsFolder))
        {
            Directory.CreateDirectory(statsFolder);
        }

        if (Directory.Exists(mcDataDir))
        {
            foreach (var filePath in Directory.GetFiles(mcDataDir, "stats_*.dat"))
            {
                var fileName = Path.GetFileName(filePath);
                var targetPath = Path.Combine(statsFolder, fileName);

                if (!File.Exists(targetPath))
                {
                    s_logger.LogInformation($"Relocating {fileName}");
                    File.Move(filePath, targetPath);
                }
            }
        }

        _statsSyncer = new StatsSynchronizer(session, this, statsFolder);
    }

    public void ReadStat(StatBase stat, int increment)
    {
        WriteStatToMap(_statsSyncedData, stat, increment);
        WriteStatToMap(_statsData, stat, increment);
        _statsExist = true;
    }

    private static void WriteStatToMap(Dictionary<StatBase, int> map, StatBase stat, int increment)
    {
        map.TryGetValue(stat, out var current);
        map[stat] = current + increment;
    }

    public Dictionary<StatBase, int> GetStatsSyncedData() => new(_statsSyncedData);

    public void LoadStats(Dictionary<StatBase, int> statsMap)
    {
        if (statsMap != null)
        {
            _statsExist = true;
            foreach (var kvp in statsMap)
            {
                WriteStatToMap(_statsSyncedData, kvp.Key, kvp.Value);
                WriteStatToMap(_statsData, kvp.Key, kvp.Value);
            }
        }
    }

    public void AddStats(Dictionary<StatBase, int> newStats)
    {
        if (newStats != null)
        {
            foreach (var kvp in newStats)
            {
                _statsSyncedData.TryGetValue(kvp.Key, out var currentSynced);
                _statsData[kvp.Key] = kvp.Value + currentSynced;
            }
        }
    }

    public void SetStats(Dictionary<StatBase, int> newStats)
    {
        if (newStats != null)
        {
            _statsExist = true;
            foreach (var kvp in newStats)
            {
                WriteStatToMap(_statsSyncedData, kvp.Key, kvp.Value);
            }
        }
    }

    public static Dictionary<StatBase, int> CreateStatsMap(string statsFileContents)
    {
        var statsMap = new Dictionary<StatBase, int>();
        try
        {
            var sb = new StringBuilder();

            using var statsJson = JsonDocument.Parse(statsFileContents);
            var root = statsJson.RootElement;

            if (root.TryGetProperty("stats-change", out var statsChangeArray))
            {
                foreach (var statJson in statsChangeArray.EnumerateArray())
                {
                    var prop = statJson.EnumerateObject().First();

                    var id = int.Parse(prop.Name);
                    var value = prop.Value.ValueKind == JsonValueKind.Number
                        ? prop.Value.GetInt32()
                        : int.Parse(prop.Value.GetString() ?? "0");

                    var statBase = Stats.GetStatById(id);
                    if (statBase == null)
                    {
                        s_logger.LogInformation($"{id} is not a valid stat");
                    }
                    else
                    {
                        sb.Append(statBase.StatGuid).Append(",");
                        sb.Append(value).Append(",");
                        statsMap[statBase] = value;
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            s_logger.LogError(ex, "Exception");
        }

        return statsMap;
    }

    public static string SerializeStats(string username, string salt, Dictionary<StatBase, int> statsMap)
    {
        var sb = new StringBuilder();
        var isFirst = true;

        sb.Append("{\r\n");
        if (username != null && salt != null)
        {
            sb.Append("  \"user\":{\r\n");
            sb.Append("    \"name\":\"").Append(username).Append("\",\r\n");
            sb.Append("    \"sessionid\":\"").Append(salt).Append("\"\r\n");
            sb.Append("  },\r\n");
        }

        sb.Append("  \"stats-change\":[");
        var hashDataBuilder = new StringBuilder();

        foreach (var kvp in statsMap)
        {
            var stat = kvp.Key;
            var value = kvp.Value;

            if (!isFirst)
                sb.Append("},");
            else
                isFirst = false;

            sb.Append("\r\n    {\"").Append(stat.Id).Append("\":").Append(value);

            hashDataBuilder.Append(stat.StatGuid).Append(",");
            hashDataBuilder.Append(value).Append(",");
        }

        if (!isFirst)
            sb.Append("}");

        sb.Append("\r\n  ],\r\n");
        sb.Append("  \"checksum\":\"").Append("NoChecksum").Append("\"\r\n");
        sb.Append("}");

        return sb.ToString();
    }

    public bool HasAchievementUnlocked(Achievement achievement) => _statsData.ContainsKey(achievement);

    public bool CanUnlockAchievement(Achievement achievement) => achievement.parent == null || HasAchievementUnlocked(achievement.parent);

    public int GetStatValue(StatBase stat) => _statsData.TryGetValue(stat, out var val) ? val : 0;

    public static void Tick()
    {
    }

    public void SyncStats() => _statsSyncer.SyncStatsFileWithMap(GetStatsSyncedData());

    public void SyncStatsIfReady()
    {
        if (_statsExist && _statsSyncer.IsReadyToSync())
        {
            _statsSyncer.SendStats(GetStatsSyncedData());
        }

        _statsSyncer.Tick();
    }
}
