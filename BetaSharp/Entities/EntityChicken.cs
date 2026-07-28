using BetaSharp.Items;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityChicken : EntityAnimal
{
    private float _flapSpeed = 1.0F;
    public float DestPos;
    public float FlapProgress;
    public bool Jockey = false;
    public float PrevDestPos;
    public float PrevFlapProgress;

    public EntityChicken(IWorldContext world) : base(world, EntityRegistry.ByName("chicken"))
    {
    }

    protected override void TickMovement()
    {
        base.TickMovement();
        if (World.IsRemote)
        {
            OnGround = Math.Abs(Y - PrevY) < 0.02D;
        }

        PrevFlapProgress = FlapProgress;
        PrevDestPos = DestPos;
        DestPos = (float)(DestPos + (OnGround ? -1 : 4) * 0.3D);
        if (DestPos < 0.0F)
        {
            DestPos = 0.0F;
        }

        if (DestPos > 1.0F)
        {
            DestPos = 1.0F;
        }

        if (!OnGround && _flapSpeed < 1.0F)
        {
            _flapSpeed = 1.0F;
        }

        _flapSpeed = (float)(_flapSpeed * 0.9D);
        if (!OnGround && VelocityY < 0.0D)
        {
            VelocityY *= 0.6D;
        }

        FlapProgress += _flapSpeed * 2.0F;
    }

    protected override void OnLanding(float fallDistance)
    {
    }

}
