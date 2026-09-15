#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
artifact_dir="${E2E_ARTIFACTS_DIR:-$repo_root/artifacts/e2e-local/$(date -u +%Y%m%dT%H%M%SZ)}"
timeout_seconds="${E2E_TIMEOUT_SECONDS:-90}"
configuration="${CONFIGURATION:-Debug}"
requested_scenario="${1:-all}"
if [[ ( "$requested_scenario" == "chunk-visibility-baseline" || "$requested_scenario" == "terrain-lod-fixed-camera" || "$requested_scenario" == "entity-render-baseline" || "$requested_scenario" == "entity-lod-selection" || "$requested_scenario" == "entity-tracking-distance" || "$requested_scenario" == entity-impostor-* ) && -z "${E2E_TIMEOUT_SECONDS:-}" ]]; then
    timeout_seconds=300
fi
scenarios=(menu world-management multiplayer language-options create-world smoke debug-smoke fps-limit simulation-distance chunk-mesh-deadlines teleport-preload flying-chunk-streaming frustum-directional liquid-boundary-visibility world-generation-control terrain-lod-presentation)
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

    run_root="$(mktemp -d "${TMPDIR:-/tmp}/omniblock-e2e.XXXXXX")"
    run_roots+=("$run_root")
    data_root="$run_root/data"
    game_data_dir="$data_root/OmniBlock"
    world_dir="$game_data_dir/saves/e2e-smoke"
    flat_world_dir="$game_data_dir/saves/e2e-flat"
    scenario_artifacts="$artifact_dir/$scenario"
    mkdir -p "$world_dir" "$flat_world_dir" "$scenario_artifacts"
    base64 --decode "$script_dir/fixtures/e2e-smoke/level.dat.base64" > "$world_dir/level.dat"
    base64 --decode "$script_dir/fixtures/e2e-flat/level.dat.base64" > "$flat_world_dir/level.dat"
    echo "Running scenario: $scenario"
    launch_args=(--username OmniE2E)
    if [[ "$scenario" == "multiplayer" ]]; then
        launch_args+=(--token e2e-session)
    fi
    if [[ "$scenario" == "debug-smoke" ]]; then
        launch_args+=(--debug)
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

    if (( status == 0 )); then
        echo "Scenario passed: $scenario"
    else
        echo "Scenario failed: $scenario (exit $status)" >&2
        suite_status=1
    fi
done

exit "$suite_status"
