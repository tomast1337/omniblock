using OmniBlock.Blocks.Materials;
using OmniBlock.Client.Input;
using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Items;
using OmniBlock.Entities;
using OmniBlock.Profiling;
using OmniBlock.Util.Hit;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Generation.Biomes;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering;

public class GameRenderer
{
    public static readonly CommonShaderInfo ShaderInfo = new();
    private readonly OmniBlock _client;

    private readonly bool _cloudFog = false;
    private readonly MouseFilter _mouseFilterXAxis = new();
    private readonly MouseFilter _mouseFilterYAxis = new();
    private readonly JavaRandom _random = new();
    public readonly CameraController CameraController;
    public readonly HeldItemRenderer ItemRenderer;
    private float _fogColorBlue;
    private float _fogColorGreen;
    private float _fogColorRed;

    private long _prevFrameTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private int _rainSoundCounter;
    private Entity? _targetedEntity;
    private int _ticks;
    private float _viewDistance;

    public GameRenderer(OmniBlock game)
    {
        _client = game;
        ItemRenderer = new HeldItemRenderer(game);
        CameraController = new CameraController(game);
    }

    /// <summary>
    ///     The colour the world pass starts from, which is the distance fog's.
    /// </summary>
    /// <remarks>
    ///     Read by a backend that clears as part of opening its pass rather than with a call, and so
    ///     has to know the colour before the pass exists. Valid once
    ///     <see cref="BeginWorldFrame" /> has run for the frame.
    /// </remarks>
    public Vector4D<float> WorldClearColor => new(_fogColorRed, _fogColorGreen, _fogColorBlue, 1.0f);

    public void UpdateCamera()
    {
        CameraController.UpdateCamera();
        ++_ticks;
        ItemRenderer.updateEquippedItem();
        RenderRain();
    }

    public void Tick(float tickDelta)
    {
        if (_client.WorldRenderer != null)
        {
            _client.WorldRenderer.Tick(_client.Camera, tickDelta);
        }
    }

    public void UpdateTargetedEntity(float tickDelta)
    {
        if (_client.Camera == null)
        {
            return;
        }

        if (_client.World == null)
        {
            return;
        }

        double reachDistance = _client.PlayerController.GetBlockReachDistance();
        _client.ObjectMouseOver = _client.Camera.RayTrace(reachDistance, tickDelta);
        var cameraPosition = _client.Camera.GetPosition(tickDelta);

        if (_client.ObjectMouseOver.Type != HitResultType.Miss)
        {
            reachDistance = Math.Min(
                _client.ObjectMouseOver.Pos.DistanceTo(cameraPosition),
                _client.PlayerController.GetEntityReachDistance()
            );
        }
        else
        {
            reachDistance = _client.PlayerController.GetEntityReachDistance();
        }

        var lookVec = _client.Camera.GetLook(tickDelta);
        var targetVec = cameraPosition + reachDistance * lookVec;
        _targetedEntity = null;

        var searchMargin = 1.0F;
        var entities = _client.World.Entities.GetEntities(_client.Camera, _client.Camera.BoundingBox.Stretch(lookVec.X * reachDistance, lookVec.Y * reachDistance, lookVec.Z * reachDistance).Expand(searchMargin, searchMargin, searchMargin));

        var closestDistance = double.MaxValue;
        foreach (var ent in entities)
        {
            if (ent.HasCollision)
            {
                var targetingMargin = ent.TargetingMargin;
                var box = ent.BoundingBox.Expand(targetingMargin, targetingMargin, targetingMargin);
                var hit = box.Raycast(cameraPosition, targetVec);

                if (box.Contains(cameraPosition))
                {
                    _targetedEntity = ent;
                    closestDistance = 0.0D;
                    break;
                }

                if (hit.Type != HitResultType.Miss)
                {
                    var hitDistance = cameraPosition.DistanceTo(hit.Pos);
                    if (hitDistance < closestDistance)
                    {
                        _targetedEntity = ent;
                        closestDistance = hitDistance;
                    }
                }
            }
        }

        if (_targetedEntity != null)
        {
            _client.ObjectMouseOver = new HitResult(_targetedEntity);
        }
    }


    /// <summary>
    ///     Loads the projection and model-view the world is seen through, including the zoom, the
    ///     damage tilt, the view bob and the portal distortion.
    /// </summary>
    /// <remarks>
    ///     Draws nothing — it only writes the matrix stacks, which is why a backend that does not
    ///     go through <see cref="RenderFrame" /> can still call it to place its own camera.
    /// </remarks>
    public void SetupWorldCamera(float tickDelta)
    {
        _viewDistance = _client.Options.RenderDistance * 16.0f;
        RenderSystem.Projection.LoadIdentity();

        if (CameraController.CameraZoom != 1.0D)
        {
            RenderSystem.Projection.Translate((float)CameraController.CameraYaw, (float)-CameraController.CameraPitch, 0.0F);
            RenderSystem.Projection.Scale((float)CameraController.CameraZoom, (float)CameraController.CameraZoom, 1.0F);
            GLU.gluPerspective(CameraController.GetFov(tickDelta), _client.DisplayWidth / (float)_client.DisplayHeight, 0.05F, _viewDistance * 2.0F);
        }
        else
        {
            GLU.gluPerspective(CameraController.GetFov(tickDelta), _client.DisplayWidth / (float)_client.DisplayHeight, 0.05F, _viewDistance * 2.0F);
        }

        RenderSystem.ModelView.LoadIdentity();

        CameraController.ApplyDamageTiltEffect(tickDelta);
        if (_client.Options.ViewBobbing)
        {
            CameraController.ApplyViewBobbing(tickDelta);
        }

        var screenDistortion = _client.Player.LastScreenDistortion + (_client.Player.ChangeDimensionCooldown - _client.Player.LastScreenDistortion) * tickDelta;
        if (screenDistortion > 0.0F)
        {
            var distortionScale = 5.0F / (screenDistortion * screenDistortion + 5.0F) - screenDistortion * 0.04F;
            distortionScale *= distortionScale;
            RenderSystem.ModelView.Rotate((_ticks + tickDelta) * 20.0F, 0.0F, 1.0F, 1.0F);
            RenderSystem.ModelView.Scale(1.0F / distortionScale, 1.0F, 1.0F);
            RenderSystem.ModelView.Rotate(-(_ticks + tickDelta) * 20.0F, 0.0F, 1.0F, 1.0F);
        }

        CameraController.ApplyCameraTransform(tickDelta);
    }

    private void RenderFirstPersonHand(float tickDelta)
    {
        RenderSystem.Projection.LoadIdentity();
        if (CameraController.CameraZoom != 1.0D)
        {
            RenderSystem.Projection.Translate((float)CameraController.CameraYaw, (float)-CameraController.CameraPitch, 0.0F);
            RenderSystem.Projection.Scale((float)CameraController.CameraZoom, (float)CameraController.CameraZoom, 1.0F);
        }

        GLU.gluPerspective(CameraController.GetFov(tickDelta, true), _client.DisplayWidth / (float)_client.DisplayHeight, 0.05F, _viewDistance * 2.0F);
        RenderSystem.ModelView.LoadIdentity();

        RenderSystem.ModelView.Push();
        CameraController.ApplyDamageTiltEffect(tickDelta);
        if (_client.Options.ViewBobbing)
        {
            CameraController.ApplyViewBobbing(tickDelta);
        }

        if (_client.Options.CameraMode == CameraMode.FirstPerson && !_client.Camera.IsSleeping && !_client.Options.HideGUI)
        {
            ItemRenderer.renderItemInFirstPerson(tickDelta);
        }

        RenderSystem.ModelView.Pop();
        if (_client.Options.CameraMode == CameraMode.FirstPerson && !_client.Camera.IsSleeping)
        {
            ItemRenderer.renderOverlays(tickDelta);
            CameraController.ApplyDamageTiltEffect(tickDelta);
        }

        if (_client.Options.ViewBobbing)
        {
            CameraController.ApplyViewBobbing(tickDelta);
        }
    }

    /// <summary>
    ///     Turns the frame's mouse and controller movement into a look direction, and reads the
    ///     zoom key.
    /// </summary>
    /// <remarks>
    ///     Per frame rather than per tick, which is what makes looking around smooth at any frame
    ///     rate. Public because it is the only place the mouse delta is consumed, so a backend that
    ///     does not go through <see cref="OnFrameUpdate" /> has to call it or the view cannot turn.
    /// </remarks>
    public void ProcessLookInput()
    {
        if (!Display.isActive())
        {
            if (_client.Options.PauseOnFocusLossOption.Value &&
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _prevFrameTime > 500L)
            {
                _client.DisplayInGameMenu();
            }

            // Background simulation and scripting may continue, but physical look input belongs
            // to the focused window only.
            return;
        }
        else
        {
            _prevFrameTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        if (_client.InGameHasFocus)
        {
            _client.MouseHelper.MouseXYChange();
            var baseSensitivity = _client.Options.MouseSensitivity * 0.6F + 0.2F;
            var lookScale = baseSensitivity * baseSensitivity * baseSensitivity * 8.0F;
            var yawDelta = _client.MouseHelper.DeltaX * lookScale;
            var pitchDelta = _client.MouseHelper.DeltaY * lookScale;

            var zoomHeldForSensitivity = _client.CurrentScreen == null && _client.InGameHasFocus && Keyboard.isKeyDown(_client.Options.KeyBindZoom.ScanCode);
            if (zoomHeldForSensitivity)
            {
                var zoomProgress = 1.0F / Math.Clamp(_client.Options.ZoomScale, 1.25F, 20.0F);
                var sensitivityFloor = 0.4F;
                var zoomSensitivityMultiplier = sensitivityFloor + (1.0F - sensitivityFloor) * zoomProgress;
                yawDelta *= zoomSensitivityMultiplier;
                pitchDelta *= zoomSensitivityMultiplier;
            }

            ControllerManager.HandleLook(ref yawDelta, ref pitchDelta, lookScale, _client.Timer.DeltaTime);
            var invertMultiplier = -1;
            if (_client.Options.InvertMouse)
            {
                invertMultiplier = 1;
            }

            if (_client.Options.SmoothCamera)
            {
                yawDelta = _mouseFilterXAxis.Smooth(yawDelta, 0.05F * lookScale);
                pitchDelta = _mouseFilterYAxis.Smooth(pitchDelta, 0.05F * lookScale);
            }

            _client.Player.ChangeLookDirection(yawDelta, pitchDelta * invertMultiplier);
        }

        var zoomHeld = (_client.CurrentScreen == null && _client.InGameHasFocus && Keyboard.isKeyDown(_client.Options.KeyBindZoom.ScanCode)) || ControllerManager.IsZoomHeld();
        CameraController.SetZoomState(zoomHeld, _client.Options.ZoomScale);
    }

    /// <summary>
    ///     Everything the world pass needs decided before it opens: what the camera is pointed at,
    ///     the shader clock, and the frame's sky and fog colours.
    /// </summary>
    /// <remarks>
    ///     Split from <see cref="DrawWorld" /> because under WebGPU the clear is part of beginning
    ///     the pass, so <see cref="WorldClearColor" /> has to be settled while there is still no
    ///     pass open. Nothing here draws.
    /// </remarks>
    public void BeginWorldFrame(float tickDelta, long time)
    {
        // The frame's baseline, and the state every renderer below is traced against. It carries
        // the depth write mask, which the two enables it replaces did not: a depth clear is masked
        // by that mask, and the interface pass this frame follows leaves it off, so the clear
        // below only does anything because this turns it back on.
        RenderSystem.State.Apply(RenderState.Opaque);

        using (Profiler.Begin("GetMouseOver"))
        {
            UpdateTargetedEntity(tickDelta);
        }

        ShaderInfo.Time = time;
        ShaderInfo.DeltaTime = tickDelta;
        ShaderInfo.DayTime = ((int)(time % 24000) + tickDelta) / 20f;

        using (Profiler.Begin("UpdateFog"))
        {
            // A pass covers its whole attachment unless something sets otherwise, and nothing here
            // does, so no viewport call is needed.
            UpdateSkyAndFogColors(tickDelta);
        }
    }

    /// <summary>
    ///     Draws the world into whatever target is current, which the caller has already cleared.
    /// </summary>
    public void DrawWorld(float tickDelta, bool includeHand = true)
    {
        var entity = _client.Camera;
        var worldRenderer = _client.WorldRenderer;
        var particleManager = _client.ParticleManager;
        var entX = entity.LastTickX + (entity.X - entity.LastTickX) * tickDelta;
        var entY = entity.LastTickY + (entity.Y - entity.LastTickY) * tickDelta;
        var entZ = entity.LastTickZ + (entity.Z - entity.LastTickZ) * tickDelta;

        SetupWorldCamera(tickDelta);
        // Capture the camera transforms once. Later entity, particle, selection, and hand draws
        // mutate the compatibility stacks; both terrain passes must use this same world view.
        var worldModelView = RenderSystem.ModelView.Top;
        var worldProjection = RenderSystem.Projection.Top;
        Frustum.Instance();
        if (_client.Options.RenderDistance >= 8)
        {
            ApplyFog(-1);
            worldRenderer.RenderSky(tickDelta);
        }

        RenderSystem.FogEnabled = true;
        ApplyFog(1);

        FrustrumCuller frustrumCuller = new();
        frustrumCuller.SetPosition(entX, entY, entZ);

        ApplyFog(0);
        RenderSystem.FogEnabled = true;
        _client.TextureManager.BindTexture(_client.TextureManager.GetTextureId("/terrain.png"));
        Lighting.turnOff();

        using (Profiler.Begin("SortAndRender"))
        {
            worldRenderer.SortAndRender(
                entity, 0, tickDelta, frustrumCuller, worldModelView, worldProjection);
        }

        RenderSystem.ShadeModel = ShadeModel.Flat;
        Lighting.turnOn();

        using (Profiler.Begin("RenderEntities"))
        {
            worldRenderer.RenderEntities(entity.GetPosition(tickDelta), frustrumCuller, tickDelta);
        }

        particleManager.renderSpecialParticles(entity, tickDelta);

        Lighting.turnOff();
        ApplyFog(0);

        using (Profiler.Begin("RenderParticles"))
        {
            particleManager.renderParticles(entity, tickDelta);
        }

        EntityPlayer entityPlayer;
        if (_client.ObjectMouseOver.Type != HitResultType.Miss && entity.IsInFluid(Material.Water) && entity is EntityPlayer)
        {
            entityPlayer = (EntityPlayer)entity;
            RenderSystem.AlphaTestEnabled = false;
            worldRenderer.DrawBlockBreaking(entityPlayer, _client.ObjectMouseOver, entityPlayer.Inventory.ItemInHand, tickDelta);
            worldRenderer.DrawSelectionBox(entityPlayer, _client.ObjectMouseOver, 0, entityPlayer.Inventory.ItemInHand, tickDelta);
            RenderSystem.AlphaTestEnabled = true;
        }

        ApplyFog(0);

        // Water and glass: blended and unculled so the far side of a body of water is drawn, but
        // still depth writing. The depth write was previously inherited rather than stated — the
        // entity pass before this leaves it on only because shadows put it back — which is what
        // the DepthMask below is cleaning up after.
        RenderSystem.State.Apply(RenderState.Entity with
        {
            Blend = BlendMode.Alpha
        });
        _client.TextureManager.BindTexture(_client.TextureManager.GetTextureId("/terrain.png"));

        using (Profiler.Begin("SortAndRenderTranslucent"))
        {
            worldRenderer.SortAndRender(
                entity, 1, tickDelta, frustrumCuller, worldModelView, worldProjection);

            RenderSystem.ShadeModel = ShadeModel.Flat;
        }

        //TODO: SELCTION BOX/BLOCK BREAKING VISUALIZATON DON'T APPEAR PROPERLY MOST OF THE TIME, SAME WITH ENTITY SHADOWS. VIEW BOBBING MAKES ENTITES BOB UP AND DOWN

        RenderSystem.State.Apply(RenderState.Opaque);
        if (!CameraController.IsZoomActive && entity is EntityPlayer && _client.ObjectMouseOver.Type != HitResultType.Miss && !entity.IsInFluid(Material.Water))
        {
            entityPlayer = (EntityPlayer)entity;
            RenderSystem.AlphaTestEnabled = false;
            worldRenderer.DrawBlockBreaking(entityPlayer, _client.ObjectMouseOver, entityPlayer.Inventory.ItemInHand, tickDelta);
            worldRenderer.DrawSelectionBox(entityPlayer, _client.ObjectMouseOver, 0, entityPlayer.Inventory.ItemInHand, tickDelta);
            RenderSystem.AlphaTestEnabled = true;
        }

        RenderSnow(tickDelta);
        RenderSystem.FogEnabled = false;
        if (_targetedEntity != null)
        {
        }

        ApplyFog(0);
        RenderSystem.FogEnabled = true;

        if (_client.ShowChunkBorders)
        {
            RenderChunkBorders(tickDelta);
        }

        var cloudBlurPass = _client.Options is { SoftClouds: true, CloudsQuality: >= 2 }
                            && RenderSystem.CloudBlurPassOrNull is not null;

        if (cloudBlurPass) RenderSystem.CloudBlurPassOrNull!.Begin();
        worldRenderer.RenderClouds(tickDelta);
        if (cloudBlurPass) RenderSystem.CloudBlurPassOrNull!.End();
        RenderSystem.FogEnabled = false;
        ApplyFog(1);

        if (includeHand)
        {
            RenderFirstPersonHandIfNeeded(tickDelta);
        }
    }

    /// <summary>
    ///     Draws the first-person held item/hand over whatever the world pass already left in the
    ///     depth buffer, unless the camera is zoomed (which hides it entirely).
    /// </summary>
    /// <remarks>
    ///     The hand is drawn over the world rather than into it, so it wants a fresh depth buffer —
    ///     otherwise it is depth-tested against nearby geometry and gets clipped by it. Under GL
    ///     that is a mid-pass depth clear, done here. WebGPU has no such call, so
    ///     <see cref="Core.WebGPU.WebGpuGameRenderer" /> instead skips this from
    ///     <see cref="DrawWorld" /> via <c>includeHand: false</c> and calls it again itself inside a
    ///     second pass opened with a cleared depth attachment.
    /// </remarks>
    public void RenderFirstPersonHandIfNeeded(float tickDelta)
    {
        if (CameraController.IsZoomActive) return;

        RenderFirstPersonHand(tickDelta);
    }

    private void RenderChunkBorders(float tickDelta)
    {
        var camera = _client.Camera;
        var camX = camera.LastTickX + (camera.X - camera.LastTickX) * tickDelta;
        var camY = camera.LastTickY + (camera.Y - camera.LastTickY) * tickDelta;
        var camZ = camera.LastTickZ + (camera.Z - camera.LastTickZ) * tickDelta;

        var playerChunkX = _client.Player.ChunkX;
        var playerChunkZ = _client.Player.ChunkZ;

        RenderSystem.ModelView.Push();
        RenderSystem.ModelView.Translate((float)-camX, (float)-camY, (float)-camZ);

        RenderSystem.TextureEnabled = false;
        RenderSystem.LightingEnabled = false;
        RenderSystem.FogEnabled = false;

        // What the world pass already left set — this only ever asserted half of it, and the half
        // it left out is what decided whether the lines were blended.
        RenderSystem.State.Apply(RenderState.Opaque);

        var minX = playerChunkX * 16.0;
        var maxX = (playerChunkX + 1) * 16.0;
        var minZ = playerChunkZ * 16.0;
        var maxZ = (playerChunkZ + 1) * 16.0;

        var tess = Tessellator.instance;
        tess.startDrawing(1);

        tess.setColorRGBA_F(1.0F, 1.0F, 0.0F, 1.0F);

        for (var i = 0; i <= 16; i += 4)
        {
            var x = minX + i;
            var z = minZ + i;

            tess.addVertex(x, 0.0, minZ);
            tess.addVertex(x, 128.0, minZ);

            tess.addVertex(x, 0.0, maxZ);
            tess.addVertex(x, 128.0, maxZ);

            tess.addVertex(minX, 0.0, z);
            tess.addVertex(minX, 128.0, z);

            tess.addVertex(maxX, 0.0, z);
            tess.addVertex(maxX, 128.0, z);
        }

        for (var y = 0; y <= 128; y += 4)
        {
            if (y % 16 == 0) tess.setColorRGBA_F(0.0F, 0.0F, 1.0F, 1.0F);
            tess.addVertex(minX, y, minZ);
            tess.addVertex(minX, y, maxZ);

            tess.addVertex(maxX, y, minZ);
            tess.addVertex(maxX, y, maxZ);

            tess.addVertex(minX, y, minZ);
            tess.addVertex(maxX, y, minZ);

            tess.addVertex(minX, y, maxZ);
            tess.addVertex(maxX, y, maxZ);
            if (y % 16 == 0) tess.setColorRGBA_F(1.0F, 1.0F, 0.0F, 1.0F);
        }

        minX = (playerChunkX - 1) * 16.0;
        maxX = (playerChunkX + 2) * 16.0;
        minZ = (playerChunkZ - 1) * 16.0;
        maxZ = (playerChunkZ + 2) * 16.0;

        tess.setColorRGBA_F(1.0F, 0.0F, 0.0F, 1.0F);

        for (var i = 0; i < 4; i++)
        {
            var x = minX + i * 16;
            var z = minZ + i * 16;

            tess.addVertex(x, 0.0, minZ);
            tess.addVertex(x, 128.0, minZ);

            tess.addVertex(x, 0.0, maxZ);
            tess.addVertex(x, 128.0, maxZ);

            tess.addVertex(minX, 0.0, z);
            tess.addVertex(minX, 128.0, z);

            tess.addVertex(maxX, 0.0, z);
            tess.addVertex(maxX, 128.0, z);
        }

        tess.draw(ProgramSlot.Line);
        RenderSystem.ModelView.Pop();
        RenderSystem.TextureEnabled = true;
    }

    private void RenderRain()
    {
        var rainGradient = _client.World.Environment.GetRainGradient(1.0F);

        if (rainGradient != 0.0F)
        {
            _random.SetSeed(_ticks * 312987231L);
            var camera = _client.Camera;
            var world = _client.World;
            var cameraBlockX = MathHelper.Floor(camera.X);
            var cameraBlockY = MathHelper.Floor(camera.Y);
            var cameraBlockZ = MathHelper.Floor(camera.Z);
            var presentationPolicy = WorldPresentationPolicy.From(_client.Options);
            var searchRadius = presentationPolicy.WeatherRadius;
            var rainSoundX = 0.0D;
            var rainSoundY = 0.0D;
            var rainSoundZ = 0.0D;
            var validDropCount = 0;

            var weatherSamples = (int)(100.0F * rainGradient * rainGradient *
                                       presentationPolicy.WeatherSpawnDensity);
            for (var sampleIndex = 0; sampleIndex < weatherSamples; ++sampleIndex)
            {
                var sampleX = cameraBlockX + _random.NextInt(searchRadius) - _random.NextInt(searchRadius);
                var sampleZ = cameraBlockZ + _random.NextInt(searchRadius) - _random.NextInt(searchRadius);
                var topSolidY = world.Reader.GetTopSolidBlockY(sampleX, sampleZ);
                var blockBelowId = world.Reader.GetBlockId(sampleX, topSolidY - 1, sampleZ);
                if (topSolidY <= cameraBlockY + searchRadius && topSolidY >= cameraBlockY - searchRadius && world.GetBiomeSource().GetBiome(sampleX, sampleZ).CanSpawnLightningBolt())
                {
                    var xOffset = _random.NextFloat();
                    var zOffset = _random.NextFloat();
                    if (blockBelowId > 0)
                    {
                        if (_client.Content.Blocks.GetByProtocolId(blockBelowId).Material == Material.Lava)
                        {
                            _client.ParticleManager.AddSmoke(sampleX + xOffset, topSolidY + 0.1F - _client.Content.Blocks.GetByProtocolId(blockBelowId).BoundingBox.MinY, sampleZ + zOffset, 0.0, 0.0, 0.0);
                        }
                        else
                        {
                            ++validDropCount;
                            if (_random.NextInt(validDropCount) == 0)
                            {
                                rainSoundX = sampleX + xOffset;
                                rainSoundY = topSolidY + 0.1 - _client.Content.Blocks.GetByProtocolId(blockBelowId).BoundingBox.MinY;
                                rainSoundZ = sampleZ + zOffset;
                            }

                            _client.ParticleManager.AddRain(sampleX + xOffset, topSolidY + 0.1F - _client.Content.Blocks.GetByProtocolId(blockBelowId).BoundingBox.MinY, sampleZ + zOffset);
                        }
                    }
                }
            }

            if (validDropCount > 0 && _random.NextInt(3) < _rainSoundCounter++)
            {
                _rainSoundCounter = 0;
                if (rainSoundY > camera.Y + 1.0D && world.Reader.GetTopSolidBlockY(MathHelper.Floor(camera.X), MathHelper.Floor(camera.Z)) > MathHelper.Floor(camera.Y))
                {
                    _client.World.Broadcaster.PlaySoundAtPos(rainSoundX, rainSoundY, rainSoundZ, "ambient.weather.rain", 0.1F, 0.5F);
                }
                else
                {
                    _client.World.Broadcaster.PlaySoundAtPos(rainSoundX, rainSoundY, rainSoundZ, "ambient.weather.rain", 0.2F, 1.0F);
                }
            }
        }
    }

    protected void RenderSnow(float tickDelta)
    {
        var rainGradient = _client.World.Environment.GetRainGradient(tickDelta);
        if (rainGradient > 0.0F)
        {
            var camera = _client.Camera;
            var world = _client.World;
            var cameraBlockX = MathHelper.Floor(camera.X);
            var cameraBlockY = MathHelper.Floor(camera.Y);
            var cameraBlockZ = MathHelper.Floor(camera.Z);
            var tessellator = Tessellator.instance;

            // Culling off because the rain and snow quads are camera-facing strips with no
            // meaningful back, and still depth writing, which is what they have always done.
            RenderSystem.State.Apply(RenderState.Entity with
            {
                Blend = BlendMode.Alpha
            });
            RenderSystem.Normal = new Vector3D<float>(0.0F, 1.0F, 0.0F);

            // Lower than the usual 0.1 so the faint tail of a raindrop is not cut off.
            RenderSystem.AlphaThreshold = 0.01F;
            _client.TextureManager.BindTexture(_client.TextureManager.GetTextureId("/environment/snow.png"));
            var renderX = camera.LastTickX + (camera.X - camera.LastTickX) * tickDelta;
            var renderY = camera.LastTickY + (camera.Y - camera.LastTickY) * tickDelta;
            var renderZ = camera.LastTickZ + (camera.Z - camera.LastTickZ) * tickDelta;
            var cameraYFloor = MathHelper.Floor(renderY);
            var renderRadius = WorldPresentationPolicy.From(_client.Options).WeatherRadius;

            var biomes = world.GetBiomeSource().GetBiomesInArea(cameraBlockX - renderRadius, cameraBlockZ - renderRadius, renderRadius * 2 + 1, renderRadius * 2 + 1);
            var biomeIndex = 0;

            int sampleX;
            int sampleZ;
            Biome biome;
            int topSolidY;
            int minY;
            int maxY;
            float textureScroll;
            for (sampleX = cameraBlockX - renderRadius; sampleX <= cameraBlockX + renderRadius; ++sampleX)
            {
                for (sampleZ = cameraBlockZ - renderRadius; sampleZ <= cameraBlockZ + renderRadius; ++sampleZ)
                {
                    biome = biomes[biomeIndex++];
                    if (biome.GetEnableSnow())
                    {
                        topSolidY = world.Reader.GetTopSolidBlockY(sampleX, sampleZ);
                        if (topSolidY < 0)
                        {
                            topSolidY = 0;
                        }

                        minY = topSolidY;
                        if (topSolidY < cameraYFloor)
                        {
                            minY = cameraYFloor;
                        }

                        maxY = cameraBlockY - renderRadius;
                        var maxRenderY = cameraBlockY + renderRadius;
                        if (maxY < topSolidY)
                        {
                            maxY = topSolidY;
                        }

                        if (maxRenderY < topSolidY)
                        {
                            maxRenderY = topSolidY;
                        }

                        textureScroll = 1.0F;
                        if (maxY != maxRenderY)
                        {
                            _random.SetSeed(sampleX * sampleX * 3121 + sampleX * 45238971 + sampleZ * sampleZ * 418711 + sampleZ * 13761);
                            var animationTime = _ticks + tickDelta;
                            var textureVOffset = ((_ticks & 511) + tickDelta) / 512.0F;
                            var textureUDrift = _random.NextFloat() + animationTime * 0.01F * (float)_random.NextGaussian();
                            var textureVDrift = _random.NextFloat() + animationTime * (float)_random.NextGaussian() * 0.001F;
                            var dx = sampleX + 0.5 - camera.X;
                            var dz = sampleZ + 0.5 - camera.Z;
                            var distanceFactor = MathHelper.Sqrt(dx * dx + dz * dz) / renderRadius;
                            tessellator.startDrawingQuads();
                            var brightness = world.GetLuminance(sampleX, minY, sampleZ);
                            RenderSystem.Color = new Vector4D<float>(brightness, brightness, brightness, ((1.0F - distanceFactor * distanceFactor) * 0.3F + 0.5F) * rainGradient);
                            tessellator.setTranslationD(-renderX * 1.0D, -renderY * 1.0D, -renderZ * 1.0D);
                            tessellator.addVertexWithUV(sampleX + 0, maxY, sampleZ + 0.5D, 0.0F * textureScroll + textureUDrift, maxY * textureScroll / 4.0F + textureVOffset * textureScroll + textureVDrift);
                            tessellator.addVertexWithUV(sampleX + 1, maxY, sampleZ + 0.5D, 1.0F * textureScroll + textureUDrift, maxY * textureScroll / 4.0F + textureVOffset * textureScroll + textureVDrift);
                            tessellator.addVertexWithUV(sampleX + 1, maxRenderY, sampleZ + 0.5D, 1.0F * textureScroll + textureUDrift, maxRenderY * textureScroll / 4.0F + textureVOffset * textureScroll + textureVDrift);
                            tessellator.addVertexWithUV(sampleX + 0, maxRenderY, sampleZ + 0.5D, 0.0F * textureScroll + textureUDrift, maxRenderY * textureScroll / 4.0F + textureVOffset * textureScroll + textureVDrift);
                            tessellator.addVertexWithUV(sampleX + 0.5D, maxY, sampleZ + 0, 0.0F * textureScroll + textureUDrift, maxY * textureScroll / 4.0F + textureVOffset * textureScroll + textureVDrift);
                            tessellator.addVertexWithUV(sampleX + 0.5D, maxY, sampleZ + 1, 1.0F * textureScroll + textureUDrift, maxY * textureScroll / 4.0F + textureVOffset * textureScroll + textureVDrift);
                            tessellator.addVertexWithUV(sampleX + 0.5D, maxRenderY, sampleZ + 1, 1.0F * textureScroll + textureUDrift, maxRenderY * textureScroll / 4.0F + textureVOffset * textureScroll + textureVDrift);
                            tessellator.addVertexWithUV(sampleX + 0.5D, maxRenderY, sampleZ + 0, 0.0F * textureScroll + textureUDrift, maxRenderY * textureScroll / 4.0F + textureVOffset * textureScroll + textureVDrift);
                            tessellator.setTranslationD(0.0D, 0.0D, 0.0D);
                            tessellator.draw(ProgramSlot.Weather);
                        }
                    }
                }
            }

            _client.TextureManager.BindTexture(_client.TextureManager.GetTextureId("/environment/rain.png"));
            biomeIndex = 0;

            for (sampleX = cameraBlockX - renderRadius; sampleX <= cameraBlockX + renderRadius; ++sampleX)
            {
                for (sampleZ = cameraBlockZ - renderRadius; sampleZ <= cameraBlockZ + renderRadius; ++sampleZ)
                {
                    biome = biomes[biomeIndex++];
                    if (biome.CanSpawnLightningBolt())
                    {
                        topSolidY = world.Reader.GetTopSolidBlockY(sampleX, sampleZ);
                        minY = cameraBlockY - renderRadius;
                        maxY = cameraBlockY + renderRadius;
                        if (minY < topSolidY)
                        {
                            minY = topSolidY;
                        }

                        if (maxY < topSolidY)
                        {
                            maxY = topSolidY;
                        }

                        var rainUvScale = 1.0F;
                        if (minY != maxY)
                        {
                            _random.SetSeed(sampleX * sampleX * 3121 + sampleX * 45238971 + sampleZ * sampleZ * 418711 + sampleZ * 13761);
                            textureScroll = (((_ticks + sampleX * sampleX * 3121 + sampleX * 45238971 + sampleZ * sampleZ * 418711 + sampleZ * 13761) & 31) + tickDelta) / 32.0F * (3.0F + _random.NextFloat());
                            var rainDx = sampleX + 0.5 - camera.X;
                            var rainDz = sampleZ + 0.5 - camera.Z;
                            var rainDistanceFactor = MathHelper.Sqrt(rainDx * rainDx + rainDz * rainDz) / renderRadius;
                            tessellator.startDrawingQuads();
                            var rainBrightness = world.GetLuminance(sampleX, 128, sampleZ) * 0.85F + 0.15F;
                            RenderSystem.Color = new Vector4D<float>(rainBrightness, rainBrightness, rainBrightness, ((1.0F - rainDistanceFactor * rainDistanceFactor) * 0.5F + 0.5F) * rainGradient);
                            tessellator.setTranslationD(-renderX * 1.0D, -renderY * 1.0D, -renderZ * 1.0D);
                            tessellator.addVertexWithUV(sampleX + 0, minY, sampleZ + 0.5D, 0, minY * rainUvScale / 4.0F + textureScroll * rainUvScale);
                            tessellator.addVertexWithUV(sampleX + 1, minY, sampleZ + 0.5D, rainUvScale, minY * rainUvScale / 4.0F + textureScroll * rainUvScale);
                            tessellator.addVertexWithUV(sampleX + 1, maxY, sampleZ + 0.5D, rainUvScale, maxY * rainUvScale / 4.0F + textureScroll * rainUvScale);
                            tessellator.addVertexWithUV(sampleX + 0, maxY, sampleZ + 0.5D, 0, maxY * rainUvScale / 4.0F + textureScroll * rainUvScale);
                            tessellator.addVertexWithUV(sampleX + 0.5D, minY, sampleZ + 0, 0, minY * rainUvScale / 4.0F + textureScroll * rainUvScale);
                            tessellator.addVertexWithUV(sampleX + 0.5D, minY, sampleZ + 1, rainUvScale, minY * rainUvScale / 4.0F + textureScroll * rainUvScale);
                            tessellator.addVertexWithUV(sampleX + 0.5D, maxY, sampleZ + 1, rainUvScale, maxY * rainUvScale / 4.0F + textureScroll * rainUvScale);
                            tessellator.addVertexWithUV(sampleX + 0.5D, maxY, sampleZ + 0, 0, maxY * rainUvScale / 4.0F + textureScroll * rainUvScale);
                            tessellator.setTranslationD(0.0D, 0.0D, 0.0D);
                            tessellator.draw(ProgramSlot.Weather);
                        }
                    }
                }
            }

            RenderSystem.State.Apply(RenderState.Opaque);
            RenderSystem.AlphaThreshold = 0.1F;
        }
    }

    /// <summary>
    ///     Draws the HUD and the current screen over whatever the frame already holds.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="OnFrameUpdate" /> because the world and the interface reach the
    ///     screen by different routes under WebGPU — the world into an offscreen target that is
    ///     blitted, the interface straight onto the swapchain in a pass of its own — so the WebGPU
    ///     renderer calls this itself. Everything it does goes through the draw-command seam; the
    ///     two direct GL calls left are the ones with no meaning off OpenGL, and they are skipped
    ///     rather than emulated.
    /// </remarks>
    public void RenderInterface(float tickDelta)
    {
        ScaledResolution scaledResolution = new(_client.Options, _client.DisplayWidth, _client.DisplayHeight);
        GetScaledMouse(scaledResolution, out var scaledMouseX, out var scaledMouseY);

        if (_client.World != null)
        {
            using (Profiler.Begin("RenderGameOverlay"))
            {
                if (!_client.Options.HideGUI || _client.CurrentScreen != null)
                {
                    SetupHudRender();
                    _client.HUD.Render(scaledMouseX, scaledMouseY, tickDelta);
                }
            }
        }
        else
        {
            RenderSystem.Projection.LoadIdentity();
            RenderSystem.ModelView.LoadIdentity();
            SetupHudRender();
        }

        if (_client.CurrentScreen != null)
        {
            // Nothing to clear here: the pass the interface is drawn in owns its depth
            // attachment and clears it when it begins.
            SetupHudRender();
            _client.CurrentScreen.Render(scaledMouseX, scaledMouseY, tickDelta);

            if (_client.IsControllerMode)
            {
                DrawVirtualCursor(scaledMouseX, scaledMouseY);
            }
        }
    }

    /// <summary>Where the pointer is in interface coordinates, past the F3 viewport's offset.</summary>
    private void GetScaledMouse(ScaledResolution resolution, out int x, out int y)
    {
        var scaledWidth = resolution.ScaledWidth;
        var scaledHeight = resolution.ScaledHeight;

        if (_client.IsControllerMode)
        {
            x = (int)(_client.VirtualCursor.X * scaledWidth / _client.DisplayWidth);
            y = (int)(_client.VirtualCursor.Y * scaledHeight / _client.DisplayHeight);
            return;
        }

        var vpOffsetX = (int)_client.DebugViewportOffset.X;
        var vpOffsetY = (int)_client.DebugViewportOffset.Y;

        x = (Mouse.getX() - vpOffsetX) * scaledWidth / _client.DisplayWidth;
        y = scaledHeight - (Mouse.getY() - vpOffsetY) * scaledHeight / _client.DisplayHeight - 1;
    }

    public void SetupHudRender()
    {
        ScaledResolution sr = new(_client.Options, _client.DisplayWidth, _client.DisplayHeight);

        // See RenderInterface: the pass clears its own depth.
        RenderSystem.Projection.LoadIdentity();
        RenderSystem.Projection.Ortho(0.0D, sr.ScaledWidthDouble, sr.ScaledHeightDouble, 0.0D, 1000.0D, 3000.0D);
        RenderSystem.ModelView.LoadIdentity();
        RenderSystem.ModelView.Translate(0.0F, 0.0F, -2000.0F);
    }

    public void DrawVirtualCursor(int x, int y)
    {
        if (_client.IsControllerMode && _client.CurrentScreen?.IsEditingSlider != true)
        {
            RenderSystem.LightingEnabled = false;

            // Drawn over the screen the pointer is pointing at, so it takes no part in the depth
            // buffer at all. Same state as everything else in the interface.
            RenderSystem.State.Apply(RenderState.Interface);
            RenderSystem.Color = new Vector4D<float>(1.0f, 1.0f, 1.0f, 1.0f);

            var textureId = _client.TextureManager.GetTextureId("/gui/Pointer.png");
            _client.TextureManager.BindTexture(textureId);

            const int width = 32;
            const int height = 32;

            x -= width / 2;
            y -= height / 2;

            const float zLevel = 10.0f;
            var tess = Tessellator.instance;
            tess.startDrawingQuads();
            tess.addVertexWithUV(x, y + height, zLevel, 0.0, 1.0);
            tess.addVertexWithUV(x + width, y + height, zLevel, 1.0, 1.0);
            tess.addVertexWithUV(x + width, y, zLevel, 1.0, 0.0);
            tess.addVertexWithUV(x, y, zLevel, 0.0, 0.0);
            tess.draw(ProgramSlot.Textured);

            // Leaving the interface state named here is still better than leaving half of it
            // toggled back for whatever draws next.
            RenderSystem.State.Apply(RenderState.Interface);
        }
    }

    private void UpdateSkyAndFogColors(float tickDelta)
    {
        var world = _client.World;
        var camera = _client.Camera;
        var fogBlend = 4.0F / _client.Options.RenderDistance;
        fogBlend = Math.Clamp(fogBlend, 0.25f, 1.0f);
        fogBlend = 1.0F - (float)Math.Pow(fogBlend, 0.25D);
        var skyColor = world.Environment.GetSkyColor(_client.Camera, tickDelta);
        var skyRed = (float)skyColor.X;
        var skyGreen = (float)skyColor.Y;
        var skyBlue = (float)skyColor.Z;
        var fogColor = world.GetFogColor(tickDelta);
        _fogColorRed = (float)fogColor.X;
        _fogColorGreen = (float)fogColor.Y;
        _fogColorBlue = (float)fogColor.Z;
        _fogColorRed += (skyRed - _fogColorRed) * fogBlend;
        _fogColorGreen += (skyGreen - _fogColorGreen) * fogBlend;
        _fogColorBlue += (skyBlue - _fogColorBlue) * fogBlend;
        var rainGradient = world.Environment.GetRainGradient(tickDelta);
        float rainDarken;
        float fogBrightness;
        if (rainGradient > 0.0F)
        {
            rainDarken = 1.0F - rainGradient * 0.5F;
            fogBrightness = 1.0F - rainGradient * 0.4F;
            _fogColorRed *= rainDarken;
            _fogColorGreen *= rainDarken;
            _fogColorBlue *= fogBrightness;
        }

        rainDarken = world.Environment.GetThunderGradient(tickDelta);
        if (rainDarken > 0.0F)
        {
            fogBrightness = 1.0F - rainDarken * 0.5F;
            _fogColorRed *= fogBrightness;
            _fogColorGreen *= fogBrightness;
            _fogColorBlue *= fogBrightness;
        }

        if (_cloudFog)
        {
            var cloudColor = world.Environment.GetCloudColor(tickDelta);
            _fogColorRed = (float)cloudColor.X;
            _fogColorGreen = (float)cloudColor.Y;
            _fogColorBlue = (float)cloudColor.Z;
        }
        else if (camera.IsInFluid(Material.Water))
        {
            _fogColorRed = 0.02F;
            _fogColorGreen = 0.02F;
            _fogColorBlue = 0.2F;
        }
        else if (camera.IsInFluid(Material.Lava))
        {
            _fogColorRed = 0.6F;
            _fogColorGreen = 0.1F;
            _fogColorBlue = 0.0F;
        }

        fogBrightness = CameraController.LastViewBob + (CameraController.ViewBob - CameraController.LastViewBob) * tickDelta;
        _fogColorRed *= fogBrightness;
        _fogColorGreen *= fogBrightness;
        _fogColorBlue *= fogBrightness;

        // The clear colour is carried on the pass descriptor instead; see WorldClearColor.
    }

    private void ApplyFog(int mode)
    {
        var camera = _client.Camera;
        Vector4D<float> color = new(_fogColorRed, _fogColorGreen, _fogColorBlue, 1.0f);
        RenderSystem.Normal = new Vector3D<float>(0.0F, -1.0F, 0.0F);
        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);

        if (_cloudFog || camera.IsInFluid(Material.Water))
        {
            RenderSystem.Fog = RenderSystem.Fog with
            {
                Color = color,
                Curve = FogCurve.Exponential,
                Density = 0.1f
            };
        }
        else if (camera.IsInFluid(Material.Lava))
        {
            RenderSystem.Fog = RenderSystem.Fog with
            {
                Color = color,
                Curve = FogCurve.Exponential,
                Density = 2.0f
            };
        }
        else
        {
            var start = _viewDistance * 0.25F;
            var end = _viewDistance;

            if (mode < 0)
            {
                start = 0.0F;
                end = _viewDistance * 0.8F;
            }

            if (_client.World.Dimension.IsNether)
            {
                start = 0.0F;
            }

            RenderSystem.Fog = RenderSystem.Fog with
            {
                Color = color,
                Curve = FogCurve.Linear,
                Start = start,
                End = end
            };
        }
    }
}
