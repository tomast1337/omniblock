using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Sound;

public class MusicCategory
{
    private readonly JavaRandom _rand = new();

    public MusicCategory(ResourceLocation name, int minDelayTicks, int maxDelayTicks)
    {
        Name = name;
        MinDelayTicks = minDelayTicks;
        MaxDelayTicks = maxDelayTicks;
        TicksBeforeNext = _rand.NextInt(minDelayTicks);
    }

    public ResourceLocation Name { get; }
    public SoundPool Pool { get; } = new();
    public int MinDelayTicks { get; }
    public int MaxDelayTicks { get; }
    public int TicksBeforeNext { get; set; }

    public void ResetDelay() => TicksBeforeNext = _rand.NextInt(MinDelayTicks) + MaxDelayTicks - MinDelayTicks;
}
