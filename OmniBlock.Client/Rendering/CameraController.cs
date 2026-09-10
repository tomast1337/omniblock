using OmniBlock.Blocks;
using OmniBlock.Blocks.Materials;
using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Entities;
using OmniBlock.Util.Hit;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering;

public class CameraController
{
    private readonly float _cameraRoll = 0.0F;
    private readonly float _cameraRollAmount = 0.0F;
    private readonly float _frontThirdPersonDistance = 4.0F;
    private readonly OmniBlock _game;
    private readonly float _thirdPersonDistance = 4.0F;
    private readonly float _thirdPersonPitch = 0.0F;
    private readonly float _thirdPersonYaw = 0.0F;
    private float _prevCameraRoll;
    private float _prevCameraRollAmount;
    private float _prevFrontThirdPersonDistance = 4.0F;

    private float _prevThirdPersonDistance = 4.0F;
    private float _prevThirdPersonPitch;
    private float _prevThirdPersonYaw;
    private float _zoomScale = 2.0F;

    public CameraController(OmniBlock game) => _game = game;
    public float ViewBob { get; private set; }
    public float LastViewBob { get; private set; }
    public double CameraZoom { get; private set; } = 1.0D;
    public double CameraYaw { get; } = 0.0D;
    public double CameraPitch { get; } = 0.0D;
    public bool IsZoomActive { get; private set; }

    public void UpdateCamera()
    {
        LastViewBob = ViewBob;
        _prevThirdPersonDistance = _thirdPersonDistance;
        _prevFrontThirdPersonDistance = _frontThirdPersonDistance;
        _prevThirdPersonYaw = _thirdPersonYaw;
        _prevThirdPersonPitch = _thirdPersonPitch;
        _prevCameraRoll = _cameraRoll;
        _prevCameraRollAmount = _cameraRollAmount;

        var luminance = _game.World.GetLuminance(MathHelper.Floor(_game.Camera.X), MathHelper.Floor(_game.Camera.Y), MathHelper.Floor(_game.Camera.Z));
        var renderDistFactor = Math.Clamp((_game.Options.RenderDistance - 4.0F) / 28.0F, 0.0F, 1.0F);
        var targetBob = luminance * (1.0F - renderDistFactor) + renderDistFactor;
        ViewBob += (targetBob - ViewBob) * 0.1F;
    }

    public void SetZoomState(bool isHeld, float zoomScale)
    {
        IsZoomActive = isHeld;
        _zoomScale = Math.Clamp(zoomScale, 1.25F, 20.0F);
        CameraZoom = 1.0D;
    }

    public float GetFov(float tickDelta, bool isHand = false)
    {
        var cameraEntity = _game.Camera;
        var fov = isHand ? 70.0F : 30.0F + _game.Options.Fov * 90.0F;

        if (cameraEntity.IsInFluid(Material.Water))
        {
            fov -= 10.0F;
        }

        if (cameraEntity.Health <= 0)
        {
            var deathTimeF = cameraEntity.DeathTime + tickDelta;
            fov /= (1.0F - 500.0F / (deathTimeF + 500.0F)) * 2.0F + 1.0F;
        }

        if (IsZoomActive && !isHand)
        {
            var zoomProgress = 1.0F / _zoomScale;
            var easedZoomProgress = (float)Math.Pow(zoomProgress, 2.0D);
            fov = 1.0F + (fov - 1.0F) * easedZoomProgress;
        }

        return fov + _prevCameraRoll + (_cameraRoll - _prevCameraRoll) * tickDelta;
    }

    public void ApplyDamageTiltEffect(float tickDelta)
    {
        var cameraEntity = _game.Camera;
        var hurtTimeF = cameraEntity.HurtTime - tickDelta;

        if (cameraEntity.Health <= 0)
        {
            var deathTimeF = cameraEntity.DeathTime + tickDelta;
            RenderSystem.ModelView.Rotate(40.0F - 8000.0F / (deathTimeF + 200.0F), 0.0F, 0.0F, 1.0F);
        }

        if (hurtTimeF >= 0.0F)
        {
            hurtTimeF /= cameraEntity.MaxHurtTime;
            hurtTimeF = MathHelper.Sin(hurtTimeF * hurtTimeF * hurtTimeF * hurtTimeF * (float)Math.PI);
            var attackedYaw = cameraEntity.AttackedAtYaw;
            RenderSystem.ModelView.Rotate(-attackedYaw, 0.0F, 1.0F, 0.0F);
            RenderSystem.ModelView.Rotate(-hurtTimeF * 14.0F, 0.0F, 0.0F, 1.0F);
            RenderSystem.ModelView.Rotate(attackedYaw, 0.0F, 1.0F, 0.0F);
        }
    }

    public void ApplyViewBobbing(float tickDelta)
    {
        if (_game.Camera is EntityPlayer player)
        {
            var speedDelta = player.HorizontalSpeed - player.PrevHorizontalSpeed;
            var speed = -(player.HorizontalSpeed + speedDelta * tickDelta);
            var bobAmount = player.PrevStepBobbingAmount + (player.StepBobbingAmount - player.PrevStepBobbingAmount) * tickDelta;
            var pitch = player.CameraPitch + (player.Tilt - player.CameraPitch) * tickDelta;

            RenderSystem.ModelView.Translate(MathHelper.Sin(speed * (float)Math.PI) * bobAmount * 0.5F, -Math.Abs(MathHelper.Cos(speed * (float)Math.PI) * bobAmount), 0.0F);
            RenderSystem.ModelView.Rotate(MathHelper.Sin(speed * (float)Math.PI) * bobAmount * 3.0F, 0.0F, 0.0F, 1.0F);
            RenderSystem.ModelView.Rotate(Math.Abs(MathHelper.Cos(speed * (float)Math.PI - 0.2F) * bobAmount) * 5.0F, 1.0F, 0.0F, 0.0F);
            RenderSystem.ModelView.Rotate(pitch, 1.0F, 0.0F, 0.0F);
        }
    }

    public void ApplyCameraTransform(float tickDelta)
    {
        var cameraEntity = _game.Camera;
        var eyeHeightOffset = cameraEntity.StandingEyeHeight - 1.62F;
        var x = cameraEntity.PrevX + (cameraEntity.X - cameraEntity.PrevX) * tickDelta;
        var y = cameraEntity.PrevY + (cameraEntity.Y - cameraEntity.PrevY) * tickDelta - eyeHeightOffset;
        var z = cameraEntity.PrevZ + (cameraEntity.Z - cameraEntity.PrevZ) * tickDelta;

        RenderSystem.ModelView.Rotate(_prevCameraRollAmount + (_cameraRollAmount - _prevCameraRollAmount) * tickDelta, 0.0F, 0.0F, 1.0F);

        if (cameraEntity.IsSleeping)
        {
            eyeHeightOffset = (float)(eyeHeightOffset + 1.0D);
            RenderSystem.ModelView.Translate(0.0F, 0.3F, 0.0F);
            if (!_game.Options.DebugCamera)
            {
                var blockId = _game.World.Reader.GetBlockId(MathHelper.Floor(cameraEntity.X), MathHelper.Floor(cameraEntity.Y), MathHelper.Floor(cameraEntity.Z));
                if (blockId == _game.Content.Blocks.Get("bed").Id)
                {
                    var meta = _game.World.Reader.GetBlockMeta(MathHelper.Floor(cameraEntity.X), MathHelper.Floor(cameraEntity.Y), MathHelper.Floor(cameraEntity.Z));
                    var rotation = meta & 3;
                    RenderSystem.ModelView.Rotate(rotation * 90, 0.0F, 1.0F, 0.0F);
                }

                RenderSystem.ModelView.Rotate(cameraEntity.PrevYaw + (cameraEntity.Yaw - cameraEntity.PrevYaw) * tickDelta + 180.0F, 0.0F, -1.0F, 0.0F);
                RenderSystem.ModelView.Rotate(cameraEntity.PrevPitch + (cameraEntity.Pitch - cameraEntity.PrevPitch) * tickDelta, -1.0F, 0.0F, 0.0F);
            }
        }
        else if (_game.Options.CameraMode == CameraMode.ThirdPerson || _game.Options.CameraMode == CameraMode.FrontThirdPerson)
        {
            double currentDistance;
            if (_game.Options.CameraMode == CameraMode.FrontThirdPerson)
            {
                currentDistance = _prevFrontThirdPersonDistance + (_frontThirdPersonDistance - _prevFrontThirdPersonDistance) * tickDelta;
            }
            else
            {
                currentDistance = _prevThirdPersonDistance + (_thirdPersonDistance - _prevThirdPersonDistance) * tickDelta;
            }

            float targetPitch;
            float targetYaw;

            if (_game.Options.DebugCamera)
            {
                targetYaw = _prevThirdPersonYaw + (_thirdPersonYaw - _prevThirdPersonYaw) * tickDelta;
                targetPitch = _prevThirdPersonPitch + (_thirdPersonPitch - _prevThirdPersonPitch) * tickDelta;
                RenderSystem.ModelView.Translate(0.0F, 0.0F, (float)-currentDistance);
                RenderSystem.ModelView.Rotate(targetPitch, 1.0F, 0.0F, 0.0F);
                RenderSystem.ModelView.Rotate(targetYaw, 0.0F, 1.0F, 0.0F);
            }
            else
            {
                targetYaw = cameraEntity.Yaw;
                targetPitch = cameraEntity.Pitch;

                var vecX = -MathHelper.Sin(targetYaw / 180.0F * (float)Math.PI) * MathHelper.Cos(targetPitch / 180.0F * (float)Math.PI) * currentDistance;
                var vecZ = MathHelper.Cos(targetYaw / 180.0F * (float)Math.PI) * MathHelper.Cos(targetPitch / 180.0F * (float)Math.PI) * currentDistance;
                var vecY = -MathHelper.Sin(targetPitch / 180.0F * (float)Math.PI) * currentDistance;

                for (var i = 0; i < 8; ++i)
                {
                    var offsetX = ((i & 1) * 2 - 1) * 0.1F;
                    var offsetY = (((i >> 1) & 1) * 2 - 1) * 0.1F;
                    var offsetZ = (((i >> 2) & 1) * 2 - 1) * 0.1F;

                    var hit = new HitResult(HitResultType.Miss);

                    if (_game.Options.CameraMode == CameraMode.FrontThirdPerson)
                    {
                        hit = _game.World.Reader.Raycast(
                            new Vec3D(x + offsetX, y + offsetY, z + offsetZ),
                            new Vec3D(x + vecX + offsetX + offsetZ, y + vecY + offsetY, z + vecZ + offsetZ)
                        );
                    }
                    else
                    {
                        hit = _game.World.Reader.Raycast(
                            new Vec3D(x + offsetX, y + offsetY, z + offsetZ),
                            new Vec3D(x - vecX + offsetX + offsetZ, y - vecY + offsetY, z - vecZ + offsetZ)
                        );
                    }

                    if (hit.Type != HitResultType.Miss)
                    {
                        var dist = hit.Pos.DistanceTo(new Vec3D(x, y, z));
                        if (dist < currentDistance)
                        {
                            currentDistance = dist;
                        }
                    }
                }

                RenderSystem.ModelView.Rotate(cameraEntity.Pitch - targetPitch, 1.0F, 0.0F, 0.0F);
                RenderSystem.ModelView.Rotate(cameraEntity.Yaw - targetYaw, 0.0F, 1.0F, 0.0F);
                RenderSystem.ModelView.Translate(0.0F, 0.0F, (float)-currentDistance);
                if (_game.Options.CameraMode == CameraMode.FrontThirdPerson)
                {
                    RenderSystem.ModelView.Rotate(180.0F, 0.0F, 1.0F, 0.0F);
                }

                RenderSystem.ModelView.Rotate(targetYaw - cameraEntity.Yaw, 0.0F, 1.0F, 0.0F);
                RenderSystem.ModelView.Rotate(targetPitch - cameraEntity.Pitch, 1.0F, 0.0F, 0.0F);
            }
        }
        else
        {
            RenderSystem.ModelView.Translate(0.0F, 0.0F, -0.1F);
        }

        if (!_game.Options.DebugCamera)
        {
            RenderSystem.ModelView.Rotate(cameraEntity.PrevPitch + (cameraEntity.Pitch - cameraEntity.PrevPitch) * tickDelta, 1.0F, 0.0F, 0.0F);
            RenderSystem.ModelView.Rotate(cameraEntity.PrevYaw + (cameraEntity.Yaw - cameraEntity.PrevYaw) * tickDelta + 180.0F, 0.0F, 1.0F, 0.0F);
        }

        RenderSystem.ModelView.Translate(0.0F, eyeHeightOffset, 0.0F);
    }
}
