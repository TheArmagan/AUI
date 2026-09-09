# AUI reference

The complete description of the package for someone who has to change it. `README.md` is the
short version for someone who only wants to use it. Everything here was true at v0.1.0; where a
fact was measured rather than assumed, it says so.

Contents

1. What it is and what it is not
2. Repository map
3. The pipeline, end to end
4. Markup reference (.aui)
5. Stylesheet reference (.auis)
6. Layout model
7. Languages and fonts
8. Runtime: the classes and how they talk
9. Generated code: the contract with the author
10. Index map: how codegen and the builder agree
11. Prefab builder rules
12. Diagnostics
13. Editor tooling: language server and VS Code extension
14. Tests
15. Measured platform facts that shaped decisions
16. Workflows for common changes
17. Known issues
18. Not verified

---

## 1. What it is and what it is not

AUI turns an HTML-like view file and a CSS-like stylesheet into a Unity uGUI prefab with
UdonSharp behaviours on it. Every visible string is a translation key; the player's language
can change while the world runs and every live view repaints. All parsing, layout and styling
happens in the Unity editor. At runtime there is an array-indexed state machine and nothing else.

It is not a runtime UI framework: the markup never ships, no element is created in-world, and a
`<repeat>` bakes a fixed number of rows. It is not networked: every behaviour is
`BehaviourSyncMode.None` and nothing about a view goes on the wire. It is not a general layout
engine: a small flex subset, resolved once at compile time.

Package id `rest.armagan.aui`. Runtime namespace `Armagan.AUI`. Generated view classes always
live in `Armagan.AUI.Generated`. Editor code in `Armagan.AUI.Editor`. Compiler core in
`Armagan.AUI.Core`, which has no Unity references and is also compiled into the language server.

## 2. Repository map

```
package.json                  VPM manifest; vpmDependencies com.vrchat.worlds ^3.10.4
README.md  REFERENCE.md       this document and the short one
Runtime/                      asmdef Armagan.AUI (autoReferenced) + Armagan.AUI.U#.asset
  AuiView.cs                  base class of every view: tables, states, transitions, pages, repeats, selects, input, i18n
  AuiView.Styles.cs / .Layout.cs / .Create.cs / .Modal.cs / .Elements.cs / .Variables.cs  the runtime cascade, solver, templates, modals, selectors, :root variables
  AuiSignal.cs                per-element relay: uGUI UnityEvent -> view._OnSignal(id, kind)
  AuiLanguageService.cs       the flat language table, detection, persistence, fan-out, tween probe
  AuiInputPanel.cs            the invisible keyboard click target (port of the world's InputPanelManager)
  AuiInspectorBridge.cs       editor-only reflection hop so generated inspectors reach the editor assembly
  AuiSettings.cs              ScriptableObject: project configuration (lang folder, default language, fonts, ranges)
  AuiFontSet.cs               ScriptableObject: the baked font assets
  Views/AuiLanguagePicker.*   the built-in language picker view (aui, cs, g.cs, asset, prefab)
Core/                         asmdef Armagan.AUI.Core (editor platform, noEngineReferences)
  Schema/AuiSchema.cs         element, attribute, event and style-property tables; the single source of truth
  Parsing/AuiParser.cs        markup parser with positions and recovery
  Parsing/AuiMarkupDocument.cs  tree + lifted <style>/<link> + diagnostics
  Parsing/AuiStyleParser.cs   stylesheet parser
  Model/AuiNode.cs            element node
  Model/AuiStyleSheet.cs      rules, selectors, declarations, variables
  Model/AuiResolvedStyle.cs   cascade result per element with typed getters
  Model/AuiValues.cs          AuiColor, AuiLength, AuiBox, AuiBackground, AuiTransition, AuiNum
  Model/AuiViewInfo.cs        semantic model: bindings, handlers, repeats, page groups, selects, keys, nested views; AuiNaming
  Resolve/AuiStyleResolver.cs the cascade
  Resolve/AuiAnalyzer.cs      schema validation + semantic model
  Resolve/AuiIndexMap.cs      element and signal index assignment (see 10)
  Resolve/AuiLayout.cs        IAuiTextMeasurer, AuiApproxTextMeasurer, AuiRect, AuiLayoutNode, AuiLayoutContext
  Resolve/AuiLayoutSolver.cs  measure + layout passes
  Codegen/AuiCodegen.cs       Foo.g.cs emitter
  I18n/AuiJson.cs             flattening JSON reader with positions
  I18n/AuiLanguageTable.cs    AuiLanguageFile, AuiLanguageTable, key checks, placeholders
  Diagnostics/AuiDiagnostic.cs, AuiCodes.cs, AuiWorkspace.cs
  AuiCompiler.cs              the one pipeline both hosts call
Editor/                       asmdef Armagan.AUI.Editor
  AuiEditorEnv.cs             package root, settings lookup, workspace scan, Unity-flavoured Compile()
  AuiViewEditor.cs            the inspector drawer (static Draw) used by every generated per-view inspector
  Importers/AuiImporter.cs    ScriptedImporter for .aui (-> AuiViewAsset) and .auis (-> AuiStyleSheetAsset)
  Importers/AuiIconImportSettings.cs  forces Textured Sprite on package/consumer SVG icons; AUI/Reimport Icons
  Build/AuiBuildQueue.cs      AuiPostprocessor + the post-reload queue (SessionState), RebuildOne/EnqueueAll/DrainNow
  Build/AuiSourceWriter.cs    writes Foo.g.cs when changed, scaffolds Foo.cs once
  Build/AuiProgramAssets.cs   sibling .asset creation, sync-mode re-assertion
  Build/AuiPrefabBuilder.cs   the tree builder (see 11)
  Build/AuiLanguageTableBuilder.cs  bakes the table onto the scene's AuiLanguageService
  Build/AuiInputPanelBuilder.cs     authors the scene's AuiInputPanel
  Build/AuiSpriteGenerator.cs       Rounded/Border/Circle/Square PNGs, imported at 100 PPU
  Build/AuiMenu.cs            the AUI menu
  Build/AuiCreateMenu.cs      Project window Create/AUI items (.aui with name = typed stem, .auis)
  Fonts/AuiFontBaker.cs       charset collection + static atlas baking + fallbacks
  Preview/AuiPreview.cs       the live preview: file watchers, DontSave scene object, follow-the-edit
  Preview/AuiPreviewWindow.cs AUI/Preview window: view, language, pages, popups, diagnostics, build
  Resolve/AuiTextMeasurer.cs  TMP-backed IAuiTextMeasurer with a session-wide measurement cache
  Model/AuiViewAsset.cs, AuiStyleSheetAsset.cs
  Diagnostics/AuiLog.cs       "[AUI] " prefixed console output
Theme/Default.auis            the package theme (variables + every element's default look)
Sprites/                      generated 9-slice sprites (committed)
Icons/*.svg                   Lucide icons, white strokes, imported as Textured Sprite
Fonts/src/                    Inter TTF (4 weights), Noto Sans JP/KR/SC OTF (Regular, Bold); editor-only sources
lang/{en,tr,ja}.json          the package's own keys (aui.lang.*)
Samples~/Demo/                the demo view, its stylesheet, three language files, a README
Tests/Editor/                 NUnit edit-mode tests + Golden/Demo.g.cs.txt
tools~/aui-lsp/               .NET 8 language server; compiles Core/**/*.cs by <Compile Include>
tools~/vscode-aui/            VS Code extension; install.ps1 / install.sh build and install it
```

Consumer side (a world): `Assets/AUI/AuiSettings.asset`, `Assets/AUI/Lang/*.json`,
`Assets/AUI/Fonts/` (baked), views anywhere under `Assets/` (`Name.aui`, `Name.auis`, `Name.cs`,
generated `Name.g.cs`, `Name.asset`, `Name.prefab`), plus two scene objects named
`AUI Language Service` and `AUI Input Panel`.

## 3. The pipeline, end to end

```
Name.aui (+ Name.auis, lang/*.json, Theme/Default.auis)
   |  AuiImporter (ScriptedImporter)           pure: parse, cascade, analyze, layout, hash; records dependencies
   v
AuiViewAsset (hash, source text, diagnostics)  console shows [AUI] path:line:col code: message
   |  AuiPostprocessor.OnPostprocessAllAssets   enqueues the .aui path in SessionState
   v
AuiBuildQueue.Drain (delayCall / InitializeOnLoad / DrainNow)
   |  step A  AuiSourceWriter: write Name.g.cs if its text changed; scaffold Name.cs once
   |          -> if anything was written: AssetDatabase.Refresh, STOP; the queue resumes after the reload
   |  step B  AuiProgramAssets.Ensure(Name.cs) for EVERY queued view, then ONE UdonSharp CompileSync
   |          if any program asset is new or empty
   |          AuiPrefabBuilder.Build: skip when prefab's auiBakeHash == hash:BuilderVersion (unless forced)
   |  after   AuiLanguageTableBuilder.ApplyIfChanged: rebake the scene's service when its table differs (every drain)
   v
Name.prefab (root: Canvas + GraphicRaycaster + VRCUiShape + BoxCollider + the view; Content child; elements)
```

Cost: the importer logs `[AUI] import path: T ms` (with "cached compile" when the result was
reused) and the drain logs `[AUI] drain: N view(s), M built, T ms (scan, compile with K cached,
udonsharp, build)`. The first build of a view pays one UdonSharp compile (seconds) for the whole
drain, not one per view. The source header hash covers the generated source only, so a
layout-only change does not rewrite `Foo.g.cs` and does not force a reload; the prefab stamp
covers layout and styles too.

What is cached, and what drops it (`AuiEditorEnv`, `AuiPostprocessor`):

- The workspace scan (views, icons, language paths, theme path). Dropped only when a `.aui`,
  `.svg` or lang `.json` is added, deleted or moved, or `AuiSettings.asset` is imported. A
  re-saved view, any `.auis`, a font: nothing. `ScanFor` rescans when a view's own stem is
  missing (the importer runs before the postprocessor, so a just-created file is not listed yet).
  A lang `.json` that appears, goes or moves also queues EVERY view (non-forced): the key list is
  the sorted union across languages, so every baked key index may have shifted, and no view
  declared a file that did not exist. Two views with one stem are logged (AUI107); the scan keeps
  the last one it walked.
- The merged language table: dropped when a listed language file's content changes.
- The theme: dropped when `Theme/Default.auis` is imported.
- Compile results, per view: the result plus SHA-1 of every file that compile read (source,
  sheets, theme, language files, nested views, `<image src>` files) and the identity of the scan,
  table and theme it used, plus the font set's `bakedHash`. A lookup re-hashes those files; a hit
  returns the whole result (the builder compares node references, so results are never mixed).
  The importer's compile is what the drain uses: one compile per save instead of three. A domain
  reload empties it, which is a miss, nothing more. A font bake and an import under
  `Assets/AUI/Fonts` empty it too (`ForgetAllCompiles`): a rebake with the same charset keeps the
  stamp while the glyphs the rects were measured against are gone. A file the compile probed for
  and did not find (the sibling `Name.auis`, an unwritten `<link href>`) is declared as a
  dependency all the same, so creating it re-imports the view.
- Nested-view models (`ChildInfo`): reused while the child's text is unchanged.
- Text measurements (`AuiTextMeasurer`): one hidden TMP probe for the session and a static
  table keyed by font, size, wrap width and text. Cleared by `AUI/Bake Fonts` and when anything
  under `Assets/AUI/Fonts` is imported (a rebake keeps the asset's identity, so the id alone
  would not invalidate). The probe is destroyed before every assembly reload.
- Sprite lookups in the builder (package sprites, named sprites, icons): cleared when a `.png`
  or `.svg` is imported or deleted.

`AUI/Auto-build Views` (also in the preview window) pauses the drains the postprocessor starts;
saved views stay queued in `SessionState` and build when the toggle turns back on or an explicit
rebuild runs (menu, inspector button, preview window, font bake).

Live preview (`AuiPreview`, section 16 for the workflow): a `FileSystemWatcher` per folder the
previewed view reads from (its own, linked sheets, nested views, `Theme/`, the language folders,
`componentFolders`), events queued to the main thread and applied after 150 ms of quiet;
`AuiEditorEnv.Compile` from disk (no import); `AuiPrefabBuilder.BuildPreview` under a
`HideFlags.DontSave` root named `AUI Preview: Name`, placed at the first scene instance of
`Name.prefab` (hidden with `SceneVisibilityManager` meanwhile; instance ids in `SessionState` so
a reload or a crash never leaves it hidden) or 1.5 m in front of the scene camera. Preview mode
in the builder adds no `VRCUiShape`, collider, raycaster, view component, signal or Udon
component, never touches Undo or `SetDirty`, compiles nested views from source and builds them
inline (depth cap 8), can force a page per `pages` group and leave a select popup open
(`AuiPreviewOptions`), and draws the shown page's tab in its `:selected` values. Follow-the-edit
diffs each saved `.aui`/`.auis` against the last text, finds the deepest node covering the
changed lines (or the first element a changed rule's selector matches) and walks up: every
`page` ancestor becomes its group's shown page, an `option` opens its select, and a nested
view's host pages are selected in the parent. The preview is torn down on play mode entry,
scene close and quit, and rebuilt after a domain reload when live.

Why two phases: an importer cannot write a source file, and adding the view component needs the
class to exist and UdonSharp to have compiled it. Children (nested views) are built before parents;
a parent whose child prefab is missing stays queued (not counted as an attempt) and the child is
added to the queue at the end of the drain, after `Save`, which used to overwrite it. A path that
fails four times is dropped with AUI503 naming the step it was stuck on. `AuiBuildQueue.Forced` is
set by `AUI/Rebuild All Views` and rebuilds even when the hash matches. Bump
`AuiPrefabBuilder.BuilderVersion` whenever the builder's output changes without the markup
changing; it is part of the stamp.

The prefab hash (`AuiCompiler.ComputeHash`) covers the generated source, every layout rect (and
repeat instance rects, and the per-language width and height rows), every resolved style value
and state, the key list, the raw node tree and the whole runtime rule table. Anything the builder
copies onto the view and the hash does not cover never reaches an existing prefab; two such holes
shipped (a rule matching no resting element, a translation edit that did not move the widest
width). The `.g.cs` header hash is `Sha1` of the generated text alone.

## 4. Markup reference (.aui)

XML-strict: one root, every tag closed, every attribute quoted (double or single). Entities
`&lt; &gt; &amp; &quot; &apos; &nbsp; &#NN; &#xHH;`. Comments `<!-- -->`. A `<?xml?>` prolog is
skipped. `<style>` content is raw until `</style>` and becomes an inline sheet. `<link href/>`
is lifted out of the tree.

Literal text inside an element is trimmed and whitespace-collapsed and kept on the element as
`Text`; it is not a child node. Text and child elements can mix (`<button>Buy <icon/></button>`).

Root: `<view name="Foo" width="440" height="330" show="fade-scale" start-hidden="false">`.
`name` must equal the file stem and be a C# identifier. Width and height are millimetres.

Common attributes on every element: `id` (identifier; becomes `E_ID` / `S_ID` constants),
`class` (space separated), `style` (inline declarations), `if="{bool}"` (SetActive from a
binding; the binding defaults to true), `hidden` (baked inactive — **not** on an element with an
`icon=` or a `key=`; the baker does not lay out a hidden subtree and an icon's offset is written
once, so the glyph is stuck outside the button forever, see §15).

| Tag | Attributes | Children | Notes |
|---|---|---|---|
| `panel`, `div` | | any | plain container |
| `text` | `key`, `text="{s}"`, `args="{a} {b}"`, `wrap`, `max-lines`, `ellipsis`, `runtime` | none | exactly one of key / text / literal content; `args` only with `key`; `max-lines` is TMP's `maxVisibleLines` on a wrapped text; `runtime` measures a keyed label like a bound one (for `SetText` from code); `wrap` with no width wraps at the available width |
| `image` | `src` (relative to the .aui), `mode` simple/sliced/fit | none | src must import as a Sprite |
| `icon` | `name` (Lucide file stem), `size` | none | tinted by `icon-color` |
| `spacer` | `size` | none | grows when no size |
| `button` | `key`, `icon`, `disabled="{b}"`, `selected="{b}"`; events `onclick onhover onleave onpress onrelease` | any | |
| `toggle` | `bind="{b}"`, `key`, `icon`, `disabled`; events `onchange` + pointer | any | a Button with a selected state; the icon is the "on" mark |
| `slider` | `bind="{f}"`, `min`, `max`, `step` (>=1 means whole numbers), `format-key`, `decimals`, `disabled`; `onchange` | none | `format-key` text contains `{value}` |
| `input` | `bind="{s}"`, `placeholder-key`, `maxlength`, `disabled`; `onsubmit` | none | click arms AuiInputPanel with the element's `maxlength` (baked in `elemInputMax`); the panel cuts a longer submission |
| `select` | `bind="{i}"`, `max` (1..32), `disabled`; `onchange` | `option` | popup is baked; `max` bakes that many rows, the ones past the authored options starting inactive and empty for `SetSelectOptionLabel` to fill. The popup resizes to the rows that are showing. Not inside a `repeat`/`template` (nor `pages`/`tabs`): the index map adds the popup and scrim per node, not per row (AUI112) |
| `option` | `value`, `key` or literal | none | must be under `select`; `value` is a name for the reader and is not baked (the select reports the index) |
| `tabs` | `bind="{i}"`, `for` (pages id); `onchange` | `tab` | `for` is required when a view has more than one `pages` |
| `tab` | `page` (a page id), `key`, `icon` | any | must be under `tabs` |
| `pages` | `id`, `mode` crossfade/sequential, `default` (page id); `onchange` | `page` | one child visible at a time |
| `page` | `id` | any | must be under `pages`; gets a CanvasGroup and `page-transition` |
| `repeat` | `id`, `max` (1..64); `onselect` | one template + optional `empty` | template bindings are `{item.x}`; no nested repeats |
| `empty` | | any | shown when the repeat count is 0 |
| `modal` | `id`, `enabled` (`{bool}` or true/false), `dismissable`, `show`; `onopen`, `onclose` | any | a dialog over a dimming backdrop; direct child of `view`; out of the flow (theme centres it); backdrop click closes unless `dismissable="false"` |
| `template` | `id`, `max` (1..64) | one child (no `modal`, `repeat`, `template` inside) | `max` parked copies, baked last in element order and hidden; `CreateElement(T_ID, parent, index)` links one under any container, `DestroyElement` parks it; `{item.x}` bindings become `SetIdX(elem, value)`; direct child of `view` |
| `slot` | `id`, `views` (view names), `max` | none | sugar: one template per listed view holding a parked prefab copy; `CreateView(...)` / generated `CreateIdView(parent)` returns the `AuiView` |
| `prop` | `name`, `type` (string, int, float, bool; default string), `default` | none | declares a typed property of the view (a binding that exists even when unused) or of a `component`; lifted out of the tree |
| `event` | `name`, `type` (payload type or absent) | none | declares an event the view raises with `EmitString/EmitInt/EmitFloat/EmitBool`; a host's `on-name` handler takes the payload |
| `component` | `name` (capitalised) | one template child, plus `prop` declarations | an inline template; `<Name prop="v"/>` in the same file is replaced by a clone with `{prop}` substituted (literal or a passed-through `{binding}`); the usage's `id`, `class`, `style`, `if` merge onto the clone, inner ids get `usageId_` |
| `Name` | any attribute = the child view's binding; `on-<event>="Handler"` | none | nested view `Name.aui`; capitalised tag |

Bindings: `{name}` (view level) or `{item.name}` (inside a repeat template). Names are identifiers.
Types are inferred from use and must agree: `text`/`args`/input bind are string; `if`/`disabled`/
`selected`/toggle bind are bool; slider bind is float; select and tabs bind are int. A nested-view
attribute takes the child's type. Handler attributes name a method; signatures are derived (see 9).

Elements that get a pointer signal: every clickable tag, any element with a `:hover`/`:pressed`
rule or a pointer handler, and a repeat template root when the repeat has `onselect`.

## 5. Stylesheet reference (.auis)

```
:root { --name: value; }                 variables (only here)
selector[, selector] { property: value; ... }
```

Selectors: `tag`, `.class`, `tag.class`, any of those `:hover` / `:pressed` / `:selected` /
`:disabled`, `*`. No descendant or child combinators (error AUI205). `panel` and `div` match each
other. Comments `/* */` and `//`. A missing `;` before a line that looks like `name:` is tolerated
with a warning.

Selectors: `tag`, `.class`, `tag.class`, `*`, each with an optional `:hover` / `:pressed` /
`:selected` / `:disabled`; `:root` for variables; `modal::backdrop` (also `modal.x::backdrop`)
for the dimming scrim behind a modal; `.*name` for a star class (style-only: no `C_` constant,
not searchable, its own 256 budget). The built-in utilities (`Core/Schema/AuiUtilityClasses.cs`,
generated) are star classes in the Utility tier; a view bakes only the ones it references
(markup or `<view utilities="...">`).

Bound values: in a `style=""` attribute a declaration may be `{binding}` tokens only
(`width: {w}`, `offset: {x} {y}`, `padding: {v} {h}`); the resolver records them in
`AuiResolvedStyle.Bound`, the bake uses the default, and the generated setter calls
`SetStyleBindingF/C/S(elem, prop, index, value)` which writes an attribute-tier override the
runtime cascade applies last. Types by kind: Number/Offset float, Color/Background `Color`,
Length/Box/Enum string (parsed in Udon by `ParseLength` / `EnumOrdinal`). AUI213 for a mixed
or unbindable value; sheet rules cannot bind.

Cascade: tiers Theme < Utility < Linked < Inline (`<style>`) < Attribute (`style=""`); within a tier
specificity tag 1, class 2, tag.class 3, plus 10 with a state; later declaration wins ties.
Variables are collected from every `:root` in tier order (later wins) and substituted as
`var(--x)` or `var(--x, fallback)`; an undefined variable is an error. The closing paren is the
MATCHING one (a fallback may be `rgba(...)` or another `var()`, which is resolved in turn); a
`--variable` anywhere but `:root` is AUI200. Inheritance is limited to `color`, `font`,
`font-weight`, `font-size`, `text-align`, `line-height`, `icon-color`. The `font` shorthand
resets `font-size` and `font-weight` when it wins (as in CSS), so a later or more specific
`font: 500 18` beats an inherited `font-size: 15`; the getters prefer a longhand only when it was
declared after the shorthand. The Udon cascade does the same (`ResolveElem`).

Sheets a view uses: the package theme; then the `<link href>` sheets in order, or when there is
no link, the sibling `Name.auis` if it exists; then `<style>` blocks; then `style=""`.

Properties (unit mm unless stated):

| Property | Values | Notes |
|---|---|---|
| `width`, `height`, `min-*`, `max-*` | number, `Nmm`, `N%`, `auto` | percent against the parent's content box |
| `anchor` | none, fill, top, bottom, left, right, center | absolute placement in the parent; `fill` equals `position: absolute` with four zero edges |
| `display` | flex, none | `none` removes the element and the space it took (so does the `hidden` attribute) |
| `position` | static, absolute | `absolute` leaves the flow; place it with the edges below |
| `top`, `right`, `bottom`, `left` | number, `Nmm`, `N%` | edges from the parent's content box for `position: absolute`; negatives allowed; two opposite edges without a size stretch the element between them; a size wins over the opposite edge (AUI403) |
| `padding`, `margin` | 1 to 4 numbers (CSS order) | |
| `gap` | number | between flow children |
| `flex-direction` | row, column | default column |
| `justify` | start, center, end, space-between | main axis |
| `align` | start, center, end, stretch | cross axis; default stretch |
| `grow` | number | share of leftover main-axis space |
| `wrap` | true, false | row containers only |
| `color` | `#RGB`, `#RGBA`, `#RRGGBB`, `#RRGGBBAA`, `rgb()`, `rgba()`, transparent, white, black | text; animatable |
| `background` | none, color, `sprite(Name) color`, `material(Name) color`, `shader(Name) color` | animatable (color part); a sprite or material change is instant. `material(Name)` is a Material asset found by name; `shader(Name)` gets one generated material per shader under `Assets/AUI/Generated/Materials`. Either combines with `sprite()`; the sprite (or the radius sprite) stays the texture and the colour the vertex tint. Not valid in `color` |
| `border` | color or none | a Border child image; animatable |
| `radius` | number | picks Rounded4/8/12/16/24 |
| `opacity` | number | needs a CanvasGroup (added automatically); animatable |
| `scale` | number | animatable |
| `offset` | `x y` | state-only displacement; animatable |
| `icon-color` | color | animatable |
| `font` | `weight size` | weight: regular/medium/semibold/bold or 400..700 |
| `font-weight`, `font-size` | | |
| `text-align` | left, center, right | |
| `line-height` | number | multiplier |
| `transition` | `prop dur ease, ...` | dur `0.12s` or `120ms`; ease from the VRCTween list (`out-quad` default); `all` allowed |
| `page-transition` | none, fade, slide-left/right/up/down, scale | on `page` |
| `show` | none, fade, fade-scale, fade-slide | on `view`; the `show` attribute overrides |

State rules may only set `color`, `background`, `border`, `icon-color`, `opacity`, `scale`,
`offset` (error AUI210 otherwise). At runtime the state precedence is disabled > pressed > selected
> hover > normal, and the normal values are re-applied before a state's declarations so leaving a
state restores every property.

### The cascade at runtime

The stylesheet is baked twice: into the prefab (the look at rest) and into flat tables on the
view (`AuiRuleTable`: every rule of every sheet plus every `style=""`, pre-sorted by tier,
specificity and order; declarations typed as float4 plus a reference; per-element tag, node and
class masks). `Runtime/AuiView.Styles.cs` re-resolves an element from those tables whenever its
classes change: inherit from the parent, then let each matching rule of the tag bucket overwrite,
which is the compile-time cascade without sorting. Only the properties whose value changed are
applied, through the element's own `transition`, and the element's descendants are re-resolved
so inherited properties follow. State rules are collected per element and applied on top as
before.

Classes are 64-bit masks per element (two ints; more than 64 classes visible to one view is
error AUI211): the static mask from the markup, one mask per `{binding}` in the class attribute,
a mask for `AddClass`, and a mask for `RemoveClass` of a static or bound class. The attribute
mixes both: `class="card {tone} {extra}"`, where each binding is a string of space-separated
names set through its generated property. API on every view: `ClassId(name)`,
`AddClass/RemoveClass/ToggleClass/HasClass(elem, classId)`, the `...Named(elem, name)` variants,
`SetClasses(elem, "a b")` for the API mask, and generated `C_NAME` constants for every class any
sheet of the view defines (so a class used only at runtime still needs a rule somewhere).

Runtime creation (`Runtime/AuiView.Create.cs`): a `<template>` is an `AuiRepeatInfo` with
`IsTemplate`; `AuiPoolSynthesizer` turns `<view pool="text:16">` and `<slot>` into templates
(`PoolTag`, `SlotId`/`SlotView`). The index map walks templates after everything else so a
parked instance's index is higher than any parent it may get; the solver's pre-order rule
survives reparenting. The layout tables keep the template and its instance roots hidden;
`CreateElement` picks a free row, moves the instance's GameObject under the parent, links it into
the mutable `layParent/layFirstChild/layNextSibling` arrays at the index, shows it, re-resolves
its cascade and marks the layout dirty. `DestroyElement` unlinks, hides and parks it under the
template's holder object. `MoveElement` relinks. `CreateView` does the same for a slot's parked
prefab copy and returns its `AuiView` (`elemChildView`). Class changes on a parent re-resolve
the created instances linked below it too. Tables: `repeatIsTemplate`, `repeatElem`,
`poolTag/poolTemplate`, `slotId/slotView/slotTemplate`. Codegen: `T_ID`, `CreateId(parent)`,
`SetIdX(elem, value)` (row from `InstanceRow`), `CreateSlotView(parent)`; a view inside a template
has no single generated field and its `on-x` handlers receive the instance element first.

Components and props: an inline `<component>` is expanded by `Core/Resolve/AuiTemplateExpander.cs`
right after parsing (before the cascade), so its clones are ordinary elements; nothing of it
exists at runtime. A cross-file nested view declares `<prop>`s, which the analyzer turns into
typed bindings with defaults (`AuiBinding.IsProp`, `Default`); a host that names an undeclared
prop on a child that declares any gets error AUI121, and a bound prop takes the child's type.
`<event name type>` declarations give the host's `on-name` handler a typed parameter; the child
calls `EmitString/EmitInt/EmitFloat/EmitBool(name, value)`, which parks the value in the public
`emitS/emitI/emitF/emitB` fields before `_OnChildEvent`, and the generated `_OnChildEvent`
passes `child.emitX` to the handler. Payloads cross the behaviour boundary through those
exported fields, the same rule as `inputResult`.

Modals (`Runtime/AuiView.Modal.cs`): `<modal id enabled dismissable show>` with `onopen` /
`onclose`, a direct child of `<view>`, out of the flow (the theme anchors it centre). The index
map bakes a `KIND_MODAL_SCRIM` element (full-size image, own signal, CanvasGroup) right before
the body; the builder appends both to the popups so they are the last children of Content, and
bakes them inactive unless `enabled="true"` (or the preview shows them). Runtime tables
`modalElem/modalScrimElem/modalScrimSig/modalKind/modalDismissable/modalStartOpen`;
`SetModal(m, open, notify)`, generated `OpenX/CloseX/IsXOpen` and `M_X`, `IsModalOpen`,
`TopModal`, `Back()` (select first, then the top dismissable modal), `_OnModalChanged` override
(binding + handlers). Opening re-sends scrim and body to the end of Content (last opened on
top), fades the scrim and runs the body's `show` transition; a `_ModalSnap` deactivates closed
ones after the tween. The backdrop is styled through the `modal::backdrop` pseudo-element
(`AuiSelector.Pseudo`, resolved into `AuiResolvedStyle.Backdrop`; background and opacity are
read).

Selectors and element ops (`Runtime/AuiView.Elements.cs`): `GetElement("#id")` (baked
`idNames`/`idElem`, row 0 of a repeat), `GetElements(".class" | "tag" | "#id")`, `SetText`
(escaped, re-measured), `GetText`, `SetIcon` (from `iconNames`/`iconSprites`: every icon the
view uses plus `<view icons="...">`), `SetBackground` (from the rule table's sprite refs plus
`<view sprites="...">`), `GetRect`, `GetObject`, `GetBackgroundImage`, `IsElementShown`. An element
with an id is built with a CanvasGroup so `SetOpacity` works on it.

Runtime-changeable: every property except `radius`, `font-weight`, the weight half of `font`,
`page-transition` and `show` (marked BakeOnly in the schema). Udon cannot swap a text's font
asset, so a class that sets a weight and is not used literally in the markup gets warning AUI212;
its font size still applies. Layout properties changed by a class write the layout tables and
schedule a solve; `font-size` also re-measures the text. `SetStyleValue(elem, PROP_X, value)` and
the `SetOpacity/SetColor/SetBackgroundColor/SetIconColor/SetScale/SetFontSize/SetTextAlign`
helpers write one property directly, until the next class change on that element.

Variables at runtime (`Runtime/AuiView.Variables.cs`): the rule table carries the merged `:root`
variables (`VarNames` sorted, `VarValues`) and, per declaration, `DeclTemplate`: the raw value when
it contains `var()`, else null (the builder bakes null as `""`). `SetStyleVariable(name, value)`
(name with or without `--`, value as stylesheet text) rewrites `varValues` and schedules one
flush for the next frame (`_AuiFlushStyleVariables`; `FlushStyleVariables()` runs it now), so a
preset writing ten variables or a slider writing one per frame both cost one pass per view. The
flush re-substitutes only the templated declarations that read a changed variable (an index of
`var()` names per declaration, built on the first flush; `SubstituteVars`, the same loop as
`AuiRuleTable.Substitute`), re-encodes it in Udon (`EncodeStyleValue`, the runtime half of
`AuiRuleTable.Encode`: colours through `ParseColor`, lengths through `ParseLength`, enums through
`EnumOrdinal`; the sprite/material halves of a background are their own declarations and are
skipped) into `declValue`, marking the declarations and properties that changed, then
`ApplyChangedDecls` walks the elements once without the cascade: `_resolvedSrc` remembers where
every resolved value came from (a declaration index, `SRC_INHERITED`, an attribute override, or a
direct `SetStyleValue`), so a property is updated only when its source declaration changed or, for
an inherited one, when the parent's value did (parents precede children in element order). Changes
go through each element's transition unless the previous flush was less than a quarter second ago
(a dragged slider snaps instead of restarting a tween every frame); an element with state rules re-applies its state when one of
their declarations changed. The call is forwarded to every `elemChildView`. `SetStyleVariableColor`
writes `#RRGGBBAA`, `SetStyleVariableFloat` a locale-free number; `GetStyleVariable` reads the
current text at once (the look follows a frame later). A value that fails to encode leaves that
declaration and logs once. `AuiLanguageService.SetStyleVariable` remembers the value and calls
`_ApplyStyleVariable` (this view only, quiet) on every registered view; a view's `_AuiStart` reads
the remembered list back (`StyleVariableCount/Name/Value`) right after registering and flushes it
once, instantly (`_instantResolve`), so late starters match. `modal::backdrop` rules never enter
the table (they would read as plain `modal` rules and paint the body with the scrim colour), and a
node-less element (scrim, popup) hangs off the root in `ElemSubtreeEnd`, so the root's subtree
reaches the modal body baked after the scrim.

Eases: linear, in/out/in-out quad, cubic, quart, sine, expo, back, bounce, elastic, written
`out-quad`, `in-out-back`, etc. The builder maps them to `VRCTweenEase` by name.

## 6. Layout model

`AuiLayoutSolver`, pure C#, two passes.

Measure (bottom-up): text is measured with the host's `IAuiTextMeasurer` over every translation of
its key (widest wins) plus 2 mm; a bound text is measured with the settings' sample
(`boundTextSample`, default eleven `M` plus one CJK character, so the box is as tall as the Noto
fallback's line; a box shorter than a line renders nothing under Ellipsis) unless it has
`max-width`; icons take
`size`; images take their sprite size or 64; containers sum children on the main axis (plus gaps
and padding) and take the max on the cross axis; a button/tab/option with `key` measures its widest
translation plus icon and padding; a select measures its widest option plus the chevron; a repeat
reserves `max` copies (in a row, one line; the layout pass wraps). Explicit non-percent width and
height override; min/max clamp.

Layout (top-down): each child gets a rect. Grow shares leftover; justify places the rest; align
positions on the cross axis (stretch fills unless the child has an explicit size). On overflow,
text and content-sized children shrink proportionally (floor 24 mm) before a warning AUI400 is
raised. `wrap: true` rows break into lines. Out-of-flow children (`display: none`, `hidden`,
`position: absolute`, `anchor`) take no space; `display: none` and `hidden` are not laid out at
all — and neither is anything inside them, which is what makes a baked-hidden icon permanent
(§15) — the others go through one placement routine (`LayoutAbsolute`): an explicit size wins, two
opposite edges stretch, otherwise the intrinsic size; a set edge pins that side, otherwise the
anchor centres. `pages` children fill. A repeat lays out instance 0 and records `Instances[]`
offsets for the clones; a column repeat stretches rows to its width; with zero rows the box is
the size of its `<empty>`. Select options are stacked below the header at its width (the builder
moves them into the popup).

Output per node: `Local` (relative to parent top-left, y down), `Absolute`, `AnchorFill`,
`Hidden`, `OutOfFlow`, `ContentSized`, `Content` box, `Instances` for repeats, and the raw
measurements the runtime solver starts from (`RawW/H`, per-language `LangW/H` for text, per-language
`ContentW/H` for a container's own label).

Baking: anchors top-left, pivot centre (scale tweens grow from the middle), `anchoredPosition =
(x + w/2, -(y + h/2))`, `sizeDelta = (w, h)` for every element, including fills and bound texts.

### The runtime solver

The same layout runs again in-world. `AuiLayoutTables.Build` flattens the compile result into
arrays indexed by runtime element index (parent, first child, next sibling, kind, flags, enums,
padding, margin, sizes as (kind, value) pairs, edges, intrinsic sizes, per-language text and label
sizes) and the builder copies them onto the view (`lay*` fields). `Runtime/AuiView.Layout.cs` is a
port of the solver over those arrays: no recursion (indices are pre-order, so measure is a reverse
loop and place a forward loop), no allocation per solve, one solve per frame.

What triggers a solve: a text written through `SetElemText` (every generated text setter and
`SetText` use it), `SetActiveElem` / `SetDisplay`, `SetRepeatCount`, a page entering, a language
change (`_Retranslate` swaps in that language's baked text sizes), `SetPosition`, `SetSize`, a
nested view's host giving it a new size. Setters call `MarkLayoutDirty()`, which schedules
`_AuiSolveLayout` one frame later with `SendCustomEventDelayedFrames`; `Relayout()` /
`RelayoutNow()` solve at once; `SetAutoRelayout(false)` parks the flag so a batch of changes
solves once on `Relayout()` or `SetAutoRelayout(true)`. `_AuiStart` ends with a synchronous solve,
so the initial binding values and the player's language are laid out before the first frame.

Text a setter changed is measured through `LayoutUtility.GetPreferredWidth/Height` on the label's
RectTransform (TMP's own `GetPreferredValues` is not exposed to Udon; `TextMeshProUGUI` implements
`ILayoutElement`, so `LayoutUtility` asks it). A wrapped text is first given the width it wraps at
(`max-width` / `width`, else the width of the last solve) and rebuilt with
`LayoutRebuilder.ForceRebuildLayoutImmediate`. An inactive text (on a hidden page) keeps its dirty
bit and is measured on the solve that follows its activation. Untouched text uses the baked size
of the current language, so a view with no runtime text never measures anything. A *keyed* label
inside an element baked `hidden` is the case that does not recover on its own until something
wakes it: it is measured at zero and the clickable around it collapses to its padding (§15).

Nested views: the host reserves the child's declared `<view width height>` (the bake's size),
writes the solved rect and calls `_AuiSetLayoutSize` on the child, which re-solves its own tree
at that size. Select popups follow their header (below it, or above when there is no room,
clamped into the view, the builder's rule). `debugLayout` on a view logs every solve with its
time. `Tests/Editor/AuiLayoutParityTests.cs` runs the Core solver and the runtime solver on the
same fixtures and asserts every rect matches, also at runtime repeat counts.

## 7. Languages and fonts

Files: `Assets/AUI/Lang/<code>.json`, nested objects flattened to dotted keys, `_name` is the
native display name. Arrays and nulls are errors; numbers and booleans become their text. The
languages a project ships are exactly its files; the package's `lang/` contributes `aui.lang.*`
only for those codes (or all of its languages when the project has none).

Fonts (`AuiFontBaker`): the charset is every code point (surrogate pairs combined, so emoji and
CJK Extension B count) of every language string, every literal text, `extraCharacters`,
`boundTextSample` and the `unicodeRanges` (`0400-04FF`; `U+` and `0x` prefixes are stripped, a
range that does not parse is logged and skipped). Static Inter atlases per weight fall through
Noto JP/KR/SC, then a capped dynamic Inter atlas (`dynamicLatin`), then a capped dynamic Noto JP
atlas (`dynamicCjk`) for runtime strings. `bakedHash` is the charset hash; the language table
builder compares it against a fresh collection whenever it rebakes the scene's service and warns
AUI600 once per distinct charset.

`AuiLanguageTable` merges everything: sorted codes, sorted keys, `Flat()` = `values[lang * keyCount
+ key]`, `""` for missing. `Check()` raises AUI303 unknown key, AUI301 missing in the default
language (error), AUI300 missing in some languages (warning, listing them), AUI307 placeholder
and args mismatches. `{value}` on a slider's format key is exempt.

At bake the builder writes each text's key INDEX into `elemKey`, and `AUI/Rebuild Language Table`
writes the same table onto the scene's `AuiLanguageService`. Both come from the same files; after
editing a language file, both re-run (the importer depends on every language file; the queue calls
the table builder). Adding a key shifts indices, so every view rebuilds; that is by design.

Fonts (`AuiFontBaker`): the charset is the union of every language value, `AuiSettings.extraCharacters`,
and the `unicodeRanges` list (defaults cover Latin, Latin Extended, Greek, Cyrillic, punctuation,
currency, arrows, CJK punctuation, fullwidth forms, and U+200B). For each Inter weight a dynamic
atlas is created, `TryAddCharacters` is called with the whole set, the missing characters go to
Noto JP, then KR, then SC, and every atlas is then switched to Static and saved under
`Assets/AUI/Fonts/` with its material and texture as sub-assets (GUIDs preserved on rebake).
Fallback chain per weight: Noto atlases, then a dynamic Inter atlas (1024, single texture) and,
optionally, a dynamic Noto JP atlas for runtime strings such as player names. The result is
`AuiFontSet.asset`, assigned to `AuiSettings.fontSet`; `bakedHash` lets the importer warn
(AUI600) when the charset changed. Views rebuild after a bake.

## 8. Runtime: the classes and how they talk

All `[UdonBehaviourSyncMode(BehaviourSyncMode.None)]`. Nothing runs in `Update` except
`AuiInputPanel` while armed.

`AuiView` (base of every view). Baked, hidden fields: `lang`, `inputPanel`, `content`,
`contentGroup`, `contentRect`, `auiBakeHash`; element tables indexed by runtime element index:
`elemObjects`, `elemRects`, `elemGroups`, `elemBg`, `elemBorder`, `elemIcon`, `elemText`,
`elemButtons`, `elemSliders`, `elemBasePos`, `elemSignal`, `elemKey`, `elemKind`; signal tables
`sigElem`, `signals`; style rows `styleRangeStart/Count[sig * 5 + state]`, `styleProp`,
`styleValue`, `styleSprite`, `styleDur`, `styleEase`, `styleChannel`, `channelCount`; view
transition `showKind`, `showDur`, `hideDur`, `slideFrom`, `scaleFrom`, `startHidden`; pages
`pageGroupStart/Count/Mode/Default`, `pageElem`, `pageTabSig`, `pageKind`, `pageDur`; repeats
`repeatFirst`, `repeatStride`, `repeatMax`, `repeatEmpty`; selects `selectElem`,
`selectPopupElem`, `selectOptionFirst`, `selectOptionCount` (capacity: the authored options plus
the rows `max` reserved), `selectOptionShown` (how many were baked active), `selectOptionH`,
`selectPopupUp`, `selectPopupEdge` (one row's height and the edge a popup keeps while it
resizes), `selectScrimSig`; `inputResult`,
`inputResultId` (written by AuiInputPanel); `parentView`.

Every public entry that writes starts the view first (`EnsureStarted` or the stash prologue);
`ShowInstant`, `HideInstant`, `SetActiveElem`, `CloseSelect` and `SetText` included, since
`_AuiLayoutInit` would otherwise reset `_shown` from the baked flags over a pre-start write.
`_AuiStart` allocates every runtime array before validating the tables, so a view whose tables
are wrong (`_broken`) still answers a parent's calls instead of halting its start chain.
`OnRetranslate` runs only after `OnStart` returned (`_startDone`), never for the start-up paint.

Public API: `Show()`, `Hide()`, `ShowInstant()`, `HideInstant()`, `Toggle()`, `IsVisible()`;
`_OnSignal(id, kind)`; `SetSelected(id, on)`, `IsSelected(id)`, `SetEnabled(id, on)`,
`SetEnabledElem(elem, on)`, `SetActiveElem(elem, on)`; `GetToggle(elem)`, `SetToggle(elem, on)`;
`GetPage(group)`, `SelectPage(group, page, notify)`; `GetRepeatCount(r)`, `SetRepeatCount(r, n)`,
`RowElem(r, row, offset)` (-1 for a bad index, never an exception); `GetSelect(s)`,
`SetSelect(s, i)`, `SetSelectOptionLabel(s, i, label)`, `SetSelectOptionCount(s, n)`,
`CloseSelect()`; `Escape(s)`, `Fill1..Fill4`, `FormatNumber(v, decimals)` (culture-free: a `.`
whatever the client's locale, so it can feed a style binding), `InputMaxLength(elem)`;
`_Retranslate()`; `SetStyleVariable(name, value)`, `SetStyleVariableColor(name, c)`,
`SetStyleVariableFloat(name, f)`, `GetStyleVariable(name)`, `StyleVariableId(name)` (section 5);
`WarnOnce(key, message)` for author code that would otherwise log every frame.

Snaps are guarded by due time, not by sequence number: `_PageSnap` and `_ModalSnap` take no
arguments, so `SelectPage` and `SetModal` record `Time.time + delay` per group / per modal and a
snap that fires before that time leaves the group alone (a newer change is still animating and
its own snap follows). In sequential page mode the snap is scheduled after BOTH durations, the
exit and the enter; at one duration it fired 20 ms into the enter fade and snapped the page to
full alpha over a tween still climbing from zero.

Author hooks (override in your `.cs`): `OnStart()` after wiring and initial values,
`OnRetranslate()` after a language switch, `OnShown()`, `OnHidden()`. `Emit("name")` in a nested
view runs the parent's `on-name` handler through `_OnChildEvent`.

Start sequence (`_AuiStart`): validate every table's length (arrays from the heap are empty, not
null; a mismatch logs and disables the view), find `AUI Language Service` and `AUI Input Panel`
by name when the fields are null (a prefab cannot reference scene objects), register with the
service, `_Retranslate`, apply normal-state rows to every signal, hide every page and select the
defaults, render selects, show or hide, generated `_AfterStart`, then `OnStart`.

Transitions: `Enter`/`Exit` build up to three tweens (fade, scale, slide) with VRCTween, only when
the service's probe says tweens run; otherwise the final state is applied instantly. Every
view-level transition is trailed by a sequence-guarded `_SnapView` delayed event that applies the
final state and deactivates the content when hidden, so a stalled tween can never leave a view
half-faded. The behaviour's own object is never deactivated; only `content` and pages are, and
only in a snap step.

Pages: `SelectPage` deselects the old tab, selects the new, exits the old page, enters the new
(crossfade) or waits for the exit (sequential, via `_PageEnterPending`), then `_PageSnap`
deactivates the others.

Selects: the header click toggles the popup (`Enter` on its CanvasGroup); the popup is a nested
canvas with sorting order 100 and its own raycaster, positioned inside the view (above the header
when there is no room below); a scrim inside it closes on a click elsewhere; an option click sets
the value, renders the header label from the option's label, closes, and calls `_OnSelectChanged`.

`AuiSignal`: `view`, `id`, and `_Click/_Enter/_Exit/_Down/_Up/_ValueChanged`, each a typed call to
`view._OnSignal`. Wired by the builder with string persistent listeners.

`AuiLanguageService`: `langCodes`, `langNames`, `defaultLang`, `keyCount`, `keys`, `values`
(baked, hidden). `Get(key)` falls back default language then key text. `Register(view)`,
`SetLanguage(i)`, `SetLanguageByCode(code)`, `ClearChoice()`, `GetLanguage()`,
`GetLanguageCode()`, `LanguageCount()`, `LanguageName(i)`, `IndexOfKey(name)` (slow, editor use);
`SetStyleVariable(name, value)` / `GetStyleVariable(name)` / `StyleVariableCount/Name/Value` (the
world-wide theme override list, section 5).
Detection: default language at start, then the local player's join reads
`VRCPlayerApi.GetCurrentLanguage()` (exact code, then base code, then default); a pick is
remembered in PlayerData `aui.lang` as the code, after `OnPlayerRestored`. `TweensAvailable()`
and `ProbeDone()` come from a `VRCTween.DelayedCall` at 0.05 s raced against a plain delayed event
at 1 s.

`AuiInputPanel`: `OpenText(initial, view, elemId)`, `OpenTextMax(initial, view, elemId, maxLength)`
(the view calls this one, with the element's `maxlength`; a longer submission is cut in the
panel rather than through `TMP_InputField.characterLimit`, whose Udon exposure is unverified),
`Cancel()`, `IsOpen()`; on submit writes `view.inputResult`, `view.inputResultId` and calls
`view._OnInputResult()`; on timeout or cancel calls `_OnInputCancelled()`, and so does arming it
for another element while a view was still waiting (that view's waiting state would otherwise
never clear). `AuiView.Back()` cancels an armed panel before it closes a select. The view hands
the panel the RAW text (zero-width spaces stripped): the stored value is the escaped one, and a
round trip through the bound setter added one more space after every `<` per edit. Everything the world's InputPanelManager learned is kept: the field
is never on an inactive object, focus drops with `enabled = false`, `OnValueChanged` while not
focused is the submit, the armed panel expires after `clickTimeout`, the component disables itself
while idle.

## 9. Generated code: the contract with the author

For `Foo.aui`, `Foo.g.cs` contains, inside `namespace Armagan.AUI.Generated`:

- `[UdonBehaviourSyncMode(BehaviourSyncMode.None)] public partial class Foo : AuiView`.
- Constants: `E_<ID>` element index and `S_<ID>` signal id for every element with an id (row 0
  inside repeats), `<REPEAT>_MAX`, `<PAGES>_COUNT`, `<PAGES>_<PAGE>` indices.
- `[HideInInspector] public <Child> child<Child>[<Id>]` per nested view, assigned by the builder.
- One property per view-level binding: `T Name { get; set; }` with a private backing field
  `_name` (an author field of that name is a duplicate-definition error). The setter runs
  `EnsureStartedS/I/F/B(value)` first, which starts a not-yet-started view and returns the
  caller's value even when the view's `OnStart` wrote the same property meanwhile (see §15 on
  Udon argument slots), escapes strings (zero-width space after every `<`), stores, and pushes
  to every use:
  re-renders texts, `SetActiveElem` for `if`, `SetEnabledElem` for `disabled`, `SetSelected` for
  `selected`, `SetToggle`, slider `SetValueWithoutNotify` + label render, `SetSelect`,
  `SelectPage(..., false)`, `RenderInput`, child property. Setting from code never fires a handler.
  A bool used by `if` defaults to true; other bools false; strings ""; numbers 0.
- Per repeat: `int <Id>Count { get; set; }` and `Set<Id><Field>(int row, T value)` per item
  binding. Item string values used by `args` are kept in arrays so a language switch can re-render.
- Per page group: `Get<Id>Page()`, `Select<Id>Page(int)`; `_OnPageChanged` override updating the
  binding and calling the `onchange` handler.
- Per select: `Set<Id>OptionLabel(i, s)`, `Set<Id>OptionCount(n)`; `_OnSelectChanged` override.
- `Render_<elem>()` for each text with a view-level binding or a key with args, and for each slider's
  format label; `_Retranslate` override calling base then the renders; `_AfterStart` starting
  every nested child (`child.EnsureStarted()`), applying initial renders, reading slider
  defaults, and pushing literal/bound attributes into children. Children therefore start before
  the parent's `OnStart`, whatever page they sit on.
- Per repeat item binding, `Set<Id><Field>(int row, T value)` parks `row` and `value` in the
  `_auiStash*` slots before starting the view for the same reason.
- `_OnInputResult` override: matches `inputResultId`, sets the bound property, calls `onsubmit`.
- `_Dispatch(id, kind)`: `if (id == S) { Handler(); return; }` per handler; repeat rows use
  `if (id >= base && id < base + stride * max && (id - base) % stride == offset) { int row = ...;
  if (row >= <Id>Count) return; Handler(row); }`. Toggles pass `GetToggle(elem)`; sliders read
  `elemSliders[e].value`; pointer events check the kind.
- `_OnChildEvent` override routing a child's `Emit` to `on-<event>` handlers.
- An editor-guarded `Foo_AuiInspector : UnityEditor.Editor` whose `OnInspectorGUI` calls
  `AuiInspectorBridge.Draw(this)`.

Handler signatures the author must provide (a missing one is a plain C# error): `void H()` for
onclick and pointer events, `void H(bool value)` toggle onchange, `void H(float value)` slider
onchange, `void H(int value)` select/tabs/pages onchange, `void H(string value)` onsubmit,
`void H(int row)` onselect; inside a repeat every handler gets `int row` first. The same name
used twice must have the same signature (AUI108). Binding names that collide with AuiView members
are refused (`Lang`, `Content`, `Show`, `Hide`, ... see `AuiCodegen.Reserved`).

Determinism: the same model gives byte-identical text; the header carries the hash so the writer
can skip. The scaffold (`AuiSourceWriter.Scaffold`) writes `Foo.cs` once with an empty method per
handler, in the same namespace.

## 10. Index map: how codegen and the builder agree

`AuiIndexMap.Build(doc, info, styles)` walks the tree in document order and assigns every baked
GameObject a runtime element index; `NodeOf[e]`, `KindOf[e]`, `SignalOf[e]`, `RowOf[e]`. A repeat
template subtree occupies `Max` consecutive blocks of `Stride` elements: instance `i` of node `n`
is `Base[n] + i * Stride[n]`. Signals are numbered in element order; a repeat's signals are
likewise strided (`RepeatSignalBase`, `RepeatSignalStride`). A select gets two extra elements after
its options: the popup root and the scrim (`PopupElem`, `ScrimElem`). `link` and `style` nodes get
nothing. The builder never counts on its own; it asks the map for every index it writes. Kinds:
NONE 0, BUTTON 1, TOGGLE 2, TAB 3, OPTION 4, INPUT 5, SELECT 6, SLIDER 7, SCRIM 8, mirrored as
constants in `AuiView`.

## 11. Prefab builder rules

- Builds under a temporary root in the open scene when the prefab does not exist, otherwise INSIDE
  `PrefabUtility.LoadPrefabContents(path)` after stripping every child and every non-root component
  except RectTransform, Canvas, GraphicRaycaster, VRCUiShape, BoxCollider, the view class and its
  backing UdonBehaviour. That keeps the root ids, so a scene instance's overrides (position, author
  fields) survive a rebuild. Saved with `SaveAsPrefabAsset`, unloaded with `UnloadPrefabContents`.
- Root: `RectTransform` sized W x H, scale 0.001, world-space Canvas, GraphicRaycaster,
  `VRCUiShape` (without it nothing is clickable in VRChat and nothing tells you), trigger
  BoxCollider W x H x 10. The view component is added with `UdonSharpUndo.AddComponent`.
- `Content` child: stretch, CanvasGroup; element 0 (the `<view>`) is this object.
- Sprites are imported at 100 pixels per unit to cancel `Canvas.referencePixelsPerUnit` (100); a
  14 px slice border is then 14 mm. Setting the canvas reference to 1 was measured not to survive
  into the saved prefab.
- Labels inside clickables span the full height (no vertical padding inset): a TMP rect shorter
  than one line renders nothing under ellipsis overflow. Alignment uses `Left`/`Center`/`Right`
  (vertical middle), never `Midline*`, which shifts with descenders.
- Every Selectable gets `Navigation.Mode.None` and `transition = None`. Pointer EventTriggers are
  added only where a state rule or pointer handler needs them.
- Toggles are Buttons with a selected state, not uGUI Toggles. Sliders are built by hand (track,
  fill, circle handle from `Circle.png`, optional value label): `background` is the track
  (`elemBg`), `color` the fill and the value label (`elemText`; the runtime tints the fill through
  `Slider.fillRect`, which has no table of its own), `icon-color` the handle (`elemIcon`, falling
  back to the fill colour at bake). Inputs are Buttons with a label and
  an optional placeholder key. Selects are a header button plus a popup (nested Canvas, sorting
  order 100, own GraphicRaycaster, 1 mm toward the viewer) containing a scrim and the options; the
  popup is baked with the header's `background` and, having no node, is re-tinted at runtime by
  copying the header's background change (`SelectPopupOf`).
- Style rows: for each signal, the properties any state touches get a normal-state baseline row
  and one row per state override, each with duration and ease from the element's `transition`,
  and a channel index so tweens are killed per property.
- Pages not default are baked inactive with alpha 0. Nested views are `InstantiatePrefab` of the
  child prefab with its root-only components removed and `parentView` set.
- After filling the tables: `ApplySyncMode`, `CopyProxyToUdon` on the view, every signal and every
  nested view, then a check that `elemObjects` reached the Udon heap and that `inputResult`,
  `inputResultId`, `elemObjects`, `auiBakeHash`, `layParent`, `layLangW`, `elemChildView` are exported symbols. `AuditComponents` logs any
  destroyed component left in the tree.

## 12. Diagnostics

Format: `[AUI] <file>:<line>:<col> <severity> <code>: <message>`. Codes are stable and grouped:
0xx markup syntax (001 unexpected char, 002 unclosed, 003 mismatched close, 004 multiple roots,
005 unquoted attribute, 006 duplicate attribute, 007 empty, 008 bare ampersand, 009 unknown
entity, 010 text outside root, 011 unterminated comment, 012 unterminated attribute); 1xx markup
semantics (100 unknown tag, 101 unknown attribute, 102 duplicate id, 103 view name mismatch, 104
root not view, 105 repeat template count, 106 tab without page, 107 nested view not found, 108
binding type conflict, 109 handler not identifier, 110 missing required attribute, 111 bad value,
112 element not allowed here, 113 nested cycle, 114 tabs without pages, 115 text and key both, 116
bad binding syntax, 117 literal text info, 118 item binding outside repeat, 119 unknown icon, 120
repeat max, 121 unknown component prop, 122 bad prop declaration); 2xx stylesheet (200 syntax,
201 unknown property, 202 bad value, 203 undefined
variable, 204 unknown state, 205 bad selector, 206 unused class, 207 unknown class, 208 sheet not
found, 209 bad transition, 210 non-animatable in a state, 211 more than 64 classes, 212 bake-only
font weight on a runtime-only class, 213 bad bound value, 214 more than 256 star classes);
3xx languages (300 missing translation,
301 missing default, 302 file syntax, 303 unknown key, 304 placeholder not bound, 305 no
languages, 306 duplicate key, 307 placeholder mismatch); 4xx layout (400 overflow, 401 negative
size, 402 absolute without edges, 403 edge conflict, 404 edge on a non-absolute element, 405
display/position on the root); 5xx build (500 class missing, 501 program asset missing, 502 handler missing, 503 prefab
build failed, 504 export missing); 6xx fonts (600 stale, 601 glyph missing, 602 source missing).
Unknown names come with a "did you mean" from an optimal-string-alignment distance (a swapped
pair costs 1).

## 13. Editor tooling: language server and VS Code extension

`tools~/aui-lsp` is a .NET 8 console app (OmniSharp.Extensions.LanguageServer 0.19) that compiles
`Core/**/*.cs` directly, so its diagnostics are the importer's. `Workspace` finds the package root
(the folder itself, `Packages/rest.armagan.aui`, any package under `Packages/` named
`rest.armagan.aui`, then the same from each parent folder), reads `Assets/AUI/AuiSettings.asset`
for `langFolder`, `defaultLanguage`, `componentFolders` and `iconFolders` (else `Assets/AUI/Lang`
in any case), keeps open-document overlays, and caches the scan, the parsed theme and the language
table until a watched file appears or disappears or their text changes. A view opened outside
the scanned tree pulls its nearest `~` folder in (`AuiWorkspace.Include`). Compiles run 150 ms
after the last keystroke; a save or a request that needs the result runs at once
(`EnsureCompiled`). Diagnostics are aggregated per file across every view that touched it, so
shared sheets and language files show the union; `AUI206` (unused class) on a linked sheet only
survives when every view linking the sheet reports it. A sheet no view links is parsed on its own.

Handlers (`Handlers.cs`, `Features.cs`), all over `**/*.aui`, `**/*.auis` and
`**/{lang,Lang}/*.json`: text sync (full), completion with explicit replace ranges (tags, nested
views, `</` closing, attributes minus the ones present, keys, classes with their sheet, icons,
page ids, enums, flags, bindings scoped to the enclosing repeat, handlers from the view and from
`public void` methods in its `.cs`, `href` paths, selectors by segment, properties, values,
variables, sprites, eases), hover, definition, references, prepare/rename (classes and variables
workspace-wide, keys in views and every language file keeping the nesting, bindings, handlers and
ids in the view, plus the C# method declaration), code actions ("did you mean" replacements,
insert `;`, create a key in the default or every language, add a key to the languages lacking it
with or without the default text, create a missing view or sheet file, create a handler method,
remove an unused rule; the `aui.reveal` client command lands the caret on what was created),
document colours and presentations, semantic tokens for every piece of stylesheet text (`.auis`
files, `<style>` bodies, `style=""` values; `SemanticTokens.cs`, a forgiving lexer independent of
the parser, so the embedded stylesheet is coloured by the server whatever grammar the client has),
inlay hints (default-language text after `key=`), document
links (`href`, `src`), document symbols for views (with details) and sheets, workspace symbols
(views, classes, keys), watched files (`.aui`, `.auis`, language JSON, `.svg`; created and deleted
files invalidate the scan and re-check dependents), and the custom request `aui/generated` that
returns the generated C# of a view. `Occurrences.cs` decides what a position names (`Target`) and
finds every occurrence; `LangJson.cs` inserts a key into a language file keeping its formatting and
tells which key a JSON position is on; `Cursor.cs` decides the completion context from raw text so
it works in a broken file.

`tools~/vscode-aui` contributes the `aui` and `auis` languages, two TextMate grammars (the
`style=""` attribute embeds the sheet grammar), snippets, language configurations with
Enter-between-tags indentation, and a client that runs `dotnet server/aui-lsp.dll` (settings
`aui.serverPath`, `aui.dotnetPath`, `aui.autoClosingTags`, `aui.inlayHints.translations`,
`aui.trace.server`). The client adds auto-closing tags (decided from the text, as the HTML
extension does), the `AUI: Show Generated C#` command (an `aui-generated:` document refreshed as
the view changes), `AUI: Restart Language Server`, and the internal `aui.reveal` command. It is
bundled with esbuild into `out/extension.js` because `node_modules` is not packaged. `install.ps1`
/ `install.sh` build the server, package the `.vsix` and install it; `npm run smoke` (`smoke.js`)
drives the server without VS Code and checks every capability against the package's samples.

## 14. Tests

`Tests/Editor` (asmdef `Armagan.AUI.Tests.Editor`, NUnit): parser (positions, entities, recovery,
lifting), values, stylesheet parser, resolver (tiers, specificity, variables, inheritance, state
collection, class usage, default theme parses clean), analyzer (types, signatures, repeats, pages,
selects, nested views, keys, structure errors), layout (flow, grow, justify, anchors, percent,
widest translation, content sizing, wrap, repeats, grids, pages, selects), JSON reader, language
table (merge, fallback, placeholders, checks), index map, codegen (typed surface, no partial
methods, determinism, golden file `Golden/Demo.g.cs.txt`; delete the golden to regenerate it after
an intentional codegen change). Run them from the Test Runner or UnityMCP `run_tests`. Eighty
tests at v0.1.0.

The runtime is covered by the gallery (`Samples~/Demo/Views/Gallery`): a hub view nesting five
feature views, with a `RunTests` method (custom event `_TestRun`) that writes through the typed
API and reads back what rendered, 17 checks, logged in-world and as `[AUI gallery] PASS/FAIL`.
It is the regression suite for `AuiView`, codegen and the builder together; run it in play mode
after touching any of them.

## 15. Measured platform facts that shaped decisions

- A ScriptedImporter cannot emit a `.cs` file, and `UdonSharpEditorUtility.RunBehaviourSetup`
  needs a compiled program asset and an Undo context, so the build is a post-reload queue.
- UdonSharp compiles partial classes (one program asset per type, pointing at `Name.cs`) and
  rejects partial methods, so handlers are plain methods called by name.
- UdonSharp exports a parametered method as `__N_Name` and its parameters as `__N_param__param`;
  sending `SetLanguage` by string does nothing. Typed calls between UdonSharp behaviours handle
  this; string events are only used for parameterless methods (`_Retranslate`, `_Click`).
- A field written from another behaviour must be public and serialized; `SetProgramVariable` on
  an unexported symbol is a silent no-op. Hence `inputResult`, `inputResultId`, `view`, `id`, and
  every table are public with `[HideInInspector]`, never `[NonSerialized]`.
- Arrays coming back from the Udon heap are empty, not null. Every table length is validated.
- **Udon has no call stack for arguments.** A method's parameters and locals are heap slots, so
  a method that re-enters itself (a setter that starts the view, whose `OnStart` writes the same
  property) sees its argument overwritten by the inner call. Measured on the gallery: the first
  write to every not-yet-started child came back as the child's own default. `AuiView` parks the
  outer arguments in `_auiStash*` fields that are written only while `_started` is false, which
  is exactly once, and restores them after `_AuiStart`. Any author method that can re-enter
  itself needs the same care.
- A view on an inactive object (a hidden page) has no `Start` until it is shown, but calls from
  other behaviours reach it; its runtime arrays are null until `_AuiStart`. Hence
  `EnsureStarted` on every public entry and child auto-start in `_AfterStart`.
- A bound text with no fixed width is positioned by `BuildText` itself (pivot on the left); it
  must use the row's rect, not the template's, or every row of a repeat lands on row 0
  (BuilderVersion 9). It is a fixed box, not a content-grown one: the solver's reserved width,
  widened to the parent's remaining content width when nothing follows it in the row, with
  `Ellipsis` overflow. A preferred-size fitter let a long runtime string run off the canvas
  (BuilderVersion 11). Every single-line text ellipsises, keyed or bound.
- **TextMeshProUGUI draws every glyph with the first atlas page's texture.** Inter's 1012 glyphs
  at 48 pt overflowed a 1024 atlas onto three pages, and the ellipsis (page 2) rendered as a
  fragment of another glyph. Static bakes are one 2048 page with multi-atlas off; a bake that
  still overflows warns and lets the glyphs fall through the fallback chain.
- Noto Sans JP/KR lines are 1.448 em against Inter's 1.21 em. A bound text measured with a Latin
  sample got a box too short for "日本語" and the picker's Japanese chip rendered nothing; the
  sample carries a CJK character for that reason. Overflow shrinking runs on the x axis only: a
  column that shrank its texts' heights made the gallery's title vanish the same way.
- A rebake copies the new font over the existing asset to keep its GUID and destroys the
  temporary; the baker must keep using the existing object, or the fallback wiring throws
  `ArgumentNullException: target` after the atlases are already written.
- `TextMeshProUGUI.richText` and `.rectTransform` are not exposed to Udon. Untrusted text is
  escaped with a zero-width space after `<`; rect transforms are baked. `string[i]` DOES compile:
  `SystemString.__get_Chars` is not in the node list, but UdonSharp lowers the indexer to
  `ToCharArray(i, 1)[0]`, one `char[1]` allocation per access (an earlier version of this list
  said it fails). Hot paths (`ParseColor`, `ColorToHex`, `SubstituteVars`) take one `ToCharArray`
  per call instead.
- `VRCPlayerApi.GetCurrentLanguage()` throws a null delegate under ClientSim until ClientSim has
  started, which is after the service's `Start`. Detection runs on the local player's join.
- ClientSim does not raise `OnLanguageChanged`; the override exists for the live client only.
- VRCTween does not animate under ClientSim and stops when its object is deactivated. The service
  probes for tweens; views apply final states instantly until the probe lands and always trail a
  transition with a snap.
- Overwriting a prefab that holds UdonSharp components with `SaveAsPrefabAsset` throws
  `ArgumentNullException: unityObject` from `UdonSharpBehaviour.OnBeforeSerialize` (0 errors on a
  fresh save, 18 on an overwrite, measured). Rebuilding inside `LoadPrefabContents` keeps the root
  ids (a fresh file loses every scene override); the reimport of a loaded prefab still logs two
  such lines, which are harmless.
- `Canvas.referencePixelsPerUnit = 1` on a freshly added canvas did not survive into the saved
  prefab; sprites are imported at 100 PPU instead.
- TMP `Midline*` alignment moves with the glyphs on the line (a descender shifts it); `Left`,
  `Center`, `Right` (vertical middle) do not. A TMP rect shorter than one line renders nothing
  under `Ellipsis` overflow.
- uGUI raycasting for a world-space canvas needs a raycast target under the pointer; elements
  with state rules but no background get an invisible Image.
- UdonSharp routes custom inspectors through its own map (`UdonSharpCustomEditorManager`), which
  honours a base-class `[CustomEditor(..., true)]` but only after its cache initializes; a per-view
  exact editor emitted into the generated file is what is used.
- Unity treats a junction under `Packages/` as an embedded package but disables directory
  monitoring for the session; refreshes must be requested (focusing the editor does it).
- SVG icons must be imported as Textured Sprite (`SVGImporter.SvgType`); the default Vector Sprite
  has no texture and draws nothing in an Image. The preprocessor sets it, and `AUI/Reimport
  Icons` fixes files imported before the package compiled.
- **`<select max="N">` reserved nothing at all until BuilderVersion 16, and said nothing about
  it.** The analyzer parsed and range-checked `max`, the schema and this document both described
  it as reserving runtime-filled rows, and `ControlsDemo` shipped using it — but the index map
  walks a select's authored children and only those, and the builder wrote `selectOptionCount`
  from `Options.Count`. A select with one `<option>` and `max="20"` therefore had exactly one
  row, and `SetSelectOptionLabel` dropped every index past 0 through its `index >=
  selectOptionCount` guard without a log line. Measured on a four-filter panel: each popup showed
  one option and 19 of every 20 writes went nowhere. The rows are now expanded into real
  `<option>` nodes in `AuiTemplateExpander`, before the cascade, so nothing downstream needs a
  special case. Two things that expansion has to get right: a reserved row carries an empty
  literal text, because `BuildClickableContent` only makes a label when there is a `key` or text
  and `SetSelectOptionLabel` would otherwise write into a null; and `SetSelectOptionCount`
  resizes the popup, because a well baked for twenty rows showing four is as wrong as the bug.
- **A reserved select row measures shorter than an authored one, and the well overshot by the
  difference.** A reserved row has no key, so the solver measured it at the option's `min-height`
  (32) while the authored row with its label measured 45.96. The builder baked each row at its own
  height but wrote the tallest as `selectOptionH`, and `SetSelectOptionCount` sizes the well to
  `count * selectOptionH`: twenty rows ended at 654 mm inside a 927 mm well, 270 mm of bare
  background under the last option. Since BuilderVersion 17 every row is baked at the tallest
  row's height, so the runtime's arithmetic and the baked positions agree by construction.
- **`hidden` on an element that carries an icon bakes the icon at the wrong offset, permanently.**
  A clickable's icon and label are not layout elements: `BuildClickableContent` anchors the icon
  left-middle (pivot 0, anchor 0/0.5) and writes its offset once, and `ApplyLayout` only touches
  elements the solver owns — it forces anchor (0,1) and pivot (0.5,0.5), which is not what an
  icon carries — so nothing moves an icon after the bake, ever. `Place()` returns before
  descending into a hidden element, so a button baked hidden is 0 x 0 when its glyph is placed,
  and a centred single child of a zero-wide content box lands at `(0 - 18) / 2 = -9`. Showing it
  later resizes the button and leaves the glyph nine millimetres outside its own left edge; no
  relayout can put it back, because the icon is not something a relayout writes. Measured in
  avtr.zip: all three arrows on all four pagers, plus every icon-only stepper on four screens.
  Two things narrow it. Only **icon-only** clickables break this way — one with a label as well
  puts its icon at the padding, which does not depend on the width. And a **keyed** clickable
  baked hidden fails differently and recoverably: TMP measures nothing on an inactive object, so
  the label solves at zero width and the button collapses to its own padding (a blank accent
  square where "Search these" should be), which `_AuiWake` re-measures when the page enters.
  Until the baker places the subtree of a hidden element, the authoring rule is: **do not bake
  `hidden` on anything with an `icon=` or a `key=`.** Bake it live and let the model push the
  binding false — a host pushes its child's props from `_AfterStart` anyway, so `hidden` is
  usually buying one frame that something else was already paying for.
  To audit a built prefab: walk it for `RectTransform`s named `Icon` whose parent has exactly one
  child, and flag any with `anchoredPosition.x < 0`.

## 16. Workflows for common changes

Add a view: create `Name.aui` (and optionally `Name.auis`) next to where the prefab should live,
save, wait for `[AUI] wrote Name.g.cs` and the reload, then `[AUI] built Name.prefab`; write logic
in the scaffolded `Name.cs`; drop the prefab into a scene that has the two service objects.

Prototype a view: `AUI/Preview`, pick the view, Live. Turn "Auto-build prefabs" off if the
view's structure is still moving (every structural save would otherwise reload the domain).
Edit the `.aui`/`.auis` in any editor; the scene view follows each save, including pages and
popups around the edit. When the layout is right, "Build prefab" (or auto-build back on) runs
the normal pipeline; test clicks and transitions in play mode with the real prefab.

Add a translation key: add it to every `lang/<code>.json` (missing ones warn per language), use it
with `key=`, `AUI/Bake Fonts` if it introduces new characters, `AUI/Rebuild Language Table` if the
queue did not (it does after a build).

Add a language: add `lang/<code>.json` with `_name`, bake fonts, rebuild the table; the picker
shows it automatically.

Add an element type: `AuiSchema.Elements` (attributes, events, children rules), `AuiAnalyzer`
(attribute semantics, binding types, handler signature), `AuiIndexMap.KindOfTag` and the runtime
`KIND_*` if it is clickable, `AuiLayoutSolver.Measure` for its intrinsic size, `AuiPrefabBuilder`
for its objects, `AuiCodegen` for its setters and dispatch, `AuiView` for its runtime behaviour,
the theme for its default look, the grammars for highlighting, tests, README table,
`tools~/docs/reference/elements.md`.

Add a style property: `AuiSchema.Props` (kind, inherited, animatable), `AuiResolvedStyle` getter,
the builder's use of it (and `PropId`/`RowValue`/`ApplyProp` if animatable), the grammar list,
`tools~/docs/reference/properties.md` (including the theme default column).

Add a diagnostic code: `AuiCodes.cs` (append to its block, never fill a gap), the raiser, section 12
above, `tools~/docs/reference/diagnostics.md` with a cause and a fix.

Change the public `AuiView` API: `tools~/docs/reference/view-api.md`, and
`tools~/docs/reference/generated-code.md` if codegen emits it.

Change the builder's output without changing markup: bump `BuilderVersion`; `AUI/Rebuild All Views`.

Change codegen: delete `Tests/Editor/Golden/Demo.g.cs.txt`, run the tests (it regenerates and reports
Inconclusive), review the file, run again, commit it. Existing views regenerate on their next build;
a view whose author half no longer matches needs both halves changed together or the compile that
would regenerate it cannot run (bootstrap by hand, as was done for the namespace move).

Rebuild the editor tooling: `tools~/vscode-aui/install.ps1`, then reload VS Code windows.

## 17. Known issues

- Two `ArgumentNullException: unityObject` lines per rebuild of a prefab that is loaded through
  a scene instance (UdonSharp's serializer during the reimport). Harmless.
- A hand-written `Name.cs` that references generated members before `Name.g.cs` exists blocks
  the compile that would generate it. Create the `.aui` first; let the scaffold appear.
- Only the first four `args` of a text are substituted.
- Han unification: a character shared by JP/KR/SC renders with the Japanese glyph shape
  (`AuiFontSet.cjkRegular` order).
- Nested views cannot sit inside a repeat template and take no children; nor can a `select`,
  `pages` or `tabs` (AUI112).
- A diagnostic raised inside an expanded `<component>` points at the component's definition,
  once per usage, not at the usage.
- `AUI502` is defined but not raised: a missing handler surfaces as UdonSharp's own error plus
  `AUI501`.
- `<option value>` is accepted and never read.
- The bound-text sample is a guess; a very long runtime string ellipsises.
- A scene instance rebuilt before v0.1.0's stable-id change carries dead override entries;
  Prefab > Revert All and re-set its fields once.

## 18. Not verified

Compiles, passes the tests, and runs under the editor's play mode, where these cannot be seen:
VRCTween transitions and the probe in the live client; the VRChat keyboard opening on the input
box's second click on desktop and in VR; laser hover enter/exit reliability on Quest; the live
client raising `OnLanguageChanged`; PlayerData persistence of the language pick across sessions;
dropdown clicks through the laser (verified structurally, not by a real click); text legibility
of the default sizes at 2 m; atlas memory on Quest.
