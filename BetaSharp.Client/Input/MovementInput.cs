using BetaSharp.Entities;

namespace BetaSharp.Client.Input;

public class MovementInput
{
    public float moveStrafe = 0.0F;
    public float moveForward = 0.0F;
    public bool field_1177_c = false;
    public bool jump = false;
    public bool sneak = false;

    public virtual void updatePlayerMoveState(EntityPlayer player)
    {
    }

    public virtual void resetKeyState()
    {
    }

    public virtual void checkKeyForMovementInput(int keyCode, bool pressed)
    {
    }
}
