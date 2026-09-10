The client creates `OMNI` at runtime. VS Code needs `omni.d.luau` to understand
its members, parameters, and return types.

1. Install **Luau Language Server** (`JohnnyMorganz.luau-lsp`) in VS Code.
2. Open `OmniBlock.code-workspace` using **File > Open Workspace from File**.
   This opens the whole repository with the definitions configured.
3. Open a `.luau` script and type `OMNI.` for completion. If the workspace was
   already open, run **Luau: Reload Language Server** from the command palette.

For an existing workspace opened at the repository root, merge these settings
into its `.vscode/settings.json` instead:

```json
{
    "files.associations": { "*.luau": "luau" },
    "luau-lsp.platform.type": "standard",
    "luau-lsp.sourcemap.enabled": false,
    "luau-lsp.types.definitionFiles": {
        "@omniblock": "Scripting/omni.d.luau"
    }
}
```

The file's language mode should be **Luau**. These settings use the current
Luau Language Server format; update the extension if it expects an array for
`definitionFiles`.

The declarations are editor metadata: do not execute or `require` them. Keep
them in sync with `OmniBlock.Luau/Host` and
`OmniBlock.Client/Options/GameOptions.cs` when changing the host API. Nullable
results reflect runtime behavior: a selector can miss,
and `OMNI.test` exists only during explicit E2E launches. In strict scripts,
use `local test = assert(OMNI.test)` before calling `test.pass()`.

Configuration follows the language server's
[custom-environment setup](https://github.com/JohnnyMorganz/luau-lsp#with-other-editors).
