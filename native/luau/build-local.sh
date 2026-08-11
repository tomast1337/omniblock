#!/usr/bin/env bash
# Local dev build of omniblock_luau (docs/luau-ffi-embedding-plan.md, Part 1.5).
#
# Bridges the gap before the GitHub Actions matrix + RID-packaged NuGet exist:
# builds the shared library locally and drops it next to OmniBlock.Luau's
# output so `dotnet run`/`dotnet test` can find it via a bare-name P/Invoke
# (e.g. [LibraryImport("omniblock_luau")]) without any packaging step.
# Re-run this after every `git submodule update` that bumps the pinned Luau
# commit, or after editing CMakeLists.txt/shim.cpp.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="$SCRIPT_DIR/build"
JOBS="$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo 4)"

# Only what's actually built by this repo's CMakeLists.txt for the local platform.
case "$(uname -s)" in
    Linux)  ARTIFACT_NAME="libomniblock_luau.so" ;;
    Darwin) ARTIFACT_NAME="libomniblock_luau.dylib" ;;
    *)
        echo "build-local.sh only targets Linux/macOS local dev; Windows uses the CI matrix (docs/luau-ffi-embedding-plan.md 1.3)." >&2
        exit 1
        ;;
esac

echo "Configuring ($BUILD_DIR)..."
cmake -S "$SCRIPT_DIR" -B "$BUILD_DIR" -G Ninja -DCMAKE_BUILD_TYPE=Debug

echo "Building omniblock_luau (-j$JOBS)..."
cmake --build "$BUILD_DIR" --config Debug --target omniblock_luau -j"$JOBS"

ARTIFACT_PATH="$(find "$BUILD_DIR" -name "$ARTIFACT_NAME" -print -quit)"
if [ -z "$ARTIFACT_PATH" ]; then
    echo "Build succeeded but $ARTIFACT_NAME wasn't found under $BUILD_DIR — CMakeLists.txt target/output-name changed?" >&2
    exit 1
fi

# OmniBlock.Luau doesn't exist yet (Part 2 of the plan) — create the directory
# now so this script needs no changes once it does; a stray directory under an
# unbuilt project's bin/ is harmless and dotnet build will use it as-is.
DEST_DIR="$SCRIPT_DIR/../../OmniBlock.Luau/bin/Debug/net10.0"
mkdir -p "$DEST_DIR"
cp "$ARTIFACT_PATH" "$DEST_DIR/"

echo "Copied $ARTIFACT_NAME -> $DEST_DIR/"

# Sanity-check the copy loads and exports symbols, independent of the real
# Luau API — catches a silently-broken WHOLE_ARCHIVE link (see CMakeLists.txt)
# before it becomes a confusing P/Invoke EntryPointNotFoundException later.
if command -v python3 >/dev/null 2>&1; then
    python3 - "$DEST_DIR/$ARTIFACT_NAME" <<'PYEOF'
import ctypes, sys
path = sys.argv[1]
lib = ctypes.CDLL(path)
lib.omniblock_luau_abi_version.restype = ctypes.c_int
version = lib.omniblock_luau_abi_version()
lib.lua_newstate  # raises AttributeError (unresolved symbol) if WHOLE_ARCHIVE didn't work
print(f"Sanity check OK: omniblock_luau_abi_version() = {version}, lua_newstate present.")
PYEOF
else
    echo "python3 not found, skipping load sanity check (build artifact is still copied)."
fi
