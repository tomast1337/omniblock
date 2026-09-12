# Client Luau E2E suite

`run-local.sh` launches the real client against a disposable platform-data
directory. It does not read or write the normal OmniBlock saves, options, logs,
statistics, or chunk cache.

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
OMNI.test.isMeshCurrent(x, y, z) -- latest section epoch has an installed mesh
OMNI.test.meshDeadlineMissCount(x, y, z) -- section-scoped lifetime counter
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
OMNI.client.state.presentedSolidLayerCount      -- selected solid render layers
OMNI.client.state.presentedTranslucentLayerCount -- selected translucent render layers
OMNI.client.state.emptyLayersSubmitted          -- uniforms submitted for layers with no draw
OMNI.client.state.terrainDrawCalls              -- solid plus translucent terrain draws
OMNI.client.state.terrainUniformEntries         -- per-section uniforms submitted to WebGPU
OMNI.client.state.findVisibleMs                 -- last completed visibility-selection CPU time
OMNI.client.state.terrainSubmitCpuMs             -- last completed terrain command-recording CPU time
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

`OMNI.test.dumpTerrain("label")` writes three CPU-only TSV artifacts:

- `terrain-label.tsv`: the column grid and aggregate lifecycle counters/gauges.
- `mesh-lifecycle-label.tsv`: the latest 8,192 lifecycle events, with monotonic timestamps,
  section lifetime ID, request ID, coordinates, epoch, priority, queued/deadline frames, dirty
  reasons, and discard reason.
- `mesh-sections-label.tsv`: current per-section epochs, deferred reasons, pending/resident request
  IDs and stages, plus request/stage ages in milliseconds. This remains useful when a stalled
  request's original events have rolled out of the bounded history.

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
