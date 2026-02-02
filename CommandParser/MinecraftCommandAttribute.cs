namespace betareborn.CommandParser {
    // Assign this attribute to methods to add it as a command
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public class MinecraftCommandAttribute : Attribute {
        public string Name { get; }
        public string[] Aliases { get; }

        public MinecraftCommandAttribute(string name, params string[] aliases) {
            Name = name.ToLower();
            Aliases = aliases.Select(a => a.ToLower()).ToArray();
        }
    }

    public class CommandContext {
        public Minecraft Game { get; }
        public string RawCommand { get; }

        public CommandContext(Minecraft game, string rawCommand) {
            Game = game;
            RawCommand = rawCommand;
        }

        public void Reply(string message) => Console.WriteLine(message);
    }
}