using System.Text;
using OmniBlock.Luau.Host;

const string begin = "-- BEGIN GENERATED OmniClientState";
const string end = "-- END GENERATED OmniClientState";

if (args.Length > 1 || args.Length == 1 && args[0] != "--check")
{
    Console.Error.WriteLine("Usage: dotnet run --project Scripting/GenerateDefinitions [-- --check]");
    return 2;
}

var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OmniBlock.Luau", "OmniBlock.Luau.csproj")))
    directory = directory.Parent;
if (directory == null)
{
    Console.Error.WriteLine("Could not find the OmniBlock repository root.");
    return 2;
}

var path = Path.Combine(directory.FullName, "Scripting", "omni.d.luau");
var source = File.ReadAllText(path);
var start = source.IndexOf(begin, StringComparison.Ordinal);
var stop = source.IndexOf(end, StringComparison.Ordinal);
if (start < 0 || stop <= start || source.IndexOf(begin, start + begin.Length, StringComparison.Ordinal) >= 0 ||
    source.IndexOf(end, stop + end.Length, StringComparison.Ordinal) >= 0)
{
    Console.Error.WriteLine("Expected exactly one OmniClientState generated section in omni.d.luau.");
    return 2;
}

var properties = LuauClientStateHost.GetStateDefinition();
var duplicate = properties.GroupBy(property => property.Name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
if (duplicate != null)
{
    Console.Error.WriteLine($"Duplicate client state property: {duplicate.Key}");
    return 2;
}

var generated = new StringBuilder();
generated.AppendLine(begin);
generated.AppendLine("export type OmniClientState = {");
generated.AppendLine("    -- Properties are read-only at runtime.");
foreach (var (name, type) in properties)
    generated.Append("    ").Append(name).Append(": ").Append(type).AppendLine(",");
generated.AppendLine("}");
generated.Append(end);

var updated = source[..start] + generated + source[(stop + end.Length)..];
if (updated == source)
{
    Console.WriteLine("omni.d.luau is up to date.");
    return 0;
}

if (args.Length == 1)
{
    Console.Error.WriteLine("omni.d.luau is out of date. Run: dotnet run --project Scripting/GenerateDefinitions");
    return 1;
}

File.WriteAllText(path, updated, new UTF8Encoding(false));
Console.WriteLine($"Updated {path}");
return 0;
