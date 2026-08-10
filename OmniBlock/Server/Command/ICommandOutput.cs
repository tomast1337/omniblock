namespace OmniBlock.Server.Command;

public interface ICommandOutput
{
    string Name { get; }
    byte PermissionLevel { get; }
    void SendMessage(string message);
}
