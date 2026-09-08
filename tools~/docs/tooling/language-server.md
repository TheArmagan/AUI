# The language server

The [VS Code extension](/tooling/vscode) bundles a language server, `aui-lsp`. It speaks LSP over
stdio and compiles the very same `Core/` sources the Unity importer uses, which is why its
diagnostics match the Unity console exactly rather than approximating it.

You do not need this page to use the extension. It matters if you want AUI's diagnostics in another
editor, or in a check step.

## Requirements

The .NET 8 runtime or newer, on the PATH as `dotnet`.

## Using it from another editor

Any LSP client can drive it. Point the client at the bundled server and register the two languages:

| Language id | Extension |
|---|---|
| `aui` | `.aui` |
| `auis` | `.auis` |

Launch it as `dotnet <path>/aui-lsp.dll`, communicating over stdio. The workspace root should be
the **world project folder**, the one containing `Assets/`.

The server discovers the project the same way the Unity editor does: it reads
`Assets/AUI/AuiSettings.asset` for the language folder, the default language and the component and
icon folders, and falls back to `Assets/AUI/Lang` or any folder called `lang`.

## Capabilities

Diagnostics, completion, hover, go to definition, find references, rename, code actions, document
links, colour provider, inlay hints, document symbols, workspace symbols, and semantic tokens.

Semantic tokens matter for embedded stylesheets: the server tokenises `style=""` attributes and
`<style>` blocks with a forgiving lexer that does not depend on the client's grammar, so they
colour correctly wherever you use it.

## Checking a project without an editor

The extension ships a smoke script that drives the server over stdio and exercises every
capability against a real view:

```
node smoke.js <workspaceRoot> <path/to/View.aui>
```

It exits `1` on a failure, so it works as a check step in a build script.

For validating the actual views in a project, Unity's own `AUI/Validate` is the better tool: it
compiles every view and prints every diagnostic, warnings included.

## Why the diagnostics always agree

`Core/` is a plain C# library with **no Unity references at all**, and both hosts compile the same
files: the Unity editor assembly includes them, and the language server project includes them by
path. There is no second implementation of the parser, the cascade or the layout solver to drift
out of sync.

The only difference is what each host supplies: the Unity host measures text with real TMP metrics
and records asset dependencies, while the server uses a heuristic measurer. So layout **warnings**
can differ slightly in the editor; everything else is identical.

## Next

- [The VS Code extension](/tooling/vscode) for the normal way to use this.
- [How it works](/guide/how-it-works) for the pipeline both hosts run.
