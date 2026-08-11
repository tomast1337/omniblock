// This file is part of OmniBlock, not the vendored Luau submodule.
//
// This translation unit exists to force the linker's hand. With LUAU_EXTERN_C=ON
// (see CMakeLists.txt) the vendored lua.h/lualib.h/luacode.h already declare every
// function we need with flat, unmangled C linkage — there is no per-function
// wrapping to do here. What a plain `add_library(omniblock_luau SHARED)` with no
// source referencing any of those functions would NOT do on its own is pull their
// object code out of the static libraries at all: WHOLE_ARCHIVE in CMakeLists.txt
// is what forces that. This file's includes exist so the shared library carries
// the vendored version headers as a build-time record of exactly which Luau
// surface it was compiled against.
#include <lua.h>
#include <lualib.h>
#include <luacode.h>

#if defined(_WIN32)
#define OMNIBLOCK_LUAU_API extern "C" __declspec(dllexport)
#else
#define OMNIBLOCK_LUAU_API extern "C" __attribute__((visibility("default")))
#endif

// Not part of the Luau C API. A fixed, always-present symbol the local dev
// script (build-local.sh) and later OmniBlock.Luau's native-library loader can
// probe for to confirm the shared library actually built and exports symbols
// correctly, independent of exercising the real Luau VM.
OMNIBLOCK_LUAU_API int omniblock_luau_abi_version(void)
{
    return 1;
}
