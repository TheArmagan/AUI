# AUI for VS Code

Language support for `.aui` view files and `.auis` stylesheets, backed by the same compiler the
Unity importer runs, so a squiggle here is the line the Unity console would print.

What it does:

- Diagnostics as you type, for views, stylesheets and the language files under `Lang/`. Shared
  files (the theme, a linked sheet, `en.json`) show the union of what every view found; "class
  not used" on a shared sheet only appears when no view uses it.
- Completion: tags and nested views, attributes per tag (the ones already written are left out),
  translation keys with their text, classes with the sheet they come from, icons, page ids,
  bindings, handlers from the view and from its `.cs`, `href` paths; stylesheet selectors,
  properties, values, `var(--x)`, sprites, eases. `</` completes the innermost open tag.
- Hover: a key with every language's text, a binding with its C# type and generated property, a
  handler with its signature and where it is defined, a class with its declarations, a variable
  with its value, a nested view with its bindings, element and property docs.
- Go to definition: key to each language file, class and variable to their rule, handler to its
  C# method, nested view to its file, `page=` to the page, icon to the SVG, `href` to the sheet.
- Find all references and rename for classes (every view and sheet), keys (every view and every
  language file), variables, and, within a view, bindings, handlers and ids. Works from inside a
  language file too.
- Quick fixes: "Did you mean" replacements, insert a missing `;`, create a missing key in the
  default language or in every language (the caret lands on the new value), add a key to the
  languages that lack it, create a missing nested view or stylesheet file, create a handler method
  in the view's `.cs`, remove an unused rule.
- Colour swatches and a picker for stylesheet colours, inlay hints with the default-language text
  after `key="..."`, links on `href` and `src`, document outline for views and sheets, workspace
  symbols for views, classes and keys.
- Auto-closing tags, Enter between tags indents, snippets (`view`, `panel`, `button`, `tabs`,
  `repeat`, `rule`, `state`, ...).
- `AUI: Show Generated C#` (editor title button) previews the class the view generates, refreshed
  as you type. `AUI: Restart Language Server` when something is off.
- File icons for `.aui` and `.auis` in the explorer and tabs (`icons/aui.svg`, `icons/auis.svg`).
  File icon themes that define their own association for an extension win, so the icons show with
  Seti and with any theme that leaves these extensions unmapped.

Requires the .NET 8 runtime (or newer) on the PATH as `dotnet`, or `aui.dotnetPath` set.

## Install

```
cd tools~/vscode-aui
./install.ps1          # Windows (PowerShell)
sh install.sh          # macOS / Linux
```

Both build the server into `./server`, compile the client, package `aui-<version>.vsix` and run
`code --install-extension` on it. The manual steps are `npm install`, `npm run package`,
`code --install-extension aui-0.2.0.vsix`.

Open the world project folder (the one containing `Assets/` and `Packages/rest.armagan.aui`) or
the package repository itself. The server reads `Assets/AUI/AuiSettings.asset` for the language
folder, default language, component and icon folders, the same way the Unity editor does; without
it, `Assets/AUI/Lang` or any folder called `lang`. A view opened outside the scanned tree (a sample
under `Samples~`) brings its own folder in.

## Settings

| Setting | Default | |
| --- | --- | --- |
| `aui.serverPath` | bundled | Path to `aui-lsp.dll` or a self-contained `aui-lsp` |
| `aui.dotnetPath` | `dotnet` | Host for the dll |
| `aui.autoClosingTags` | `true` | `>` inserts the closing tag, `</` completes it |
| `aui.inlayHints.translations` | `true` | Text after `key="..."` |
| `aui.trace.server` | `off` | Protocol trace in the output channel |

## Check without VS Code

```
npm run smoke                                   # the package's own samples
node smoke.js <workspaceRoot> <path/to/View.aui>  # a world project
```

Drives the server over stdio and checks every capability; exit code 1 on a failure.
