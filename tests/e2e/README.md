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

The opt-in `view-distance-32-diagnostic` scenario holds a real single-player session at the maximum
distance for 30 seconds and logs frame time plus mesh pressure. It is intentionally outside the
default suite because it is a sustained performance regression rather than a fast functional check.

The opt-in `flying-chunk-streaming` scenario teleports a persistently flying creative player ten
chunks away and above the world ceiling, then looks straight down and requires all 29 loaded columns
(232 vertical sections) in the radial safety ring to have completed meshes. It captures CPU-side
terrain-state TSV grids instead of screenshots, avoiding GPU readback while measuring the pipeline.

Every scenario gets a fresh disposable game-data directory and its own artifact
subdirectory. The suite covers main-menu structure and navigation, world
rename/delete/create forms, multiplayer server add/edit/delete, language
selection, options, new-world loading, and loading the checked-in fixture.
Each script exits through `OMNI.test.pass()`; results and captured client logs
are written under `artifacts/e2e-local/` by default.

World scenarios can observe the renderer without controlling renderer internals:

```lua
OMNI.client.state.meshPending         -- queued, dirty, or awaiting-upload meshes
OMNI.client.state.meshRequestToGpuMs  -- average request-to-upload latency in milliseconds
OMNI.client.state.frameTimeMs         -- latest full client frame time in milliseconds
```

These values are live and read-only. They are intended for streaming-health assertions and
diagnostics; performance budgets should account for the CI renderer and host hardware.

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
