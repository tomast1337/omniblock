using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Diagnostics;
using OmniBlock.NBT;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Client.Resource.Pack;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>
/// E2E-only presentation fixture. Replicas are never registered, ticked, networked, or saved.
/// This isolates render cost; it deliberately does not benchmark natural spawning/server tracking.
/// A floating grid avoids terrain/other entities occluding the reference population.
/// </summary>
internal sealed class EntityRenderBaseline : IDisposable
{
    private const int MaxSamples = 10_000;
    private readonly OmniBlock _game;
    private readonly IWorldContext _world;
    private readonly List<Frame> _frames = [];
    private readonly object _manifest;
    private long _serial;
    private bool _sampling;
    private bool _overflow;
    private readonly double _x, _y, _z;
    private readonly int _width, _height;
    private readonly float _fov;
    private long _worldTime = 6000;
    private float _rain;
    private float _thunder;
    private bool _changed;
    private readonly bool _previousHideGui;
    private readonly TexturePack _pack;
    private readonly (int Distance, int Simulation, int Quality, float Gamma, bool VSync, int? Fps, bool Bob) _settings;
    public List<Entity> Entities { get; }
    private readonly int _expectedInstances;

    internal readonly record struct Frame(double FrameMs, double EntityCpuMs, double PoseMs,
        double BatchSubmitMs, int VisibleEntities, int Instances, int DrawBatches,
        int InstanceUploads, long InstanceUploadBytes, int TerrainPending,
        double TerrainSubmitCpuMs, double VisibilityCpuMs, double ServerTickMs,
        EntityLodSelector.Snapshot EntityLod);

    public EntityRenderBaseline(OmniBlock game, string scene, int count, double distance)
    {
        _game = game;
        _world = game.World ?? throw new InvalidOperationException("No client world for baseline.");
        var camera = game.Camera ?? throw new InvalidOperationException("No camera for baseline.");
        _x = camera.X; _y = camera.Y; _z = camera.Z;
        _width = game.DisplayWidth; _height = game.DisplayHeight; _fov = game.Options.Fov;
        _settings = ReadSettings();
        Entities = CreateEntities(_world, scene, count, distance, _x, _y, _z);
        _expectedInstances = Entities.Count + Entities.Count(e =>
            e.Behaviors.Find<WoolBehavior>() is { } wool && !wool.IsShearedOn(e));
        var assets = new SortedDictionary<string, string>();
        var pack = _pack = game.TexturePackList.SelectedTexturePack;
        var types = Entities.Select(e => e.Type!).Distinct().ToArray();
        foreach (var type in types)
        {
            void HashTexture(string path)
            {
                using var stream = pack.GetResourceAsStream(path)
                    ?? throw new InvalidOperationException($"Baseline missing texture '{path}'.");
                assets[path] = Convert.ToHexString(SHA256.HashData(stream));
            }
            HashTexture(type.RequireDefinition().Texture);
            if (type.RenderDescriptor is not { } descriptor) continue;
            foreach (var field in new[] { "Model", "OverlayModel" })
                if (descriptor.Definition.TryGetProperty(field, out var model))
                {
                    var path = ModelAssetPath(model.GetString()!);
                    using var stream = File.OpenRead(path);
                    assets[$"models/{model.GetString()}"] = Convert.ToHexString(SHA256.HashData(stream));
                }
            if (descriptor.Definition.TryGetProperty("OverlayTexture", out var overlay))
                HashTexture(overlay.GetString()!);
        }
        _manifest = new
        {
            schema = 1, fixture = "stationary-grid-v1", scene, count, distance,
            expectedInstances = _expectedInstances,
            seed = _world.Seed, worldTime = 6000, rain = 0, thunder = 0,
            camera = new { x = _x, y = _y, z = _z, yaw = 0, pitch = 0 },
            viewport = new { width = _width, height = _height }, fovOption = _fov,
            game.Options.RenderDistance, game.Options.SimulationDistance, game.Options.PresentationQuality,
            game.Options.Gamma, game.Options.ViewBobbing,
            game.Options.VSync, game.Options.MaxFramesPerSecond,
            texturePack = pack.TexturePackFileName, assets,
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            processors = Environment.ProcessorCount,
            assemblyVersion = typeof(OmniBlock).Assembly.GetName().Version?.ToString(),
            buildConfiguration = typeof(OmniBlock).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
            clientAssemblySha256 = HashFile(typeof(OmniBlock).Assembly.Location),
            sharedAssemblySha256 = HashFile(typeof(Entity).Assembly.Location),
            renderDescriptors = types.Select(t => new { t.Id, t.RenderDescriptor?.Definition }).ToArray(),
            gpuEntityPassMs = (double?)null,
            gpuTimingStatus = "unavailable: device does not request timestamp queries",
            population = "client-only replicas; server simulation continues independently",
            tracking = new { ordinaryMobRange = 512, clamp = "server view-distance cap; fixture bypasses tracking" },
            uploadScope = "pose instance storage only; excludes uniforms, static geometry, textures",
            geometryPath = "selected client presentation; per-frame EntityLod records 3D/impostor use"
        };
        _previousHideGui = game.Options.HideGUI;
        game.Options.HideGUI = true; // keep HUD, arm and screenshot chat out of reference pixels
    }

    public void Dispose() => _game.Options.HideGUI = _previousHideGui;

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    // Fixed built-in fixture adapters, not a general mod capture/dependency API.
    internal static string ModelAssetPath(string model) => BbModelLoader.GetEntityModelPath(model switch
    {
        "sheep_fur" => "sheepfur",
        "pig_saddle" => "pig", // ModelPig with inflation, same source geometry
        "creeper_charged" => "creeper", // ModelCreeper with inflation, same source geometry
        "slime" => "slimebody",
        "slime_cube" => "slimecube",
        _ => model
    });

    internal static List<Entity> CreateEntities(IWorldContext world, string scene, int count,
        double distance, double x, double y, double z)
    {
        if (scene is not ("chicken" or "cow" or "creeper" or "ghast" or "giant" or "pig" or
            "pigzombie" or "sheep" or "skeleton" or "slime" or "spider" or "squid" or "wolf" or
            "zombie" or "mixed" or "empty") ||
            count is < 0 or > 256 ||
            !double.IsFinite(distance) || distance is < 8 or > 120 ||
            (scene == "empty" ? count != 0 : count == 0))
            throw new ArgumentException(
                "Use a shipped living mob or mixed (1..256), or empty (0), distance 8..120.");
        var entities = new List<Entity>(count);
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = columns == 0 ? 0 : (count + columns - 1) / columns;
        for (var i = 0; i < count; i++)
        {
            var name = scene == "mixed" ? new[] { "cow", "sheep", "pig" }[i % 3] : scene;
            var entity = world.Content.EntityTypes.Create("omniblock:" + name, world);
            entity.SetPosition(x + (i % columns - (columns - 1) / 2.0) * 2.5,
                y + (i / columns - (rows - 1) / 2.0) * 2.5, z + distance);
            entity.LastTickX = entity.PrevX = entity.X;
            entity.LastTickY = entity.PrevY = entity.Y;
            entity.LastTickZ = entity.PrevZ = entity.Z;
            entity.Yaw = entity.PrevYaw = entity.Pitch = entity.PrevPitch = 0;
            if (entity.Behaviors.Find<WoolBehavior>() is { } wool)
            {
                var nbt = new NBTTagCompound();
                nbt.SetByte("Color", (sbyte)(i % 16));
                nbt.SetBoolean("Sheared", (i / 16) % 2 != 0);
                wool.OnReadNbt(entity, nbt);
            }
            entities.Add(entity);
        }
        return entities;
    }

    private (int, int, int, float, bool, int?, bool) ReadSettings() =>
        (_game.Options.RenderDistance, _game.Options.SimulationDistance, _game.Options.PresentationQuality,
            _game.Options.Gamma, _game.Options.VSync, _game.Options.MaxFramesPerSecond, _game.Options.ViewBobbing);

    public bool BelongsTo(IWorldContext? world) => ReferenceEquals(_world, world);

    /// <summary>Restricted deterministic animation/effect state for impostor E2E coverage.</summary>
    public bool SetImpostorState(string state)
    {
        if (Entities.Count == 0 || Entities.Any(e => e is not EntityLiving)) return false;
        static void Set(EntityLiving entity, string property, object value) =>
            typeof(EntityLiving).GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .SetValue(entity, value);
        foreach (var entity in Entities.Cast<EntityLiving>())
        {
            if (state == "idle")
            {
                Set(entity, nameof(EntityLiving.LastWalkAnimationSpeed), 0f);
                Set(entity, nameof(EntityLiving.WalkAnimationSpeed), 0f);
                Set(entity, nameof(EntityLiving.AnimationPhase), 0f);
                Set(entity, nameof(EntityLiving.HurtTime), 0);
            }
            else if (state is "walk-0" or "walk-1" or "walk-2" or "walk-3")
            {
                var phase = state[^1] - '0';
                Set(entity, nameof(EntityLiving.LastWalkAnimationSpeed), 1f);
                Set(entity, nameof(EntityLiving.WalkAnimationSpeed), 1f);
                Set(entity, nameof(EntityLiving.AnimationPhase), phase * MathF.PI / 2 / .6662f);
                Set(entity, nameof(EntityLiving.HurtTime), 0);
            }
            else if (state == "hurt") entity.AnimateHurt();
            else return false;
        }
        return true;
    }

    /// <summary>Restricted deterministic environment states for paired Phase 4 screenshots.</summary>
    public bool SetEnvironment(string state)
    {
        (_worldTime, _rain, _thunder) = state switch
        {
            "day" => (6000, 0f, 0f),
            "night" => (18000, 0f, 0f),
            "storm" => (6000, 1f, 1f),
            _ => (_worldTime, _rain, _thunder)
        };
        return state is "day" or "night" or "storm";
    }

    public void PrepareFrame()
    {
        // Pin client presentation only, never touch the integrated server's world/RNG.
        var world = _game.World;
        if (world is null || !BelongsTo(world)) { _changed = true; return; }
        world.SetTime(_worldTime);
        world.Environment.SetRainGradient(_rain);
        world.Environment.SetThunderGradient(_thunder);
        world.Environment.UpdateSkyBrightness();
        var camera = _game.Camera;
        if (camera != null)
        {
            var luminance = world.GetLuminance(
                MathHelper.Floor(camera.X), MathHelper.Floor(camera.Y), MathHelper.Floor(camera.Z));
            var renderDistanceFactor = Math.Clamp((_game.Options.RenderDistance - 4f) / 28f, 0f, 1f);
            _game.GameRenderer.CameraController.PinWorldBrightness(
                luminance * (1 - renderDistanceFactor) + renderDistanceFactor);
        }
        _changed |= camera == null || Math.Abs(camera.X - _x) > 0.001 ||
            Math.Abs(camera.Y - _y) > 0.001 || Math.Abs(camera.Z - _z) > 0.001 ||
            camera.Yaw != 0 || camera.Pitch != 0 || _game.Options.Fov != _fov ||
            _game.DisplayWidth != _width || _game.DisplayHeight != _height ||
            ReadSettings() != _settings || !ReferenceEquals(_pack, _game.TexturePackList.SelectedTexturePack);
    }

    public void BeginSample()
    {
        _frames.Clear();
        _overflow = false;
        _serial = EntityPresentationMetrics.Last.Serial;
        _sampling = true;
    }

    public void RecordFrame(double frameMs)
    {
        if (!_sampling) return;
        var metrics = EntityPresentationMetrics.Last;
        if (metrics.Serial == _serial) return; // no world pass this frame, not stale data
        _serial = metrics.Serial;
        if (_frames.Count == MaxSamples) { _overflow = true; return; }
        _frames.Add(new Frame(frameMs, metrics.CpuMs, metrics.PoseMs, metrics.SubmitMs,
            _game.WorldRenderer.CountEntitiesRendered, metrics.Instances, metrics.DrawBatches,
            metrics.InstanceUploads, metrics.InstanceUploadBytes,
            _game.WorldRenderer.ChunkRenderer.PendingMeshWork,
            _game.WorldRenderer.ChunkRenderer.PresentationProfile.TerrainSubmit.LastMs,
            _game.WorldRenderer.ChunkRenderer.PresentationProfile.FindVisible.LastMs,
            MetricRegistry.Get(ServerMetrics.Mspt), _game.WorldRenderer.EntityLod.Last));
    }

    public (bool Valid, string Json) FinishSample()
    {
        _sampling = false;
        var impostors = _game.WorldRenderer.EntityImpostors;
        var valid = !_changed && !_overflow && _frames.Count >= 30 &&
            _frames.All(f => f.VisibleEntities == Entities.Count && f.Instances == _expectedInstances);
        var json = JsonSerializer.Serialize(new
        {
            manifest = _manifest, valid, environmentChanged = _changed, overflow = _overflow,
            sampleCount = _frames.Count,
            impostorDiagnostics = new
            {
                impostors.Enabled, impostors.Ready, impostors.CompletedViews,
                impostors.Failures, impostors.Invalidations, impostors.PendingFallbacks,
                impostors.MemoryHits, impostors.DiskHits, impostors.CacheMisses,
                impostors.CacheWrites, impostors.CacheErrors, impostors.Cancellations,
                impostors.StaleResults, impostors.CapturedViews,
                impostors.OldestBakeQueueAgeMs, impostors.LastBakeLatencyMs,
                impostors.AverageBakeLatencyMs, impostors.CaptureCpuMs,
                impostors.MemoryBytes, impostors.ResidentGpuBytes, impostors.StagingBytes,
                impostors.ResidentAtlasCount, impostors.LastDrawBatches
            },
            summary = new
            {
                frameMs = Summarize(_frames.Select(f => f.FrameMs)),
                entityCpuMs = Summarize(_frames.Select(f => f.EntityCpuMs)),
                poseMs = Summarize(_frames.Select(f => f.PoseMs)),
                batchSubmitMs = Summarize(_frames.Select(f => f.BatchSubmitMs))
            },
            frames = _frames
        }, new JsonSerializerOptions { WriteIndented = true });
        return (valid, json);
    }

    internal static Distribution Summarize(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return new Distribution(null, null, null);
        double Percentile(double p) => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * p) - 1)];
        return new Distribution(sorted.Average(), Percentile(0.5), Percentile(0.95));
    }
    internal readonly record struct Distribution(double? Mean, double? P50, double? P95);
}
