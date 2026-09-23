using OmniBlock.Client.Entities;
using OmniBlock.Entities;
using OmniBlock.Network.Messages;
using OmniBlock.Stats;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Client.Network;

public class EntityClientPlayerMP : ClientPlayerEntity
{
    private bool hasReceivedInitialHealth;
    private int inventorySyncTickCounter;
    private bool lastOnGround;
    private double lastSentMinY;
    private double oldPosX;
    private double oldPosY;
    private double oldPosZ;
    private float oldRotationPitch;
    private float oldRotationYaw;
    public ClientNetworkHandler sendQueue;
    private bool wasSneaking;

    public EntityClientPlayerMP(OmniBlock game, World world, Session session, ClientNetworkHandler clientNetworkHandler) : base(game, world, session, 0) => sendQueue = clientNetworkHandler;

    public override bool Damage(Entity? ent, int amount) => false;

    public override void Heal(int amount)
    {
    }

    public override void Tick()
    {
        if (World.Reader.IsPosLoaded(MathHelper.Floor(X), 64, MathHelper.Floor(Z)))
        {
            base.Tick();
            func_4056_N();
        }
    }

    public void func_4056_N()
    {
        if (inventorySyncTickCounter++ == 20)
        {
            sendInventoryChanged();
            inventorySyncTickCounter = 0;
        }

        var isSneaking = base.IsSneaking();
        if (isSneaking != wasSneaking)
        {
            if (isSneaking)
            {
                sendQueue.SendMessage(new ClientCommandMessage
                {
                    EntityId = ID,
                    Mode = 1
                });
            }
            else
            {
                sendQueue.SendMessage(new ClientCommandMessage
                {
                    EntityId = ID,
                    Mode = 2
                });
            }

            wasSneaking = isSneaking;
        }

        var dx = X - oldPosX;
        var dMinY = BoundingBox.MinY - lastSentMinY;
        var dy = Y - oldPosY;
        var dz = Z - oldPosZ;
        var dYaw = (double)(Yaw - oldRotationYaw);
        var yPitch = (double)(Pitch - oldRotationPitch);
        var positionChanged = dMinY != 0.0D || dy != 0.0D || dx != 0.0D || dz != 0.0D;
        var rotationChanged = dYaw != 0.0D || yPitch != 0.0D;
        if (Vehicle != null)
        {
            if (rotationChanged)
            {
                sendQueue.SendMessage(new PlayerMovePositionMessage
                {
                    X = VelocityX,
                    Y = -1000.0D,
                    EyeHeight = -1000.0D,
                    Z = VelocityZ,
                    OnGround = OnGround
                });
            }
            else
            {
                sendQueue.SendMessage(new PlayerMoveFullMessage
                {
                    X = VelocityX,
                    Y = -1000.0D,
                    EyeHeight = -1000.0D,
                    Z = VelocityZ,
                    Yaw = Yaw,
                    Pitch = Pitch,
                    OnGround = OnGround
                });
            }

            positionChanged = false;
        }
        else if (positionChanged && rotationChanged)
        {
            sendQueue.SendMessage(new PlayerMoveFullMessage
            {
                X = X,
                Y = BoundingBox.MinY,
                EyeHeight = Y,
                Z = Z,
                Yaw = Yaw,
                Pitch = Pitch,
                OnGround = OnGround
            });
        }
        else if (positionChanged)
        {
            sendQueue.SendMessage(new PlayerMovePositionMessage
            {
                X = X,
                Y = BoundingBox.MinY,
                EyeHeight = Y,
                Z = Z,
                OnGround = OnGround
            });
        }
        else if (rotationChanged)
        {
            sendQueue.SendMessage(new PlayerMoveLookMessage
            {
                Yaw = Yaw,
                Pitch = Pitch,
                OnGround = OnGround
            });
        }
        else if (lastOnGround != OnGround)
        {
            sendQueue.SendMessage(new PlayerMoveMessage
            {
                OnGround = OnGround
            });
        }

        lastOnGround = OnGround;
        if (positionChanged)
        {
            oldPosX = X;
            lastSentMinY = BoundingBox.MinY;
            oldPosY = Y;
            oldPosZ = Z;
        }

        if (rotationChanged)
        {
            oldRotationYaw = Yaw;
            oldRotationPitch = Pitch;
        }
    }

    public override void DropSelectedItem()
    {
        if (!Game.Player.GameMode.CanDrop) return;

        var selected = GetHand();
        if (selected != null && selected.Count > 0)
        {
            IncreaseStat(Stats.Stats.DropStat, 1);
        }

        sendQueue.SendMessage(new PlayerActionMessage
        {
            Action = (byte)PlayerActionMessage.Actions.DropSelectedItem
        });
    }

    private void sendInventoryChanged()
    {
    }

    protected override void SpawnItem(Entity ent)
    {
    }

    public override void SendChatMessage(string message) => sendQueue.SendMessage(new ChatMessage
    {
        Text = message
    });

    public override void SwingHand()
    {
        base.SwingHand();
        sendQueue.SendMessage(new EntityAnimationMessage
        {
            EntityId = ID,
            AnimationId = (byte)EntityAnimationMessage.EntityAnimation.SwingHand
        });
    }

    public override void Respawn()
    {
        sendInventoryChanged();
        sendQueue.SendMessage(new PlayerRespawnMessage
        {
            DimensionId = (sbyte)DimensionId
        });
    }

    protected override void ApplyDamage(int amount) => Health -= amount;

    public override void CloseHandledScreen()
    {
        sendQueue.SendMessage(new CloseScreenMessage
        {
            SyncId = (sbyte)(CurrentScreenHandler?.SyncId ?? 0)
        });
        Inventory.SetCursorStack(null);
        base.CloseHandledScreen();
    }

    public override void setHealth(int amount)
    {
        if (hasReceivedInitialHealth)
        {
            base.setHealth(amount);
        }
        else
        {
            Health = amount;
            hasReceivedInitialHealth = true;
        }
    }

    public override void IncreaseStat(StatBase stat, int amount)
    {
        if (stat != null)
        {
            if (stat.LocalOnly)
            {
                base.IncreaseStat(stat, amount);
            }
        }
    }

    public void IncreaseRemoteStat(StatBase stat, int amount)
    {
        if (stat != null && !stat.LocalOnly)
        {
            base.IncreaseStat(stat, amount);
        }
    }
}
