namespace OmniBlock.Client.Rendering.PathMovers;

/// <summary>
///     Struct-of-arrays store for a straight-line, mathematically-driven visual — an item gliding
///     through a pipe, not a physics/AI entity. Sibling to <see cref="Particles.ParticleBuffer" />,
///     not a <see cref="Particles.ParticleType" />: nothing here has gravity, friction, or collision.
/// </summary>
public sealed class PathMoverBuffer
{
    public const int MaxMovers = 256;

    public int Count;

    public readonly double[] X = new double[MaxMovers];
    public readonly double[] Y = new double[MaxMovers];
    public readonly double[] Z = new double[MaxMovers];
    public readonly double[] PrevX = new double[MaxMovers];
    public readonly double[] PrevY = new double[MaxMovers];
    public readonly double[] PrevZ = new double[MaxMovers];

    public readonly double[] StartX = new double[MaxMovers];
    public readonly double[] StartY = new double[MaxMovers];
    public readonly double[] StartZ = new double[MaxMovers];
    public readonly double[] EndX = new double[MaxMovers];
    public readonly double[] EndY = new double[MaxMovers];
    public readonly double[] EndZ = new double[MaxMovers];

    /// <summary>0..1 fraction of the way from start to end.</summary>
    public readonly float[] Progress = new float[MaxMovers];

    /// <summary>Precomputed at spawn: how much <see cref="Progress" /> advances per tick.</summary>
    public readonly float[] ProgressPerTick = new float[MaxMovers];

    /// <summary>Index into the items.png 16x16 icon grid, e.g. <c>ItemStack.GetTextureId()</c>.</summary>
    public readonly int[] IconIndex = new int[MaxMovers];

    public readonly float[] Scale = new float[MaxMovers];
    public readonly bool[] Dead = new bool[MaxMovers];

    /// <summary>
    ///     Adds a mover travelling in a straight line from <paramref name="startX" />/Y/Z to
    ///     <paramref name="endX" />/Y/Z at <paramref name="worldUnitsPerTick" />. A zero-length path
    ///     completes in exactly one tick rather than dividing by zero.
    /// </summary>
    public int Spawn(double startX, double startY, double startZ,
        double endX, double endY, double endZ,
        double worldUnitsPerTick, int iconIndex, float scale)
    {
        if (Count >= MaxMovers)
        {
            SwapRemove(0);
        }

        int i = Count++;

        StartX[i] = startX; StartY[i] = startY; StartZ[i] = startZ;
        EndX[i] = endX; EndY[i] = endY; EndZ[i] = endZ;

        double dx = endX - startX;
        double dy = endY - startY;
        double dz = endZ - startZ;
        double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        ProgressPerTick[i] = length > 0.0 ? (float)(worldUnitsPerTick / length) : 1.0f;

        Progress[i] = 0f;
        X[i] = startX; Y[i] = startY; Z[i] = startZ;
        PrevX[i] = startX; PrevY[i] = startY; PrevZ[i] = startZ;

        IconIndex[i] = iconIndex;
        Scale[i] = scale;
        Dead[i] = false;

        return i;
    }

    public void SwapRemove(int i)
    {
        int last = Count - 1;
        if (i != last)
        {
            X[i] = X[last]; Y[i] = Y[last]; Z[i] = Z[last];
            PrevX[i] = PrevX[last]; PrevY[i] = PrevY[last]; PrevZ[i] = PrevZ[last];
            StartX[i] = StartX[last]; StartY[i] = StartY[last]; StartZ[i] = StartZ[last];
            EndX[i] = EndX[last]; EndY[i] = EndY[last]; EndZ[i] = EndZ[last];
            Progress[i] = Progress[last];
            ProgressPerTick[i] = ProgressPerTick[last];
            IconIndex[i] = IconIndex[last];
            Scale[i] = Scale[last];
            Dead[i] = Dead[last];
        }
        Count--;
    }

    public void Clear()
    {
        Count = 0;
    }
}
