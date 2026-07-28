using BetaSharp.Entities.State;
using BetaSharp.Items;
using BetaSharp.NBT;
using BetaSharp.Util;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityCreeper : EntityMonster
{
    private readonly SyncedProperty<byte> _creeperState;
    private int _lastActiveTime;
    private int _timeSinceIgnited;

    public EntityCreeper(IWorldContext world) : base(world, EntityRegistry.ByName("creeper"))
    {
        _creeperState = DataSynchronizer.Get<byte>(SyncedPropertyFactory.Resolve<byte>(Definition, "state").Id);
        Powered = DataSynchronizer.Get<bool>(SyncedPropertyFactory.Resolve<bool>(Definition, "powered").Id);
    }

    /// <summary>Declared in creeper.json, including its NBT key — no persistence override needed.</summary>
    public SyncedProperty<bool> Powered { get; }



    protected override void attackBlockedEntity(Entity entity, float distance)
    {
        if (World.IsRemote) return;
        if (_timeSinceIgnited <= 0) return;

        _creeperState.Value = 255;
        --_timeSinceIgnited;
        if (_timeSinceIgnited < 0)
        {
            _timeSinceIgnited = 0;
        }
    }

    public override void Tick()
    {
        _lastActiveTime = _timeSinceIgnited;
        if (World.IsRemote)
        {
            int state = (sbyte)_creeperState.Value;
            if (state > 0 && _timeSinceIgnited == 0)
            {
                World.Broadcaster.PlaySoundAtEntity(this, "random.fuse", 1.0F, 0.5F);
            }

            _timeSinceIgnited += state;
            if (_timeSinceIgnited < 0)
            {
                _timeSinceIgnited = 0;
            }

            if (_timeSinceIgnited >= 30)
            {
                _timeSinceIgnited = 30;
            }
        }

        base.Tick();
        if (World.IsRemote || Target != null || _timeSinceIgnited <= 0) return;

        _creeperState.Value = 255;
        --_timeSinceIgnited;
        if (_timeSinceIgnited < 0)
        {
            _timeSinceIgnited = 0;
        }
    }

    protected override void attackEntity(Entity entity, float distance)
    {
        if (World.IsRemote) return;

        int state = (sbyte)_creeperState.Value;
        if ((state <= 0 && distance < 3.0F) || (state > 0 && distance < 7.0F))
        {
            if (_timeSinceIgnited == 0)
            {
                World.Broadcaster.PlaySoundAtEntity(this, "random.fuse", 1.0F, 0.5F);
            }

            _creeperState.Value = 1;
            ++_timeSinceIgnited;
            if (_timeSinceIgnited >= 30)
            {
                World.CreateExplosion(this, X, Y, Z, Powered.Value ? 6.0F : 3.0F);
                MarkDead();
            }

            HasAttacked = true;
        }
        else
        {
            _creeperState.Value = 255;
            --_timeSinceIgnited;
            if (_timeSinceIgnited < 0)
            {
                _timeSinceIgnited = 0;
            }
        }
    }

    public float GetCreeperFlashTime(float partialTick) => (_lastActiveTime + (_timeSinceIgnited - _lastActiveTime) * partialTick) / 28.0F;

    public override void OnStruckByLightning(EntityLightningBolt bolt)
    {
        base.OnStruckByLightning(bolt);
        Powered.Value = true;
    }
}
