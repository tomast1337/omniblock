namespace betareborn.Server.Commands
{
    public interface CommandOutput
    {
        void sendMessage(string message);

        string getName();
    }

}
