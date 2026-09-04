namespace OmniBlock.Stats;

public class StatCrafting : StatBase
{
    public StatCrafting(int id, string statName, int itemId) : base(id, statName) => ItemId = itemId;
    public int ItemId { get; }
}
