# OmniBlock-owned build for the vendored Luau submodule (native/luau/vendor/luau,
# pinned per docs/luau-ffi-embedding-plan.md Part 1.1). This file is NOT part of the
# Luau submodule; it configures Luau's own CMakeLists.txt and adds one target on top:
# omniblock_luau, a single shared library exporting the flat lua_*/luaL_*/luau_* C ABI
# that OmniBlock.Luau's [LibraryImport] boundary (Part 2) P/Invokes against.
cmake_minimum_required(VERSION 3.24) # $<LINK_LIBRARY:WHOLE_ARCHIVE,...> needs 3.24+
project(omniblock_luau LANGUAGES CXX C)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
# Luau's own CMakeLists doesn't set this; without it, linking the static libs
# below into our shared library fails/warns on non-Windows platforms.
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Configure the vendored Luau build before add_subdirectory pulls in its targets.
#   LUAU_BUILD_SHARED=OFF: we build ONE combined shared library ourselves (below),
#     not Luau's own per-module .so/.dll set (Luau.VM.so, Luau.Compiler.so, ...).
#   LUAU_EXTERN_C=ON: makes lua.h/lualib.h/luacode.h resolve LUA_API/LUACODE_API/
#     LUACODEGEN_API to `extern "C"` instead of plain C++ linkage, so the symbols
#     in the final shared library have flat, unmangled C names .NET can P/Invoke by.
set(LUAU_BUILD_CLI OFF CACHE BOOL "" FORCE)
set(LUAU_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(LUAU_BUILD_SHARED OFF CACHE BOOL "" FORCE)
set(LUAU_EXTERN_C ON CACHE BOOL "" FORCE)

add_subdirectory(vendor/luau)

add_library(omniblock_luau SHARED shim.cpp)

# Luau.VM, Luau.Compiler, Luau.CodeGen are the only static libs whose full C API
# surface we want preserved in the output — that's the VM (lua_*), the source
# compiler (luau_compile), and the native code generator. shim.cpp itself calls
# none of their functions, so a plain target_link_libraries would only pull in
# whichever object files satisfy SOME other reference and silently drop the rest
# of the API. WHOLE_ARCHIVE forces every object file from these three in, whole.
#
# Their own private dependencies (Luau.Ast, Luau.Bytecode, Luau.Common,
# Luau.VM.Internals) are deliberately NOT whole-archived: those are internal
# implementation detail, not part of the C ABI we expose, so ordinary
# undefined-symbol-driven linking (pull in only what VM/Compiler/CodeGen
# actually call) is correct and keeps the binary smaller.
target_link_libraries(omniblock_luau PRIVATE
    $<LINK_LIBRARY:WHOLE_ARCHIVE,Luau.VM>
    $<LINK_LIBRARY:WHOLE_ARCHIVE,Luau.Compiler>
    $<LINK_LIBRARY:WHOLE_ARCHIVE,Luau.CodeGen>
)

if(WIN32)
    # MSVC/MinGW DLLs export nothing by default even with extern "C" linkage
    # (unlike ELF/Mach-O shared objects, which export non-hidden symbols by
    # default). This is the modern CMake equivalent of hand-writing a .def file
    # enumerating every lua_*/luaL_*/luau_* symbol, and it doesn't need updating
    # when the vendored Luau version bumps and the symbol set changes.
    set_target_properties(omniblock_luau PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS ON)
endif()

set_target_properties(omniblock_luau PROPERTIES OUTPUT_NAME "omniblock_luau")
