#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
artifact_dir="${E2E_ARTIFACTS_DIR:-$repo_root/artifacts/e2e-local/$(date -u +%Y%m%dT%H%M%SZ)}"
timeout_seconds="${E2E_TIMEOUT_SECONDS:-90}"
configuration="${CONFIGURATION:-Debug}"
requested_scenario="${1:-all}"
if [[ ( "$requested_scenario" == "terrain-lod-near-quality" || "$requested_scenario" == "terrain-lod-remote-handoff" || "$requested_scenario" == "terrain-lod-natural-cave" || "$requested_scenario" == "terrain-lod-cave-mouth-remote" || "$requested_scenario" == "terrain-lod-quality-upgrade" ) && -z "${E2E_TIMEOUT_SECONDS:-}" ]]; then
    timeout_seconds=240
fi
if [[ "$requested_scenario" == "terrain-lod-cave-mouth-remote" && -z "${E2E_TIMEOUT_SECONDS:-}" ]]; then
    timeout_seconds=480
fi
prepared_fixture="${E2E_PREPARED_FIXTURE:-}"
if [[ -n "$prepared_fixture" ]]; then
    if [[ "$requested_scenario" != "terrain-lod-generated-patch" || "$prepared_fixture" != /* ]]; then
        echo "E2E_PREPARED_FIXTURE requires an absolute directory and terrain-lod-generated-patch." >&2
        exit 2
    fi
    python3 "$script_dir/prepared_fixture.py" init "$prepared_fixture"
    # Hold ownership across preparation, sealing, restore and measurement, including game children.
    exec 9>"$prepared_fixture/.lock"
    flock -n 9 || { echo "Prepared fixture is already in use." >&2; exit 2; }
fi
if [[ ( "$requested_scenario" == "chunk-visibility-baseline" || "$requested_scenario" == "frame-profiler" || "$requested_scenario" == "terrain-lod-fixed-camera" || "$requested_scenario" == "terrain-lod-spatial-shadow" || "$requested_scenario" == "terrain-lod-server-cache-transport" || "$requested_scenario" == "world-generation-job-lifecycle" || "$requested_scenario" == "entity-render-baseline" || "$requested_scenario" == "entity-lod-selection" || "$requested_scenario" == "entity-tracking-distance" || "$requested_scenario" == entity-impostor-* ) && -z "${E2E_TIMEOUT_SECONDS:-}" ]]; then
    timeout_seconds=300
fi
if [[ "$requested_scenario" == terrain-lod-scale-* && -z "${E2E_TIMEOUT_SECONDS:-}" ]]; then
    timeout_seconds=600
fi
if [[ "$requested_scenario" == "terrain-lod-generated-patch" && -z "${E2E_TIMEOUT_SECONDS:-}" ]]; then
    # Radius-32 real generation has four times the radius-16 target count. This is a wall-clock
    # allowance only: worker, queue, GPU and per-sample starvation ceilings stay unchanged.
    timeout_seconds=1080
fi
scenarios=(menu world-management multiplayer language-options create-world smoke debug-smoke fps-limit simulation-distance chunk-mesh-deadlines teleport-preload flying-chunk-streaming frustum-directional liquid-boundary-visibility world-generation-control world-generation-job-lifecycle terrain-lod-presentation terrain-lod-spatial-shadow terrain-lod-server-cache-transport)
run_roots=()

cleanup() {
    for run_root in "${run_roots[@]}"; do
        case "$run_root" in
            "${TMPDIR:-/tmp}"/omniblock-e2e.*) rm -rf -- "$run_root" ;;
        esac
    done
}
trap cleanup EXIT

if [[ "$requested_scenario" != "all" ]]; then
    scenarios=("$requested_scenario")
fi

echo "Running OmniBlock client E2E suite: ${scenarios[*]}"
echo "Artifacts: $artifact_dir"

# Build once before replacing XDG_DATA_HOME with each isolated game-data directory. `dotnet run`
# otherwise attempts a restore under that temporary environment for every scenario; recent SDKs
# can fail that project walk without emitting a useful error, and rebuilding per scenario also
# distorts the suite runtime.
env LUAU_NATIVE_LOCAL=1 dotnet build "$repo_root/OmniBlock.Client/OmniBlock.Client.csproj" \
    --configuration "$configuration" --no-restore -m:1 --nologo --verbosity:quiet \
    -p:WarningLevel=0

suite_status=0
for scenario in "${scenarios[@]}"; do
    script="$script_dir/$scenario.luau"
    if [[ ! -f "$script" ]]; then
        echo "Unknown E2E scenario: $scenario" >&2
        exit 2
    fi

    fixture_mode="prepare"
    if [[ -n "$prepared_fixture" ]]; then
        if [[ -e "$artifact_dir/$scenario/result.json" || -e "$artifact_dir/$scenario/prepare/result.json" ]]; then
            echo "Prepared-fixture runs require a fresh artifact directory (stale result found)." >&2
            exit 2
        fi
        fixture_mode="$(python3 "$script_dir/prepared_fixture.py" restore "$prepared_fixture" \
            --repo "$repo_root" --build "$repo_root/OmniBlock.Client/bin/$configuration/net10.0")"
        run_root="$prepared_fixture/work"
    else
        run_root="$(mktemp -d "${TMPDIR:-/tmp}/omniblock-e2e.XXXXXX")"
        run_roots+=("$run_root")
    fi
    data_root="$run_root/data"
    game_data_dir="$data_root/OmniBlock"
    world_dir="$game_data_dir/saves/e2e-smoke"
    flat_world_dir="$game_data_dir/saves/e2e-flat"
    scenario_artifacts="$artifact_dir/$scenario"
    mkdir -p "$world_dir" "$flat_world_dir" "$scenario_artifacts"
    if [[ "$fixture_mode" == "prepare" ]]; then
        base64 --decode "$script_dir/fixtures/e2e-smoke/level.dat.base64" > "$world_dir/level.dat"
        base64 --decode "$script_dir/fixtures/e2e-flat/level.dat.base64" > "$flat_world_dir/level.dat"
    else
        echo "Reusing validated prepared baseline: $prepared_fixture"
        cp "$prepared_fixture/baseline/manifest.json" "$scenario_artifacts/prepared-fixture.json"
    fi
    echo "Running scenario: $scenario"
    launch_args=(--username OmniE2E)
    if [[ "$scenario" == "multiplayer" ]]; then
        launch_args+=(--token e2e-session)
    fi
    if [[ "$scenario" == "debug-smoke" ]]; then
        launch_args+=(--debug)
    fi

    # The measured terrain-LOD gate must consume a genuinely pregenerated persistent cache, not
    # records that remain resident in the process that created them. Prepare with one client/server
    # lifetime, close it, then run the benchmark below against the same isolated data directory.
    prepare_script="$script_dir/$scenario-prepare.luau"
    if [[ -f "$prepare_script" && "$fixture_mode" == "prepare" ]]; then
        prepare_artifacts="$scenario_artifacts/prepare"
        mkdir -p "$prepare_artifacts"
        set +e
        (
            cd "$repo_root/OmniBlock.Client"
            env XDG_DATA_HOME="$data_root" LUAU_NATIVE_LOCAL=1 dotnet run \
                --project . --configuration "$configuration" --no-launch-profile --no-build --no-restore -- \
                "${launch_args[@]}" --e2e-script "$prepare_script" \
                --e2e-timeout "$timeout_seconds" --e2e-artifacts "$prepare_artifacts"
        )
        prepare_status=$?
        set -e
        prepare_result="$prepare_artifacts/result.json"
        if [[ -f "$prepare_result" ]]; then
            prepare_exit_code="$(sed -n 's/^[[:space:]]*"exitCode":[[:space:]]*\([0-9][0-9]*\),\{0,1\}[[:space:]]*$/\1/p' "$prepare_result" | head -n 1)"
            if [[ -n "$prepare_exit_code" && "$prepare_status" == 0 ]]; then
                prepare_status="$prepare_exit_code"
            else
                prepare_status=1
            fi
        else
            prepare_status=1
        fi
        if (( prepare_status != 0 )); then
            echo "Scenario fixture preparation failed (exit $prepare_status)" >&2
            suite_status=1
            continue
        fi
        if [[ -n "$prepared_fixture" ]]; then
            python3 "$script_dir/prepared_fixture.py" seal "$prepared_fixture" \
                --repo "$repo_root" --build "$repo_root/OmniBlock.Client/bin/$configuration/net10.0" \
                --artifacts "$prepare_artifacts"
            cp "$prepared_fixture/baseline/manifest.json" "$scenario_artifacts/prepared-fixture.json"
            echo "Prepared baseline retained at: $prepared_fixture (subsequent runs restore it before measuring)"
        fi
    fi
    set +e
    (
        cd "$repo_root/OmniBlock.Client"
        env XDG_DATA_HOME="$data_root" LUAU_NATIVE_LOCAL=1 dotnet run \
            --project . --configuration "$configuration" --no-launch-profile --no-build --no-restore -- \
            "${launch_args[@]}" \
            --e2e-script "$script" --e2e-timeout "$timeout_seconds" \
            --e2e-artifacts "$scenario_artifacts"
    )
    status=$?
    set -e
    echo "Client process exit status: $status"

    if [[ -d "$game_data_dir/screenshots" ]]; then
        mkdir -p "$scenario_artifacts/screenshots"
        cp -a "$game_data_dir/screenshots/." "$scenario_artifacts/screenshots/"
    fi

    result_file="$scenario_artifacts/result.json"
    if [[ -f "$result_file" ]]; then
        artifact_exit_code="$(sed -n 's/^[[:space:]]*"exitCode":[[:space:]]*\([0-9][0-9]*\),\{0,1\}[[:space:]]*$/\1/p' "$result_file" | head -n 1)"
        if [[ -n "$artifact_exit_code" ]]; then
            status="$artifact_exit_code"
        else
            echo "E2E result has no valid exitCode: $result_file" >&2
            status=1
        fi
    else
        echo "E2E client produced no result artifact: $result_file" >&2
        status=1
    fi

    if [[ -n "$prepared_fixture" && "$status" == 0 ]]; then
        if ! python3 "$script_dir/prepared_fixture.py" verify "$prepared_fixture" \
            --repo "$repo_root" --build "$repo_root/OmniBlock.Client/bin/$configuration/net10.0" \
            --artifacts "$scenario_artifacts"; then
            status=1
        fi
    fi

    # This scenario needs a genuinely new process, not only a cleared in-memory registry.
    # Keep the same mktemp-owned data directory until the warm launch has verified disk reuse.
    if [[ "$scenario" == "entity-impostor-cache" && "$status" == 0 ]]; then
        warm_artifacts="$scenario_artifacts/warm"
        mkdir -p "$warm_artifacts"
        set +e
        (
            cd "$repo_root/OmniBlock.Client"
            env XDG_DATA_HOME="$data_root" LUAU_NATIVE_LOCAL=1 dotnet run \
                --project . --configuration "$configuration" --no-launch-profile --no-build --no-restore -- \
                "${launch_args[@]}" --e2e-script "$script_dir/entity-impostor-cache-warm.luau" \
                --e2e-timeout "$timeout_seconds" --e2e-artifacts "$warm_artifacts"
        )
        status=$?
        set -e
        if [[ -f "$warm_artifacts/result.json" ]]; then
            warm_exit_code="$(sed -n 's/^[[:space:]]*"exitCode":[[:space:]]*\([0-9][0-9]*\),\{0,1\}[[:space:]]*$/\1/p' "$warm_artifacts/result.json" | head -n 1)"
            if [[ -n "$warm_exit_code" && "$status" == 0 ]]; then status="$warm_exit_code"; else status=1; fi
        else
            status=1
        fi
        if [[ -d "$game_data_dir/screenshots" ]]; then
            mkdir -p "$warm_artifacts/screenshots"
            cp -a "$game_data_dir/screenshots/." "$warm_artifacts/screenshots/"
        fi
    fi

    if (( status == 0 )) && [[ "$scenario" == "terrain-lod-near-quality" ]]; then
        if ! python3 "$script_dir/check_near_quality.py" "$scenario_artifacts"; then
            status=1
        fi
    fi
    if (( status == 0 )) && [[ "$scenario" == "terrain-lod-remote-handoff" ]]; then
        if ! python3 "$script_dir/check_remote_handoff.py" "$scenario_artifacts"; then
            status=1
        fi
    fi
    if (( status == 0 )) && [[ "$scenario" == "terrain-lod-natural-cave" ]]; then
        if ! python3 "$script_dir/check_natural_cave.py" "$scenario_artifacts"; then
            status=1
        fi
    fi

    if (( status == 0 )); then
        echo "Scenario passed: $scenario"
    else
        echo "Scenario failed: $scenario (exit $status)" >&2
        suite_status=1
    fi
done

exit "$suite_status"
