# Client Luau E2E suite

`run-local.sh` launches the real client against a disposable platform-data
directory. It does not read or write the normal OmniBlock saves, options, logs,
statistics, or chunk cache.

Each isolated run installs `e2e-smoke` for ordinary streaming/gameplay coverage and `e2e-flat`
for repeatable entity/impostor visual scenes. The latter keeps the presentation camera close to a
level surface so distant mobs remain locatable in screenshots instead of floating in empty sky.

Before the first run, build the local Luau runtime:

```sh
native/luau/build-local.sh
```

Run the full suite or one scenario:

```sh
tests/e2e/run-local.sh
tests/e2e/run-local.sh multiplayer
```

World scenarios set `OMNI.config.pauseOnFocusLoss = false` before loading so the game continues
when another window has focus. This is the persisted **Pause on Focus Loss** toggle under
Options → UI Settings; it defaults to enabled. Disabling it also removes the extra background
sleep. Opening the pause menu explicitly still pauses single-player normally.

They also set `OMNI.config.captureMouse = false` to release the cursor without opening a screen
or pausing the world. **Capture Mouse** is a separate persisted toggle in UI Settings, enabled by
default. `OMNI.test.setLook()` and `OMNI.test.flyPath()` remain available with capture disabled.
These settings control input focus and cursor capture; a compositor may still suspend rendering
for a hidden or minimized window. Keep the test window visible when measuring background rendering.

The opt-in `view-distance-32-diagnostic` scenario holds a real single-player session at the maximum
distance for 30 seconds and logs frame time plus mesh pressure. It is intentionally outside the
default suite because it is a sustained performance regression rather than a fast functional check.

`terrain-lod-fixed-camera` is the opt-in Phase 5 scale gate. A dedicated first process prepares the
exact adaptive 64-chunk radial partition in the integrated server's persistent derived-data cache
and exits. The measured process reopens that cache, waits for 100% client coverage, then pins a
flying camera and samples 120 frames. It reports frame and
terrain-CPU distributions, draw counts, LOD GPU/boundary/cache memory, process working set, and the
required/available adaptive-tile contract plus current in-flight, pending, missing, and deferred
counts. It also writes the hierarchical CPU and GPU profiler snapshots used to distinguish forest
selection, seam validation, submission, and actual device work. GPU pass timing uses timestamp
queries when the adapter supports them and reports the
unsupported state otherwise. A passing run now proves complete source coverage for the requested
64-chunk horizon; fixture preparation is outside the timed sample. Run it with
`xvfb-run -a tests/e2e/run-local.sh terrain-lod-fixed-camera`.

`terrain-lod-scale-512` and `terrain-lod-scale-1024` are opt-in dormant-level release gates. Each
uses the same two-process persistent-cache contract but selects an E2E-only L7 or L8 session before
opening the world. They assert the hard node, draw-page, CPU-cache, compilation-queue, GPU-byte,
transport, compatibility, and WebGPU bounds. These scenarios do not widen the normal 256-chunk/L6
setting. Their output separately reports transport and presentation convergence time; on timeout
they dump both terrain state and profiler data before failing.

`terrain-lod-scale-512-movement` extends the L7 gate through a ten-chunk cinematic flight,
rotation, teleport, return to the original center, repeated block edits, and disconnect with work
pending. Its separate preparation process persists overlapping radial partitions along the route.
The measured process samples coverage, queue ages, GPU memory, and validation errors while moving,
checks actual arrival coordinates, and writes terrain/profiler artifacts at each stage and on
failure. Run with `xvfb-run -a tests/e2e/run-local.sh terrain-lod-scale-512-movement`.
`terrain-lod-scale-1024-movement` repeats the identical route at L8 with the same 256 MiB spatial
GPU ceiling, queue-age bounds, timeouts, and coverage assertions. Its own preparation process
persists the larger radial partitions; it does not carry resident tiles into the measured process.
Run with `xvfb-run -a tests/e2e/run-local.sh terrain-lod-scale-1024-movement`. A parity test keeps
the two standalone scripts aligned so the larger gate cannot quietly relax its guarantees.
The restricted `OMNI.test.prepareTerrainLodFixture(radius, x, z)` accepts an optional world-space
center for preparing routes (omitting both coordinates uses the player). `OMNI.test.disconnect()`
queues the normal world teardown after the Luau callback returns.
`OMNI.test.hasBlock(id, x, y, z)` reads the client's loaded block data, allowing edit tests to
observe server acknowledgments before asserting mesh readiness.
`dumpTerrain` also writes `terrain-lod-<label>.json` with source/convergence, spatial residency,
worker queues, and coverage-oracle snapshots, so admission stalls can be diagnosed from artifacts.
The scale fixture uses uniform synthetic reduced terrain to isolate lifecycle and budget behavior;
it is not a visual-quality baseline for caves, foliage, liquids, or arbitrary modded content.

`terrain-lod-near-quality` is an opt-in **visual characterization** with a narrow empty-layer
regression gate, not a whole-scene quality acceptance gate.
Run `xvfb-run -a tests/e2e/run-local.sh terrain-lod-near-quality` (240-second watchdog).
It uses isolated regular terrain, creative flight, a stationary camera, and controlled steps,
an open arch, snow, foliage, and a water basin five chunks ahead. It captures an eight-chunk exact
reference, changes to four exact chunks at the same position, captures initial and later LOD
presentation, then looks downward. The horizon is 16 chunks; no synthetic reduced tiles or large
pregeneration job are used. Passing proves setup and capture completed without the checked render
errors. The runner also checks the captured selected levels: the arch column must retain an empty
L0 translucent selection without a body draw, and the adjacent basin must present real L0 water.
It does **not** assert full terrain convergence, whole-scene visual fidelity, or natural-cave correctness.

`terrain-lod-<label>.json` now includes an on-demand `Quality` snapshot: camera/FOV/viewport and
distance settings, published spatial tile bounds and mesh sampling/span budgets, current CPU
source/hash agreement and immediate child availability, plus selected local solid/translucent
levels and their uploaded/non-empty layer levels. Spatial quality metadata belongs to the actual
uploaded presentation, including retained predecessors, rather than the latest CPU revision.
Spatial rows are ownership candidates, not proof of pixels drawn; check `Authoritative` and
submission counters. Local rows are selected layers, not all residency; a selected compiled empty
layer is retained with `HasSelectedLayerGeometry=false`. `LayerBodyDrawn` distinguishes no body
submission from a transition still drawing its old level. `Relation` compares the
spatial selection to the distance target and minimum level; it does not guess a fallback cause.
`MaximumRenderedSpans` is an observed maximum, not the configured vertical budget. Collection is
CPU-only and on demand; there is no additional GPU readback or per-frame diagnostic list build.
`CanonicalSpans`, `CaveCulledColumns`, `VerticalReducedColumns`, `RenderedSpans`, and
`CaveCullBelowY` record the actual mesh-build simplification stages. Column counters report how
many columns changed, not how many visible cave openings disappeared. They cannot measure detail
already lost before the client received a canonical tile.

For that source-side comparison, run the CPU fixture:

```sh
LUAU_NATIVE_LOCAL=1 dotnet test OmniBlock.Tests/OmniBlock.Tests.csproj --no-restore \
  --filter FullyQualifiedName~TerrainLod_generated_near_detail --logger 'console;verbosity=detailed'
```

It generates two real 4x4-chunk patches and compares the shipped 1x1 near source with a test-only
policy-v1 2x2 counterfactual and an unreduced reference. Output includes compressed bytes, mesh payload bytes,
material mismatches and air filled beneath opaque roofs, separately for skylit samples. These are
CPU/source fidelity measurements, not frame-time, screenshot, or complete-horizon acceptance.

`terrain-lod-remote-handoff` is an opt-in stationary **real-source handoff** check (240-second
watchdog). Run `xvfb-run -a tests/e2e/run-local.sh terrain-lod-remote-handoff`. It uses the ordinary
generated spawn region, waits for initial streaming, then reduces exact distance from eight to
four chunks without moving the camera. `check_remote_handoff.py` verifies the captured distances,
unchanged camera, current selection-cache revision, and at least one authoritative server-received
L2 tile selected for solid submission with 64x64 block-scale source columns. It also checks the
local ownership oracle for holes/overlaps and verifies that shrinking the exact radius actually
changes local ownership. Already authoritative tiles must persist; a fresh save may have no new
server tile available immediately after the change.
No synthetic LOD tiles or far-area generation are used. Missing ungenerated portions of the
16-chunk horizon are allowed: this does not claim a fully sourced horizon or visual-quality parity.

Quality dumps expose `PresentationRevision`, `CachedForestRevision`, and `ReadySpatialTiles` to
distinguish resident but unselected tiles from missing data. Per-tile `SelectedSolidPages` and
`SelectedTranslucentPages` count pages admitted by the frustum/authority filters, not pixel visibility
or GPU completion. `MatchesRecentRemoteSource` compares the uploaded hash with a bounded history
of 64 server receipts; false means unknown provenance, not necessarily locally generated content.

`terrain-lod-generated-patch` is a separate opt-in **real-source** check. Preparation uses
`OMNI.worldgen.start` to generate and durably save a 32-chunk-radius patch (3,209 targets) at (1024,1024),
outside spawn, with the ordinary generator, decoration, lighting and LOD conversion. A second
process reopens the save, verifies the completed job, teleports a creative flying player above
the patch, and waits for disk-cache transport and GPU residency. It checks eight downward camera
directions, a four-chunk flight, teleport, eight acknowledged block edits, and return-to-origin.
During rotation, flight and edits it samples the existing local camera-footprint ownership/seam
oracle, requires a nonempty sample, and checks unchanged GPU/queue/age limits. Teleport destinations
are allowed to converge before coverage assertions resume. Stage and failure dumps include terrain
and profiler state. It never calls `prepareTerrainLodFixture`. Run with
`xvfb-run -a tests/e2e/run-local.sh terrain-lod-generated-patch` (1,080-second watchdog per process;
generation itself has a 900-second wait). The larger wall-clock allowance is only for preparing
four times the previous target count: GPU/worker/queue and 30-second compilation-age limits are
unchanged. Generation failure now dumps terrain and profiler state, as does successful preparation.
The surrounding horizon is intentionally incomplete; this is not a zero-hole whole-horizon or
512/1024 performance gate. Generated-column integration tests separately verify voxel/material,
light and cave/overhang fidelity through near-parent construction, cache reopen and transport.
Generated 4x4-chunk integration tests also check actual 2:1 parent reduction, descendant edits,
and persistence of the replacement parent in Overworld, Sky and Nether.
Preparation now waits for ten consecutive 100-ms samples with no dirty sources, owned conversions,
pending/deferred parent builds, or queued/running spatial cache writes after the job completes.
It rejects offline conversion drops, conversion/build/write failures and persistence admission
deferrals, and verifies that the job's committed chunks reached conversion admission. This is a
quiet-period diagnostic, not an atomic storage barrier or a proof of complete horizon coverage.
Reusable-baseline sealing additionally rejects any spatial-hierarchy capacity rejection (or a
missing rejection counter). Conversion admission does not guarantee spatial leaf publication;
quiet queues must not mask a leaf refused by a full hierarchy. This conservative gate also rejects
parent capacity pressure that might subsequently recover, instead of claiming loss-free preparation.
Inactive generation now awaits bounded conversion admission after the terrain commit instead of
discarding LOD sources when the converter is full. The wait is cancellable and does not block the
simulation tick or create an overflow queue.
`dumpTerrain` includes the integrated server's LOD snapshot (null on a remote connection), exposing
source counts, build queues, cache writes/failures and `PreparationPendingWork` beside client state.
Restricted `OMNI.test.terrainLodFixtureMetric` readers include `preparationPending`,
`preparationFailures`, `offlineSubmitted`, `offlineDropped`, `persistenceDeferred`, and
`persistenceRunning`; these return -1 when the integrated server snapshot is unavailable.

### Reusing a real-terrain baseline

The generated-patch scenario can retain preparation across runs (Python 3.11+ and `flock` required):

```bash
E2E_PREPARED_FIXTURE="$PWD/artifacts/e2e-fixtures/generated-patch-r32-v1" \
    xvfb-run -a tests/e2e/run-local.sh terrain-lod-generated-patch
```

Run the same command again to skip generation. This opt-in currently supports only
`terrain-lod-generated-patch`; ordinary runs still use disposable temporary data. Choose a **new
absolute directory**, not an existing save or the normal game data directory.
The former radius-16 baseline cannot satisfy the radius-32 contract. Keep it for historical
evidence and use a new root; do not edit its manifest to make validation pass.

- First run: prepare normally, require a successful process/result and drained LOD diagnostics,
  then publish `baseline/data` and its manifest together with an atomic directory rename.
- Later runs: validate the compiled game assemblies, shipped assets, seed fixtures and preparation
  script; validate every baseline file with SHA-256; restore a fresh disposable `work/data` before
  measurement. Prior measured edits, generation jobs, player state and cache writes cannot leak
  into the next run. Measurement-script changes alone do not invalidate preparation.
- The manifest records world, generator, content, material-rule and LOD schema/policy identity.
  The measured process must report that same server identity at its final checkpoint. Terrain
  dumps expose it as `ServerIdentity` (null without an integrated server).
- Production world identity includes its storage path. The work directory therefore stays at
  the same absolute path; **moving/copying a fixture root to another path is not supported**.
  No production identity check is bypassed to make reuse work.
- One nonblocking lock covers build, preparation, restore and measurement. Existing unowned roots,
  symlinks, interrupted preparation/restores, changed inputs or corrupt files fail explicitly.
  Inspect failed work and select a new root to rebuild; the runner never overwrites a baseline.
- The root is retained deliberately. Budget space for baseline, current work and a transient
  restore copy (roughly three data copies). Only the previous disposable `work` is removed after
  a successful restore. The original baseline remains recoverable and personal saves are untouched.

Each run needs fresh artifacts (the default timestamp directory provides this); its
`prepared-fixture.json` archives the validated manifest. Preparation and measurement are separate
processes, but restored client caches and OS filesystem caches may be warm. This is not a cold-I/O
benchmark, and does not establish full 64/512/1024 horizon coverage. The baseline is byte-checked;
live simulation timing and performance samples are not guaranteed deterministic.

Run the ownership/integrity tests without launching the game:

```bash
python3 -m unittest discover -s tests/e2e -p test_prepared_fixture.py -v
```

`entity-render-baseline` is an opt-in 300-second-watchdog benchmark for the existing GPU-instanced
mob renderer. It uses deterministic client-only cow/sheep/mixed replicas (not server-spawned mobs),
a stationary flying camera, and an empty-population control. It writes per-frame JSON, resource
hashes, p50/p95 CPU/frame timings, actual draw/instance/upload counts, and six reference screenshots
outside timed intervals. Run with `xvfb-run -a tests/e2e/run-local.sh entity-render-baseline` or use
`CONFIGURATION=Release tests/e2e/run-local.sh entity-render-baseline` on your normal GPU. GPU timing
is explicitly unavailable; no FPS assertion is made on software-rendered CI. See
`docs/entity-render-baseline.md` for the capture contract, exclusions and visual acceptance budget.

`entity-lod-selection` is the opt-in Phase 1 observer contract test. It checks near/far cow selection,
projected-size changes through FOV, continued 3D instance submission for the intended impostor tier,
layered-sheep provider selection, and expired cow state. Run with
`xvfb-run -a tests/e2e/run-local.sh entity-lod-selection`. It writes a far-cow baseline JSON and
screenshot; the read-only `OMNI.client.state.entityLod*` counters distinguish intended selection
from actual representation. No sprites, atlas capture, or rendering-distance changes are enabled.

The opt-in Phase 2 scenarios are `entity-impostor-prototype` (capture/fallback/replacement and a
64-cow instancing sample), `entity-impostor-orbit` (52 paired screenshots across 26 directions),
and `entity-impostor-occlusion` (uncovered/half/full stone-wall screenshot pairs). Run each with
`xvfb-run -a tests/e2e/run-local.sh <scenario>`. They use isolated fixtures and a 300-second watchdog,
and assert zero `OMNI.client.state.webGpuErrorCount`. `OMNI.test.entityImpostors(true, true)` forces
the tier for close visual inspection only; unsupported states/providers still fall back to 3D.
Validated providers are now enabled by default; these historical force-tier scenarios still isolate
specific comparisons. `entity-impostor-catalog` requires all fourteen shipped living mobs to bake
and submit through their declared provider without disappearing or doubling. The detailed appearance
matrix includes cow, layered sheep, wild wolf, zombie, and creeper pairs. Tamed, angry, sitting, or
shaking wolves deliberately retain their 3D renderer.
Submission checks do not replace visual review of their artifacts.
The Phase 4 slice expands each atlas to idle plus four gait poses. The prototype scenario uses
the restricted `OMNI.test.entityBaselineState("idle"|"walk-0".."walk-3"|"hurt")` control and checks
`entityImpostorPoseMask`/`entityImpostorHurtSubmissions`; this proves real GPU-path selection without
turning the presentation fixture into a simulated entity. Hurt and movement must not silently fall
back to 3D. Sheep uses one two-layer atlas: base RGBA and separately captured fleece coverage. The
same scenario creates all sixteen colors in both unsheared and sheared states and asserts that
runtime tinting submits fleece only for the unsheared half.

`entity-impostor-cache` exercises Phase 3 cold capture, memory reuse, disk reuse, effective cow-skin
changes, cancellation during capture/readback, rapid texture-pack switching, and prototype GPU-resource
recreation. Run `xvfb-run -a tests/e2e/run-local.sh entity-impostor-cache`. After the cold scenario
succeeds, the runner automatically launches `entity-impostor-cache-warm.luau` in a fresh process
using the same disposable game-data directory: it must load from disk and capture zero views.

`entity-impostor-appearance` records paired 3D/impostor Phase 4 screenshots for daylight, night,
hurt, a walking pose, and all sixteen sheep colors plus sheared state. Its restricted
`entityBaselineEnvironment(day|night|storm)` control pins only the client presentation fixture; it
does not change the integrated server or expose a gameplay scripting API.

`entity-impostor-rollout` exercises the persistent, script-configurable production gate without
forcing the tier. It writes paired 256-cow 3D/warmed-impostor timings, an enabled near-scene sample,
and selected screenshots, then checks zoom, moving flight and teleport. The impostor timing JSON
also contains cold-bake, cache, memory, invalidation and draw-batch diagnostics. Run with
`xvfb-run -a tests/e2e/run-local.sh entity-impostor-rollout`; screenshots remain outside samples.
Do not run the warm script alone against an empty cache. Its result is under the cold artifact
directory's `warm/` subdirectory; either process failing fails the runner.

Restricted `OMNI.test.impostorCache(action)` supports `clear-memory`, `dispose-session`,
`hold-capture`/`release-capture`, `hold-readback`/`release-readback`, `reload`, and
`pack-red`/`pack-green`/`pack-original`. The synthetic packs tint only the effective cow texture;
holds make cancellation points deterministic. These controls exist only in the E2E test host.
Read-only `OMNI.client.state.entityImpostor*` diagnostics expose `MemoryHits`, `DiskHits`,
`CacheMisses`, `CacheWrites`, `CacheErrors`, `Cancellations`, `StaleResults`, `CapturedViews`,
`MemoryBytes`, `ReadbackPending`, bake age/timing, invalidations, known GPU/staging bytes, atlas and
draw-batch counts (for example `entityImpostorDiskHits`). All rendering scenarios
assert zero WebGPU errors. Physical device-loss recovery is not simulated by `dispose-session`.
`entityClientResident`, `entityPresented`, and `entityHidden` separate network residency from the
per-frame presentation decision when diagnosing entity-distance failures.

`frustum-directional` disables VSync, samples the real client while looking at the horizon and
straight down, and records average presented meshes, solid/translucent draws, and frame time. Its
portable assertion is that camera direction materially changes terrain selection; timing remains
diagnostic because CI commonly uses a software renderer. This also exercises WebGPU's uncapped
Mailbox/Immediate presentation selection instead of silently remaining in FIFO mode.

`chunk-visibility-baseline` is an opt-in long-running benchmark for stable view distances 4 and 16.
It is excluded from the default suite so CI is not extended by several minutes. Run it with
`xvfb-run -a tests/e2e/run-local.sh chunk-visibility-baseline`. Distance 32 deliberately uses the
fixed 30-second warm-up in `view-distance-32-diagnostic`; waiting for full residency there measures
world-generation throughput as much as renderer traversal. Both scenarios write the counters and
terrain dumps used by the chunk performance plan.

`fps-limit` runs at the main menu with VSync disabled, verifies that the minimum slider value paces
complete client frames to 30 FPS, then switches the slider to Unlimited and verifies that the cap
is released. Frame telemetry includes the pacing wait, matching the rate users actually observe.

The `flying-chunk-streaming` scenario teleports a persistently flying creative player above
the world ceiling, points the camera straight down, requires all 29 loaded columns (232 vertical
sections) in the radial safety ring to have completed meshes, then flies a full ten-chunk/160-block
path before releasing movement. It captures a CPU-side terrain-state TSV at every chunk crossing
instead of screenshots, avoiding GPU readback while measuring the pipeline.

`simulation-distance.luau` starts an integrated session with render distance 32 and simulation
distance 8, verifies the server-authoritative values returned by the session protocol, then changes
simulation distance to 2 and proves the terrain streaming distance remains 32.

`frame-profiler` is an opt-in maximum-near-distance diagnostic. It holds an unfocused flying
camera still, captures separate forward and straight-down profiler snapshots, and writes the
generic scope timings plus the chunk visibility/sort/submission breakdown without a GPU readback.

`entity-tracking-distance` is an opt-in integrated-server regression test for distant mob
persistence. It spawns a real cow, moves the player 200 blocks away while retaining the cow's chunk
inside a 16-chunk terrain radius, and proves the cow remains networked and presented outside the
two-chunk simulation radius. This guards the separation between terrain streaming, mob tracking,
client presentation, and expensive AI/pathfinding ticks.

`entity-hostile-tracking-distance` covers the distinct hostile lifecycle. It spawns a real creeper,
moves 96 blocks away, and requires an actual impostor submission while the creeper remains below
the intentional 128-block gameplay despawn boundary. It then crosses that boundary and proves the
reason-coded removal created and completed the bounded client-only sink/darken/fade/puff visual.

`chunk-mesh-deadlines` breaks a compact patch of nearby fixture terrain through the normal
multiplayer player-controller path. It verifies critical forward progress, deadline-accounting
invariants, bounded cancellation counters, and that no resident near-field mesh disappears. Missed
deadlines fail the workload; currently overdue work remains visible in the output and terrain
artifacts. The critical deadline is 50 ms and never fewer than two rendered frames, keeping the
contract stable for uncapped, VSync, and software-rendered CI clients. `debug-smoke` is
launched with `--debug`, proves the
dashboard is actually open through `OMNI.client.state.debugOpen`, enters the fixture world, and
keeps it running long enough to exercise both ImGui and world rendering.

Restricted E2E scripts can control a flying player without synthesizing keyboard or mouse events:

```lua
OMNI.test.setFlying(true)
OMNI.test.setLook(yaw, pitch)
OMNI.test.setMovement(forward, strafe, vertical)
OMNI.test.setMovement(0, 0, 0) -- release every movement axis
OMNI.test.flyPath(ax, ay, az, bx, by, bz, seconds)
OMNI.test.breakBlock(x, y, z) -- true when a non-air block was submitted for breaking
OMNI.test.setBlock("omniblock:flowing_water", x, y, z) -- E2E-only server command
OMNI.test.summon("omniblock:cow", 1) -- E2E-only server command; maximum count is 256
OMNI.test.worldGenerationAuto("prepare", 8) -- E2E-only integrated-server control
OMNI.test.worldGenerationMetric("saved") -- read-only moving-generation diagnostic
OMNI.test.prepareTerrainLodFixture(64)    -- E2E-only derived-data scale fixture
OMNI.test.configureTerrainLodScaleProfile(512) -- before loading a world; accepts 512 or 1024
OMNI.test.terrainLodFixtureMetric("complete")
OMNI.test.terrainLodFixtureMetric("cacheReadHits") -- proves warm-process disk reuse
OMNI.test.countEntities("omniblock:cow", 180, 220) -- client-resident entities in a distance band
OMNI.test.isMeshCurrent(x, y, z) -- latest section epoch has an installed mesh
OMNI.test.meshDeadlineMissCount(x, y, z) -- section-scoped lifetime counter
OMNI.test.dumpProfiler("steady") -- rolling main/server timings plus chunk-visibility breakdown
```

Movement values are clamped to `[-1, 1]`. The controls persist until changed, which lets a script
sample streaming state while the player follows a repeatable path. `flyPath` linearly interpolates
the player and camera from A to B on the normal 20 Hz client ticks, while preserving path velocity
for the renderer's movement prediction.

Every scenario gets a fresh disposable game-data directory and its own artifact
subdirectory. The suite covers main-menu structure and navigation, world
rename/delete/create forms, multiplayer server add/edit/delete, language
selection, options, new-world loading, and loading the checked-in fixture.
Each script exits through `OMNI.test.pass()`; results and captured client logs
are written under `artifacts/e2e-local/` by default.

World scenarios can observe the renderer without controlling renderer internals:

```lua
OMNI.client.state.debugOpen           -- whether the debug dashboard is active
OMNI.client.state.meshPending         -- queued, dirty, or awaiting-upload meshes
OMNI.client.state.meshRequestToGpuMs  -- average request-to-upload latency in milliseconds
OMNI.client.state.frameTimeMs         -- latest full client frame time in milliseconds
OMNI.client.state.residentMeshCount   -- uploaded sub-chunk meshes retained by the renderer
OMNI.client.state.presentedMeshCount  -- resident meshes selected for the current frame
OMNI.client.state.foregroundPending   -- unresolved foreground mesh requests
OMNI.client.state.backgroundPending   -- unresolved background mesh requests
OMNI.client.state.oldestForegroundAge -- age in client scheduler ticks (20 ticks/second)
OMNI.client.state.presentationRegressionCount -- cumulative near-field presentation regressions
OMNI.client.state.residentSolidLayerCount      -- resident sections with solid geometry
OMNI.client.state.residentTranslucentLayerCount -- resident sections with translucent geometry
OMNI.client.state.visibilityCandidates         -- resident sections classified by the culler
OMNI.client.state.frustumTests                  -- exact bounding-box/frustum tests in the last frame
OMNI.client.state.portalVisited                 -- distinct sections reached by portal traversal
OMNI.client.state.safetyRescued                 -- near sections conservatively restored after traversal
OMNI.client.state.renderDistance                -- server-authoritative terrain streaming radius in chunks
OMNI.client.state.simulationDistance            -- server-authoritative gameplay ticking radius in chunks
OMNI.client.state.presentedSolidLayerCount      -- selected solid render layers
OMNI.client.state.presentedTranslucentLayerCount -- selected translucent render layers
OMNI.client.state.emptyLayersSubmitted          -- uniforms submitted for layers with no draw
OMNI.client.state.terrainDrawCalls              -- solid plus translucent terrain draws
OMNI.client.state.terrainUniformEntries         -- compatibility name: per-section metadata records submitted to WebGPU
OMNI.client.state.terrainSubmissionBatches      -- metadata-buffer writes (normally one per non-empty layer)
OMNI.client.state.terrainPipelineBinds          -- terrain pipeline binds recorded this frame
OMNI.client.state.terrainTextureBinds           -- terrain-array binds recorded this frame
OMNI.client.state.terrainUniformArenaCapacity   -- compatibility name: bounded reusable draw-metadata entries
OMNI.client.state.terrainUniformArenaGrowths    -- lifetime metadata-arena reallocations for active pipelines
OMNI.client.state.findVisibleMs                 -- last completed visibility-selection CPU time
OMNI.client.state.terrainSubmitCpuMs             -- last completed terrain command-recording CPU time
OMNI.client.state.terrainLodSolidCpuMs            -- last reduced opaque/cutout pass CPU time
OMNI.client.state.terrainLodTranslucentCpuMs      -- last reduced translucent pass CPU time
OMNI.client.state.terrainLodGpuBytes              -- known resident LOD mesh/light GPU bytes
OMNI.client.state.terrainLodBoundaryBytes         -- retained CPU boundary-summary bytes
OMNI.client.state.terrainLodCacheBytes            -- persistent client hierarchy-cache bytes
OMNI.client.state.terrainLodMeshOwned              -- bounded compilation requests/results currently owned
OMNI.client.state.terrainLodMeshCoverageQueued     -- missing-coverage compilations waiting for the worker
OMNI.client.state.terrainLodMeshRefinementQueued   -- detail-upgrade compilations waiting for the worker
OMNI.client.state.terrainLodMeshCompletedBytes     -- retained completed CPU mesh/boundary bytes
OMNI.client.state.terrainLodMeshPredictedBytes     -- estimated queued/running result bytes
OMNI.client.state.terrainLodMeshPredictedMs        -- estimated queued/running worker milliseconds
OMNI.client.state.terrainLodMeshAdmissionDeferrals -- compilation time/byte/capacity backpressure events
OMNI.client.state.terrainLodMeshUploadDeferrals    -- render-thread upload-budget deferrals
OMNI.client.state.terrainLodRemoteCoverageRequired -- adaptive tiles forming the requested radial horizon
OMNI.client.state.terrainLodRemoteCoverageAvailable -- tiles covered directly or by complete descendants
OMNI.client.state.terrainLodRemoteCoverageInFlight -- required tiles awaiting a server response
OMNI.client.state.terrainLodRemoteCoveragePending  -- required tiles awaiting server cache work
OMNI.client.state.terrainLodRemoteCoverageMissing  -- required tiles absent from the server cache
OMNI.client.state.terrainLodRemoteCoverageDeferred -- required tiles delayed by gameplay/bandwidth pressure
OMNI.client.state.terrainLodRemoteCoverageComplete -- 1 only when every required tile has source coverage
OMNI.client.state.terrainLodSpatialGpuBytes         -- resident spatial-tile and seam GPU estimate
OMNI.client.state.clientWorkingSetBytes           -- current client process working set
OMNI.client.state.meshCancelledCount  -- discarded/abandoned requests, including superseded work
OMNI.client.state.meshSupersededCount -- subset discarded due to a newer revision/replacement
OMNI.client.state.meshBuildFailureCount -- snapshot or worker exceptions (normally zero)
OMNI.client.state.meshAwaitingUpload  -- completed requests awaiting installation
OMNI.client.state.meshAwaitingDraw    -- nonempty uploaded revisions not yet drawn (can be offscreen)
OMNI.client.state.meshCooperativeCancellationCount -- obsolete builds that observed cancellation
OMNI.client.state.meshCriticalCompletedCount -- completed critical revisions with a deadline
OMNI.client.state.meshCriticalDeadlineMissCount -- completed critical revisions after their deadline
OMNI.client.state.meshCriticalOverdueCount -- currently live critical revisions past their deadline
```

These values are live and read-only. They are intended for streaming-health assertions and
diagnostics; performance budgets should account for the CI renderer and host hardware.

`OMNI.test.dumpTerrain("label")` writes three CPU-only TSV artifacts and, for an integrated
server, one JSON generation profile:

- `terrain-label.tsv`: the column grid and aggregate lifecycle counters/gauges.
- `mesh-lifecycle-label.tsv`: the latest 8,192 lifecycle events, with monotonic timestamps,
  section lifetime ID, request ID, coordinates, epoch, priority, queued/deadline frames, dirty
  reasons, and discard reason.
- `mesh-sections-label.tsv`: current per-section epochs, deferred reasons, pending/resident request
  IDs and stages, plus request/stage ages in milliseconds. This remains useful when a stalled
  request's original events have rolled out of the bounded history.
- `world-generation-label.json`: bounded timing/allocation distributions per generation stage,
  queue depths and peak, failures, and a conservative retained chunk-payload lower bound.

`OMNI.test.dumpProfiler("label")` writes `profiler-label.tsv` with the last, rolling average,
P50, P95, and recent period maximum for every main/client and integrated-server profiler scope.
It also writes `gpu-profiler-label.tsv` when WebGPU is active. That sibling contains a delayed,
non-blocking timestamp-query snapshot for the world, impostor capture, hand, interface, composite,
and exact encoder-level render span, plus framebuffer resolution, physical-pass counts, and query
ring drops. Milliseconds use wgpu-native's timestamp period when its ABI exposes one. The bundled
0.19 backend does not, so Vulkan obtains the period from a uniquely vendor/device-matched physical
adapter without creating another logical device; ambiguous matches remain raw ticks. The diagnostic
override `OMNIBLOCK_GPU_TIMESTAMP_PERIOD_NS` remains available for backend development. Other
unsupported cases label and preserve raw ticks rather than assuming the device's unit.
When a world renderer is active it also writes `chunk-presentation-label.tsv`, separating spatial
frustum traversal, near-to-far candidate sorting, portal traversal, and terrain submission. The
presentation artifact records how many exact-frustum candidates were nevertheless outside render
distance and how many comparisons the candidate sort performed, so tests can distinguish a costly
sort from culling, traversal, or command recording.

`world-generation-control.luau` is the Phase 0 control workload: minimum view distance, a fixed
near-field edit and lava update, and a 32-block diagonal cinematic flight. Automatic background
pregeneration is absent/disabled by construction. Its start, pressure, and settled dumps provide
repeatable evidence before a background generation service is introduced.

`world-generation-job-lifecycle.luau` exercises the user-facing `OMNI.worldgen` facade against the
real integrated server. It starts and immediately pauses a persistent circular job, verifies the
immutable progress/list views, resumes until server-owned work begins, and cancels while asserting
the persisted cancellation contract that committed terrain is retained. The fixed-area service's
focused tests separately cover cancellation after a durable batch; the E2E does not wait for an
expensive generation batch. Mutations travel through the same FIFO server command boundary as the
in-game preparation screen; scripts never touch the generation service from the client thread.
Remote clients expose `OMNI.worldgen.available == false` until a permissioned network control
protocol is added.

The terrain header also reports `leadingEdgeQueued`, `leadingEdgePending`, and
`evictionGraceMeshes`. The first is the coalesced frontier waiting for loaded source data, the
second is admitted frontier work, and the last counts resident meshes temporarily preserved outside
the `R + 2` retention boundary during the 30-frame hysteresis window.

Lifecycle stages distinguish invalidation, deferred production, queue admission, snapshotting,
worker queueing, building, awaiting upload, upload, and first draw recording. `EmptyReady` completes
empty meshes without pretending they need a draw. Events with request ID zero describe the section
(invalidation, deferred production, eviction), rather than a particular build. Request ages start
at admission; invalidation/deferred timestamps remain separate so deferred production is not
mistaken for worker queue time. Repeated deferred notifications coalesce into one event.

`DrawRecorded` means the CPU recorded a draw command, **not** that the GPU finished or the display
presented it. Cancellation records request abandonment or result discard; a separate
`CancellationObserved` event and counter prove when a worker cooperatively stopped. Reasons
distinguish supersession, leaving retention,
renderer disposal, orphan recovery, duplicate admission, build/snapshot failure, and section
lifetime replacement. A section's identity does not reset when its pooled version object is reused.
An undrawn resident replaced by a newer mesh counts as superseded; an already drawn mesh does not.

Counters are cumulative for one `ChunkRenderer` lifetime and independent of **Reset mesh profile**.
The trace reports overwritten-event count explicitly. No per-frame filesystem writes or GPU
readbacks are involved; dumps are generated only on request. Trace-derived timing distributions
are diagnostic samples, not unbiased lifetime percentiles once history has rolled over.

The following environment variables are optional:

- `E2E_ARTIFACTS_DIR`: artifact output directory.
- `E2E_TIMEOUT_SECONDS`: watchdog timeout; defaults to 90 seconds.
- `CONFIGURATION`: .NET build configuration; defaults to Debug.
- `E2E_PREPARED_FIXTURE`: opt-in absolute prepared-baseline directory for
  `terrain-lod-generated-patch`; see the isolation and invalidation rules above.

CI runs the same script under Xvfb and Mesa Lavapipe. `WGPU_BACKEND=vulkan`
selects WebGPU's Vulkan backend, while `VK_ICD_FILENAMES` points it at Mesa's
software Vulkan ICD so the job does not depend on a physical GPU.

`run-with-display.sh` owns display selection separately from the Luau scenario.
It currently supports `E2E_DISPLAY_BACKEND=xvfb`. The reserved `headless` branch
fails explicitly until an in-process headless client backend exists. When that
backend lands, only this launcher branch and client bootstrap should change;
`smoke.luau`, its selectors, waits, and assertions stay unchanged.

`teleport-preload.luau` performs a ten-chunk same-dimension relocation. It asserts that gameplay is
covered by the terrain-loading screen while the destination is incomplete, then waits for the same
decoded-column and uploaded-mesh contract used by initial world entry before accepting the player.
