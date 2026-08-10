# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**OmniBlock** is a hard fork of **BetaSharp**, which is a C# recreation of Minecraft Beta 1.7.3. It targets .NET 10 and is a multi-project solution with a shared core library, game client, dedicated server, and an Avalonia-based launcher.

OmniBlock is no longer trying to be a faithful reimplementation. It keeps Beta 1.7.3's *world* — the terrain you get from a seed, and the saves on disk — and rebuilds everything around it. Two things are the point of this fork:

1. **Scripting-based modding.** Content should be authored, not compiled in. The inherited data-driven layer (455 JSON definitions across blocks, items, entities, recipes, materials, sound groups, biome spawns, gamemodes, backed by `Registries/`) is the substrate. The scripting layer on top runs TypeScript/JS mods on **Jint** (a pure C# JavaScript engine) — mods ship as assets and scripts, not as forks of the engine. It has not landed yet; the closest shipped piece is the data-driven game-rules system under `Rules/`. The constraints it must obey are enshrined under "Scripting Determinism & Security" in Hard Invariants.
2. **A rebuilt network protocol.** The inherited protocol is Beta 1.7.3's: a flat `PacketId : byte` enum, one byte of ID space, hand-rolled per-packet serialization. It is being replaced with a UDP-based, versioned, extensible message layer — registry-negotiated IDs, hand-written serializers, and explicit transport / session / message / domain layering. The rewrite is mid-flight: `UdpConnection`, `ProtocolHandshake`, `SendPriority`, and per-message `*.Wire.cs` files (each hand-written from the same field-list shape a source generator used to emit) are in place, while the legacy packet enum still coexists. This is expected to break wire compatibility with upstream.

> **Note**: The client and server expect the Minecraft JAR file (`b1.7.3.jar`) to be in their running directory.

## Hard Invariants

These invariants are non-negotiable and constrain otherwise-reasonable refactors. Check against them before changing anything.

### Scripting Determinism & Security

These constrain the scripting layer's design (see the Overview). Not implemented yet, but the rules bind whatever lands:

- **Jint over V8:** Mod scripts execute on Jint, not on a V8/ClearScript engine. Wall-clock execution timeouts are non-deterministic across different CPUs, which would break multiplayer sync; Jint's instruction counting gives deterministic budgeting.
- **Client-side execution only:** A server may declare which mods it requires (by ID and version), but a client must NEVER download and execute a script payload provided by a server. Mods are installed locally on disk; executing server-provided scripts is a Remote Code Execution (RCE) path.
- **Two-phase mod API:** Scripts cannot touch the world while the engine is rebuilding registries. The TypeScript contract strictly isolates `namespace Registry` (load phase, idempotent) from `namespace Host` (tick phase, world access).
- **IDs over objects:** The C#/JS boundary stays flat. Scripts manipulate the world via primitive IDs (e.g. `Host.getEntityPosX(uint entityId)`), never via deep C# object references crossing the FFI boundary.

### Save file compatibility

Worlds written by upstream BetaSharp / Minecraft Beta 1.7.3 must load, and worlds OmniBlock writes must stay readable by them. The surface is:

- `OmniBlock/NBT/` — Named Binary Tag serialization. The on-disk tag encoding is a wire format; treat it as frozen.
- `OmniBlock/Worlds/Storage/RegionFormat/` — `RegionFile`, `RegionIo`, `RegionChunkStorage`, `ChunkDataStream`. The region/chunk layout on disk is likewise frozen.
- `OmniBlock/Worlds/Storage/` — level metadata, player data, `PersistentState`.

New data may be added, but only as additive NBT tags that older readers can ignore. Never repurpose or reorder existing ones.

### 1:1 world generation parity

The same seed must produce byte-identical terrain to upstream. This is what makes `JavaRandom` load-bearing:

- **`OmniBlock/Util/Maths/JavaRandom.cs` must stay a bit-exact port of Java's 48-bit LCG.** It is used by ~65 files. It is the last Java-shaped thing in the codebase and it is deliberate — Beta 1.7.3's terrain is a specific sequence of draws from that exact generator. Swapping it for `System.Random`, changing draw order, or "optimizing" the call sequence in a generator silently changes every world.
- Generation lives in two places: `OmniBlock/Worlds/Gen/Chunks/` (the `Overworld`/`Nether`/`Sky`/`Common` chunk generators) and `OmniBlock/Worlds/Generation/` (`Biomes/`, `Generators/Features/`, `Generators/Carvers/`).
- Refactors there are fine as long as the *sequence and count* of `JavaRandom` draws is preserved exactly. If you cannot prove that, don't land it. **Mod scripts are structurally banned from the worldgen path** so modding can never change the draw sequence.

Note that the IKVM/Java-interop constraint from upstream BetaSharp is **gone** — there are no IKVM package references and no `java.*` types anywhere in the tree. `JavaRandom` is a plain C# class and is the sole intentional exception to "write idiomatic C#, not Java-style code".

## Build & Run Commands

The solution file is `OmniBlock.slnx` (the newer XML solution format — there is no `.sln`).

```bash
# Run the launcher (recommended entry point — handles auth and starts client)
cd OmniBlock.Launcher && dotnet run --configuration Release

# Build a specific project
cd OmniBlock.(Client|Server|Launcher) && dotnet build --configuration Release

# Build everything from root
dotnet build --configuration Release

# Run all tests (1326 passing, 4 skipped as of the fork)
dotnet test

# Run a specific test
dotnet test --filter "FullyQualifiedName=OmniBlock.Tests.UnitTest1.Test1"
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

The fork is called OmniBlock, but **project directories, assembly names, and the root namespace are all still `OmniBlock`** (a rename from BetaSharp happened in place). A rename is a deliberate future task, not something to do opportunistically — it would touch every file and destroy `git blame`. Until it happens, `OmniBlock` in code means "this project".

## Solution Structure

| Project | Type | Purpose |
|---------|------|---------|
| `OmniBlock/` | Library | Shared core: blocks, entities, items, worlds, network, server logic |
| `OmniBlock.Client/` | Executable | Game client with WebGPU rendering, UI, input, audio |
| `OmniBlock.Server/` | Executable | Standalone dedicated server |
| `OmniBlock.Launcher/` | WinExe (Avalonia) | Launcher with Microsoft account auth (MSAL), AOT compiled |
| `OmniBlock.Tests/` | Test (xUnit) | Unit tests |

## Architecture

### Core Library (`OmniBlock/`)

- **`Bootstrap.cs` / `Registries/DefaultRegistries.cs`** — Initialization entry point; registers all blocks, items, and entities via the registry pattern.
- **`Registries/`** — The data-driven backbone: `IRegistry`/`IndexedRegistry`, `RegistryKey`, `Holder`, and a reload pipeline (`RegistryReloadPipeline`, `IRegistryReloadListener`). This is where scripting-based modding will hook in.
- **`assets/`** — JSON definitions loaded into those registries: `block/`, `item/`, `entity/`, `recipe/`, `material/`, `item_material/`, `armor_material/`, `sound_group/`, `biome_spawn/`, `gamemode/`. Content changes usually belong here, not in C#.
- **`Blocks/`, `Items/`, `Entities/`** — Definitions and behavior. All three are composition-based: behavior is assembled from named behavior classes (`Entities/Behaviors/`, `Blocks/Behaviors/`) referenced by the JSON, rather than by subclassing.
- **`Worlds/`** — `Core/` (server world), `Chunks/`, `Storage/` (NBT persistence), `Gen/` + `Generation/` (terrain), `Lighting/`, `Mechanics/`, `ClientData/`. See Hard Invariants before touching `Storage/`, `Gen/`, or `Generation/`.
- **`Rules/`** — Data-driven game rules (`GameRule`, `IRulesProvider`, `RuleRegistry`). Closest shipped thing to the scripting goal.
- **`Server/OmniBlockServer.cs`** — Base server shared by multiplayer and dedicated server. Contains `ChunkMap`, `PlayerManager`, and `Commands/`.
- **`Network/`** — Mid-rewrite. Two generations coexist:
  - Legacy Beta 1.7.3 path: `Connection`, `NetHandler`, and packets split into `C2SPlay`/`S2CPlay`/`Play` namespaces, dispatched off the `PacketId : byte` enum. `ExtendedProtocolPacket` is the thin extension point.
  - The rewrite: `Transport/` (`UdpConnection` over LiteNetLib, `SendPriority`), `ProtocolHandshake`, `Messages/` (registry-negotiated message IDs — `MessageRegistrySyncS2CPacket`, `OmniMessagePacket`). Each message type is a hand-defined class plus a `*.Wire.cs` companion file with its `Id`/`Key`/`SchemaVersion`/`Read`/`Write`/`Size`; `MessageRegistrations.cs` lists every one. See `docs/network-rewrite.md` for the design.
- **`NBT/`** — Named Binary Tag serialization. Frozen; see Hard Invariants.
- **`PathFinding/`** — Entity AI pathfinding. `PathingCoordinator` batches path requests and applies results a tick later, off the game-tick thread (see `docs/parallel-pathfinding.md`).

### Client (`OmniBlock.Client/`)

- **`OmniBlock.cs`** — Main game loop and client initialization; has a static `Instance` singleton.
- **`Display.cs`** — Window/display management via Silk.NET.GLFW. Requests a WebGPU surface (wgpu native backend); loads its bundled GLFW explicitly so ImGui's GLFW backend and Silk.NET share one library.
- **`Rendering/`** — WebGPU is the sole rendering backend (Silk.NET.WebGPU + `Silk.NET.WebGPU.Native.WGPU`, WGSL shaders in `OmniBlock/shaders/`). No fixed-function OpenGL path remains — some files still import `Silk.NET.OpenGL` purely for `GLEnum` format constants, and texture APIs are shaped like GL's, but rendering runs through WebGPU.
  - `Core/WebGPU/` — `WebGpuDevice`, `WgpuPipeline`, `WgpuMesh`, `WgpuDynamicBuffer`, `WgpuStorageBuffer`, `WgpuFramebuffer`, `WgpuTextureArray`, `ImGuiWgpuBackend`
  - `Chunks/` — Chunk mesh building and rendering with frustum culling
  - `Entities/` — Two paths: `EntityInstanceBatchRenderer` (GPU-instanced, storage-buffer pose matrices, used for the main world entity loop) and `EntityBatchRenderer` (CPU-baked, used for single-draw sites like the held item and GUI mob previews). See `docs/gpu-instanced-entity-rendering.md`.
  - `Blocks/`, `Items/`, `Particles/`, `UI/` — Model and UI renderers
  - `GameRenderer.cs` / `WorldRenderer.cs` — Top-level orchestrators
- **`Guis/`** — Custom GUI layout system using a Flexbox-based engine (see `CREDITS.md`).
- **`UI/Screens/`** — Individual screens: menus, HUD, pause screen, containers.
- **`DynamicTexture/`** — Procedurally animated textures (fire, water, lava, portal, clock, compass).
- **`Sound/`** — Audio via SFML.Audio.
- **`Resource/Pack/`** — Texture pack loading.

Shaders live in `OmniBlock/shaders/` (WGSL for the WebGPU path) and are embedded resources. **Adding a shader file requires an explicit `defineEmbeddedAsset` entry in `OmniBlock/AssetManager.cs`** — the `EmbeddedResource` glob in the `.csproj` alone is not enough, and the omission only shows up as a runtime crash.

`OmniBlock.Client` bans raw `ImGui.Text`/`TextColored`/`TextDisabled`/`TextWrapped` via `BannedSymbols.txt`; use the `ImGuiTextSafe` wrappers (raw calls treat their argument as a format string and segfault on `%`).

### Key Technologies

- **Silk.NET** — WebGPU bindings (`Silk.NET.WebGPU`, wgpu native) and windowing/input (GLFW)
- **ImGui.NET** (Hexa.NET.ImGui) — Debug/development overlays, rendered through the WebGPU backend
- **SFML.Audio** — Sound
- **SixLabors.ImageSharp** (+ `ImageSharp.Drawing`, `SixLabors.Fonts`) — Image loading and text
- **Avalonia 12** — Launcher UI (with CommunityToolkit.Mvvm, Serilog)
- **Microsoft.Identity.Client (MSAL)** — Microsoft account authentication
- **Brigadier.NET** — Command parsing
- **LiteNetLib** — UDP transport for the network rewrite
- **Roslyn** (`Microsoft.CodeAnalysis`) — `OmniBlock.Tests`' determinism call-graph analysis synthesises and walks a compilation of the core library

## Code Conventions

- Write idiomatic **C#**. See [Microsoft C# conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions).
- Prefer data over code. If something can be a JSON definition in `OmniBlock/assets/` plus an existing behavior, it should be, rather than a new class.
- Behavior composition over inheritance — match the existing `Behaviors/` pattern rather than adding subclasses.
- `JavaRandom` is the one sanctioned Java-ism. Everything else Java-shaped that you find is fair game to modernize, subject to the parity rule above.
- Include tests with new features; when fixing a bug, start with a test that reproduces it.
- Comments say why, not what. A comment that points at a plan (`Phase 3 of docs/foo.md §5.4`) is a comment that will be wrong within a month and cannot be checked by anyone reading the file — say what the constraint is instead, so the reason survives the plan.

## Docs

`docs/` is gitignored — a local scratch area for design and exploration notes (the network rewrite, the WebGPU port, the data-driven migration guides). Nothing there is committed, and none of it is authoritative. A design note describes intentions and goes stale the moment a change lands; because it is not checked in, no one has to trust it. Whatever is worth keeping belongs next to the code it constrains, as a comment that a reader can check against what it sits on. Don't commit `docs/`.
