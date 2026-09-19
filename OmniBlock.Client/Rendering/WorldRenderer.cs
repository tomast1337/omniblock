using System.Numerics;
using System.Diagnostics;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Blocks.Entities;
using OmniBlock.Client.Entities.FX;
using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Client.Rendering.Blocks.Entities;
using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Client.Rendering.Particles;
using OmniBlock.Client.Worlds;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using OmniBlock.Network.Messages;
using OmniBlock.Profiling;
using OmniBlock.Util;
using OmniBlock.Util.Hit;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering;

public class WorldRenderer : IWorldEventListener, IDisposable
{
    private const int CloudsRenderDistance = 128;

    private const uint SkyUniformSize = 192;
    private const uint CloudUniformSize = 256;
    private readonly OmniBlock _game;
    private readonly IStaticMesh _skyAbove;
    private readonly IStaticMesh _skyBelow;
    private readonly IStaticMesh _stars;
    private readonly TextureManager _textureManager;
    private int _cloudOffsetX;

    /// <summary>
    ///     One mesh for the shader-based clouds, four for the legacy ones (bottom, top, and the two
    ///     side faces), rebuilt whenever the clouds quality option changes.
    /// </summary>
    private IStaticMesh[] _clouds = [];

    private int _cloudsQuality = -1;
    private Vector3D<float> _fogColor;
    private int _renderDistance = -1;
    private int _renderEntitiesStartupCounter = 2;

    private World _world;

    public WorldRenderer(OmniBlock gameInstance, TextureManager textureManager)
    {
        _game = gameInstance;
        _textureManager = textureManager;

        _stars = BuildStars();

        ChunkRenderer = new ChunkRenderer(gameInstance.World, _game.Options);
        EntityBatchRenderer.Initialize(_game.Options);

        OnCloudsQualityChanged();

        _skyAbove = BuildSkyPlane(16.0F, true);
        _skyBelow = BuildSkyPlane(-16.0F, false);

        // The sky and cloud geometry above is backend-agnostic — it goes through the draw-command
        // seam. Registrations rather than a lazy build, because the sky and cloud shaders are not
        // built from GL state (unlike gbuffers, which vary by RenderState). One pipeline per slot —
        // the draw target matches the slot it was given, so each pipeline is built once.
        if (RenderSystem.DrawTargetOrNull is WebGpuDrawTarget wgpuTarget)
        {
            wgpuTarget.RegisterSlotPipeline(ProgramSlot.SkyBasic, "shaders/sky.wgsl",
                SkyUniformSize, true);
            wgpuTarget.RegisterSlotPipeline(ProgramSlot.SkyTextured, "shaders/sky.wgsl",
                SkyUniformSize, true);
            wgpuTarget.RegisterSlotPipeline(ProgramSlot.Clouds, "shaders/cloud.wgsl",
                CloudUniformSize, true);
        }
    }

    public int CountEntitiesTotal { get; private set; }
    internal EntityLodSelector EntityLod { get; } = new();
    internal EntityImpostorSystem EntityImpostors { get; } = new();
    public int CountEntitiesRendered { get; private set; }
    public int CountEntitiesHidden { get; private set; }
    public int CountBlockEntitiesTotal { get; private set; }
    public int CountBlockEntitiesRendered { get; private set; }
    public int CountBlockEntitiesHidden { get; private set; }
    public ChunkRenderer ChunkRenderer { get; private set; }
    internal ClientTerrainLodRenderer? TerrainLod { get; private set; }
    public float DamagePartialTime { get; set; }

    /// <summary>Whether the draw target has slot pipelines registered for the sky.</summary>
    private bool HasSkySlotPipeline =>
        RenderSystem.DrawTargetOrNull is WebGpuDrawTarget t
        && t.HasSlotPipeline(ProgramSlot.SkyBasic);

    /// <summary>Whether the draw target has a slot pipeline registered for clouds.</summary>
    private bool HasCloudSlotPipeline =>
        RenderSystem.DrawTargetOrNull is WebGpuDrawTarget t
        && t.HasSlotPipeline(ProgramSlot.Clouds);

    public void Dispose()
    {
        EntityImpostors.Dispose();
        if (_world is Worlds.ClientWorld clientWorld)
            clientWorld.NetworkHandler.PresentationRelocated -= OnPresentationRelocated;
        EntityLod.Clear();
        TerrainLod?.Dispose();
        ChunkRenderer?.Dispose();

        _stars.Dispose();
        _skyAbove.Dispose();
        _skyBelow.Dispose();

        foreach (var mesh in _clouds)
        {
            mesh.Dispose();
        }

        _clouds = [];
    }

    public void BlockUpdate(int x, int y, int z)
    {
        TerrainLod?.ObserveRegion(x, z, x, z);
        MarkBlocksDirty(x - 1, y - 1, z - 1, x + 1, y + 1, z + 1);
    }

    public void SetBlocksDirty(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        if (!_world.BlockHost.IsRegionLoaded(minX, minY, minZ, maxX, maxY, maxZ))
        {
            return;
        }

        TerrainLod?.ObserveRegion(minX, minZ, maxX, maxZ);
        MarkBlocksDirty(minX - 1, minY - 1, minZ - 1, maxX + 1, maxY + 1, maxZ + 1);
    }

    public void SetLightDirty(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        if (!_world.BlockHost.IsRegionLoaded(minX, minY, minZ, maxX, maxY, maxZ)) return;

        var (start, end) = GetSectionRange(
            minX - 1, minY - 1, minZ - 1,
            maxX + 1, maxY + 1, maxZ + 1);
        start.Y = Math.Max(0, start.Y);
        end.Y = Math.Min(ChuckFormat.WorldHeight / SubChunkRenderer.Size - 1, end.Y);

        for (var x = start.X; x <= end.X; x++)
        for (var y = start.Y; y <= end.Y; y++)
        for (var z = start.Z; z <= end.Z; z++)
            ChunkRenderer.MarkLightDirty(new Vector3D<int>(x, y, z) * SubChunkRenderer.Size);
    }

    public void SetBlocksDirtyForStreaming(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        // A streamed chunk is allowed to arrive before any of its neighbors. Do not reject the
        // whole notification based on that incomplete neighborhood: MarkStreamingDirty checks the
        // source column per section, while the expanded range below invalidates any already
        // resident mesh on either side of each newly available boundary.
        TerrainLod?.ObserveRegion(minX, minZ, maxX, maxZ);
        var (start, end) = GetStreamingSectionRange(
            minX, minY, minZ,
            maxX, maxY, maxZ);

        for (var x = start.X; x <= end.X; x++)
        for (var y = start.Y; y <= end.Y; y++)
        for (var z = start.Z; z <= end.Z; z++)
            ChunkRenderer.MarkStreamingDirty(new Vector3D<int>(x, y, z) * SubChunkRenderer.Size);
    }

    internal static (Vector3D<int> Start, Vector3D<int> End) GetStreamingSectionRange(
        int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        var (start, end) = GetSectionRange(
            minX - 1, minY - 1, minZ - 1,
            maxX + 1, maxY + 1, maxZ + 1);
        start.Y = Math.Max(0, start.Y);
        end.Y = Math.Min(ChuckFormat.WorldHeight / SubChunkRenderer.Size - 1, end.Y);
        return (start, end);
    }

    public void PlayStreaming(string soundName, int x, int y, int z)
    {
        if (soundName != null)
        {
            _game.HUD.Chat.SetRecordPlaying(soundName);
        }

        _game.SoundManager.PlayStreaming(soundName, x, y, z, 1.0F, 1.0F);
    }

    public void PlaySound(string soundName, double x, double y, double z, float volume, float pitch)
    {
        var maxDistance = 16.0F;
        if (volume > 1.0F)
        {
            maxDistance *= volume;
        }

        if (_game.Camera.GetSquaredDistance(x, y, z) < maxDistance * maxDistance)
        {
            _game.SoundManager.PlaySound(soundName, (float)x, (float)y, (float)z, volume, pitch);
        }
    }

    public void SpawnParticle(string particleName, double x, double y, double z, double velocityX, double velocityY, double velocityZ)
    {
        if (_game != null && _game.Camera != null && _game.ParticleManager != null)
        {
            var cameraDx = _game.Camera.X - x;
            var cameraDy = _game.Camera.Y - y;
            var cameraDz = _game.Camera.Z - z;
            var maxDistance = 16.0D;
            if (cameraDx * cameraDx + cameraDy * cameraDy + cameraDz * cameraDz <= maxDistance * maxDistance)
            {
                var pm = _game.ParticleManager;
                switch (particleName)
                {
                    case "bubble": pm.AddBubble(x, y, z, velocityX, velocityY, velocityZ); break;
                    case "smoke": pm.AddSmoke(x, y, z, velocityX, velocityY, velocityZ); break;
                    case "note": pm.AddNote(x, y, z, velocityX, velocityY, velocityZ); break;
                    case "portal": pm.AddPortal(x, y, z, velocityX, velocityY, velocityZ); break;
                    case "explode": pm.AddExplode(x, y, z, velocityX, velocityY, velocityZ); break;
                    case "flame": pm.AddFlame(x, y, z, velocityX, velocityY, velocityZ); break;
                    case "lava": pm.AddLava(x, y, z); break;
                    case "footstep": pm.AddSpecialParticle(new LegacyParticleAdapter(new EntityFootStepFX(_textureManager, _world, x, y, z))); break;
                    case "splash": pm.AddSplash(x, y, z, velocityX, velocityY, velocityZ); break;
                    case "largesmoke": pm.AddSmoke(x, y, z, velocityX, velocityY, velocityZ, 2.5f); break;
                    case "reddust": pm.AddReddust(x, y, z, (float)velocityX, (float)velocityY, (float)velocityZ); break;
                    case "snowballpoof": pm.AddSlime(x, y, z, _world.Content.Items.Get("omniblock:snowball")); break;
                    case "snowshovel": pm.AddSnowShovel(x, y, z, velocityX, velocityY, velocityZ); break;
                    case "slime": pm.AddSlime(x, y, z, _world.Content.Items.Get("omniblock:slimeball")); break;
                    case "heart": pm.AddHeart(x, y, z, velocityX, velocityY, velocityZ); break;
                }
            }
        }
    }

    public void NotifyEntityAdded(Entity entity)
    {
        entity.UpdateCloak();
        EntityRenderDispatcher.Instance.SkinManager.RequestDownload((entity as EntityPlayer)?.Name);
    }

    public void NotifyEntityRemoved(Entity entity)
    {
    }

    public void NotifyAmbientDarknessChanged()
    {
        // Chunk vertices carry raw sky and block light independently. Ambient darkness is applied
        // by chunk.wgsl from the per-frame WorldLight uniform, so sunrise and sunset require no
        // geometry rebuild. Actual light propagation still reaches BlockUpdate/SetBlocksDirty.
    }

    public void UpdateBlockEntity(int x, int y, int z, BlockEntity blockEntity)
    {
    }

    public void WorldEvent(EntityPlayer? player, int eventId, int x, int y, int z, int data)
    {
        var random = _world.Random;
        int blockId;
        switch (eventId)
        {
            case 1000:
                _game.SoundManager.PlaySound("random.click", x, y, z, 1.0F, 1.0F);
                break;
            case 1001:
                _game.SoundManager.PlaySound("random.click", x, y, z, 1.0F, 1.2F);
                break;
            case 1002:
                _game.SoundManager.PlaySound("random.bow", x, y, z, 1.0F, 1.2F);
                break;
            case 1003:
                _game.SoundManager.PlayDoorSound(x, y, z);
                break;
            case 1004:
                _game.SoundManager.PlaySound("random.fizz", x + 0.5F, y + 0.5F, z + 0.5F, 0.5F, 2.6F + (random.NextFloat() - random.NextFloat()) * 0.8F);
                for (var particleIndex = 0; particleIndex < Random.Shared.Next(8, 12); ++particleIndex)
                {
                    _world.Broadcaster.AddParticle("largesmoke", x + random.NextDouble(), y + 1.2D, z + random.NextDouble(), 0.0D, 0.0D, 0.0D);
                }

                break;
            case 1005:
                if (_world.Content.Items.TryGetByProtocolId(data, out var item) && item?.GetBehavior<RecordBehavior>() is { } record)
                {
                    _game.SoundManager.PlayStreaming(record.RecordName, x, y, z, 1.0F, 1.0F);
                }
                else
                {
                    _game.SoundManager.PlayStreaming(null, x, y, z, 1.0F, 1.0F);
                }

                break;
            case 2000:
                var offsetX = data % 3 - 1;
                var offsetZ = data / 3 % 3 - 1;
                var particleX = x + offsetX * 0.6D + 0.5D;
                var particleY = y + 0.5D;
                var particleZ = z + offsetZ * 0.6D + 0.5D;

                for (blockId = 0; blockId < 10; ++blockId)
                {
                    var speed = random.NextDouble() * 0.2D + 0.01D;
                    var smokeX = particleX + offsetX * 0.01D + (random.NextDouble() - 0.5D) * offsetZ * 0.5D;
                    var smokeY = particleY + (random.NextDouble() - 0.5D) * 0.5D;
                    var smokeZ = particleZ + offsetZ * 0.01D + (random.NextDouble() - 0.5D) * offsetX * 0.5D;
                    var velocityX = offsetX * speed + random.NextGaussian() * 0.01D;
                    var velocityY = -0.03D + random.NextGaussian() * 0.01D;
                    var velocityZ = offsetZ * speed + random.NextGaussian() * 0.01D;
                    SpawnParticle("smoke", smokeX, smokeY, smokeZ, velocityX, velocityY, velocityZ);
                }

                return;
            case 2001: // This is for breaking a block
                WorldEventBreak(data & 255, (data >> 8) & 255, x, y, z);
                break;
        }
    }

    public void PlayNote(int x, int y, int z, int soundType, int pitch)
    {
    }

    public void BroadcastEntityEvent(Entity entity, byte @event)
    {
    }

    private void OnCloudsQualityChanged()
    {
        if (_cloudsQuality == _game.Options.CloudsQuality) return;
        if (_cloudsQuality == -1 || _game.Options.CloudsQuality == 0 || _game.Options.CloudsQuality == 2)
        {
            // The meshes belong to whichever quality built them, so the old set goes before the new
            // one is built rather than leaking a buffer on every change.
            foreach (var mesh in _clouds)
            {
                mesh.Dispose();
            }

            _clouds = [];

            if (_game.Options.CloudsQuality <= 0) _clouds = BuildLegacyCloudMeshes();
            else if (_game.Options.CloudsQuality > 1) _clouds = [BuildCloudMesh()];
        }

        _cloudsQuality = _game.Options.CloudsQuality;
    }

    public void SetFogColor(float r, float g, float b) => _fogColor = new Vector3D<float>(r, g, b);

    /// <summary>
    ///     The flat sheet of quads the sky colour is painted onto, at <paramref name="y" />.
    /// </summary>
    /// <remarks>
    ///     Wound the opposite way for the sheet below the horizon, since it is seen from the other
    ///     side. The two used to be built by separate loops that differed only in that and in the
    ///     height, one of them starting a batch per quad and the other batching the lot; the
    ///     geometry was always the same.
    /// </remarks>
    private static IStaticMesh BuildSkyPlane(float y, bool facingUp)
    {
        const int step = 64;
        const int radius = 256 / step + 2;

        var tessellator = Tessellator.instance;
        tessellator.startDrawingQuads();

        for (var x = -step * radius; x <= step * radius; x += step)
        {
            for (var z = -step * radius; z <= step * radius; z += step)
            {
                if (facingUp)
                {
                    tessellator.addVertex(x + 0, y, z + 0);
                    tessellator.addVertex(x + step, y, z + 0);
                    tessellator.addVertex(x + step, y, z + step);
                    tessellator.addVertex(x + 0, y, z + step);
                }
                else
                {
                    tessellator.addVertex(x + step, y, z + 0);
                    tessellator.addVertex(x + 0, y, z + 0);
                    tessellator.addVertex(x + 0, y, z + step);
                    tessellator.addVertex(x + step, y, z + step);
                }
            }
        }

        return tessellator.captureStatic();
    }

    private static IStaticMesh BuildStars()
    {
        Random random = new(10842);
        var tessellator = Tessellator.instance;
        tessellator.startDrawingQuads();

        for (var starIndex = 0; starIndex < 1500; ++starIndex)
        {
            var dirX = random.NextDouble() * 2.0 - 1.0;
            var dirY = random.NextDouble() * 2.0 - 1.0;
            var dirZ = random.NextDouble() * 2.0 - 1.0;
            var starSize = 0.25 + random.NextDouble() * 0.25;
            var dirLengthSq = dirX * dirX + dirY * dirY + dirZ * dirZ;
            if (dirLengthSq < 1.0 && dirLengthSq > 0.01)
            {
                dirLengthSq = 1.0 / Math.Sqrt(dirLengthSq);
                dirX *= dirLengthSq;
                dirY *= dirLengthSq;
                dirZ *= dirLengthSq;
                var starX = dirX * 100.0;
                var starY = dirY * 100.0;
                var starZ = dirZ * 100.0;
                var yaw = Math.Atan2(dirX, dirZ);
                var sinYaw = Math.Sin(yaw);
                var cosYaw = Math.Cos(yaw);
                var pitch = Math.Atan2(Math.Sqrt(dirX * dirX + dirZ * dirZ), dirY);
                var sinPitch = Math.Sin(pitch);
                var cosPitch = Math.Cos(pitch);
                var roll = random.NextDouble() * Math.PI * 2.0;
                var sinRoll = Math.Sin(roll);
                var cosRoll = Math.Cos(roll);

                for (var cornerIndex = 0; cornerIndex < 4; ++cornerIndex)
                {
                    var cornerY = 0.0D;
                    var cornerX = ((cornerIndex & 2) - 1) * starSize;
                    var cornerZ = (((cornerIndex + 1) & 2) - 1) * starSize;
                    var rotatedCornerX = cornerX * cosRoll - cornerZ * sinRoll;
                    var rotatedCornerZ = cornerZ * cosRoll + cornerX * sinRoll;
                    var pitchedCornerY = rotatedCornerX * sinPitch + cornerY * cosPitch;
                    var pitchedCornerX = cornerY * sinPitch - rotatedCornerX * cosPitch;
                    var finalX = pitchedCornerX * sinYaw - rotatedCornerZ * cosYaw;
                    var finalZ = rotatedCornerZ * sinYaw + pitchedCornerX * cosYaw;
                    tessellator.addVertex(starX + finalX, starY + pitchedCornerY, starZ + finalZ);
                }
            }
        }

        return tessellator.captureStatic();
    }

    public void ChangeWorld(World world)
    {
        EntityImpostors.Reset();
        if (_world is Worlds.ClientWorld previousClientWorld)
            previousClientWorld.NetworkHandler.PresentationRelocated -= OnPresentationRelocated;
        EntityLod.Clear();
        _world?.EventListeners.Remove(this);

        EntityRenderDispatcher.Instance.World = world;
        _world = world;
        if (_world is Worlds.ClientWorld clientWorld)
            clientWorld.NetworkHandler.PresentationRelocated += OnPresentationRelocated;
        if (world != null)
        {
            world.EventListeners.Add(this);
            LoadRenderers();
        }
    }

    public void Tick(Entity view, float partialTicks)
    {
        if (view == null)
        {
            return;
        }

        OnCloudsQualityChanged();
        var viewX = view.LastTickX + (view.X - view.LastTickX) * partialTicks;
        var viewY = view.LastTickY + (view.Y - view.LastTickY) * partialTicks;
        var viewZ = view.LastTickZ + (view.Z - view.LastTickZ) * partialTicks;
        ChunkRenderer.Tick(
            new Vector3D<double>(viewX, viewY, viewZ),
            new Vector3D<double>(view.VelocityX, view.VelocityY, view.VelocityZ));
        var viewPosition = new Vector3D<double>(viewX, viewY, viewZ);
        if (TerrainLod is { } terrainLod)
        {
            if (_world is ClientWorld clientWorld)
            {
                for (var observed = 0; observed < 8 &&
                     clientWorld.TryDequeueTerrainLodStatus(out var status); observed++)
                    terrainLod.ObserveRemoteSpatialStatus(status.Tile, status.Status);
                for (var admitted = 0; admitted < 2 &&
                     clientWorld.TryDequeueTerrainLodTile(out var transfer); admitted++)
                    terrainLod.ObserveRemoteSpatialTile(transfer.Tile, transfer.WireBytes);
            }
            terrainLod.Tick(viewPosition);
            if (_world is ClientWorld remoteWorld)
            {
                if (remoteWorld.NetworkHandler.TryGetTerrainLodIdentity(
                        _world.Dimension.Id, out var cacheIdentity))
                {
                    var requests = terrainLod.TakeRemoteSpatialRequests(
                        viewPosition,
                        _game.Options.RenderDistance,
                        _game.Options.TerrainHorizonDistance,
                        maximumRequests: 4);
                    if (requests.Length > 0)
                        remoteWorld.NetworkHandler.SendMessage(new TerrainLodTileRequestMessage
                        {
                            Dimension = _world.Dimension.Id,
                            CacheIdentity = cacheIdentity,
                            Keys = requests
                        });
                }
            }
        }
    }

    public void LoadRenderers()
    {
        LeavesBehavior.SetGraphicsLevel(_world.Content.Blocks.Get("leaves"), true);
        _renderDistance = _game.Options.RenderDistance;

        // Spatial LOD presentations lease ranges from the chunk renderer's regional arenas, so
        // release those leases before destroying their shared arena owner.
        TerrainLod?.Dispose();
        ChunkRenderer?.Dispose();
        ChunkRenderer = new ChunkRenderer(_world, _game.Options);
        TerrainLod = new ClientTerrainLodRenderer(
            _world, (_world as ClientWorld)?.TerrainLodCache);
        ChunkRenderer.PresentationHandoff = TerrainLod;
        ChunkMeshVersion.ClearPool();

        _renderEntitiesStartupCounter = 2;
    }

    public void RenderEntities(Vec3D cameraPos, ICuller culler, float partialTicks)
    {
        if (_renderEntitiesStartupCounter > 0)
        {
            EntityLod.Clear();
            --_renderEntitiesStartupCounter;
        }
        else
        {
            using var entityMeasurement = EntityPresentationMetrics.Begin();
            BlockEntityRenderer.Instance.CacheActiveRenderInfo(_world, _textureManager, _game.TextRenderer, _game.Camera, partialTicks);
            EntityRenderDispatcher.Instance.CacheRenderInfo(_world, _textureManager, _game.TextRenderer, _game.Camera, _game.Options, partialTicks);

            EntityBatchRenderer.Instance.Begin();
            EntityInstanceBatchRenderer.Instance.Begin();
            CountEntitiesTotal = 0;
            CountEntitiesRendered = 0;
            CountEntitiesHidden = 0;
            CountBlockEntitiesTotal = 0;
            CountBlockEntitiesRendered = 0;
            CountBlockEntitiesHidden = 0;
            var camera = _game.Camera;
            var look = camera.GetLook(partialTicks);
            var cameraForward = new Vector3((float)look.X, (float)look.Y, (float)look.Z);
            var lodCamera = new LodPoint(cameraPos.X, cameraPos.Y, cameraPos.Z);
            var controller = _game.GameRenderer.CameraController;
            var lodFov = controller.GetFov(partialTicks);
            var effectiveLodFov = controller.CameraZoom > 0 && double.IsFinite(controller.CameraZoom) ?
                Math.Atan(Math.Tan(lodFov * Math.PI / 360) / controller.CameraZoom) * 360 / Math.PI : double.NaN;
            EntityLod.BeginFrame(_world, _world.Content, _textureManager.ResourceGeneration, lodCamera);
            double lodCpuMs = 0;
            var presentationPolicy = WorldPresentationPolicy.From(_game.Options);
            var simulationDistance = _world is ClientWorld clientWorld &&
                                     clientWorld.NetworkHandler.ServerSimulationDistance > 0
                ? clientWorld.NetworkHandler.ServerSimulationDistance
                : _game.Options.SimulationDistance;
            EntityRenderDispatcher.OffsetX = camera.LastTickX + (camera.X - camera.LastTickX) * partialTicks;
            EntityRenderDispatcher.OffsetY = camera.LastTickY + (camera.Y - camera.LastTickY) * partialTicks;
            EntityRenderDispatcher.OffsetZ = camera.LastTickZ + (camera.Z - camera.LastTickZ) * partialTicks;
            BlockEntityRenderer.StaticPlayerX = camera.LastTickX + (camera.X - camera.LastTickX) * partialTicks;
            BlockEntityRenderer.StaticPlayerY = camera.LastTickY + (camera.Y - camera.LastTickY) * partialTicks;
            BlockEntityRenderer.StaticPlayerZ = camera.LastTickZ + (camera.Z - camera.LastTickZ) * partialTicks;
            var baseline = _game.EntityBaseline;
            if (baseline != null && !baseline.BelongsTo(_world)) baseline = null;
            var entities = baseline?.Entities ?? _world.Entities.Entities;
            var globalCount = baseline == null ? _world.Entities.GlobalEntities.Count : 0;
            CountEntitiesTotal = entities.Count + globalCount;

            int index;
            Entity entity;
            for (index = 0; index < globalCount; ++index)
            {
                entity = _world.Entities.GlobalEntities[index];
                var idlePose = DistantMobIdlePose.Sample(entity, _game.Player, simulationDistance,
                    _world.GetTime(), partialTicks);
                if (presentationPolicy.ShouldRenderEntity(entity, cameraPos))
                {
                    ++CountEntitiesRendered;
                    if (!ObserveLod(entity, partialTicks, idlePose))
                        EntityRenderDispatcher.Instance.RenderEntity(entity, partialTicks, idlePose);
                }
                else
                {
                    ++CountEntitiesHidden;
                }
            }

            for (index = 0; index < entities.Count; ++index)
            {
                entity = entities[index];
                var idlePose = baseline == null
                    ? DistantMobIdlePose.Sample(entity, _game.Player, simulationDistance,
                        _world.GetTime(), partialTicks)
                    : null;
                if (entities[index].Dead)
                {
                    if (entities[index] is EntityLiving living)
                    {
                        if (living.DeathTime >= 20)
                        {
                            entities.RemoveAt(index--);
                            continue;
                        }
                    }
                    else
                    {
                        entities.RemoveAt(index--);
                        continue;
                    }
                }

                if (presentationPolicy.ShouldRenderEntity(entity, cameraPos) &&
                    (entity.IgnoreFrustumCheck || culler.IsBoundingBoxInFrustum(entity.BoundingBox)) &&
                    (entity != _game.Camera || _game.Options.CameraMode != CameraMode.FirstPerson ||
                     _game.Camera.IsSleeping))
                {
                    var yFloor = MathHelper.Floor(entity.Y);
                    if (yFloor < 0)
                    {
                        yFloor = 0;
                    }
                    else if (yFloor >= ChuckFormat.WorldHeight)
                    {
                        yFloor = ChuckFormat.WorldHeight - 1;
                    }

                    if (_world.Reader.IsPosLoaded(MathHelper.Floor(entity.X), yFloor, MathHelper.Floor(entity.Z)))
                    {
                        ++CountEntitiesRendered;
                        if (!ObserveLod(entity, baseline == null ? partialTicks : 0, idlePose))
                            EntityRenderDispatcher.Instance.RenderEntity(entity,
                                baseline == null ? partialTicks : 0, idlePose);
                        continue;
                    }
                }

                ++CountEntitiesHidden;
            }

            if (baseline == null && _world is ClientWorld despawnWorld)
            {
                CountEntitiesTotal += despawnWorld.DistanceDespawnVisuals.Count;
                foreach (var visual in despawnWorld.DistanceDespawnVisuals)
                {
                    var despawning = visual.Entity;
                    if (!presentationPolicy.ShouldRenderEntity(despawning, cameraPos) ||
                        !culler.IsBoundingBoxInFrustum(despawning.BoundingBox))
                    {
                        ++CountEntitiesHidden;
                        continue;
                    }

                    var progress = visual.Progress(partialTicks);
                    var eased = progress * progress * (3.0f - 2.0f * progress);
                    EntityRenderDispatcher.Instance.RenderDistanceDespawn(
                        despawning, partialTicks, eased * 0.65f, 1.0f - eased * 0.82f, 1.0f - eased);
                    ++CountEntitiesRendered;
                }
            }

            CountBlockEntitiesTotal = baseline == null ? _world.Entities.BlockEntities.Count : 0;
            for (index = 0; index < CountBlockEntitiesTotal; ++index)
            {
                var blockEntity = _world.Entities.BlockEntities[index];
                if (!blockEntity.IsRemoved() &&
                    presentationPolicy.ShouldRenderBlockEntity(
                        blockEntity.distanceFrom(cameraPos.X, cameraPos.Y, cameraPos.Z)) &&
                    culler.IsBoundingBoxInFrustum(new Box(
                        blockEntity.X, blockEntity.Y, blockEntity.Z,
                        blockEntity.X + 1, blockEntity.Y + 1, blockEntity.Z + 1)))
                {
                    ++CountBlockEntitiesRendered;
                    BlockEntityRenderer.Instance.RenderTileEntity(blockEntity, partialTicks);
                }
                else
                {
                    ++CountBlockEntitiesHidden;
                }
            }

            EntityInstanceBatchRenderer.Instance.End();
            EntityBatchRenderer.Instance.End();
            EntityImpostors.Draw();
            EntityLod.EndFrame(EntityImpostors.LastSubmitted);
            Profiler.Record("EntityLodSelection", lodCpuMs);

            bool ObserveLod(Entity target, float delta, EntityPresentationPose? presentationPose)
            {
                var start = Stopwatch.GetTimestamp();
                var provider = EntityRenderDispatcher.Instance.GetEntityRenderObject(target).LodProvider;
                var position = new LodPoint(target.LastTickX + (target.X - target.LastTickX) * delta,
                    target.LastTickY + (target.Y - target.LastTickY) * delta,
                    target.LastTickZ + (target.Z - target.LastTickZ) * delta);
                var yaw = presentationPose?.BodyYaw ?? (target is EntityLiving living ?
                    EntityLodDirections.InterpolateYaw(living.LastBodyYaw, living.BodyYaw, delta) : target.Yaw);
                var providerScale = provider?.Scale(target) ?? 1;
                // Debug labels/boxes belong to the 3D path. Normal accepted distances are beyond
                // the existing 16-block shadow radius; never silently drop debug presentation.
                var decision = EntityLod.Select(target, position, provider != null,
                    !_game.Options.ShowDebugInfo && provider?.Supports(target, delta) == true,
                    provider?.VariantKey ?? "", (provider?.VisualDiameter ?? 0) * providerScale, yaw, cameraForward,
                    effectiveLodFov, _game.Options.CameraMode == CameraMode.FirstPerson ? _game.DisplayHeight : 0,
                    EntityImpostors.Enabled && EntityImpostors.ForceTierForTest,
                    _game.Options.EntityImpostorDistance);
                lodCpuMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                return EntityImpostors.TrySubmit(provider, decision, position.DirectionFrom(new LodPoint(
                    EntityRenderDispatcher.OffsetX, EntityRenderDispatcher.OffsetY, EntityRenderDispatcher.OffsetZ)),
                    (float)yaw, target.GetBrightnessAtEyes(delta), provider?.Pose(target, delta) ?? 0,
                    target is EntityLiving hurt && hurt.HurtTime > 0,
                    provider is IEntityImpostorProvider impostor
                        ? impostor.LayerEffects(target, delta)
                        : new Vector4(1, 1, 1, 0), providerScale);
            }
        }
    }

    private void OnPresentationRelocated(Entity? entity)
    {
        if (entity == null) EntityLod.Clear();
        else EntityLod.Forget(entity);
    }

    public int SortAndRender(
        EntityLiving camera,
        int pass,
        double partialTicks,
        ICuller cam,
        Matrix4X4<float> modelView,
        Matrix4X4<float> projection)
    {
        TerrainLod?.ObserveResourceGeneration(_textureManager.ResourceGeneration);

        if (_game.Options.RenderDistance != _renderDistance)
        {
            // Render distance is a visibility/residency policy, not a content invalidation. The
            // chunk renderer reads the radius from ChunkRenderParams and discovers newly admitted
            // sections on its next tick. Keeping both renderers alive preserves every reusable
            // near mesh and LOD presentation; shrinking is handled by the existing eviction grace.
            _renderDistance = _game.Options.RenderDistance;
        }

        var viewX = camera.LastTickX + (camera.X - camera.LastTickX) * partialTicks;
        var viewY = camera.LastTickY + (camera.Y - camera.LastTickY) * partialTicks;
        var viewZ = camera.LastTickZ + (camera.Z - camera.LastTickZ) * partialTicks;
        var viewYaw = camera.PrevYaw + (camera.Yaw - camera.PrevYaw) * (float)partialTicks;
        var viewPitch = camera.PrevPitch + (camera.Pitch - camera.PrevPitch) * (float)partialTicks;

        Lighting.turnOff();

        var renderParams = new ChunkRenderParams
        {
            Camera = cam,
            ModelView = modelView,
            Projection = projection,
            ViewPos = new Vector3D<double>(viewX, viewY, viewZ),
            RenderDistance = _renderDistance,
            TerrainHorizonDistance = _game.Options.TerrainHorizonDistance,
            TerrainLodDropoffScale = _game.Options.TerrainLodDropoffScale,
            Ticks = _world.GetTime(),
            PartialTicks = (float)partialTicks,
            DeltaTime = _game.Timer.DeltaTime,
            VerticalFovDegrees = _game.GameRenderer.CameraController.GetFov((float)partialTicks),
            ViewYawDegrees = viewYaw,
            ViewPitchDegrees = viewPitch,
            CameraMode = (int)_game.Options.CameraMode,
            ViewportHeight = _game.DisplayHeight,
            ChunkFade = _game.Options.ChunkFade,
            RenderOccluded = false,
            Fog = TerrainLodFog.Resolve(
                RenderSystem.Fog, _renderDistance,
                _game.Options.TerrainHorizonDistance,
                _game.Options.FogDistance)
        };

        if (pass == 0)
        {
            TerrainLod?.Render(renderParams, ChunkRenderer);
            ChunkRenderer.Render(renderParams);
        }
        else
        {
            TerrainLod?.RenderTransparent(renderParams, ChunkRenderer);
            ChunkRenderer.RenderTransparent(renderParams);
        }

        return 0;
    }

    public void UpdateClouds() => ++_cloudOffsetX;

    public void RenderSky(float tickDelta)
    {
        if (_game.World.Dimension.IsNether) return;

        if (!HasSkySlotPipeline)
        {
            return;
        }

        var skyColorVec = _world.Environment.GetSkyColor(_game.Camera, tickDelta);
        var skyRed = (float)skyColorVec.X;
        var skyGreen = (float)skyColorVec.Y;
        var skyBlue = (float)skyColorVec.Z;

        var groundR = _world.Dimension.HasGround ? skyRed * 0.2F + 0.04F : skyRed;
        var groundG = _world.Dimension.HasGround ? skyGreen * 0.2F + 0.04F : skyGreen;
        var groundB = _world.Dimension.HasGround ? skyBlue * 0.6F + 0.1F : skyBlue;

        var tessellator = Tessellator.instance;

        // The sky is drawn after the world, not before it, so it has to be depth tested — terrain
        // already in the buffer covers it — while writing no depth of its own, or a dome at
        // distance 100 would reject everything drawn later. That is RenderState.Translucent, and
        // it holds for the whole pass; only the blend changes below.
        RenderSystem.State.Apply(RenderState.Translucent);

        // Sky dome (top + bottom) — angle-based gradient
        SetSkyUniforms(SkyGradient(skyRed, skyGreen, skyBlue, groundR, groundG, groundB));
        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);
        DrawSkyMesh(_skyAbove, ProgramSlot.SkyBasic);
        DrawSkyMesh(_skyBelow, ProgramSlot.SkyBasic);

        // Sunrise/sunset fan was TriangleFan topology, which has no WebGPU counterpart, and is
        // skipped entirely rather than ported.
        RenderSystem.AlphaTestEnabled = false;
        Lighting.turnOff();

        // Sun and Moon (textured)
        // Sun, moon and the stars after them only ever brighten what is behind them, faded in by
        // their own alpha so the rain gradient can dim them.
        RenderSystem.State.Apply(RenderState.Translucent with
        {
            Blend = BlendMode.AdditiveByAlpha
        });
        RenderSystem.ModelView.Push();
        var rainFade = 1.0F - _world.Environment.GetRainGradient(tickDelta);
        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, rainFade);
        RenderSystem.ModelView.Rotate(_world.GetTime(tickDelta) * 360.0F, 1.0F, 0.0F, 0.0F);
        RefreshSkyModelView();
        SetSkyUniforms(SkyTextured(rainFade));
        var sunQuadSize = 30.0F;
        _textureManager.BindTexture(_textureManager.GetTextureId("/terrain/sun.png"));
        tessellator.startDrawingQuads();
        tessellator.addVertexWithUV(-sunQuadSize, 100.0D, -sunQuadSize, 0.0D, 0.0D);
        tessellator.addVertexWithUV(sunQuadSize, 100.0D, -sunQuadSize, 1.0D, 0.0D);
        tessellator.addVertexWithUV(sunQuadSize, 100.0D, sunQuadSize, 1.0D, 1.0D);
        tessellator.addVertexWithUV(-sunQuadSize, 100.0D, sunQuadSize, 0.0D, 1.0D);
        DrawSkyTessellator(ProgramSlot.SkyTextured);
        sunQuadSize = 20.0F;
        _textureManager.BindTexture(_textureManager.GetTextureId("/terrain/moon.png"));
        tessellator.startDrawingQuads();
        tessellator.addVertexWithUV(-sunQuadSize, -100.0D, sunQuadSize, 1.0D, 1.0D);
        tessellator.addVertexWithUV(sunQuadSize, -100.0D, sunQuadSize, 0.0D, 1.0D);
        tessellator.addVertexWithUV(sunQuadSize, -100.0D, -sunQuadSize, 0.0D, 0.0D);
        tessellator.addVertexWithUV(-sunQuadSize, -100.0D, -sunQuadSize, 1.0D, 0.0D);
        DrawSkyTessellator(ProgramSlot.SkyTextured);

        // Stars
        var starBrightness = _world.CalculateSkyLightIntensity(tickDelta) * rainFade;
        if (starBrightness > 0.0F)
        {
            SetSkyUniforms(SkyStars(starBrightness));
            RenderSystem.Color = new Vector4D<float>(starBrightness, starBrightness, starBrightness, starBrightness);
            DrawSkyMesh(_stars, ProgramSlot.SkyBasic);
        }

        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);
        RenderSystem.AlphaTestEnabled = true;
        RenderSystem.ModelView.Pop();

        RenderSystem.State.Apply(RenderState.Opaque);
    }

    // ── Sky slot-uniform plumbing ──────────────────────────────────────────

    /// <summary>
    ///     Copies the current model-view and projection (remapped for WebGPU) into the sky slot
    ///     uniforms, leaving every other field as <paramref name="template" /> set it.
    /// </summary>
    private void SetSkyUniforms(SkyWgslUniforms template)
    {
        if (!HasSkySlotPipeline) return;

        template.ModelViewMatrix = WebGpuDrawTarget.ToNumerics(RenderSystem.ModelView.Top);
        template.ProjectionMatrix =
            WebGpuDrawTarget.ToNumerics(WgpuClip.FromGl(RenderSystem.Projection.Top));
        RenderSystem.Context.SkySlot = template;
    }

    /// <summary>Refreshes the model-view in the sky slot after a stack mutation.</summary>
    private void RefreshSkyModelView()
    {
        if (!HasSkySlotPipeline) return;

        var u = RenderSystem.Context.SkySlot;
        u.ModelViewMatrix = WebGpuDrawTarget.ToNumerics(RenderSystem.ModelView.Top);
        RenderSystem.Context.SkySlot = u;
    }

    /// <summary>Draws <paramref name="mesh" /> under the sky's WebGPU slot.</summary>
    private static void DrawSkyMesh(IStaticMesh mesh, ProgramSlot slot) => mesh.Draw(slot);

    /// <summary>Submits the tessellator under the sky's WebGPU slot.</summary>
    private static void DrawSkyTessellator(ProgramSlot slot) => Tessellator.instance.draw(slot);

    // ── Sky uniform templates ──────────────────────────────────────────────

    /// <summary>
    ///     Gradient dome: the fragment shader blends between <c>SkyColor</c> and <c>GroundColor</c>
    ///     by the vertex Y coordinate.
    /// </summary>
    private static SkyWgslUniforms SkyGradient(float skyR, float skyG, float skyB,
        float groundR, float groundG, float groundB)
    {
        var fog = RenderSystem.Fog;
        return new SkyWgslUniforms
        {
            SkyColor = new Vector3(skyR, skyG, skyB),
            GroundColor = new Vector3(groundR, groundG, groundB),
            FogStart = fog.Start,
            FogEnd = fog.End,
            GradientMode = 1,
            UseTexture = 0,
            UseVertexColor = 0
        };
    }

    /// <summary>
    ///     Textured sun/moon quads: the fragment reads a 2D texture, modulated by <c>Tint</c>.
    /// </summary>
    private static SkyWgslUniforms SkyTextured(float alpha)
    {
        var fog = RenderSystem.Fog;
        return new SkyWgslUniforms
        {
            Tint = new Vector4(1, 1, 1, alpha),
            FogStart = fog.Start,
            FogEnd = fog.End,
            GradientMode = 0,
            UseTexture = 1,
            UseVertexColor = 0
        };
    }

    /// <summary>
    ///     Untextured points (stars / sunrise fan): the fragment uses the per-vertex colour from
    ///     the tessellator buffer.
    /// </summary>
    private static SkyWgslUniforms SkyUntextured()
    {
        var fog = RenderSystem.Fog;
        return new SkyWgslUniforms
        {
            Tint = Vector4.One,
            FogStart = fog.Start,
            FogEnd = fog.End,
            GradientMode = 0,
            UseTexture = 0,
            UseVertexColor = 1
        };
    }

    /// <summary>
    ///     Stars: an untextured flat colour, since the mesh carries no per-vertex colour of its own
    ///     (see the call site) and dims with <paramref name="brightness" /> instead.
    /// </summary>
    private static SkyWgslUniforms SkyStars(float brightness)
    {
        var fog = RenderSystem.Fog;
        return new SkyWgslUniforms
        {
            Tint = new Vector4(brightness, brightness, brightness, brightness),
            FogStart = fog.Start,
            FogEnd = fog.End,
            GradientMode = 0,
            UseTexture = 0,
            UseVertexColor = 0
        };
    }

    // ── Cloud slot-uniform plumbing ────────────────────────────────────────

    private void SetCloudUniforms(float offsetX, float offsetY, float offsetZ, float scale,
        float texOffsetU, float texOffsetV, float tintR, float tintG, float tintB, float tintA,
        Vector3 lightDir)
    {
        if (!HasCloudSlotPipeline) return;

        var fog = RenderSystem.Fog;
        RenderSystem.Context.CloudSlot = new CloudWgslUniforms
        {
            ModelViewMatrix = WebGpuDrawTarget.ToNumerics(RenderSystem.ModelView.Top),
            ProjectionMatrix =
                WebGpuDrawTarget.ToNumerics(WgpuClip.FromGl(RenderSystem.Projection.Top)),
            TextureMatrix = WebGpuDrawTarget.ToNumerics(RenderSystem.TextureMatrix.Top),
            CloudOffset = new Vector3(offsetX, offsetY, offsetZ),
            CloudScale = scale,
            FogStart = fog.Start,
            FogEnd = fog.End,
            Tint = new Vector4(tintR, tintG, tintB, tintA),
            LightDir = lightDir
        };
    }

    /// <summary>
    ///     Unit vector toward the sun (day half) or moon (night half), in the same world-relative
    ///     axes RenderSky rotates the sky dome by: a rotation about X of celestialAngle * 360°,
    ///     applied to the sun's local position at +Y.
    /// </summary>
    private Vector3 GetCelestialLightDir(float tickDelta)
    {
        var theta = _world.GetTime(tickDelta) * MathF.PI * 2.0F;
        return new Vector3(0.0F, MathF.Cos(theta), MathF.Sin(theta));
    }

    private static void DrawCloudMesh(IStaticMesh mesh) => mesh.Draw(ProgramSlot.Clouds);

    public void RenderClouds(float tickDelta)
    {
        if (!HasCloudSlotPipeline) return;

        using (Profiler.Begin("RenderClouds"))
        {
            // The options screen mutates CloudsQuality directly; pick up a change here rather than
            // only at construct/reset, or a switch to Legacy leaves _clouds sized for Fancy's single
            // mesh and RenderLegacyCloudsFancy indexes past the end of it.
            OnCloudsQualityChanged();

            if (!_game.World.Dimension.IsNether)
            {
                if (_game.Options.CloudsQuality <= 0)
                {
                    RenderLegacyCloudsFancy(tickDelta);
                }
                // 1 - No clouds
                else if (_game.Options.CloudsQuality >= 2)
                {
                    RenderCloudsFancy(tickDelta);
                }
            }
        }
    }

    private static IStaticMesh BuildCloudMesh()
    {
        var tessellator = Tessellator.instance;

        tessellator.startDrawingQuads();
        var uvScale = 1.0F / 256.0F;
        byte tileSize = CloudsRenderDistance;
        var tile = tileSize * uvScale;

        // Both cloud.frag and cloud.wgsl multiply the sampled texel by the captured vertex colour
        // unconditionally. Without this, the tessellator never touches the colour slot for this
        // draw, and the static mesh captures whatever bytes its shared scratch buffer last held —
        // tint and visibility both become a coin flip left over from unrelated geometry.
        tessellator.setColorRGBA_F(1.0F, 1.0F, 1.0F, 1.0F);
        tessellator.setNormal(0.0F, -1.0F, 0.0F);
        tessellator.addVertexWithUV(0, 0.0, tileSize, 0, tile);
        tessellator.addVertexWithUV(tileSize, 0.0, tileSize, tile, tile);
        tessellator.addVertexWithUV(tileSize, 0.0, 0, tile, 0);
        tessellator.addVertexWithUV(0, 0.0, 0, 0, 0);

        return tessellator.captureStatic();
    }

    /// <summary>Bottom, top, and the two side faces, in the order the draw path expects them.</summary>
    private static IStaticMesh[] BuildLegacyCloudMeshes()
    {
        var tessellator = Tessellator.instance;
        var meshes = new IStaticMesh[4];

        for (var i = 0; i < 4; ++i)
        {
            tessellator.startDrawingQuads();
            // See BuildCloudMesh: without an explicit colour, the captured mesh's tint comes from
            // whatever the shared tessellator buffer last held for unrelated geometry.
            tessellator.setColorRGBA_F(1.0F, 1.0F, 1.0F, 1.0F);
            var cloudHeight = 4.0F;
            var uvScale = 1.0F / 256.0F;
            var edgeInset = 1.0F / 1024.0F;
            byte tileSize = 8;
            byte cloudRadius = 3;

            for (var tileX = -cloudRadius + 1; tileX <= cloudRadius; ++tileX)
            {
                for (var tileZ = -cloudRadius + 1; tileZ <= cloudRadius; ++tileZ)
                {
                    float uvX = tileX * tileSize;
                    float uvZ = tileZ * tileSize;
                    var x = uvX;
                    var z = uvZ;

                    if (i == 0)
                    {
                        tessellator.setNormal(0.0F, -1.0F, 0.0F);
                        tessellator.addVertexWithUV(x, 0.0, z + tileSize, uvX * uvScale, (uvZ + tileSize) * uvScale);
                        tessellator.addVertexWithUV(x + tileSize, 0.0, z + tileSize, (uvX + tileSize) * uvScale, (uvZ + tileSize) * uvScale);
                        tessellator.addVertexWithUV(x + tileSize, 0.0, z, (uvX + tileSize) * uvScale, uvZ * uvScale);
                        tessellator.addVertexWithUV(x, 0.0, z, uvX * uvScale, uvZ * uvScale);
                    }

                    else if (i == 1)
                    {
                        tessellator.setNormal(0.0F, 1.0F, 0.0F);
                        tessellator.addVertexWithUV(x, cloudHeight - edgeInset, z + tileSize, uvX * uvScale, (uvZ + tileSize) * uvScale);
                        tessellator.addVertexWithUV(x + tileSize, cloudHeight - edgeInset, z + tileSize, (uvX + tileSize) * uvScale, (uvZ + tileSize) * uvScale);
                        tessellator.addVertexWithUV(x + tileSize, cloudHeight - edgeInset, z, (uvX + tileSize) * uvScale, uvZ * uvScale);
                        tessellator.addVertexWithUV(x, cloudHeight - edgeInset, z, uvX * uvScale, uvZ * uvScale);
                    }

                    else if (i == 2)
                    {
                        if (tileX > -1)
                        {
                            tessellator.setNormal(-1.0F, 0.0F, 0.0F);
                            for (var edgeSlice = 0; edgeSlice < tileSize; ++edgeSlice)
                            {
                                tessellator.addVertexWithUV(x + edgeSlice, 0.0, z + tileSize, (uvX + edgeSlice + 0.5F) * uvScale, (uvZ + tileSize) * uvScale);
                                tessellator.addVertexWithUV(x + edgeSlice, cloudHeight, z + tileSize, (uvX + edgeSlice + 0.5F) * uvScale, (uvZ + tileSize) * uvScale);
                                tessellator.addVertexWithUV(x + edgeSlice, cloudHeight, z, (uvX + edgeSlice + 0.5F) * uvScale, uvZ * uvScale);
                                tessellator.addVertexWithUV(x + edgeSlice, 0.0, z, (uvX + edgeSlice + 0.5F) * uvScale, uvZ * uvScale);
                            }
                        }

                        if (tileX <= 1)
                        {
                            tessellator.setNormal(1.0F, 0.0F, 0.0F);
                            for (var edgeSlice = 0; edgeSlice < tileSize; ++edgeSlice)
                            {
                                tessellator.addVertexWithUV(x + edgeSlice + 1.0F - edgeInset, 0.0, z + tileSize, (uvX + edgeSlice + 0.5F) * uvScale, (uvZ + tileSize) * uvScale);
                                tessellator.addVertexWithUV(x + edgeSlice + 1.0F - edgeInset, cloudHeight, z + tileSize, (uvX + edgeSlice + 0.5F) * uvScale, (uvZ + tileSize) * uvScale);
                                tessellator.addVertexWithUV(x + edgeSlice + 1.0F - edgeInset, cloudHeight, z, (uvX + edgeSlice + 0.5F) * uvScale, uvZ * uvScale);
                                tessellator.addVertexWithUV(x + edgeSlice + 1.0F - edgeInset, 0.0, z, (uvX + edgeSlice + 0.5F) * uvScale, uvZ * uvScale);
                            }
                        }
                    }

                    else if (i == 3)
                    {
                        if (tileZ > -1)
                        {
                            tessellator.setNormal(0.0F, 0.0F, -1.0F);
                            for (var edgeSlice = 0; edgeSlice < tileSize; ++edgeSlice)
                            {
                                tessellator.addVertexWithUV(x + 0.0F, cloudHeight, z + edgeSlice + 0.0F, uvX * uvScale, (uvZ + edgeSlice + 0.5F) * uvScale);
                                tessellator.addVertexWithUV(x + tileSize, cloudHeight, z + edgeSlice + 0.0F, (uvX + tileSize) * uvScale, (uvZ + edgeSlice + 0.5F) * uvScale);
                                tessellator.addVertexWithUV(x + tileSize, 0.0, z + edgeSlice + 0.0F, (uvX + tileSize) * uvScale, (uvZ + edgeSlice + 0.5F) * uvScale);
                                tessellator.addVertexWithUV(x + 0.0F, 0.0, z + edgeSlice + 0.0F, uvX * uvScale, (uvZ + edgeSlice + 0.5F) * uvScale);
                            }
                        }

                        if (tileZ <= 1)
                        {
                            tessellator.setNormal(0.0F, 0.0F, 1.0F);
                            for (var edgeSlice = 0; edgeSlice < tileSize; ++edgeSlice)
                            {
                                tessellator.addVertexWithUV(x + 0.0F, cloudHeight, z + edgeSlice + 1.0F - edgeInset, uvX * uvScale, (uvZ + edgeSlice + 0.5F) * uvScale);
                                tessellator.addVertexWithUV(x + tileSize, cloudHeight, z + edgeSlice + 1.0F - edgeInset, (uvX + tileSize) * uvScale, (uvZ + edgeSlice + 0.5F) * uvScale);
                                tessellator.addVertexWithUV(x + tileSize, 0.0, z + edgeSlice + 1.0F - edgeInset, (uvX + tileSize) * uvScale, (uvZ + edgeSlice + 0.5F) * uvScale);
                                tessellator.addVertexWithUV(x + 0.0F, 0.0, z + edgeSlice + 1.0F - edgeInset, uvX * uvScale, (uvZ + edgeSlice + 0.5F) * uvScale);
                            }
                        }
                    }
                }
            }

            meshes[i] = tessellator.captureStatic();
        }

        return meshes;
    }

    private void RenderCloudsFancy(float tickDelta)
    {
        var cameraY = (float)(_game.Camera.LastTickY + (_game.Camera.Y - _game.Camera.LastTickY) * tickDelta);
        const float cloudScale = 12.0F;
        var cloudOffsetX = (_game.Camera.PrevX + (_game.Camera.X - _game.Camera.PrevX) * tickDelta + (_cloudOffsetX + tickDelta) * 0.03F) / cloudScale;
        var cloudOffsetZ = (_game.Camera.PrevZ + (_game.Camera.Z - _game.Camera.PrevZ) * tickDelta) / cloudScale + 0.33F;
        var cloudY = _world.Dimension.CloudHeight - cameraY + 0.33F;
        var cloudChunkX = MathHelper.Floor(cloudOffsetX / 2048.0D);
        var cloudChunkZ = MathHelper.Floor(cloudOffsetZ / 2048.0D);
        cloudOffsetX -= cloudChunkX * 2048;
        cloudOffsetZ -= cloudChunkZ * 2048;
        _textureManager.BindTexture(_textureManager.GetTextureId("/environment/clouds.png"));

        // Culling off because the cloud sheet is a single plane seen from either side, depending
        // on whether the camera is above or below the cloud layer.
        RenderSystem.State.Apply(RenderState.Entity with
        {
            Blend = BlendMode.Alpha
        });

        var cloudColor = _world.Environment.GetCloudColor(tickDelta);
        var cloudRed = (float)cloudColor.X;
        var cloudGreen = (float)cloudColor.Y;
        var cloudBlue = (float)cloudColor.Z;

        const float textureScale = 1 / 256f;
        var textureOffsetU = MathHelper.Floor(cloudOffsetX) * textureScale;
        var textureOffsetV = MathHelper.Floor(cloudOffsetZ) * textureScale;
        var subCloudOffsetX = (float)(cloudOffsetX - MathHelper.Floor(cloudOffsetX)) + CloudsRenderDistance / 2;
        var subCloudOffsetZ = (float)(cloudOffsetZ - MathHelper.Floor(cloudOffsetZ)) + CloudsRenderDistance / 2;

        RenderSystem.ModelView.Scale(cloudScale, 1.0F, cloudScale);
        RenderSystem.ModelView.Push();
        RenderSystem.ModelView.Translate(-subCloudOffsetX, cloudY, -subCloudOffsetZ);

        RenderSystem.TextureMatrix.Push();
        RenderSystem.TextureMatrix.Translate(textureOffsetU, textureOffsetV, 0.0F);

        RenderSystem.Color = new Vector4D<float>(cloudRed, cloudGreen, cloudBlue, 0.8F);
        SetCloudUniforms(-subCloudOffsetX, cloudY, -subCloudOffsetZ, cloudScale / 2f,
            textureOffsetU, textureOffsetV, cloudRed, cloudGreen, cloudBlue, 0.8F,
            GetCelestialLightDir(tickDelta));
        DrawCloudMesh(_clouds[0]);

        RenderSystem.TextureMatrix.Pop();

        RenderSystem.ModelView.Pop();

        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);

        // This used to put culling back and leave blending on, so the first-person hand pass drew
        // blended or not depending on whether clouds were enabled and the camera was in the Nether.
        RenderSystem.State.Apply(RenderState.Opaque);
    }

    private void RenderLegacyCloudsFancy(float tickDelta)
    {
        var cameraY = (float)(_game.Camera.LastTickY + (_game.Camera.Y - _game.Camera.LastTickY) * tickDelta);
        const float cloudScale = 12.0F;
        const float cloudHeight = 4.0F;
        var cloudOffsetX = (_game.Camera.PrevX + (_game.Camera.X - _game.Camera.PrevX) * tickDelta + (_cloudOffsetX + tickDelta) * 0.03F) / cloudScale;
        var cloudOffsetZ = (_game.Camera.PrevZ + (_game.Camera.Z - _game.Camera.PrevZ) * tickDelta) / cloudScale + 0.33F;
        var cloudY = _world.Dimension.CloudHeight - cameraY + 0.33F;
        var cloudChunkX = MathHelper.Floor(cloudOffsetX / 2048.0D);
        var cloudChunkZ = MathHelper.Floor(cloudOffsetZ / 2048.0D);
        cloudOffsetX -= cloudChunkX * 2048;
        cloudOffsetZ -= cloudChunkZ * 2048;
        _textureManager.BindTexture(_textureManager.GetTextureId("/environment/clouds.png"));

        // Culling off because these are boxes seen from inside as often as outside — the camera
        // can sit within the cloud layer.
        var cloudState = RenderState.Entity with
        {
            Blend = BlendMode.Alpha
        };

        var cloudColor = _world.Environment.GetCloudColor(tickDelta);
        var cloudRed = (float)cloudColor.X;
        var cloudGreen = (float)cloudColor.Y;
        var cloudBlue = (float)cloudColor.Z;

        const float textureScale = 1 / 256f;
        var textureOffsetU = MathHelper.Floor(cloudOffsetX) * textureScale;
        var textureOffsetV = MathHelper.Floor(cloudOffsetZ) * textureScale;
        var subCloudOffsetX = (float)(cloudOffsetX - MathHelper.Floor(cloudOffsetX));
        var subCloudOffsetZ = (float)(cloudOffsetZ - MathHelper.Floor(cloudOffsetZ));

        RenderSystem.ModelView.Scale(cloudScale, 1.0F, cloudScale);

        for (var passIndex = 0; passIndex < 2; ++passIndex)
        {
            // Pass 0 writes only depth. With culling off, a box's near and far faces would both
            // blend into the same pixel and come out twice as opaque; laying depth down first
            // leaves pass 1 blending each surface exactly once.
            RenderSystem.State.Apply(cloudState with
            {
                ColorWrite = passIndex != 0
            });

            RenderSystem.ModelView.Push();
            RenderSystem.ModelView.Translate(-subCloudOffsetX, cloudY, -subCloudOffsetZ);

            RenderSystem.TextureMatrix.Push();
            RenderSystem.TextureMatrix.Translate(textureOffsetU, textureOffsetV, 0.0F);

            if (cloudY > -cloudHeight - 1.0F)
            {
                RenderSystem.Color = new Vector4D<float>(cloudRed * 0.7F, cloudGreen * 0.7F, cloudBlue * 0.7F, 0.8F);
                _clouds[0].DrawWithBoundProgram(); // Bottom
            }

            if (cloudY <= cloudHeight + 1.0F)
            {
                RenderSystem.Color = new Vector4D<float>(cloudRed, cloudGreen, cloudBlue, 0.8F);
                _clouds[1].DrawWithBoundProgram(); // Top
            }

            RenderSystem.Color = new Vector4D<float>(cloudRed * 0.9F, cloudGreen * 0.9F, cloudBlue * 0.9F, 0.8F);
            _clouds[2].DrawWithBoundProgram(); // Side X

            RenderSystem.Color = new Vector4D<float>(cloudRed * 0.8F, cloudGreen * 0.8F, cloudBlue * 0.8F, 0.8F);
            _clouds[3].DrawWithBoundProgram(); // Side Z

            RenderSystem.TextureMatrix.Pop();

            RenderSystem.ModelView.Pop();
        }

        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);
        RenderSystem.State.Apply(RenderState.Opaque);
    }

    public void DrawBlockBreaking(EntityPlayer entityPlayer, HitResult hit, ItemStack itemStack, float tickDelta)
    {
        if (DamagePartialTime <= 0.0F) return;

        var tessellator = Tessellator.instance;

        RenderSystem.ModelView.Push();
        RenderSystem.AlphaTestEnabled = true;

        // Culling matters here and was previously inherited: this redraws the block's own faces
        // with the crack texture multiplied over them, so with culling off the far faces multiply
        // a second time and the crack comes out twice as dark. GameRenderer calls this from two
        // places — once for the underwater case, straight after the entity pass has left culling
        // off, and once with it on — which is why the crack looked different underwater.
        RenderSystem.State.Apply(RenderState.Opaque with
        {
            Blend = BlendMode.Multiply,
            DepthBias = DepthBias.Decal
        });
        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 0.5F);

        _textureManager.BindTexture(_textureManager.GetTextureId("/terrain.png"));

        var targetBlockId = _world.Reader.GetBlockId(hit.BlockX, hit.BlockY, hit.BlockZ);
        var targetBlock = targetBlockId > 0 ? _world.Content.Blocks.GetByProtocolId(targetBlockId) : _world.Content.Blocks.Get("stone");

        var renderX = entityPlayer.LastTickX + (entityPlayer.X - entityPlayer.LastTickX) * tickDelta;
        var renderY = entityPlayer.LastTickY + (entityPlayer.Y - entityPlayer.LastTickY) * tickDelta;
        var renderZ = entityPlayer.LastTickZ + (entityPlayer.Z - entityPlayer.LastTickZ) * tickDelta;

        tessellator.startDrawingQuads();
        tessellator.setTranslationD(-renderX, -renderY, -renderZ);
        tessellator.disableColor();

        BlockRenderer.RenderBlockByRenderType(_world.Reader, _world.Content.Blocks, _world.Lighting, targetBlock, new BlockPos(hit.BlockX, hit.BlockY, hit.BlockZ), tessellator, 240 + (int)(DamagePartialTime * 10.0F), true,
            _game.Options.AlternateBlocksEnabled);
        tessellator.draw(ProgramSlot.DamagedBlock);

        tessellator.setTranslationD(0.0D, 0.0D, 0.0D);
        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);

        RenderSystem.AlphaTestEnabled = false;

        // Takes the bias back off with it, since Opaque carries DepthBias.None.
        RenderSystem.State.Apply(RenderState.Opaque);
        RenderSystem.ModelView.Pop();
    }

    public void DrawSelectionBox(EntityPlayer player, HitResult hit, int renderPass, ItemStack itemStack, float tickDelta)
    {
        if (renderPass == 0 && hit.Type == HitResultType.Tile)
        {
            // Line loops, so the culling this carries is inert; what it is here for is the depth
            // pair — tested, so the outline is hidden by blocks in front of the target, but not
            // written, so a line lying exactly on a block face does not fight with it.
            RenderSystem.State.Apply(RenderState.Translucent);
            RenderSystem.Color = new Vector4D<float>(0.0F, 0.0F, 0.0F, 0.4F);
            RenderSystem.TextureEnabled = false;
            var outlinePadding = 0.002F;
            var blockId = _world.Reader.GetBlockId(hit.BlockX, hit.BlockY, hit.BlockZ);
            if (blockId > 0)
            {
                _world.Content.Blocks.GetByProtocolId(blockId).UpdateBoundingBox(_world.Reader, hit.BlockX, hit.BlockY, hit.BlockZ);
                var renderX = player.LastTickX + (player.X - player.LastTickX) * tickDelta;
                var renderY = player.LastTickY + (player.Y - player.LastTickY) * tickDelta;
                var renderZ = player.LastTickZ + (player.Z - player.LastTickZ) * tickDelta;
                DrawOutlinedBoundingBox(_world.Content.Blocks.GetByProtocolId(blockId).GetBoundingBox(_world.Reader, _world.Entities, hit.BlockX, hit.BlockY, hit.BlockZ).Expand(outlinePadding, outlinePadding, outlinePadding)
                    .Offset(-renderX, -renderY, -renderZ));
            }

            RenderSystem.TextureEnabled = true;
            RenderSystem.State.Apply(RenderState.Opaque);
        }
    }

    private static void DrawOutlinedBoundingBox(Box box)
    {
        var tessellator = Tessellator.instance;
        tessellator.startDrawing(3);
        tessellator.addVertex(box.MinX, box.MinY, box.MinZ);
        tessellator.addVertex(box.MaxX, box.MinY, box.MinZ);
        tessellator.addVertex(box.MaxX, box.MinY, box.MaxZ);
        tessellator.addVertex(box.MinX, box.MinY, box.MaxZ);
        tessellator.addVertex(box.MinX, box.MinY, box.MinZ);
        tessellator.draw(ProgramSlot.Line);
        tessellator.startDrawing(3);
        tessellator.addVertex(box.MinX, box.MaxY, box.MinZ);
        tessellator.addVertex(box.MaxX, box.MaxY, box.MinZ);
        tessellator.addVertex(box.MaxX, box.MaxY, box.MaxZ);
        tessellator.addVertex(box.MinX, box.MaxY, box.MaxZ);
        tessellator.addVertex(box.MinX, box.MaxY, box.MinZ);
        tessellator.draw(ProgramSlot.Line);
        tessellator.startDrawing(1);
        tessellator.addVertex(box.MinX, box.MinY, box.MinZ);
        tessellator.addVertex(box.MinX, box.MaxY, box.MinZ);
        tessellator.addVertex(box.MaxX, box.MinY, box.MinZ);
        tessellator.addVertex(box.MaxX, box.MaxY, box.MinZ);
        tessellator.addVertex(box.MaxX, box.MinY, box.MaxZ);
        tessellator.addVertex(box.MaxX, box.MaxY, box.MaxZ);
        tessellator.addVertex(box.MinX, box.MinY, box.MaxZ);
        tessellator.addVertex(box.MinX, box.MaxY, box.MaxZ);
        tessellator.draw(ProgramSlot.Line);
    }

    public void MarkBlocksDirty(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
        => MarkBlocksDirty(minX, minY, minZ, maxX, maxY, maxZ, dependenciesKnown: true);

    /// <summary>
    ///     Invalidates geometry whose renderer has dependencies beyond the ordinary one-cell
    ///     block/AO/liquid halo. Mod render providers with undeclared bounds must use this
    ///     conservative entry point until they can report an exact dependency range.
    /// </summary>
    public void MarkBlocksDirtyUnbounded(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
        => MarkBlocksDirty(minX, minY, minZ, maxX, maxY, maxZ, dependenciesKnown: false);

    private void MarkBlocksDirty(
        int minX, int minY, int minZ, int maxX, int maxY, int maxZ, bool dependenciesKnown)
    {
        if (maxY < 0 || minY >= ChuckFormat.WorldHeight) return;

        var (start, end) = GetSectionRange(minX, minY, minZ, maxX, maxY, maxZ);
        start.Y = Math.Max(0, start.Y);
        end.Y = Math.Min(ChuckFormat.WorldHeight / SubChunkRenderer.Size - 1, end.Y);

        for (var x = start.X; x <= end.X; x++)
        {
            for (var y = start.Y; y <= end.Y; y++)
            {
                for (var z = start.Z; z <= end.Z; z++)
                {
                    var sectionPos = new Vector3D<int>(x, y, z) * SubChunkRenderer.Size;
                    var rebuildPlan = SectionMeshRebuildPlan.FromWorldYBounds(
                        sectionPos.Y, minY, maxY, dependenciesKnown);
                    ChunkRenderer.MarkDirty(sectionPos, true, rebuildPlan);
                }
            }
        }
    }

    internal static (Vector3D<int> Start, Vector3D<int> End) GetSectionRange(
        int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        // The range is inclusive. Ceiling the maximum and then iterating with <= marks the next
        // section for almost every ordinary block update.
        Vector3D<int> start = new(
            (int)Math.Floor(minX / (double)SubChunkRenderer.Size),
            (int)Math.Floor(minY / (double)SubChunkRenderer.Size),
            (int)Math.Floor(minZ / (double)SubChunkRenderer.Size));
        Vector3D<int> end = new(
            (int)Math.Floor(maxX / (double)SubChunkRenderer.Size),
            (int)Math.Floor(maxY / (double)SubChunkRenderer.Size),
            (int)Math.Floor(maxZ / (double)SubChunkRenderer.Size));
        return (start, end);
    }

    public void WorldEventBreak(int blockId, int meta, int x, int y, int z)
    {
        if (blockId == 0) return;
        var block = _world.Content.Blocks.GetByProtocolId(blockId);
        WorldEventBreak(block, meta, x, y, z);
    }

    public void WorldEventBreak(Block block, int meta, int x, int y, int z)
    {
        _game.SoundManager.PlayBreakSound(block.SoundGroup, x, y, z);
        _game.ParticleManager.AddBlockDestroyEffects(x, y, z, block, meta);
    }
}
