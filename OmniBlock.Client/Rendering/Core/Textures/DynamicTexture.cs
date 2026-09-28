namespace OmniBlock.Client.Rendering.Core.Textures;

public class DynamicTexture(int iconIdx)
{
    public enum FxImage { Terrain, Items }

    public readonly int Sprite = iconIdx;
    public FxImage Atlas = FxImage.Terrain;
    protected int CustomFrameCount;
    protected int CustomFrameIndex;
    protected byte[][]? CustomFrames;
    public byte[] Pixels = new byte[1024];
    public int Replicate = 1;
    // A per-instance cosmetic RNG override for render fixtures; never changes simulation RNG.
    internal Random? RandomForTest { get; set; }
    protected Random AnimationRandom => RandomForTest ?? Random.Shared;

    public void Setup(OmniBlock game)
    {
        using var context = new TextureAnimationContext(game.TexturePackList.SelectedTexturePack.GetResourceAsStream);
        Setup(context);
    }

    internal virtual void Setup(TextureAnimationContext context) { }
    internal virtual DynamicTexture CreateReloadCopy() => throw new NotSupportedException($"Animation '{GetType().Name}' has no reload factory.");

    /// <summary>Prepare a fresh set; failure cannot modify any live animation or its RNG.</summary>
    internal static List<DynamicTexture> PrepareReload(IEnumerable<DynamicTexture> current, TextureAnimationContext context)
    {
        var candidate = new List<DynamicTexture>();
        foreach (var active in current)
        {
            var copy = active.CreateReloadCopy();
            if (ReferenceEquals(copy, active)) throw new InvalidOperationException("Animation reload factory returned the live instance.");
            copy.Setup(context);
            candidate.Add(copy);
        }
        return candidate;
    }

    public virtual void tick() { }

    private protected void TryLoadCustomTexture(TextureAnimationContext context, string resourceName)
    {
        var frames = context.ReadStrip(resourceName, Atlas);
        CustomFrames = frames;
        CustomFrameIndex = 0;
        CustomFrameCount = frames?.Length ?? 0;
        Pixels = new byte[frames?[0].Length ?? 1024];
    }
}
