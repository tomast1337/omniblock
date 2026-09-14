namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal enum TerrainLodHandoffState
{
    LodOnly,
    NearPreparing,
    Overlap,
    NearOnly
}

internal readonly record struct TerrainNearHandoff(bool Active, float Progress, uint Seed)
{
    public static TerrainNearHandoff Inactive => new(false, 1, 0);
}

internal interface ITerrainPresentationHandoff
{
    TerrainNearHandoff GetNearHandoff(int chunkX, int chunkZ, bool translucent);
}

/// <summary>
///     Readiness-driven coverage transition for one column layer. Losing complete near coverage
///     restores LOD immediately: a graceful fade is never allowed to create an interaction hole.
/// </summary>
internal struct TerrainLodHandoffTransition
{
    internal const float DurationSeconds = 0.25f;

    public float Progress { get; private set; }
    public TerrainLodHandoffState State { get; private set; }
    public long Started { get; private set; }
    public long Reversals { get; private set; }

    public void InitializeNearOnly()
    {
        Progress = 1;
        State = TerrainLodHandoffState.NearOnly;
    }

    public void CopyFrom(in TerrainLodHandoffTransition other) => this = other;

    public void Update(bool nearPresent, bool nearReady, float deltaTime, bool fadeEnabled)
    {
        if (!nearPresent)
        {
            if (Progress is > 0 and < 1) Reversals++;
            Progress = 0;
            State = TerrainLodHandoffState.LodOnly;
            return;
        }

        if (!nearReady)
        {
            if (Progress is > 0 and < 1) Reversals++;
            // A partial exact column cannot complement the dither mask safely. Restore complete
            // LOD coverage immediately while its missing near sections continue preparing.
            Progress = 0;
            State = TerrainLodHandoffState.NearPreparing;
            return;
        }

        if (!fadeEnabled)
        {
            Progress = 1;
            State = TerrainLodHandoffState.NearOnly;
            return;
        }

        if (Progress <= 0) Started++;
        Progress = Math.Clamp(
            Progress + Math.Max(0, deltaTime) / DurationSeconds, 0, 1);
        State = Progress >= 1
            ? TerrainLodHandoffState.NearOnly
            : TerrainLodHandoffState.Overlap;
    }
}

internal readonly record struct TerrainLodLevelBlend(
    int PrimaryLevel,
    int SecondaryLevel,
    float Progress)
{
    public bool Active => SecondaryLevel >= 0;
    public int DominantLevel => Active && Progress >= 0.5f ? SecondaryLevel : PrimaryLevel;
}

/// <summary>Reversible transition between two already-resident hierarchy levels.</summary>
internal struct TerrainLodLevelTransition
{
    internal const float DurationSeconds = 0.25f;
    internal const float RetargetStabilitySeconds = 0.05f;
    private const int RetargetStabilityFrames = 3;

    private bool _initialized;
    private int _currentLevel;
    private int _fromLevel;
    private int _toLevel;
    private float _progress;
    private int _pendingLevel;
    private int _pendingFrames;
    private float _pendingSeconds;

    public long Started { get; private set; }
    public long Reversals { get; private set; }
    public bool Active => _initialized && _toLevel >= 0;
    public int SelectionLevel => Active ? _toLevel : _initialized ? _currentLevel : -1;

    public TerrainLodLevelBlend Update(int requestedLevel, float deltaTime, bool fadeEnabled)
    {
        if (!_initialized)
        {
            _initialized = true;
            _currentLevel = requestedLevel;
            _fromLevel = requestedLevel;
            _toLevel = -1;
            _pendingLevel = -1;
            return new TerrainLodLevelBlend(_currentLevel, -1, 1);
        }

        if (!fadeEnabled)
        {
            _currentLevel = requestedLevel;
            _fromLevel = requestedLevel;
            _toLevel = -1;
            _progress = 1;
            ClearPendingTarget();
            return new TerrainLodLevelBlend(_currentLevel, -1, 1);
        }

        if (Active && requestedLevel != _toLevel &&
            ObservePendingTarget(requestedLevel, deltaTime))
        {
            if (requestedLevel == _fromLevel)
            {
                (_fromLevel, _toLevel) = (_toLevel, _fromLevel);
                _progress = 1 - _progress;
                Reversals++;
                ClearPendingTarget();
            }
            else
            {
                _currentLevel = _progress >= 0.5f ? _toLevel : _fromLevel;
                Begin(_currentLevel, requestedLevel);
            }
        }
        else if (!Active && requestedLevel != _currentLevel)
        {
            Begin(_currentLevel, requestedLevel);
        }
        else if (!Active || requestedLevel == _toLevel)
        {
            ClearPendingTarget();
        }

        if (!Active) return new TerrainLodLevelBlend(_currentLevel, -1, 1);

        _progress = Math.Clamp(
            _progress + Math.Max(0, deltaTime) / DurationSeconds, 0, 1);
        if (_progress >= 1)
        {
            _currentLevel = _toLevel;
            _fromLevel = _currentLevel;
            _toLevel = -1;
            ClearPendingTarget();
            return new TerrainLodLevelBlend(_currentLevel, -1, 1);
        }
        return new TerrainLodLevelBlend(_fromLevel, _toLevel, _progress);
    }

    public void CopyFrom(in TerrainLodLevelTransition other) => this = other;

    private void Begin(int fromLevel, int toLevel)
    {
        _fromLevel = fromLevel;
        _toLevel = toLevel;
        _progress = 0;
        ClearPendingTarget();
        Started++;
    }

    private bool ObservePendingTarget(int level, float deltaTime)
    {
        if (_pendingLevel != level)
        {
            _pendingLevel = level;
            _pendingFrames = 1;
            _pendingSeconds = Math.Max(0, deltaTime);
        }
        else
        {
            _pendingFrames++;
            _pendingSeconds += Math.Max(0, deltaTime);
        }

        return _pendingFrames >= RetargetStabilityFrames &&
               _pendingSeconds >= RetargetStabilitySeconds;
    }

    private void ClearPendingTarget()
    {
        _pendingLevel = -1;
        _pendingFrames = 0;
        _pendingSeconds = 0;
    }
}
