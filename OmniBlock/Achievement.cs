using OmniBlock.Items;
using OmniBlock.Stats;

namespace OmniBlock;

public class Achievement : StatBase
{
    public readonly int column;
    public readonly ItemStack icon;
    public readonly Achievement parent;
    public readonly int row;

    private bool _isChallenge;
    //public Func<string>? GetTranslatedDescription { get; set; }

    public Achievement(int id, string key, int column, int row, Item displayItem, Achievement parent) : this(id, key, column, row, new ItemStack(displayItem, 1, 0), parent)
    {
    }

    public Achievement(int id, string key, int column, int row, ItemStack icon, Achievement parent) : base(5242880 + id, $"achievement.{key}.title")
    {
        this.icon = icon;
        TranslationKey = "achievement." + key;
        this.column = column;
        this.row = row;
        if (column < Achievements.minColumn)
        {
            Achievements.minColumn = column;
        }

        if (row < Achievements.minRow)
        {
            Achievements.minRow = row;
        }

        if (column > Achievements.maxColumn)
        {
            Achievements.maxColumn = column;
        }

        if (row > Achievements.maxRow)
        {
            Achievements.maxRow = row;
        }

        this.parent = parent;
    }

    public string TranslationKey { get; }

    public string? GetTranslatedTitle => Translations.Get($"{TranslationKey}.title");
    public string? GetTranslatedDescription => Translations.Get($"{TranslationKey}.desc");

    public Achievement m_66876377()
    {
        LocalOnly = true;
        return this;
    }

    public Achievement challenge()
    {
        _isChallenge = true;
        return this;
    }

    public Achievement registerAchievement()
    {
        base.RegisterStat();
        Achievements.AllAchievements.Add(this);
        return this;
    }

    public override bool IsAchievement() => true;

    public bool isChallenge() => _isChallenge;

    public override StatBase RegisterStat() => registerAchievement();

    public override StatBase SetLocalOnly() => m_66876377();
}
