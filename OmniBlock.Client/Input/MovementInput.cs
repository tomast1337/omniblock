using OmniBlock.Entities;

namespace OmniBlock.Client.Input;

public class MovementInput
{
    public bool field_1177_c = false;
    public bool jump = false;
    public float moveForward = 0.0F;
    public float moveStrafe = 0.0F;
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
