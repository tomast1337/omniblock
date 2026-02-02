using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using betareborn;
using betareborn.CommandParser;

public static class CommandService {
    private static readonly Dictionary<string, MethodInfo> _commands = new();

    static CommandService() {
        RegisterCommands();
    }

    private static void RegisterCommands() {
        var assembly = Assembly.GetExecutingAssembly();

        var methods = assembly.GetTypes()
            .SelectMany(t =>
                t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                             BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<MinecraftCommandAttribute>() != null);

        // Register all the commands
        foreach (var method in methods) {
            var attribute = method.GetCustomAttribute<MinecraftCommandAttribute>();

            RegisterCommand(attribute.Name, method);

            if (attribute.Aliases != null) {
                foreach (var alias in attribute.Aliases) {
                    RegisterCommand(alias, method);
                }
            }
        }
    }

    private static void RegisterCommand(string name, MethodInfo method) {
        if (_commands.TryAdd(name, method)) {
            Console.WriteLine($"Registered command: {name}");
        }
        else {
            Console.WriteLine($"Warning: Duplicate command '{name}' found in {method.DeclaringType.Name}");
        }
    }

    // Execute commands
    public static bool Execute(Minecraft minecraft, string input) {
        if (string.IsNullOrEmpty(input) || !input.StartsWith("/")) return false;

        var splits = input.Substring(1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (splits.Length == 0) return false;

        var commandName = splits[0].ToLower();
        var args = splits.Skip(1).ToArray();

        if (_commands.TryGetValue(commandName, out var method)) {
            try {
                var parameters = method.GetParameters();
                var invokeArgs = new object[parameters.Length];

                int argOffset = 0;

                // Create the new command context
                if (parameters.Length > 0 && parameters[0].ParameterType == typeof(CommandContext)) {
                    invokeArgs[0] = new CommandContext(minecraft, input);
                    argOffset = 1;
                }

                // Map the args
                for (int i = argOffset; i < parameters.Length; i++) {
                    var param = parameters[i];
                    var inputArgIndex = i - argOffset;

                    if (inputArgIndex < args.Length) {
                        invokeArgs[i] = ConvertArgument(args[inputArgIndex], param.ParameterType);
                    }
                    else if (param.HasDefaultValue) {
                        invokeArgs[i] = param.DefaultValue;
                    }
                    else {
                        Console.WriteLine($"Error: Missing required argument '{param.Name}'.");
                        return true;
                    }
                }

                // Create instance of the class and invoke the function. Might be good to just make this once per type..
                object instance = method.IsStatic ? null : Activator.CreateInstance(method.DeclaringType);
                method.Invoke(instance, invokeArgs);
                return true;
            }
            catch (TargetInvocationException ex) {
                Console.WriteLine($"Error executing command: {ex.InnerException?.Message ?? ex.Message}");
                return true;
            }
            catch (Exception ex) {
                Console.WriteLine($"System Error: {ex.Message}");
                return true;
            }
        }

        Console.WriteLine($"Unknown command: {commandName}");
        return false;
    }

    private static object ConvertArgument(string value, Type targetType) {
        if (targetType == typeof(string)) return value;
        if (targetType == typeof(int)) return int.Parse(value);
        if (targetType == typeof(long)) return long.Parse(value);
        if (targetType == typeof(float)) return float.Parse(value);
        if (targetType == typeof(bool)) return bool.Parse(value);

        // Tries to handle enums 
        if (targetType.IsEnum) return Enum.Parse(targetType, value, true);

        throw new ArgumentException($"Unsupported argument type: {targetType.Name}");
    }
}