#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
backend="${E2E_DISPLAY_BACKEND:-xvfb}"

case "$backend" in
    xvfb)
        exec xvfb-run \
            --auto-servernum \
            --server-args="-screen 0 ${E2E_DISPLAY_SIZE:-1280x720x24}" \
            "$script_dir/run-local.sh" "$@"
        ;;
    headless)
        echo "The OmniBlock headless display backend has not been implemented yet." >&2
        echo "Keep the Luau scenario unchanged; replace this branch when the backend lands." >&2
        exit 2
        ;;
    *)
        echo "Unknown E2E display backend '$backend' (expected xvfb or headless)." >&2
        exit 2
        ;;
esac
