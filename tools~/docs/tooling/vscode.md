# The VS Code extension

Language support for `.aui` and `.auis`, backed by **the same compiler the Unity importer runs**.
A squiggle in VS Code is the line the Unity console would print.

<!-- SCREENSHOT: VS Code with a .aui file open, showing a diagnostic squiggle, the translation
     inlay hint after a key="..." attribute, and the completion popup listing translation keys
     with their English text. -->

## Install

You receive the extension as a `.vsix` alongside the package.

1. In VS Code, open the Extensions view.
2. From the `...` menu, choose **Install from VSIX**, and pick the file.
3. Reload when prompted.

Or from a terminal:

```
code --install-extension aui-0.5.1.vsix
```

**Requires the .NET 8 runtime** (or newer) on your PATH as `dotnet`. If it lives somewhere else,
set `aui.dotnetPath`.

## Open the right folder

Open the **world project folder**: the one containing `Assets/`. The server reads
`Assets/AUI/AuiSettings.asset` for the language folder, the default language and the component and
icon folders, exactly as the Unity editor does. Without a settings asset it falls back to
`Assets/AUI/Lang`, or any folder called `lang`.

A view opened outside that tree brings its own folder in, so a sample still resolves.

## What you get

### Diagnostics

Live, for views, stylesheets and language files. Every [diagnostic code](/reference/diagnostics)
appears here first.

Shared files (the theme, a linked sheet, `en.json`) show the union of what every view found, so
"class not used" on a shared sheet only appears when **no** view uses it.

### Completion

- tags, and nested views by name
- attributes for the current tag, with the ones you already wrote left out
- translation keys, **with their text**, so you pick by meaning rather than by name
- classes, labelled with the sheet they come from
- icons, page ids, bindings, handlers (from the view and from its `.cs`), `href` paths
- in stylesheets: selectors, properties, values, `var(--x)`, sprites, eases
- `</` completes the innermost open tag

### Hover

A key shows **every language's** text. A binding shows its C# type and the generated property. A
handler shows its signature and where it is defined. A class shows its declarations, a variable its
value, a nested view its bindings. Elements and properties show their documentation.

### Go to definition

A key jumps to each language file. A class or variable to its rule. A handler to its C# method. A
nested view to its file. `page=` to the page. An icon to the SVG. `href` to the sheet.

### Find references and rename

Works across the whole workspace: classes (every view and sheet), keys (every view **and** every
language file), variables, and per-view bindings, handlers and ids. Renaming from inside a language
file works too.

This is the feature that makes renaming a translation key painless.

### Quick fixes

- "did you mean" replacements for a misspelled tag, attribute, property, class or icon
- insert a missing `;`
- **create a missing key** in the default language, or in every language at once, with the caret
  landing on the new value
- add a key to just the languages that lack it
- create a missing nested view or stylesheet file
- **create a handler method** in the view's `.cs`
- remove an unused rule

### Editor extras

- colour swatches and a picker for stylesheet colours
- inlay hints showing the default-language text after `key="..."`
- document links on `href` and `src`
- document outline for views and sheets, workspace symbols for views, classes and keys
- auto-closing tags, indentation between tags, and snippets (`view`, `panel`, `button`, `tabs`,
  `repeat`, `rule`, `state`, ...)
- file icons for `.aui` and `.auis` in the explorer and tabs

## Commands

| Command | What it does |
|---|---|
| **AUI: Show Generated C#** | previews the class the view generates, refreshed as you type. Also a button in the editor title bar |
| **AUI: Restart Language Server** | when something is off |

*Show Generated C#* is the fastest way to answer "what is this binding called in C#" without
saving and switching to Unity.

## Settings

| Setting | Default | Meaning |
|---|---|---|
| `aui.serverPath` | bundled | path to `aui-lsp.dll`, or a self-contained `aui-lsp` |
| `aui.dotnetPath` | `dotnet` | host for the dll |
| `aui.autoClosingTags` | `true` | `>` inserts the closing tag, `</` completes it |
| `aui.inlayHints.translations` | `true` | show text after `key="..."` |
| `aui.trace.server` | `off` | protocol trace in the output channel |

## If it does not start

1. Check `dotnet --version` reports 8 or newer.
2. Run **AUI: Restart Language Server**.
3. Set `aui.trace.server` to `verbose` and read the AUI output channel.
4. Confirm you opened the project folder, not a subfolder of it.

## Next

- [The language server](/tooling/language-server) for running it outside VS Code.
- [Diagnostics](/reference/diagnostics) for what the squiggles mean.
