using System.Globalization;
using OmniBlock.Stats.Achievements;

namespace OmniBlock.Stats;

public class StatBase
{
    private const string DefaultDecimalFormat = "0.00";

    public static readonly Func<int, string> IntegerFormat = FormatInteger;
    public static readonly Func<int, string> TimeProvider = StatFormatters.FormatTime;
    public static readonly Func<int, string> DistanceProvider = StatFormatters.FormatDistance;

    private readonly Func<int, string> _formatter;

    public StatBase(int id, string statName, Func<int, string> formatter)
    {
        LocalOnly = false;
        Id = id;
        StatName = statName;
        _formatter = formatter;
    }

    public StatBase(int id, string statName) : this(id, statName, IntegerFormat)
    {
    }

    public int Id { get; }
    public string StatName { get; }
    public bool LocalOnly { get; set; }
    public string StatGuid { get; set; }

    public virtual StatBase SetLocalOnly()
    {
        LocalOnly = true;
        return this;
    }

    public virtual StatBase RegisterStat()
    {
        if (Stats.IdToStat.ContainsKey(Id))
        {
            var existingStatName = Stats.IdToStat[Id].StatName;
            throw new InvalidOperationException($"Duplicate stat id: \"{existingStatName}\" and \"{StatName}\" at id {Id}");
        }

        Stats.AllStats.Add(this);
        Stats.IdToStat.Add(Id, this);
        StatGuid = AchievementMap.GetGuid(Id);

        return this;
    }

    public virtual bool IsAchievement() => false;

    public string Format(int value) => _formatter(value);

    public override string ToString() => StatName;

    public static string FormatInteger(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string FormatDecimal(double value) => value.ToString(DefaultDecimalFormat, CultureInfo.InvariantCulture);
}
