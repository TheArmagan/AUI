# AUI for VS Code

Language support for `.aui` view files and `.auis` stylesheets: syntax highlighting, the same
diagnostics the Unity importer reports, completion for tags, attributes, classes, translation
keys, icons and style properties, hover with every language's text for a key, go to definition
for keys, classes, variables and nested views, and a document outline.

Requires the .NET 8 runtime (or newer) on the PATH as `dotnet`, or `aui.dotnetPath` set.

## Install

```
cd tools~/vscode-aui
./install.ps1          # Windows (PowerShell)
sh install.sh          # macOS / Linux
```

Both build the server into `./server`, compile the client, package `aui-<version>.vsix` and run
`code --install-extension` on it. The manual steps are `npm install`, `npm run package`,
`code --install-extension aui-0.1.0.vsix`.

Open the world project folder (the one containing `Assets/` and `Packages/rest.armagan.aui`) or
the package repository itself. The server finds the theme and the language files from there.

## Smoke test without VS Code

```
node smoke.js <workspaceRoot> <path/to/View.aui>
```
