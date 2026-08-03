using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;
using BetaSharp.Blocks.Entities;
using BetaSharp.Client.Entities.FX;
using BetaSharp.Client.Options;
using BetaSharp.Client.Rendering.Blocks;
using BetaSharp.Client.Rendering.Blocks.Entities;
using BetaSharp.Client.Rendering.Chunks;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Client.Rendering.Core.Textures;
using BetaSharp.Client.Rendering.Entities;
using BetaSharp.Client.Rendering.Particles;
using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Items.Behaviors;
using BetaSharp.Profiling;
using BetaSharp.Util;
using BetaSharp.Util.Hit;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Chunks;
using BetaSharp.Worlds.Core;
using Silk.NET.Maths;

namespace BetaSharp.Client.Rendering;

public class WorldRenderer : IWorldEventListener, IDisposable
{
    private const int CloudsRenderDistance = 128;

    public int CountEntitiesTotal { get; private set; }
    public int CountEntitiesRendered { get; private set; }
    public int CountEntitiesHidden { get; private set; }
    public ChunkRenderer ChunkRenderer { get; private set; }
    public float DamagePartialTime { get; set; }

    private World _world;
    private readonly TextureManager _textureManager;
    private readonly BetaSharp _game;
    private int _cloudOffsetX;
    private readonly StaticMesh _stars;
    private readonly StaticMesh _skyAbove;
    private readonly StaticMesh _skyBelow;

    /// <summary>
    ///     One mesh for the shader-based clouds, four for the legacy ones (bottom, top, and the two
    ///     side faces), rebuilt whenever the clouds quality option changes.
    /// </summary>
    private StaticMesh[] _clouds = [];
    private int _renderDistance = -1;
    private int _renderEntitiesStartupCounter = 2;
    private readonly Shader _skyShader;
    private readonly Shader _cloudShader;
    private Vector3D<float> _fogColor;
    private int _cloudsQuality = -1;

    public WorldRenderer(BetaSharp gameInstance, TextureManager textureManager)
    {
        _game = gameInstance;
        _textureManager = textureManager;

        _stars = BuildStars();

        ChunkRenderer = new(gameInstance.World, _game.Options);
        EntityBatchRenderer.Initialize(_game.Options);

        OnCloudsQualityChanged();

        _cloudShader = new Shader(_game.Options.ShaderOptions.GetOrCreate("cloud"), "shaders/cloud.vert", "shaders/cloud.frag");
        _cloudShader.Changed += OnBuildCloudShader;

        _skyAbove = BuildSkyPlane(16.0F, facingUp: true);
        _skyBelow = BuildSkyPlane(-16.0F, facingUp: false);

        _skyShader = new Shader(_game.Options.ShaderOptions.GetOrCreate("sky"), "shaders/sky.vert", "shaders/sky.frag");
        _skyShader.Changed += OnBuildSkyShader;
    }

    private void OnBuildSkyShader(Shader _)
    {
    }

    private void OnCloudsQualityChanged()
    {
        if (_cloudsQuality == _game.Options.CloudsQuality) return;
        if (_cloudsQuality == -1 || _game.Options.CloudsQuality == 0 || _game.Options.CloudsQuality == 2)
        {
            // The meshes belong to whichever quality built them, so the old set goes before the new
            // one is built rather than leaking a buffer on every change.
            foreach (StaticMesh mesh in _clouds)
            {
                mesh.Dispose();
            }

            _clouds = [];

            if (_game.Options.CloudsQuality <= 0) _clouds = BuildLegacyCloudMeshes();
            else if (_game.Options.CloudsQuality > 1) _clouds = [BuildCloudMesh()];
        }

        _cloudsQuality = _game.Options.CloudsQuality;
    }

    private void OnBuildCloudShader(Shader _)
    {
    }

    public void SetFogColor(float r, float g, float b) => _fogColor = new(r, g, b);

    /// <summary>
    ///     The flat sheet of quads the sky colour is painted onto, at <paramref name="y" />.
    /// </summary>
    /// <remarks>
    ///     Wound the opposite way for the sheet below the horizon, since it is seen from the other
    ///     side. The two used to be built by separate loops that differed only in that and in the
    ///     height, one of them starting a batch per quad and the other batching the lot; the
    ///     geometry was always the same.
    /// </remarks>
    private static StaticMesh BuildSkyPlane(float y, bool facingUp)
    {
        const int step = 64;
        const int radius = 256 / step + 2;

        Tessellator tessellator = Tessellator.instance;
        tessellator.startDrawingQuads();

        for (int x = -step * radius; x <= step * radius; x += step)
        {
            for (int z = -step * radius; z <= step * radius; z += step)
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

    private static StaticMesh BuildStars()
    {
        Random random = new(10842);
        Tessellator tessellator = Tessellator.instance;
        tessellator.startDrawingQuads();

        for (int starIndex = 0; starIndex < 1500; ++starIndex)
        {
            double dirX = random.NextDouble() * 2.0 - 1.0;
            double dirY = random.NextDouble() * 2.0 - 1.0;
            double dirZ = random.NextDouble() * 2.0 - 1.0;
            double starSize = (0.25 + random.NextDouble() * 0.25);
            double dirLengthSq = dirX * dirX + dirY * dirY + dirZ * dirZ;
            if (dirLengthSq < 1.0 && dirLengthSq > 0.01)
            {
                dirLengthSq = 1.0 / Math.Sqrt(dirLengthSq);
                dirX *= dirLengthSq;
                dirY *= dirLengthSq;
                dirZ *= dirLengthSq;
                double starX = dirX * 100.0;
                double starY = dirY * 100.0;
                double starZ = dirZ * 100.0;
                double yaw = Math.Atan2(dirX, dirZ);
                double sinYaw = Math.Sin(yaw);
                double cosYaw = Math.Cos(yaw);
                double pitch = Math.Atan2(Math.Sqrt(dirX * dirX + dirZ * dirZ), dirY);
                double sinPitch = Math.Sin(pitch);
                double cosPitch = Math.Cos(pitch);
                double roll = random.NextDouble() * Math.PI * 2.0;
                double sinRoll = Math.Sin(roll);
                double cosRoll = Math.Cos(roll);

                for (int cornerIndex = 0; cornerIndex < 4; ++cornerIndex)
                {
                    double cornerY = 0.0D;
                    double cornerX = ((cornerIndex & 2) - 1) * starSize;
                    double cornerZ = ((cornerIndex + 1 & 2) - 1) * starSize;
                    double rotatedCornerX = cornerX * cosRoll - cornerZ * sinRoll;
                    double rotatedCornerZ = cornerZ * cosRoll + cornerX * sinRoll;
                    double pitchedCornerY = rotatedCornerX * sinPitch + cornerY * cosPitch;
                    double pitchedCornerX = cornerY * sinPitch - rotatedCornerX * cosPitch;
                    double finalX = pitchedCornerX * sinYaw - rotatedCornerZ * cosYaw;
                    double finalZ = rotatedCornerZ * sinYaw + pitchedCornerX * cosYaw;
                    tessellator.addVertex(starX + finalX, starY + pitchedCornerY, starZ + finalZ);
                }
            }
        }

        return tessellator.captureStatic();
    }

    public void ChangeWorld(World world)
    {
        _world?.EventListeners.Remove(this);

        EntityRenderDispatcher.Instance.World = world;
        _world = world;
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
        double viewX = view.LastTickX + (view.X - view.LastTickX) * partialTicks;
        double viewY = view.LastTickY + (view.Y - view.LastTickY) * partialTicks;
        double viewZ = view.LastTickZ + (view.Z - view.LastTickZ) * partialTicks;
        ChunkRenderer.Tick(new(viewX, viewY, viewZ));
    }

    public void Dispose()
    {
        _cloudShader?.Dispose();
        ChunkRenderer?.Dispose();

        _stars.Dispose();
        _skyAbove.Dispose();
        _skyBelow.Dispose();

        foreach (StaticMesh mesh in _clouds)
        {
            mesh.Dispose();
        }

        _clouds = [];
    }

    public void LoadRenderers()
    {
        LeavesBehavior.SetGraphicsLevel(BlockRegistry.Get("leaves"), true);
        _renderDistance = _game.Options.RenderDistance;

        ChunkRenderer?.Dispose();
        ChunkRenderer = new(_world, _game.Options);
        ChunkMeshVersion.ClearPool();

        _renderEntitiesStartupCounter = 2;
    }

    public void RenderEntities(Vec3D cameraPos, ICuller culler, float partialTicks)
    {
        if (_renderEntitiesStartupCounter > 0)
        {
            --_renderEntitiesStartupCounter;
        }
        else
        {
            BlockEntityRenderer.Instance.CacheActiveRenderInfo(_world, _textureManager, _game.TextRenderer, _game.Camera, partialTicks);
            EntityRenderDispatcher.Instance.CacheRenderInfo(_world, _textureManager, _game.TextRenderer, _game.Camera, _game.Options, partialTicks);

            EntityBatchRenderer.Instance.Begin();
            EntityInstanceBatchRenderer.Instance.Begin();
            CountEntitiesTotal = 0;
            CountEntitiesRendered = 0;
            CountEntitiesHidden = 0;
            EntityLiving camera = _game.Camera;
            EntityRenderDispatcher.OffsetX = camera.LastTickX + (camera.X - camera.LastTickX) * partialTicks;
            EntityRenderDispatcher.OffsetY = camera.LastTickY + (camera.Y - camera.LastTickY) * partialTicks;
            EntityRenderDispatcher.OffsetZ = camera.LastTickZ + (camera.Z - camera.LastTickZ) * partialTicks;
            BlockEntityRenderer.StaticPlayerX = camera.LastTickX + (camera.X - camera.LastTickX) * partialTicks;
            BlockEntityRenderer.StaticPlayerY = camera.LastTickY + (camera.Y - camera.LastTickY) * partialTicks;
            BlockEntityRenderer.StaticPlayerZ = camera.LastTickZ + (camera.Z - camera.LastTickZ) * partialTicks;
            List<Entity> entities = _world.Entities.Entities;
            CountEntitiesTotal = entities.Count;

            int index;
            Entity entity;
            for (index = 0; index < _world.Entities.GlobalEntities.Count; ++index)
            {
                entity = _world.Entities.GlobalEntities[index];
                ++CountEntitiesRendered;
                if (entity.ShouldRender(cameraPos))
                {
                    EntityRenderDispatcher.Instance.RenderEntity(entity, partialTicks);
                }
            }

            for (index = 0; index < entities.Count; ++index)
            {
                entity = entities[index];
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

                if (entity.ShouldRender(cameraPos) && (entity.IgnoreFrustumCheck || culler.IsBoundingBoxInFrustum(entity.BoundingBox)) && (entity != _game.Camera || _game.Options.CameraMode != CameraMode.FirstPerson || _game.Camera.IsSleeping))
                {
                    int yFloor = MathHelper.Floor(entity.Y);
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
                        EntityRenderDispatcher.Instance.RenderEntity(entity, partialTicks);
                    }
                }
            }

            for (index = 0; index < _world.Entities.BlockEntities.Count; ++index)
            {
                BlockEntity blockEntity = _world.Entities.BlockEntities[index];
                if (!blockEntity.IsRemoved() && culler.IsBoundingBoxInFrustum(new Box(blockEntity.X, blockEntity.Y, blockEntity.Z, blockEntity.X + 1, blockEntity.Y + 1, blockEntity.Z + 1)))
                {
                    BlockEntityRenderer.Instance.RenderTileEntity(blockEntity, partialTicks);
                }
            }

            EntityInstanceBatchRenderer.Instance.End();
            EntityBatchRenderer.Instance.End();
        }
    }

    public int SortAndRender(EntityLiving camera, int pass, double partialTicks, ICuller cam)
    {
        if (_game.Options.RenderDistance != _renderDistance)
        {
            LoadRenderers();
        }

        double viewX = camera.LastTickX + (camera.X - camera.LastTickX) * partialTicks;
        double viewY = camera.LastTickY + (camera.Y - camera.LastTickY) * partialTicks;
        double viewZ = camera.LastTickZ + (camera.Z - camera.LastTickZ) * partialTicks;

        Lighting.turnOff();

        var renderParams = new ChunkRenderParams
        {
            Camera = cam,
            ViewPos = new Vector3D<double>(viewX, viewY, viewZ),
            RenderDistance = _renderDistance,
            Ticks = _world.GetTime(),
            PartialTicks = (float)partialTicks,
            DeltaTime = _game.Timer.DeltaTime,
            ChunkFade = _game.Options.ChunkFade,
            RenderOccluded = false
        };

        if (pass == 0)
        {
            ChunkRenderer.Render(renderParams);
        }
        else
        {
            ChunkRenderer.RenderTransparent(renderParams);
        }

        return 0;
    }

    public void UpdateClouds()
    {
        ++_cloudOffsetX;
    }

    public void RenderSky(float tickDelta)
    {
        if (_game.World.Dimension.IsNether) return;

        Vector3D<double> skyColorVec = _world.Environment.GetSkyColor(_game.Camera, tickDelta);
        float skyRed = (float)skyColorVec.X;
        float skyGreen = (float)skyColorVec.Y;
        float skyBlue = (float)skyColorVec.Z;

        float groundR = _world.Dimension.HasGround ? skyRed * 0.2F + 0.04F : skyRed;
        float groundG = _world.Dimension.HasGround ? skyGreen * 0.2F + 0.04F : skyGreen;
        float groundB = _world.Dimension.HasGround ? skyBlue * 0.6F + 0.1F : skyBlue;

        _skyShader.Bind();
        _skyShader.SetCommonUniforms(GameRenderer.ShaderInfo);
        _skyShader.SetUniform1("u_Texture", 0);
        _skyShader.SetUniform1("u_UseTexture", 0);
        _skyShader.SetUniform3("u_SkyColor", new Vector3D<float>(skyRed, skyGreen, skyBlue));
        _skyShader.SetUniform3("u_GroundColor", new Vector3D<float>(groundR, groundG, groundB));

        // Tell the shader what the view is now, and again after every model-view mutation below,
        // because the draws are immediate-mode. The projection does not change in this method.
        _skyShader.SetUniformMatrix4("u_Projection", GLManager.Projection.Top);
        _skyShader.SetUniformMatrix4("u_ModelView", GLManager.ModelView.Top);

        Tessellator tessellator = Tessellator.instance;

        // The sky is drawn after the world, not before it, so it has to be depth tested — terrain
        // already in the buffer covers it — while writing no depth of its own, or a dome at
        // distance 100 would reject everything drawn later. That is RenderState.Translucent, and
        // it holds for the whole pass; only the blend changes below.
        GLManager.State.Apply(RenderState.Translucent);

        // Sky dome (top + bottom) — angle-based gradient
        _skyShader.SetUniform1("u_GradientMode", 1);
        GLManager.Color = new(1.0F, 1.0F, 1.0F, 1.0F);
        _skyAbove.Draw();
        _skyBelow.Draw();

        // Sunrise/sunset fan
        _skyShader.SetUniform1("u_GradientMode", 0);
        GLManager.GL.Disable(GLEnum.AlphaTest);
        Lighting.turnOff();
        float[] backgroundColor = _world.Dimension.GetBackgroundColor(_world.GetTime(tickDelta), tickDelta);
        if (backgroundColor != null)
        {
            GLManager.ShadeModel = ShadeModel.Smooth;
            GLManager.ModelView.Push();
            GLManager.ModelView.Rotate(90.0F, 1.0F, 0.0F, 0.0F);
            float celestialAngle = _world.GetTime(tickDelta);
            GLManager.ModelView.Rotate(celestialAngle > 0.5F ? 180.0F : 0.0F, 0.0F, 0.0F, 1.0F);
            _skyShader.SetUniformMatrix4("u_ModelView", GLManager.ModelView.Top);
            tessellator.startDrawing(6);
            tessellator.setColorRGBA_F(backgroundColor[0], backgroundColor[1], backgroundColor[2], backgroundColor[3]);
            tessellator.addVertex(0.0D, 100.0D, 0.0D);
            tessellator.setColorRGBA_F(backgroundColor[0], backgroundColor[1], backgroundColor[2], 0.0F);
            for (int segment = 0; segment <= 16; ++segment)
            {
                float angle = segment * (float)Math.PI * 2.0F / 16;
                float ringX = MathHelper.Sin(angle);
                float ringY = MathHelper.Cos(angle);
                tessellator.addVertex((ringX * 120.0F), (ringY * 120.0F), (-ringY * 40.0F * backgroundColor[3]));
            }

            tessellator.draw();
            GLManager.ModelView.Pop();
            _skyShader.SetUniformMatrix4("u_ModelView", GLManager.ModelView.Top);
            GLManager.ShadeModel = ShadeModel.Flat;
        }

        // Sun and Moon (textured)
        _skyShader.SetUniform1("u_UseTexture", 1);

        // Sun, moon and the stars after them only ever brighten what is behind them, faded in by
        // their own alpha so the rain gradient can dim them.
        GLManager.State.Apply(RenderState.Translucent with { Blend = BlendMode.AdditiveByAlpha });
        GLManager.ModelView.Push();
        float rainFade = 1.0F - _world.Environment.GetRainGradient(tickDelta);
        GLManager.Color = new(1.0F, 1.0F, 1.0F, rainFade);
        GLManager.ModelView.Rotate(_world.GetTime(tickDelta) * 360.0F, 1.0F, 0.0F, 0.0F);
        _skyShader.SetUniformMatrix4("u_ModelView", GLManager.ModelView.Top);
        float sunQuadSize = 30.0F;
        _textureManager.BindTexture(_textureManager.GetTextureId("/terrain/sun.png"));
        tessellator.startDrawingQuads();
        tessellator.addVertexWithUV(-sunQuadSize, 100.0D, -sunQuadSize, 0.0D, 0.0D);
        tessellator.addVertexWithUV(sunQuadSize, 100.0D, -sunQuadSize, 1.0D, 0.0D);
        tessellator.addVertexWithUV(sunQuadSize, 100.0D, sunQuadSize, 1.0D, 1.0D);
        tessellator.addVertexWithUV(-sunQuadSize, 100.0D, sunQuadSize, 0.0D, 1.0D);
        tessellator.draw();
        sunQuadSize = 20.0F;
        _textureManager.BindTexture(_textureManager.GetTextureId("/terrain/moon.png"));
        tessellator.startDrawingQuads();
        tessellator.addVertexWithUV(-sunQuadSize, -100.0D, sunQuadSize, 1.0D, 1.0D);
        tessellator.addVertexWithUV(sunQuadSize, -100.0D, sunQuadSize, 0.0D, 1.0D);
        tessellator.addVertexWithUV(sunQuadSize, -100.0D, -sunQuadSize, 0.0D, 0.0D);
        tessellator.addVertexWithUV(-sunQuadSize, -100.0D, -sunQuadSize, 1.0D, 0.0D);
        tessellator.draw();

        // Stars
        _skyShader.SetUniform1("u_UseTexture", 0);
        _skyShader.SetUniform1("u_GradientMode", 0);
        float starBrightness = _world.CalculateSkyLightIntensity(tickDelta) * rainFade;
        if (starBrightness > 0.0F)
        {
            GLManager.Color = new(starBrightness, starBrightness, starBrightness, starBrightness);
            _stars.Draw();
        }

        GLManager.Color = new(1.0F, 1.0F, 1.0F, 1.0F);
        GLManager.GL.Enable(GLEnum.AlphaTest);
        GLManager.ModelView.Pop();

        GLManager.GL.UseProgram(0);
        GLManager.State.Apply(RenderState.Opaque);
    }

    public void RenderClouds(float tickDelta)
    {
        using (Profiler.Begin("RenderClouds"))
        {
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

    private static StaticMesh BuildCloudMesh()
    {
        Tessellator tessellator = Tessellator.instance;

        tessellator.startDrawingQuads();
        float uvScale = 1.0F / 256.0F;
        byte tileSize = CloudsRenderDistance;
        float tile = tileSize * uvScale;

        tessellator.setNormal(0.0F, -1.0F, 0.0F);
        tessellator.addVertexWithUV(0, 0.0, tileSize, 0, tile);
        tessellator.addVertexWithUV(tileSize, 0.0, tileSize, tile, tile);
        tessellator.addVertexWithUV(tileSize, 0.0, 0, tile, 0);
        tessellator.addVertexWithUV(0, 0.0, 0, 0, 0);

        return tessellator.captureStatic();
    }

    /// <summary>Bottom, top, and the two side faces, in the order the draw path expects them.</summary>
    private static StaticMesh[] BuildLegacyCloudMeshes()
    {
        Tessellator tessellator = Tessellator.instance;
        StaticMesh[] meshes = new StaticMesh[4];

        for (int i = 0; i < 4; ++i)
        {
            tessellator.startDrawingQuads();
            float cloudHeight = 4.0F;
            float uvScale = 1.0F / 256.0F;
            float edgeInset = 1.0F / 1024.0F;
            byte tileSize = 8;
            byte cloudRadius = 3;

            for (int tileX = -cloudRadius + 1; tileX <= cloudRadius; ++tileX)
            {
                for (int tileZ = -cloudRadius + 1; tileZ <= cloudRadius; ++tileZ)
                {
                    float uvX = tileX * tileSize;
                    float uvZ = tileZ * tileSize;
                    float x = uvX;
                    float z = uvZ;

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
                            for (int edgeSlice = 0; edgeSlice < tileSize; ++edgeSlice)
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
                            for (int edgeSlice = 0; edgeSlice < tileSize; ++edgeSlice)
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
                            for (int edgeSlice = 0; edgeSlice < tileSize; ++edgeSlice)
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
                            for (int edgeSlice = 0; edgeSlice < tileSize; ++edgeSlice)
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
        float cameraY = (float)(_game.Camera.LastTickY + (_game.Camera.Y - _game.Camera.LastTickY) * tickDelta);
        const float cloudScale = 12.0F;
        double cloudOffsetX = (_game.Camera.PrevX + (_game.Camera.X - _game.Camera.PrevX) * tickDelta + ((_cloudOffsetX + tickDelta) * 0.03F)) / cloudScale;
        double cloudOffsetZ = (_game.Camera.PrevZ + (_game.Camera.Z - _game.Camera.PrevZ) * tickDelta) / cloudScale + 0.33F;
        float cloudY = _world.Dimension.CloudHeight - cameraY + 0.33F;
        int cloudChunkX = MathHelper.Floor(cloudOffsetX / 2048.0D);
        int cloudChunkZ = MathHelper.Floor(cloudOffsetZ / 2048.0D);
        cloudOffsetX -= cloudChunkX * 2048;
        cloudOffsetZ -= cloudChunkZ * 2048;
        _textureManager.BindTexture(_textureManager.GetTextureId("/environment/clouds.png"));

        // Culling off because the cloud sheet is a single plane seen from either side, depending
        // on whether the camera is above or below the cloud layer.
        GLManager.State.Apply(RenderState.Entity with { Blend = BlendMode.Alpha });

        Vector3D<double> cloudColor = _world.Environment.GetCloudColor(tickDelta);
        float cloudRed = (float)cloudColor.X;
        float cloudGreen = (float)cloudColor.Y;
        float cloudBlue = (float)cloudColor.Z;

        const float textureScale = 1 / 256f;
        float textureOffsetU = MathHelper.Floor(cloudOffsetX) * textureScale;
        float textureOffsetV = MathHelper.Floor(cloudOffsetZ) * textureScale;
        float subCloudOffsetX = (float)(cloudOffsetX - MathHelper.Floor(cloudOffsetX)) + (CloudsRenderDistance / 2);
        float subCloudOffsetZ = (float)(cloudOffsetZ - MathHelper.Floor(cloudOffsetZ)) + (CloudsRenderDistance / 2);

        _cloudShader.Bind();
        _cloudShader.SetCommonUniforms(GameRenderer.ShaderInfo);
        _cloudShader.SetUniform1("u_Texture", 0);
        _cloudShader.SetUniform3("u_CloudOffset", new Vector3D<float>(-subCloudOffsetX, cloudY, -subCloudOffsetZ));
        _cloudShader.SetUniform1("u_CloudScale", cloudScale / 2f);
        // Upload the matrices now and again after each mutation below — the draws are immediate.
        _cloudShader.SetUniformMatrix4("u_Projection", GLManager.Projection.Top);
        _cloudShader.SetUniformMatrix4("u_ModelView", GLManager.ModelView.Top);
        _cloudShader.SetUniformMatrix4("u_TextureMatrix", GLManager.TextureMatrix.Top);

        GLManager.ModelView.Scale(cloudScale, 1.0F, cloudScale);
        GLManager.ModelView.Push();
        GLManager.ModelView.Translate(-subCloudOffsetX, cloudY, -subCloudOffsetZ);
        _cloudShader.SetUniformMatrix4("u_ModelView", GLManager.ModelView.Top);

        GLManager.TextureMatrix.Push();
        GLManager.TextureMatrix.Translate(textureOffsetU, textureOffsetV, 0.0F);
        _cloudShader.SetUniformMatrix4("u_TextureMatrix", GLManager.TextureMatrix.Top);

        GLManager.Color = new(cloudRed, cloudGreen, cloudBlue, 0.8F);
        _clouds[0].Draw();

        GLManager.TextureMatrix.Pop();

        GLManager.ModelView.Pop();

        GLManager.GL.UseProgram(0);

        GLManager.Color = new(1.0F, 1.0F, 1.0F, 1.0F);

        // This used to put culling back and leave blending on, so the first-person hand pass drew
        // blended or not depending on whether clouds were enabled and the camera was in the Nether.
        GLManager.State.Apply(RenderState.Opaque);
    }

    private void RenderLegacyCloudsFancy(float tickDelta)
    {
        float cameraY = (float)(_game.Camera.LastTickY + (_game.Camera.Y - _game.Camera.LastTickY) * tickDelta);
        const float cloudScale = 12.0F;
        const float cloudHeight = 4.0F;
        double cloudOffsetX = (_game.Camera.PrevX + (_game.Camera.X - _game.Camera.PrevX) * tickDelta + ((_cloudOffsetX + tickDelta) * 0.03F)) / cloudScale;
        double cloudOffsetZ = (_game.Camera.PrevZ + (_game.Camera.Z - _game.Camera.PrevZ) * tickDelta) / cloudScale + 0.33F;
        float cloudY = _world.Dimension.CloudHeight - cameraY + 0.33F;
        int cloudChunkX = MathHelper.Floor(cloudOffsetX / 2048.0D);
        int cloudChunkZ = MathHelper.Floor(cloudOffsetZ / 2048.0D);
        cloudOffsetX -= cloudChunkX * 2048;
        cloudOffsetZ -= cloudChunkZ * 2048;
        _textureManager.BindTexture(_textureManager.GetTextureId("/environment/clouds.png"));

        // Culling off because these are boxes seen from inside as often as outside — the camera
        // can sit within the cloud layer.
        RenderState cloudState = RenderState.Entity with { Blend = BlendMode.Alpha };

        Vector3D<double> cloudColor = _world.Environment.GetCloudColor(tickDelta);
        float cloudRed = (float)cloudColor.X;
        float cloudGreen = (float)cloudColor.Y;
        float cloudBlue = (float)cloudColor.Z;

        const float textureScale = 1 / 256f;
        float textureOffsetU = MathHelper.Floor(cloudOffsetX) * textureScale;
        float textureOffsetV = MathHelper.Floor(cloudOffsetZ) * textureScale;
        float subCloudOffsetX = (float)(cloudOffsetX - MathHelper.Floor(cloudOffsetX));
        float subCloudOffsetZ = (float)(cloudOffsetZ - MathHelper.Floor(cloudOffsetZ));

        GLManager.ModelView.Scale(cloudScale, 1.0F, cloudScale);

        for (int passIndex = 0; passIndex < 2; ++passIndex)
        {
            // Pass 0 writes only depth. With culling off, a box's near and far faces would both
            // blend into the same pixel and come out twice as opaque; laying depth down first
            // leaves pass 1 blending each surface exactly once.
            GLManager.State.Apply(cloudState with { ColorWrite = passIndex != 0 });

            GLManager.ModelView.Push();
            GLManager.ModelView.Translate(-subCloudOffsetX, cloudY, -subCloudOffsetZ);

            GLManager.TextureMatrix.Push();
            GLManager.TextureMatrix.Translate(textureOffsetU, textureOffsetV, 0.0F);

            if (cloudY > -cloudHeight - 1.0F)
            {
                GLManager.Color = new(cloudRed * 0.7F, cloudGreen * 0.7F, cloudBlue * 0.7F, 0.8F);
                _clouds[0].Draw(); // Bottom
            }

            if (cloudY <= cloudHeight + 1.0F)
            {
                GLManager.Color = new(cloudRed, cloudGreen, cloudBlue, 0.8F);
                _clouds[1].Draw(); // Top
            }

            GLManager.Color = new(cloudRed * 0.9F, cloudGreen * 0.9F, cloudBlue * 0.9F, 0.8F);
            _clouds[2].Draw(); // Side X

            GLManager.Color = new(cloudRed * 0.8F, cloudGreen * 0.8F, cloudBlue * 0.8F, 0.8F);
            _clouds[3].Draw(); // Side Z

            GLManager.TextureMatrix.Pop();

            GLManager.ModelView.Pop();
        }

        GLManager.Color = new(1.0F, 1.0F, 1.0F, 1.0F);
        GLManager.State.Apply(RenderState.Opaque);
    }

    public void DrawBlockBreaking(EntityPlayer entityPlayer, HitResult hit, ItemStack itemStack, float tickDelta)
    {
        if (DamagePartialTime <= 0.0F) return;

        Tessellator tessellator = Tessellator.instance;

        GLManager.ModelView.Push();
        GLManager.GL.Enable(GLEnum.AlphaTest);
        GLManager.GL.Enable(GLEnum.PolygonOffsetFill);

        // Culling matters here and was previously inherited: this redraws the block's own faces
        // with the crack texture multiplied over them, so with culling off the far faces multiply
        // a second time and the crack comes out twice as dark. GameRenderer calls this from two
        // places — once for the underwater case, straight after the entity pass has left culling
        // off, and once with it on — which is why the crack looked different underwater.
        GLManager.State.Apply(RenderState.Opaque with { Blend = BlendMode.Multiply });
        GLManager.Color = new(1.0F, 1.0F, 1.0F, 0.5F);
        GLManager.GL.PolygonOffset(-3.0F, -50.0F);

        _textureManager.BindTexture(_textureManager.GetTextureId("/terrain.png"));

        int targetBlockId = _world.Reader.GetBlockId(hit.BlockX, hit.BlockY, hit.BlockZ);
        Block targetBlock = targetBlockId > 0 ? Block.Blocks[targetBlockId] : BlockRegistry.Get("stone");

        double renderX = entityPlayer.LastTickX + (entityPlayer.X - entityPlayer.LastTickX) * tickDelta;
        double renderY = entityPlayer.LastTickY + (entityPlayer.Y - entityPlayer.LastTickY) * tickDelta;
        double renderZ = entityPlayer.LastTickZ + (entityPlayer.Z - entityPlayer.LastTickZ) * tickDelta;

        tessellator.startDrawingQuads();
        tessellator.setTranslationD(-renderX, -renderY, -renderZ);
        tessellator.disableColor();

        BlockRenderer.RenderBlockByRenderType(_world.Reader, _world.Lighting, targetBlock, new BlockPos(hit.BlockX, hit.BlockY, hit.BlockZ), tessellator, 240 + (int)(DamagePartialTime * 10.0F), true, _game.Options.AlternateBlocksEnabled);
        tessellator.draw();

        tessellator.setTranslationD(0.0D, 0.0D, 0.0D);
        GLManager.GL.PolygonOffset(0.0F, 0.0F);
        GLManager.Color = new(1.0F, 1.0F, 1.0F, 1.0F);

        GLManager.GL.Disable(GLEnum.PolygonOffsetFill);
        GLManager.GL.Disable(GLEnum.AlphaTest);
        GLManager.State.Apply(RenderState.Opaque);
        GLManager.ModelView.Pop();
    }

    public void DrawSelectionBox(EntityPlayer player, HitResult hit, int renderPass, ItemStack itemStack, float tickDelta)
    {
        if (renderPass == 0 && hit.Type == HitResultType.Tile)
        {
            // Line loops, so the culling this carries is inert; what it is here for is the depth
            // pair — tested, so the outline is hidden by blocks in front of the target, but not
            // written, so a line lying exactly on a block face does not fight with it.
            GLManager.State.Apply(RenderState.Translucent);
            GLManager.Color = new(0.0F, 0.0F, 0.0F, 0.4F);
            GLManager.GL.LineWidth(2.0F);
            GLManager.GL.Disable(GLEnum.Texture2D);
            float outlinePadding = 0.002F;
            int blockId = _world.Reader.GetBlockId(hit.BlockX, hit.BlockY, hit.BlockZ);
            if (blockId > 0)
            {
                Block.Blocks[blockId].UpdateBoundingBox(_world.Reader, hit.BlockX, hit.BlockY, hit.BlockZ);
                double renderX = player.LastTickX + (player.X - player.LastTickX) * tickDelta;
                double renderY = player.LastTickY + (player.Y - player.LastTickY) * tickDelta;
                double renderZ = player.LastTickZ + (player.Z - player.LastTickZ) * tickDelta;
                DrawOutlinedBoundingBox(Block.Blocks[blockId].GetBoundingBox(_world.Reader, _world.Entities, hit.BlockX, hit.BlockY, hit.BlockZ).Expand(outlinePadding, outlinePadding, outlinePadding).Offset(-renderX, -renderY, -renderZ));
            }

            GLManager.GL.Enable(GLEnum.Texture2D);
            GLManager.State.Apply(RenderState.Opaque);
        }
    }

    private static void DrawOutlinedBoundingBox(Box box)
    {
        Tessellator tessellator = Tessellator.instance;
        tessellator.startDrawing(3);
        tessellator.addVertex(box.MinX, box.MinY, box.MinZ);
        tessellator.addVertex(box.MaxX, box.MinY, box.MinZ);
        tessellator.addVertex(box.MaxX, box.MinY, box.MaxZ);
        tessellator.addVertex(box.MinX, box.MinY, box.MaxZ);
        tessellator.addVertex(box.MinX, box.MinY, box.MinZ);
        tessellator.draw();
        tessellator.startDrawing(3);
        tessellator.addVertex(box.MinX, box.MaxY, box.MinZ);
        tessellator.addVertex(box.MaxX, box.MaxY, box.MinZ);
        tessellator.addVertex(box.MaxX, box.MaxY, box.MaxZ);
        tessellator.addVertex(box.MinX, box.MaxY, box.MaxZ);
        tessellator.addVertex(box.MinX, box.MaxY, box.MinZ);
        tessellator.draw();
        tessellator.startDrawing(1);
        tessellator.addVertex(box.MinX, box.MinY, box.MinZ);
        tessellator.addVertex(box.MinX, box.MaxY, box.MinZ);
        tessellator.addVertex(box.MaxX, box.MinY, box.MinZ);
        tessellator.addVertex(box.MaxX, box.MaxY, box.MinZ);
        tessellator.addVertex(box.MaxX, box.MinY, box.MaxZ);
        tessellator.addVertex(box.MaxX, box.MaxY, box.MaxZ);
        tessellator.addVertex(box.MinX, box.MinY, box.MaxZ);
        tessellator.addVertex(box.MinX, box.MaxY, box.MaxZ);
        tessellator.draw();
    }

    public void MarkBlocksDirty(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        int xStart = (int)Math.Floor((double)minX / SubChunkRenderer.Size);
        int yStart = (int)Math.Floor((double)minY / SubChunkRenderer.Size);
        int zStart = (int)Math.Floor((double)minZ / SubChunkRenderer.Size);
        int xEnd = (int)Math.Ceiling((double)maxX / SubChunkRenderer.Size);
        int yEnd = (int)Math.Ceiling((double)maxY / SubChunkRenderer.Size);
        int zEnd = (int)Math.Ceiling((double)maxZ / SubChunkRenderer.Size);

        for (int x = xStart; x <= xEnd; x++)
        {
            for (int y = yStart; y <= yEnd; y++)
            {
                for (int z = zStart; z <= zEnd; z++)
                {
                    ChunkRenderer.MarkDirty(new Vector3D<int>(x, y, z) * SubChunkRenderer.Size, true);
                }
            }
        }
    }

    public void BlockUpdate(int x, int y, int z)
    {
        MarkBlocksDirty(x - 1, y - 1, z - 1, x + 1, y + 1, z + 1);
    }

    public void SetBlocksDirty(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        if (!_world.BlockHost.IsRegionLoaded(minX, minY, minZ, maxX, maxY, maxZ))
        {
            return;
        }

        MarkBlocksDirty(minX - 1, minY - 1, minZ - 1, maxX + 1, maxY + 1, maxZ + 1);
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
        float maxDistance = 16.0F;
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
            double cameraDx = _game.Camera.X - x;
            double cameraDy = _game.Camera.Y - y;
            double cameraDz = _game.Camera.Z - z;
            double maxDistance = 16.0D;
            if (cameraDx * cameraDx + cameraDy * cameraDy + cameraDz * cameraDz <= maxDistance * maxDistance)
            {
                ParticleManager pm = _game.ParticleManager;
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
                    case "snowballpoof": pm.AddSlime(x, y, z, Item.ByName("snowball")); break;
                    case "snowshovel": pm.AddSnowShovel(x, y, z, velocityX, velocityY, velocityZ); break;
                    case "slime": pm.AddSlime(x, y, z, Item.ByName("slimeball")); break;
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

    public void NotifyEntityRemoved(Entity entity) { }

    public void NotifyAmbientDarknessChanged()
    {
        ChunkRenderer.UpdateAllRenderers();
    }

    public void UpdateBlockEntity(int x, int y, int z, BlockEntity blockEntity) { }

    public void WorldEvent(EntityPlayer? player, int eventId, int x, int y, int z, int data)
    {
        JavaRandom random = _world.Random;
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
                for (int particleIndex = 0; particleIndex < Random.Shared.Next(8, 12); ++particleIndex)
                {
                    _world.Broadcaster.AddParticle("largesmoke", x + random.NextDouble(), y + 1.2D, z + random.NextDouble(), 0.0D, 0.0D, 0.0D);
                }

                break;
            case 1005:
                if (Item.Items[data]?.GetBehavior<RecordBehavior>() is { } record)
                {
                    _game.SoundManager.PlayStreaming(record.RecordName, x, y, z, 1.0F, 1.0F);
                }
                else
                {
                    _game.SoundManager.PlayStreaming(null, x, y, z, 1.0F, 1.0F);
                }

                break;
            case 2000:
                int offsetX = data % 3 - 1;
                int offsetZ = data / 3 % 3 - 1;
                double particleX = x + offsetX * 0.6D + 0.5D;
                double particleY = y + 0.5D;
                double particleZ = z + offsetZ * 0.6D + 0.5D;

                for (blockId = 0; blockId < 10; ++blockId)
                {
                    double speed = random.NextDouble() * 0.2D + 0.01D;
                    double smokeX = particleX + offsetX * 0.01D + (random.NextDouble() - 0.5D) * offsetZ * 0.5D;
                    double smokeY = particleY + (random.NextDouble() - 0.5D) * 0.5D;
                    double smokeZ = particleZ + offsetZ * 0.01D + (random.NextDouble() - 0.5D) * offsetX * 0.5D;
                    double velocityX = offsetX * speed + random.NextGaussian() * 0.01D;
                    double velocityY = -0.03D + random.NextGaussian() * 0.01D;
                    double velocityZ = offsetZ * speed + random.NextGaussian() * 0.01D;
                    SpawnParticle("smoke", smokeX, smokeY, smokeZ, velocityX, velocityY, velocityZ);
                }

                return;
            case 2001: // This is for breaking a block
                WorldEventBreak(data & 255, (data >> 8) & 255, x, y, z);
                break;
        }
    }

    public void WorldEventBreak(int blockId, int meta, int x, int y, int z)
    {
        if (blockId == 0) return;
        Block block = Block.Blocks[blockId];
        WorldEventBreak(block, meta, x, y, z);
    }

    public void WorldEventBreak(Block block, int meta, int x, int y, int z)
    {
        _game.SoundManager.PlayBreakSound(block.SoundGroup, x, y, z);
        _game.ParticleManager.AddBlockDestroyEffects(x, y, z, block, meta);
    }

    public void PlayNote(int x, int y, int z, int soundType, int pitch) { }
    public void BroadcastEntityEvent(Entity entity, byte @event) { }
}
