# AUI for VS Code

Language support for `.aui` view files and `.auis` stylesheets: syntax highlighting, the same
diagnostics the Unity importer reports, completion for tags, attributes, classes, translation
keys, icons and style properties, hover with every language's text for a key, go to definition
for keys, classes, variables and nested views, and a document outline.

Requires the .NET 8 runtime (or newer) on the PATH as `dotnet`, or `aui.dotnetPath` set.

## Build

```
cd tools~/vscode-aui
npm install
npm run package        # publishes the server into ./server, compiles, writes aui-0.1.0.vsix
code --install-extension aui-0.1.0.vsix
```

Open the world project folder (the one containing `Assets/` and `Packages/rest.armagan.aui`) or
the package repository itself. The server finds the theme and the language files from there.

## Smoke test without VS Code

```
node smoke.js <workspaceRoot> <path/to/View.aui>
```
