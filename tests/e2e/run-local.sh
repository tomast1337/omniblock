#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
run_root="$(mktemp -d "${TMPDIR:-/tmp}/omniblock-e2e.XXXXXX")"
data_root="$run_root/data"
game_data_dir="$data_root/OmniBlock"
world_dir="$game_data_dir/saves/e2e-smoke"
artifact_dir="${E2E_ARTIFACTS_DIR:-$repo_root/artifacts/e2e-local/$(date -u +%Y%m%dT%H%M%SZ)}"
timeout_seconds="${E2E_TIMEOUT_SECONDS:-90}"
configuration="${CONFIGURATION:-Debug}"

cleanup() {
    case "$run_root" in
        "${TMPDIR:-/tmp}"/omniblock-e2e.*) rm -rf -- "$run_root" ;;
    esac
}
trap cleanup EXIT

mkdir -p "$world_dir" "$artifact_dir"
base64 --decode "$script_dir/fixtures/e2e-smoke/level.dat.base64" > "$world_dir/level.dat"
touch "$game_data_dir/options.txt"

echo "Running OmniBlock client E2E smoke test"
echo "Artifacts: $artifact_dir"

set +e
(
    # AssetManager resolves source-checkout assets relative to the client project.
    cd "$repo_root/OmniBlock.Client"
    env \
        XDG_DATA_HOME="$data_root" \
        LUAU_NATIVE_LOCAL=1 \
        dotnet run \
            --project . \
            --configuration "$configuration" \
            --no-launch-profile \
            -- \
            --username OmniE2E \
            --e2e-script "$script_dir/smoke.luau" \
            --e2e-timeout "$timeout_seconds" \
            --e2e-artifacts "$artifact_dir"
)
status=$?
set -e

# Some desktop launchers do not propagate Environment.ExitCode from a WinExe.
# The E2E controller's artifact is the authoritative result in either case.
result_file="$artifact_dir/result.json"
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

if (( status == 0 )); then
    echo "E2E smoke test passed."
else
    echo "E2E smoke test failed with exit code $status. See $artifact_dir" >&2
fi

exit "$status"
