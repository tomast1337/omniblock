# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**OmniBlock** is a hard fork of BetaSharp, which was itself a C# recreation of Minecraft Beta 1.7.3. It targets .NET 10 and is a multi-project solution with a shared core library, game client, dedicated server, and an Avalonia-based launcher.

OmniBlock is no longer trying to be a faithful reimplementation. It keeps Beta 1.7.3's *world* — the terrain you get from a seed, and the saves on disk — and rebuilds everything around it. Two things are the point of this fork:

1. **Scripting-based modding.** Content should be authored, not compiled in. The inherited data-driven layer (455 JSON definitions across blocks, items, entities, recipes, materials, sound groups, biome spawns, gamemodes, backed by `Registries/`) is the substrate; the goal is a scripting layer on top of it so mods are shipped as assets and scripts, not as forks of the engine.
2. **A better network protocol and system.** The inherited protocol is Beta 1.7.3's: a flat `PacketId : byte` enum, one byte of ID space, hand-rolled per-packet serialization. Replacing it — versioning, extensibility, a protocol that modded content can extend without stealing packet IDs — is in scope and expected to break wire compatibility with upstream.

> **Note**: The client and server expect the Minecraft JAR file (`b1.7.3.jar`) to be in their running directory.

## Hard Invariants

These two are non-negotiable and constrain otherwise-reasonable refactors. Check against them before changing anything under `Worlds/`.

### Save file compatibility

Worlds written by upstream BetaSharp / Minecraft Beta 1.7.3 must load, and worlds OmniBlock writes must stay readable by them. The surface is:

- `BetaSharp/NBT/` — Named Binary Tag serialization. The on-disk tag encoding is a wire format; treat it as frozen.
- `BetaSharp/Worlds/Storage/RegionFormat/` — `RegionFile`, `RegionIo`, `RegionChunkStorage`, `ChunkDataStream`. The region/chunk layout on disk is likewise frozen.
- `BetaSharp/Worlds/Storage/` — level metadata, player data, `PersistentState`.

New data may be added, but only as additive NBT tags that older readers can ignore. Never repurpose or reorder existing ones.

### 1:1 world generation parity

The same seed must produce byte-identical terrain to upstream. This is what makes `JavaRandom` load-bearing:

- **`BetaSharp/Util/Maths/JavaRandom.cs` must stay a bit-exact port of Java's 48-bit LCG.** It is used by ~65 files. It is the last Java-shaped thing in the codebase and it is deliberate — Beta 1.7.3's terrain is a specific sequence of draws from that exact generator. Swapping it for `System.Random`, changing draw order, or "optimizing" the call sequence in a generator silently changes every world.
- Generation lives in two places: `BetaSharp/Worlds/Gen/Chunks/` (the `Overworld`/`Nether`/`Sky`/`Common` chunk generators) and `BetaSharp/Worlds/Generation/` (`Biomes/`, `Generators/Features/`, `Generators/Carvers/`).
- Refactors there are fine as long as the *sequence and count* of `JavaRandom` draws is preserved exactly. If you cannot prove that, don't land it.

Note that the IKVM/Java-interop constraint from upstream BetaSharp is **gone** — there are no IKVM package references and no `java.*` types anywhere in the tree. `JavaRandom` is a plain C# class and is the sole intentional exception to "write idiomatic C#, not Java-style code".

## Build & Run Commands

The solution file is `BetaSharp.slnx` (the newer XML solution format — there is no `.sln`).

```bash
# Run the launcher (recommended entry point — handles auth and starts client)
cd BetaSharp.Launcher && dotnet run --configuration Release

# Build a specific project
cd BetaSharp.(Client|Server|Launcher) && dotnet build --configuration Release

# Build everything from root
dotnet build --configuration Release

# Run all tests (1164 passing, 25 skipped as of the fork)
dotnet test

# Run a specific test
dotnet test --filter "FullyQualifiedName=BetaSharp.Tests.UnitTest1.Test1"
```

## Formatting & Analysis

```bash
# Check formatting and analyzers (what CI runs)
dotnet format --verify-no-changes --verbosity minimal
dotnet format analyzers --verify-no-changes --verbosity minimal

# Auto-fix
dotnet format
dotnet format analyzers
```

The `.editorconfig` enforces: 4-space indentation, LF line endings for `.cs` files, UTF-8, no trailing whitespace, `s_` prefix for private static fields, and PascalCase for constants. Security diagnostics are treated as errors.

> **Pre-existing debt**: `dotnet format --verify-no-changes` currently fails on ~38 files, inherited from upstream (mostly `CHARSET` file-encoding and `IMPORTS` ordering). This is not something you broke. Don't bulk-fix it as a side effect of an unrelated change — it makes diffs unreviewable. Do keep files you touch clean.

## Naming

The fork is called OmniBlock, but **project directories, assembly names, and the root namespace are all still `BetaSharp`**. A rename is a deliberate future task, not something to do opportunistically — it would touch every file and destroy `git blame`. Until it happens, `BetaSharp` in code means "this project".

## Solution Structure

| Project | Type | Purpose |
|---------|------|---------|
| `BetaSharp/` | Library | Shared core: blocks, entities, items, worlds, network, server logic |
| `BetaSharp.Client/` | Executable | Game client with OpenGL rendering, UI, input, audio |
| `BetaSharp.Server/` | Executable | Standalone dedicated server |
| `BetaSharp.Launcher/` | WinExe (Avalonia) | Launcher with Microsoft account auth (MSAL), AOT compiled |
| `BetaSharp.Tests/` | Test (xUnit) | Unit tests |

## Architecture

### Core Library (`BetaSharp/`)

- **`Bootstrap.cs` / `Registries/DefaultRegistries.cs`** — Initialization entry point; registers all blocks, items, and entities via the registry pattern.
- **`Registries/`** — The data-driven backbone: `IRegistry`/`IndexedRegistry`, `RegistryKey`, `Holder`, and a reload pipeline (`RegistryReloadPipeline`, `IRegistryReloadListener`). This is where scripting-based modding will hook in.
- **`assets/`** — JSON definitions loaded into those registries: `block/`, `item/`, `entity/`, `recipe/`, `material/`, `item_material/`, `armor_material/`, `sound_group/`, `biome_spawn/`, `gamemode/`. Content changes usually belong here, not in C#.
- **`Blocks/`, `Items/`, `Entities/`** — Definitions and behavior. All three are composition-based: behavior is assembled from named behavior classes (`Entities/Behaviors/`, `Blocks/Behaviors/`) referenced by the JSON, rather than by subclassing.
- **`Worlds/`** — `Core/` (server world), `Chunks/`, `Storage/` (NBT persistence), `Gen/` + `Generation/` (terrain), `Lighting/`, `Mechanics/`, `ClientData/`. See Hard Invariants before touching `Storage/`, `Gen/`, or `Generation/`.
- **`Server/BetaSharpServer.cs`** — Base server shared by multiplayer and dedicated server. Contains `ChunkMap`, `PlayerManager`, and `Commands/`.
- **`Network/`** — `Connection`, `NetHandler`, and packets split into `C2SPlay`/`S2CPlay`/`Play` namespaces, dispatched off the `PacketId : byte` enum. `ExtendedProtocolPacket` is the current (thin) extension point. This whole subsystem is the fork's main rewrite target.
- **`NBT/`** — Named Binary Tag serialization. Frozen; see Hard Invariants.
- **`PathFinding/`** — Entity AI pathfinding. `PathingCoordinator` batches path requests and applies results a tick later, off the game-tick thread (see `docs/parallel-pathfinding.md`).

### Client (`BetaSharp.Client/`)

- **`BetaSharp.cs`** — Main game loop and client initialization; has a static `Instance` singleton.
- **`Display.cs`** — Window/display management via Silk.NET (GLFW). Requests a GL 4.3 core context.
- **`Rendering/`** — OpenGL rendering pipeline:
  - `Core/OpenGL/` — Low-level abstractions, including `EmulatedGL`/`LegacyGL` which stand in for fixed-function state
  - `Chunks/` — Chunk mesh building and rendering with frustum culling
  - `Entities/` — Two paths: `EntityInstanceBatchRenderer` (GPU-instanced, SSBO pose matrices, used for the main world entity loop) and `EntityBatchRenderer` (CPU-baked, used for single-draw sites like the held item and GUI mob previews). See `docs/gpu-instanced-entity-rendering.md`.
  - `Blocks/`, `Items/` — Model renderers
  - `PostProcessing/` — Post-process effects
  - `GameRenderer.cs` / `WorldRenderer.cs` — Top-level orchestrators
- **`Guis/`** — Custom GUI layout system using a Flexbox-based engine (see `CREDITS.md`).
- **`UI/Screens/`** — Individual screens: menus, HUD, pause screen, containers.
- **`DynamicTexture/`** — Procedurally animated textures (fire, water, lava, portal, clock, compass).
- **`Sound/`** — Audio via SFML.Audio.
- **`Resource/Pack/`** — Texture pack loading.

Shaders live in `BetaSharp/shaders/` and are embedded resources. **Adding a shader file requires an explicit `defineEmbeddedAsset` entry in `BetaSharp/AssetManager.cs`** — the `EmbeddedResource` glob in the `.csproj` alone is not enough, and the omission only shows up as a runtime crash.

`BetaSharp.Client` bans raw `ImGui.Text`/`TextColored`/`TextDisabled`/`TextWrapped` via `BannedSymbols.txt`; use the `ImGuiTextSafe` wrappers (raw calls treat their argument as a format string and segfault on `%`).

### Key Technologies

- **Silk.NET** — OpenGL bindings and windowing (GLFW)
- **ImGui.NET** (Hexa.NET.ImGui) — Debug/development overlays
- **SFML.Audio** — Sound
- **SixLabors.ImageSharp** — Image loading
- **Avalonia 12** — Launcher UI (with CommunityToolkit.Mvvm, Serilog)
- **Microsoft.Identity.Client (MSAL)** — Microsoft account authentication
- **Brigadier.NET** — Command parsing

## Code Conventions

- Write idiomatic **C#**. See [Microsoft C# conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions).
- Prefer data over code. If something can be a JSON definition in `BetaSharp/assets/` plus an existing behavior, it should be, rather than a new class.
- Behavior composition over inheritance — match the existing `Behaviors/` pattern rather than adding subclasses.
- `JavaRandom` is the one sanctioned Java-ism. Everything else Java-shaped that you find is fair game to modernize, subject to the parity rule above.
- Include tests with new features; when fixing a bug, start with a test that reproduces it.

## Docs

`docs/` holds design and phase-plan documents for larger efforts (`gpu-instanced-entity-rendering.md`, `parallel-pathfinding.md`, the `*-data-driven-migration.md` set, `asset-loading-system-guide.md`). They are working records — a doc describing a completed migration reflects the state when it was written, so verify against the code before relying on specifics.
