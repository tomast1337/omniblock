using System.Numerics;

namespace OmniBlock.Client.Rendering.Entities;

internal enum EntityLodTier { Model, Impostor }
internal enum EntityLodReason { NearOrLarge, ImpostorCandidate, UnsupportedProvider, UnsupportedState, InvalidView, Capacity }
internal readonly record struct LodPoint(double X, double Y, double Z)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    public double DistanceSquared(LodPoint other) =>
        (X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y) + (Z - other.Z) * (Z - other.Z);
    public Vector3 DirectionFrom(LodPoint other) => new((float)(X - other.X), (float)(Y - other.Y), (float)(Z - other.Z));
}

/// <summary>World-pass selection only. Confirm a valid atlas submission before skipping 3D.</summary>
internal sealed class EntityLodSelector(int capacity = 2048)
{
    internal const double EnterDistance = 80, ExitDistance = 64, EnterPixels = 24, ExitPixels = 32;
    private readonly Dictionary<object, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly List<object> _expired = [];
    private object? _world, _content;
    private long _resources, _frame;
    private LodPoint _camera;
    private int _observed, _intended, _unsupportedProvider, _unsupportedState, _invalid, _capacity, _transitions;
    public Snapshot Last { get; private set; }
    public int StateCount => _entries.Count;
    public long ResetCount { get; private set; }
    internal readonly record struct Snapshot(int Observed, int IntendedImpostors, int ModelDraws,
        int ImpostorDraws, int UnsupportedProvider, int UnsupportedState, int InvalidView,
        int CapacityFallbacks, int TierTransitions, int RetainedStates, long Resets);
    internal readonly record struct Decision(EntityLodTier Intended, EntityLodReason Reason, int ViewIndex, double ProjectedPixels);
    private sealed class Entry
    {
        public EntityLodTier Tier;
        public int View;
        public string Variant = "";
        public LodPoint Position;
        public long Frame;
    }

    public void Clear()
    {
        _entries.Clear();
        _world = _content = null;
        Last = default;
        ResetCount++;
    }

    public void Forget(object lifetime) => _entries.Remove(lifetime);

    public void BeginFrame(object world, object content, long resources, LodPoint camera)
    {
        if (!ReferenceEquals(world, _world) || !ReferenceEquals(content, _content) || resources != _resources ||
            !camera.IsFinite || _camera.DistanceSquared(camera) > 32 * 32)
            Clear();
        _world = world; _content = content; _resources = resources; _camera = camera;
        _frame++;
        _observed = _intended = _unsupportedProvider = _unsupportedState = _invalid = _capacity = _transitions = 0;
    }

    public Decision Select(object lifetime, LodPoint position, bool providerSupported, bool stateSupported,
        string variant, double visualDiameter, double bodyYaw, Vector3 cameraForward,
        double verticalFovDegrees, int viewportHeight, bool forceImpostorForTest = false)
    {
        _observed++;
        Decision Fallback(EntityLodReason reason)
        {
            _entries.Remove(lifetime);
            switch (reason)
            {
                case EntityLodReason.UnsupportedProvider: _unsupportedProvider++; break;
                case EntityLodReason.UnsupportedState: _unsupportedState++; break;
                case EntityLodReason.InvalidView: _invalid++; break;
                case EntityLodReason.Capacity: _capacity++; break;
            }
            return new Decision(EntityLodTier.Model, reason, -1, double.PositiveInfinity);
        }
        if (!providerSupported) return Fallback(EntityLodReason.UnsupportedProvider);
        if (!stateSupported) return Fallback(EntityLodReason.UnsupportedState);
        if (!position.IsFinite || !_camera.IsFinite || !double.IsFinite(bodyYaw) ||
            !double.IsFinite(visualDiameter) || visualDiameter <= 0 || viewportHeight <= 0 ||
            !double.IsFinite(verticalFovDegrees) || verticalFovDegrees is <= 0 or >= 179 ||
            !float.IsFinite(cameraForward.LengthSquared()) || cameraForward.LengthSquared() < 0.99f)
            return Fallback(EntityLodReason.InvalidView);

        var toEntity = position.DirectionFrom(_camera);
        if (!float.IsFinite(toEntity.LengthSquared())) return Fallback(EntityLodReason.InvalidView);
        // Use nearest sphere depth, not radial distance: off-axis / steep camera views must not
        // underestimate projected size. Provider bounds enclose the supported pose in all views.
        var depth = Vector3.Dot(toEntity, Vector3.Normalize(cameraForward)) - visualDiameter / 2;
        var pixels = depth <= 0 ? double.PositiveInfinity : visualDiameter * viewportHeight /
            (2 * depth * Math.Tan(verticalFovDegrees * Math.PI / 360));
        var distanceSquared = position.DistanceSquared(_camera);
        _entries.TryGetValue(lifetime, out var previous);
        if (previous != null && (previous.Variant != variant || previous.Position.DistanceSquared(position) > 16 * 16))
            previous = null;
        var tier = previous?.Tier ?? EntityLodTier.Model;
        if (tier == EntityLodTier.Model && distanceSquared > EnterDistance * EnterDistance && pixels <= EnterPixels) tier = EntityLodTier.Impostor;
        else if (tier == EntityLodTier.Impostor && (distanceSquared < ExitDistance * ExitDistance || pixels >= ExitPixels)) tier = EntityLodTier.Model;
        // Restricted visual comparisons only; never bypass provider/state/projection/capacity gates.
        if (forceImpostorForTest && double.IsFinite(pixels)) tier = EntityLodTier.Impostor;

        if (previous != null && previous.Tier != tier) _transitions++;

        if (!_entries.ContainsKey(lifetime) && _entries.Count >= Math.Max(0, capacity))
            return Fallback(EntityLodReason.Capacity);
        var view = tier == EntityLodTier.Impostor ?
            EntityLodDirections.Select(_camera.DirectionFrom(position), bodyYaw, previous?.View ?? -1) : -1;
        if (!_entries.TryGetValue(lifetime, out var entry)) _entries.Add(lifetime, entry = new Entry());
        entry.Tier = tier; entry.View = view; entry.Variant = variant; entry.Position = position; entry.Frame = _frame;
        if (tier == EntityLodTier.Impostor) _intended++;
        return new Decision(tier, tier == EntityLodTier.Impostor ? EntityLodReason.ImpostorCandidate : EntityLodReason.NearOrLarge, view, pixels);
    }

    public void EndFrame(int impostorSubmissions = 0)
    {
        // Only eligible, visible lifetimes retain state. Removal/culling cannot leave tombstones.
        // Iteration is capped at capacity and never enumerates the whole simulation catalog.
        _expired.Clear();
        foreach (var (key, entry) in _entries)
            if (entry.Frame != _frame) _expired.Add(key);
        foreach (var key in _expired) _entries.Remove(key);
        _expired.Clear();
        if (impostorSubmissions < 0 || impostorSubmissions > _intended) throw new ArgumentOutOfRangeException(nameof(impostorSubmissions));
        Last = new Snapshot(_observed, _intended, _observed - impostorSubmissions, impostorSubmissions, _unsupportedProvider, _unsupportedState,
            _invalid, _capacity, _transitions, _entries.Count, ResetCount);
    }
}

/// <summary>Layout v1: rings -45/0/+45, eight azimuths each, then +Y and -Y poles.</summary>
internal static class EntityLodDirections
{
    private static readonly Vector3[] s_directions = Build();
    public static Vector3 Get(int index) => s_directions[index];
    private static Vector3[] Build()
    {
        var result = new Vector3[26];
        for (var ring = 0; ring < 3; ring++)
        for (var azimuth = 0; azimuth < 8; azimuth++)
        {
            var elevation = (ring - 1) * Math.PI / 4;
            var angle = azimuth * Math.PI / 4;
            result[ring * 8 + azimuth] = new Vector3((float)(Math.Sin(angle) * Math.Cos(elevation)),
                (float)Math.Sin(elevation), (float)(Math.Cos(angle) * Math.Cos(elevation)));
        }
        result[24] = Vector3.UnitY; result[25] = -Vector3.UnitY;
        return result;
    }
    public static int Select(Vector3 entityToCamera, double bodyYaw, int previous = -1)
    {
        if (!float.IsFinite(entityToCamera.LengthSquared()) || entityToCamera.LengthSquared() < 1e-12f || !double.IsFinite(bodyYaw)) return -1;
        // Entity forward is (-sin(yaw), 0, cos(yaw)); undo that yaw to obtain local +Z.
        var yaw = (bodyYaw % 360) * Math.PI / 180;
        var direction = Vector3.Normalize(new Vector3(
            (float)(Math.Cos(yaw) * entityToCamera.X + Math.Sin(yaw) * entityToCamera.Z),
            entityToCamera.Y,
            (float)(-Math.Sin(yaw) * entityToCamera.X + Math.Cos(yaw) * entityToCamera.Z)));
        var best = 0; var score = float.NegativeInfinity;
        for (var i = 0; i < s_directions.Length; i++)
        {
            var dot = Vector3.Dot(direction, s_directions[i]);
            if (dot > score) { best = i; score = dot; }
        }
        // A 0.02 cosine advantage is required to change bins, including ring/pole boundaries.
        return previous is >= 0 and < 26 && score - Vector3.Dot(direction, s_directions[previous]) < 0.02f ? previous : best;
    }
    public static float InterpolateYaw(float previous, float current, float partialTicks)
    {
        var delta = current - previous;
        delta -= MathF.Floor((delta + 180) / 360) * 360;
        return previous + delta * partialTicks;
    }
}
