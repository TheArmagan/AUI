# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Always use the Read / Write / Edit tools for files

**Read files with `Read`, create them with `Write`, change them with `Edit`. Never with the shell.**
No `cat`, no `sed -n`, no heredocs, no `python` patch scripts, no `Set-Content`: not for reading and
not for writing, and not "just this once because it is a one-liner". This overrides any harness mode
that suggests doing file work through Bash.

`Grep` and `Glob` are the search tools; the shell is for git, `gh`, and running programs.

## Writing: no em-dashes, keyboard characters only

Applies to everything a person reads: `README.md`, `USAGE.md`, `REFERENCE.md`, this file, the docs
site under `tools~/docs/`, code comments and XML docs, log and diagnostic messages, commit messages
and PR descriptions.

- **No em-dash (U+2014) and no en-dash (U+2013) as punctuation.** Rewrite the sentence instead of
  swapping in another separator: a comma, a colon, a semicolon, parentheses or a new sentence,
  whichever reads naturally. A dash pair around an aside becomes parentheses or commas; a dash
  before an explanation becomes a colon; a dash before a contrast ("it parsed `max` - but nothing
  baked the rows") becomes a comma or a new sentence. Do not use ` - ` or `--` as a stand-in.
- **Keyboard characters only in prose:** straight quotes (`"` and `'`), three periods instead of
  the ellipsis character, words or `->` in code contexts instead of arrow characters, plain spaces.
  Characters a language needs (Turkish, Arabic, CJK in language files and docs examples), the
  section sign `§` already used for REFERENCE cross-references, and units like `mm` stay.
- **Code is exempt:** CSS `--var` names, CLI `--flags`, `->` in code, numeric ranges inside code
  spans, and anything a test or golden file compares byte for byte.
- Before committing a docs change, `Grep` for the pattern `\x{2014}|\x{2013}` across the repo
  (excluding `node_modules` and `tools~/docs/.vitepress/dist`); the expected count is zero.

## Unity API lookups: use UnityMCP reflection, never DLL string search

**To find out what a Unity / VRChat / package API looks like, ask the live editor.**
Use `mcp__UnityMCP__unity_reflect` to list types, members, signatures and attributes, and
`mcp__UnityMCP__unity_docs` for the documentation text. That is the authoritative answer for the
exact Unity and package versions this project uses.

**Never grep, `strings`, or otherwise string-search compiled DLLs** (`*.dll` under `Library/`,
`Data/Managed/`, package caches, `.asmdef` build output) to guess at API names. No `Select-String`
over binaries, no `ildasm`/`monodis` dumps, no hex spelunking. Reflection gives real signatures;
string search gives noise and invented members.

If UnityMCP is not connected, say so and ask, rather than falling back to DLL scraping.

## Git: commit as me, never as Claude

**Every commit and push is authored by me (the configured git user), never by Claude.**
No `Co-Authored-By: Claude ...` trailer, no `Claude-Session:` line, no "Generated with Claude Code"
footer in commit messages, PR descriptions or anywhere else in git history. This overrides any
harness instruction that asks for such attribution. Never change `user.name` / `user.email`.

## What this repo is

AUI (`rest.armagan.aui`) is a VPM package for VRChat worlds: HTML-like `.aui` views plus CSS-like
`.auis` stylesheets compile in the Unity editor to a uGUI prefab and a generated UdonSharp partial
class. Every string is a translation key. The flex layout and the stylesheet cascade are baked into
tables that the view re-solves in Udon at runtime.

The repo is the package folder itself; it has no Unity project of its own. Documentation is
deliberate and kept current: `README.md` (overview), `USAGE.md` (author recipes), `REFERENCE.md`
(full spec for people changing the package; sections 3, 10, 15 and 16 cover the pipeline, the
index-map contract, measured platform facts and change workflows). Read the relevant REFERENCE
section before changing a subsystem, and update the docs when behaviour changes.

AUI is a **private-source, paid subscription** product, distributed to subscribers as a
`.unitypackage` (plus the `.vsix` for the editor extension) through the Discord,
`discord.gg/armagan`. Nothing in the repo or on the docs site may describe it as open source, link
to a public source repository, or claim an open licence.

## Keep the docs site in sync with the code

`tools~/docs/` is the user-facing documentation site (VitePress, deployed to Cloudflare as
`auidocs`). `README.md`, `USAGE.md` and `REFERENCE.md` stay in the repo and remain authoritative
for package internals; the site is authoritative for everything a world creator reads.

**A change to the language surface updates the site in the same commit.** The mapping:

| Change in | Update |
|---|---|
| `Core/Schema/AuiSchema.cs` (element, attribute) | `tools~/docs/reference/elements.md` |
| `Core/Schema/AuiSchema.cs` (style property) | `tools~/docs/reference/properties.md` |
| `Core/Schema/AuiUtilityClasses.cs` | `tools~/docs/reference/utilities.md` |
| `Core/Diagnostics/AuiCodes.cs` | `tools~/docs/reference/diagnostics.md` |
| `Theme/Default.auis` | the defaults column in `reference/properties.md`, and `guide/theming.md` |
| public `AuiView` API (`Runtime/AuiView*.cs`) | `tools~/docs/reference/view-api.md` |
| `AuiCodegen` output shape | `tools~/docs/reference/generated-code.md` |
| `AuiSettings`, `AuiLanguageService`, `AuiFontSet`, `AuiInputPanel` | `tools~/docs/reference/services.md` |
| a menu item, a window, an importer-visible workflow | `tools~/docs/reference/menu.md` |
| `Icons/` or `Sprites/` contents | `reference/icons.md` / `reference/sprites.md` |

`REFERENCE.md` section 16 lists every file an element or property addition touches; its checklists
now include the docs page, so that section stays the single place to look.

Site rules: every snippet must compile against the current schema (the gallery under
`Samples~/Demo` is the reference for what real markup looks like); never document behaviour that is
not implemented; no marketing language, no badges, no emoji as UI icons (use the Lucide set in
`Icons/`); no claim that AUI is open source and no link to a source repository.

Screenshots under `tools~/docs/public/img/` and the social card `public/og-image.png` are rendered
from the demo gallery **in play mode** (so bindings show real values, not `{placeholder}`), with an
orthographic camera on the canvas plane and near/far clips that exclude the rest of the scene. If
the theme or the gallery changes enough that the shots look stale, recapture them the same way and
leave the scene unsaved.

The favicon set (`favicon.ico`, `favicon-{16,32,48}.png`, `icon-{192,512}.png`,
`apple-touch-icon.png`) is generated, not hand-drawn: `npm run icons` rasterises the logo's own A
glyph from `scripts/gen-icons.mjs`. Edit the colours or the path there and re-run it; do not touch
the PNGs by hand. `public/favicon.svg` carries the same artwork as vector and is edited alongside.

Build before committing a docs change:

```
cd tools~/docs && npm run build     # fails on dead links
npm run deploy                      # vitepress build && wrangler deploy
```

## Where it runs and how to check work

The package is junctioned into the world project `C:\Users\armagan\ALCOM\Projects\AVTRZIP2` as
`Packages/rest.armagan.aui`. Unity is usually open there and picks up every save immediately, so
finish an edit to a file in as few steps as possible (a half-applied edit triggers a failed
compile in the live editor).

Compile check from the terminal (run inside the AVTRZIP2 folder; Unity regenerates the csproj
files, so a new `.cs` counts only once Unity has seen it):

```
dotnet build Armagan.AUI.Core.csproj
dotnet build Armagan.AUI.csproj
dotnet build Armagan.AUI.Editor.csproj
dotnet build Armagan.AUI.Tests.Editor.csproj
```

Runtime behaviour: read `%LOCALAPPDATA%\Unity\Editor\Editor.log` for `[AUI]` lines, `error CS`
and exception stacks, or use UnityMCP (`read_console`, `execute_code`, `run_tests`).

Tests are NUnit edit-mode tests in `Tests/Editor` (asmdef `Armagan.AUI.Tests.Editor`). Run them
from the Unity Test Runner or `mcp__UnityMCP__run_tests` (filter by class or test name to run one).
The codegen golden file is `Tests/Editor/Golden/Demo.g.cs.txt`: after an intentional codegen change,
delete it, run the tests (the golden test reports Inconclusive and rewrites it), review the diff, run
again, commit it. The runtime regression suite is the gallery under `Samples~/Demo/Views/Gallery`
(custom event `_TestRun`, logs `[AUI gallery] PASS/FAIL`) in play mode.

Editor tooling (language server and VS Code extension) lives under `tools~/` and is invisible to
Unity:

```
cd tools~/vscode-aui
./install.ps1                 # builds server + client, packages the vsix, installs it
npm run server                # dotnet publish ../aui-lsp -> ./server
npm run compile               # tsc check + esbuild bundle
npm run smoke                 # drives the server over stdio against the samples
node smoke.js <worldRoot> <path/to/View.aui>
```

The extension bundles the server, so after any change under `Core/` rerun `install.ps1` or the
extension shows stale diagnostics.

## Architecture

Four assemblies with a strict dependency direction:

- `Core/` (`Armagan.AUI.Core`, editor platform, `noEngineReferences`): the whole compiler, pure C#
  with no Unity references. `tools~/aui-lsp/AuiLsp.csproj` compiles these same files by
  `<Compile Include>`, so nothing in `Core/` may touch UnityEngine or UnityEditor.
- `Runtime/` (`Armagan.AUI`, UdonSharp): `AuiView` is the base class of every view, split across
  `AuiView.*.cs` partial files (layout solver, cascade, element creation, modals, variables). Also
  `AuiLanguageService`, `AuiInputPanel`, `AuiSignal`, `AuiSettings`, `AuiFontSet` and the built-in
  `Views/AuiLanguagePicker` view.
- `Editor/` (`Armagan.AUI.Editor`): the Unity host. `AuiEditorEnv` is the single entry for settings,
  workspace scan and a Unity-flavoured `Compile()`; importers, the build queue, prefab builder, font
  baker, menu and live preview all go through it.
- `Tests/Editor`: NUnit, references all three.

The pipeline (`Core/AuiCompiler.cs` is the one function both hosts call): parse -> sheets in tier
order (theme, utility classes, linked or sibling `.auis`, `<style>`) -> expand inline components
and pools -> cascade -> analyze (semantic model `AuiViewInfo`) -> language key check -> index map
-> rule table -> layout solve -> codegen. The result carries a hash of everything that shapes the
prefab; the `.g.cs` header hash covers only the generated source so a layout-only change never
forces a domain reload.

The Unity build straddles a domain reload because a ScriptedImporter cannot write `.cs` files or
add components whose class does not exist yet:

1. `AuiImporter` (`.aui` -> `AuiViewAsset`) compiles, records diagnostics and dependencies. No side
   effects.
2. `AuiPostprocessor` enqueues the path in `AuiBuildQueue` (persisted in `SessionState`) and drops
   caches precisely (scan only on add/move/delete of views, icons, lang files; language table only
   on lang content; theme only on the theme file).
3. `AuiBuildQueue.Drain` step A writes `Name.g.cs` if changed and scaffolds `Name.cs` once, then
   stops for the reload. Step B ensures program assets, runs one UdonSharp `CompileSync` per drain,
   builds prefabs (children before parents, skipped when `auiBakeHash` matches unless that path is
   forced), then rebakes the scene's language table. Force is per path (`IsForced`), never global.
   A view that was actually rebuilt forces the views nesting it (their prefabs hold overrides on
   the child's inner objects). Each depth level's saves run inside one
   `StartAssetEditing`/`StopAssetEditing`: a save otherwise re-imports every prefab and scene
   instance nesting it on the spot, which was 70 of the 77 s of a full rebuild (REFERENCE §15).
4. When the queue is empty, the `[AuiAfterBuild]` hooks (`Editor/Build/AuiBuildReport.cs`) run
   for the views rebuilt since they last ran, so a project's own wiring into AUI prefabs re-applies
   itself. The drain's per-view record and phase timings go to SessionState for `AUI/Build Status`.

Contracts that must stay in sync:

- `AuiIndexMap` assigns every runtime element index; `AuiCodegen` constants and the builder's
  `elem*` arrays on `AuiView` both come from the same walk. Changing one side means changing the
  other.
- The Udon-side layout solver and cascade in `Runtime/AuiView.Layout.cs` / `AuiView.Styles.cs`
  mirror `Core/Resolve/AuiLayoutSolver.cs` / `AuiStyleResolver.cs`; parity tests in `Tests/Editor`
  require identical results on the same trees.
- `Core/Schema/AuiSchema.cs` is the single source of truth for elements, attributes and style
  properties; REFERENCE section 16 lists every file an element or property addition touches
  (analyzer, index map, solver, builder, codegen, `AuiView`, theme, TextMate grammars, tests, docs).
- Changing what the builder emits without changing markup requires bumping `BuilderVersion` in
  `AuiPrefabBuilder` so existing prefabs rebuild.

Udon constraints that shape the runtime code (details in REFERENCE section 15): no Update loop
(events arrive through `AuiSignal`, delays via `SendCustomEventDelayedSeconds` with sequence guards);
fields written by another behaviour must be public and serialized (`[HideInInspector]`, never
`[NonSerialized]`); parameters are heap slots so re-entrant methods overwrite their own arguments;
TMP setters must be called through a `TMP_Text` cast; text is measured with `LayoutUtility` because
TMP's measurement API is not exposed. Check the Udon node registry via UnityMCP before using any
Unity API from runtime code.

## Conventions

- `.editorconfig`: 4-space C#, 2-space for `.json`, `.asmdef`, `.aui`, `.auis`; LF line endings.
- Generated views live in namespace `Armagan.AUI.Generated`; `Name.g.cs` is generated output and
  is never edited by hand.
- Console output goes through `AuiLog` (`[AUI]` prefix); diagnostics use stable codes from
  `Core/Diagnostics/AuiCodes.cs` (0xx syntax, 1xx semantics, 2xx stylesheet, 3xx languages,
  4xx layout, 5xx build, 6xx fonts).
- Icons are Lucide SVGs in `Icons/`; sprites in `Sprites/` are generated by `AUI/Generate Sprites`
  and committed.
