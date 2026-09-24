# Agent instructions

## Luau editor definitions

When changing a C# API exposed to Luau scripts, update the matching declarations in `Scripting/omni.d.luau` in the same change.

- For `LuauClientStateHost` state keys or getter types, run `dotnet run --project Scripting/GenerateDefinitions` from the repository root. The `OmniClientState` section is generated; edit its C# source, then regenerate it.
- For other host APIs and `OmniBlock.Client/Options/GameOptions.cs`, update the corresponding handwritten declarations in `Scripting/omni.d.luau`.
- Before finishing a Luau API change, run `dotnet run --project Scripting/GenerateDefinitions -- --check`. CI runs this check too.

See `Scripting/README.md` for the editor setup and declaration details.
