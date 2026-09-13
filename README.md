# AUI

A UI engine for VRChat worlds written in UdonSharp. You write a view in an HTML-like file and a
small CSS-like stylesheet; the editor compiles them into a plain uGUI prefab with generated
UdonSharp wiring. Every string is a translation key and the player's language switches in-game
without a rebuild. The flex layout and the stylesheet cascade are baked into tables the view
re-runs in Udon, so text, classes, visibility, style values and created elements can change at
runtime; nothing is parsed from text in-world.

Package id `rest.armagan.aui`, namespace `Armagan.AUI`. Targets Unity 2022.3 and VRChat Worlds
SDK 3.10.4 or newer. This file is the overview; `USAGE.md` is the recipe book for building panels;
`REFERENCE.md` is the complete specification for anyone changing the package.

## Install

The package is a folder. Put it under `Packages/` of a VRChat world project, either by copying
it or by pointing a directory junction at a checkout:

```
mklink /J "<world>\Packages\rest.armagan.aui" "<checkout of this repo>"
```

Unity treats a folder under `Packages/` as an embedded package. It will warn that the path is a
symlink and disable directory monitoring for the session; that is expected, and means a refresh
has to be requested after editing files outside Unity (it happens automatically when you focus
the editor).

Then, in the editor:

1. `AUI/Create Settings` makes `Assets/AUI/AuiSettings.asset` and `Assets/AUI/Lang/`.
2. Put one `<code>.json` per language in `Assets/AUI/Lang/` (see *Languages*).
3. `AUI/Bake Fonts` builds the font atlases for exactly the characters those files use.
4. `AUI/Rebuild Language Table` and `AUI/Rebuild Input Panel` create the two scene services.

## Write a view

`Assets/AUI/Demo/SettingsDemo.aui`:

```xml
<view name="SettingsDemo" width="440" height="400">
  <panel class="row">
    <icon name="sliders-horizontal" size="22"/>
    <text key="demo.title" class="title"/>
    <spacer/>
    <button onclick="AskReset" icon="refresh-cw" class="ghost"/>
    <button id="close" onclick="Close" icon="x" class="ghost"/>
  </panel>
  <tabs bind="{page}" onchange="PageChanged">
    <tab page="general" key="demo.general" icon="monitor"/>
    <tab page="audio" key="demo.audio" icon="headset"/>
    <tab page="lang" key="demo.language" icon="globe"/>
  </tabs>
  <pages id="main" mode="crossfade">
    <page id="general">
      <panel class="setting"><text key="demo.brightness"/><spacer/>
        <slider bind="{brightness}" min="0" max="100" step="1" format-key="demo.volfmt" onchange="BrightnessChanged"/></panel>
      <panel class="setting"><text key="demo.solid"/><spacer/><toggle bind="{solid}" icon="check" onchange="SolidChanged"/></panel>
      <panel class="setting"><text key="demo.quality"/><spacer/>
        <select bind="{quality}" onchange="QualityChanged">
          <option value="low" key="demo.q.low"/><option value="medium" key="demo.q.medium"/><option value="high" key="demo.q.high"/>
        </select></panel>
    </page>
    <page id="audio">
      <panel class="setting"><text key="demo.volume"/><spacer/>
        <slider bind="{volume}" min="0" max="100" step="1" format-key="demo.volfmt" disabled="{muted}" onchange="VolumeChanged"/></panel>
      <panel class="setting"><text key="demo.mute"/><spacer/><toggle bind="{muted}" icon="check" onchange="MuteChanged"/></panel>
      <panel class="setting"><text key="demo.nick"/><spacer/><input bind="{nick}" placeholder-key="demo.nickph" onsubmit="NickSubmitted"/></panel>
    </page>
    <page id="lang"><AuiLanguagePicker/></page>
  </pages>
  <panel class="footer"><text key="demo.signedIn" args="{playerName}" class="muted"/><spacer/><button onclick="Greet" key="demo.greet" icon="sparkles" class="primary"/></panel>
  <modal id="reset" enabled="{asking}" onclose="ResetCancelled">
    <text key="demo.resetSure"/>
    <panel class="row"><spacer/><button onclick="CloseReset" key="demo.no" class="ghost"/><button onclick="DoReset" key="demo.yes" class="primary"/></panel>
  </modal>
</view>
```

`.setting`, `.footer` and `.primary` come from the sibling `SettingsDemo.auis`; `.row`, `.muted` and
`.ghost` from the package theme.

Saving the file starts the build. Two editor reloads later there is `SettingsDemo.g.cs`
(generated, do not edit), `SettingsDemo.cs` (yours, scaffolded once with an empty method per
handler), `SettingsDemo.asset` and `SettingsDemo.prefab`. Drop the prefab into the scene.

`SettingsDemo.cs` is where the logic goes. Both halves live in the namespace
`Armagan.AUI.Generated` (the scaffold puts your file there; a partial class needs one namespace),
so view names never collide with world code and other scripts reach a view with
`using Armagan.AUI.Generated;`. Bindings are properties, handlers are methods:

```csharp
namespace Armagan.AUI.Generated { }   // the file is inside this namespace; shown flat here

public partial class SettingsDemo : AuiView
{
    protected override void OnStart()
    {
        PlayerName = Networking.LocalPlayer.displayName;   // re-renders "Signed in as {playerName}"
        Brightness = 70f;                                  // moves the slider, no handler call
    }

    public void Close() { Hide(); }
    public void PageChanged(int page) { }
    public void BrightnessChanged(float value) { }
    public void SolidChanged(bool value) { }
    public void QualityChanged(int value) { }
    public void VolumeChanged(float value) { }
    public void MuteChanged(bool value) { }
    public void NickSubmitted(string value) { PlayerName = value; }   // escaped before it reaches any label
    public void Greet() { }
    public void AskReset() { Asking = true; }                          // opens the modal through its binding
    public void DoReset() { Brightness = 70f; Volume = 50f; CloseReset(); }
    public void ResetCancelled() { }
}
```

The property setters fan out to every element that uses the binding; a string is escaped once
(a zero-width space after every `<`) so untrusted text can never open a rich-text tag. A control
bound to `{x}` writes `X` back when the player uses it, then calls the handler; setting `X` from
code moves the control silently. Repeats use methods because Udon has no per-row objects:
`RowsCount = 3; SetRowsName(0, "first");`.

Two more author hooks: `OnRetranslate()` runs after a language switch, `Emit("picked")` in a
nested view runs the parent's `on-picked="Handler"` (`EmitInt("picked", 3)` when the child
declares `<event name="picked" type="int"/>`; the handler then takes `int value`).

## Change things at runtime

Every view carries its layout and its stylesheet as tables and re-solves them in Udon when
something changes. From the view's own code, or from another script holding the typed reference:

```csharp
SetActiveElem(E_DETAILS, false);          // hides and frees the space; siblings move up
AddClass(E_CARD, C_WARM);                 // restyles the card and its children through the cascade
Tone = "big";                             // class="card {tone}" mixes literals and bindings
BarWidth = "50%";                         // style="width: {barWidth}"; lengths are strings, colours are Color
SetPosition(E_BADGE, -6f, -6f, float.NaN, float.NaN);   // position: absolute at negative edges
int line = CreateLine(E_LOG);             // a parked <template> instance, linked under the log card
SetLineMsg(line, "hello"); DestroyElement(line);
OpenConfirm();                            // a <modal>, over a dimming backdrop, no z-index games
int hint = GetElement("#hint");           // or GetElements(".chip") / GetElements("button")
SetText(hint, "done"); SetIcon(hint, "check"); SetOpacity(hint, 0.5f);
SetStyleVariable("accent", "#4FC3F7");    // a :root variable: every rule that reads it follows, nested views too
lang.SetStyleVariable("base", "#101418"); // the same through the language service: every view in the world
SetAutoRelayout(false); /* many changes */ Relayout();   // one solve for a batch
```

Solves are one per frame and only when marked dirty; a 40-element view solves in about a
millisecond in the editor's Udon interpreter. Two things cannot change in-world because Udon has
no API for them: a text's font weight (a class that sets one is applied at bake and warns when
it can only be reached at runtime) and new materials (`shader()` materials are created at bake).

## Linking other scripts

Both directions are typed references; nothing goes through strings.

```csharp
// UI -> script: the view's .cs half holds a normal public field, set in the inspector on the
// scene instance (a prefab cannot reference scene objects, so it is an instance override).
public partial class SettingsDemo : AuiView
{
    public DemoBrightness brightness;
    public void BrightnessChanged(float value) { if (brightness != null) brightness.SetPercent(value); }
}

// script -> UI: hold the generated class and set its properties. Setters from code repaint
// every bound element and never call the handler.
public class DemoBrightness : UdonSharpBehaviour
{
    public SettingsDemo panel;
    private void Start() { if (panel != null) panel.Brightness = 70f; }
}
```

A script that must not depend on a specific view (a library) can instead take an `AuiView` and
use the base API: `Show()`, `Hide()`, `Back()`, `GetElement("#id")`, `SetText`, `AddClassNamed`,
`SetActiveElem`, `SetRepeatCount(i, n)`, `CreateElementByTag("text", parent, "muted", -1)`, or
send `_Retranslate`. For the generated properties the concrete class is the API; that is the
point of generating it.

## Languages

`Assets/AUI/Lang/en.json`:

```json
{
  "_name": "English",
  "demo": { "title": "Settings", "signedIn": "Signed in as {playerName}" }
}
```

Nested objects flatten to dotted keys. `_name` is the native name shown by the picker.
`{placeholders}` are filled from the element's `args="{binding}"` list. The languages a project
ships are exactly the files in that folder; the package's own keys (`aui.lang.*`) are merged in
underneath for those codes.

At runtime `AuiLanguageService` starts from the VRChat client language (matched exactly, then by
base code, then the settings' default), remembers a pick from the picker in PlayerData under
`aui.lang`, and broadcasts `_Retranslate` to every view. Views find the service and the input
panel by name (`AUI Language Service`, `AUI Input Panel`) because a prefab cannot reference scene
objects.

## Elements

| Tag | Attributes | Handler signature |
|---|---|---|
| `view` | `name` (= file stem), `width`, `height` (mm), `show`, `start-hidden` | |
| `panel` / `div` | | |
| `text` | `key`, `text="{s}"`, `args="{a} {b}"`, `wrap`, `ellipsis`, `runtime` | |
| `image` | `src`, `mode=simple\|sliced\|fit` | |
| `rawimage` | `texture="{t}"` (Texture), `uv="{r}"` (Rect); set from code, hidden while null | |
| `icon` | `name` (Lucide file name), `size` | |
| `spacer` | `size` (fixed) or none (grows) | |
| `button` | `key`, `icon`, `disabled="{b}"`, `selected="{b}"` | `onclick`: `void H()` |
| `toggle` | `bind="{b}"`, `key`, `icon` | `onchange`: `void H(bool value)` |
| `slider` | `bind="{f}"`, `min`, `max`, `step`, `format-key`, `decimals` | `onchange`: `void H(float value)` |
| `input` | `bind="{s}"`, `type` text/url, `placeholder-key`, `maxlength` | `onsubmit`: `void H(string value)`, or `void H(VRCUrl value)` for `type="url"` |
| `select` | `bind="{i}"`, `max`; children `<option value key/>` | `onchange`: `void H(int value)` |
| `tabs` / `tab` | `bind="{i}"`, `for`; `<tab page key icon/>` | `onchange`: `void H(int page)` |
| `pages` / `page` | `id`, `mode=crossfade\|sequential`, `default` | |
| `repeat` | `id`, `max` (1..64), one template child, optional `<empty>` | `onselect`: `void H(int row)`; handlers inside get `int row` first |
| `template` | `id`, `max` (1..64), one child; direct child of `view` | parked copies for `CreateId(parent)` / `CreateElement`; `{item.x}` become `SetIdX(elem, v)` |
| `slot` | `id`, `views="A B"`, `max` | parked nested views for `CreateIdA(parent)` / `CreateViewNamed` |
| `modal` | `id`, `enabled="{b}"`, `dismissable`, `show`; direct child of `view` | `onopen`, `onclose`: `void H()`; generated `OpenId()`, `CloseId()`, `IsIdOpen()` |
| `prop` | `name`, `type`, `default`; on `view` or inside `component` | declares a typed property |
| `event` | `name`, `type`; on `view` | declares what `EmitX(name, value)` sends |
| `component` | `name` (capitalised), one child, `prop`s | an inline template used as `<Name prop="v"/>`, expanded at compile time |
| `Name` | a nested `Name.aui`; attributes are its props (`{binding}` or literal); `on-<event>` | `void H()` or `void H(T value)` for a typed event |

Common attributes: `id`, `class` (literals and `{bindings}`, `*utility` classes), `style`
(values may be `{bindings}`), `if="{b}"` (active), `hidden` (never on an element with `icon=` or
`key=` — a hidden subtree is not laid out and an icon's offset is baked once, so the glyph stays
outside the button; bake it live and push the binding false instead). `view` also takes `icons`,
`sprites`, `materials`, `utilities` (preloads for `SetIcon` / `SetBackground` / `SetMaterial` /
`AddClass`) and `pool="text:16 panel:8"`. Pointer handlers `onhover`, `onleave`, `onpress`,
`onrelease` on anything clickable. Inside a repeat or template, bindings are `{item.name}`.

## Stylesheet

A view uses, in cascade order: the package theme (`Theme/Default.auis`), the built-in utility
classes, `<link href>` or the sibling `Name.auis`, `<style>` blocks, then `style=""`. Selectors
are `tag`, `.class`, `tag.class`, any of those with `:hover`, `:pressed`, `:selected` or
`:disabled`, `.*star` for a style-only class, `modal::backdrop` for a modal's scrim, and `:root`
for variables. No descendant selectors: styles are resolved per element.

```
:root { --accent: #E0A86A; }
.card { background: sprite(Rounded8) var(--card); padding: 12; gap: 8; }
button:hover { background: var(--accent); scale: 1.03; }
.glow { background: sprite(Rounded8) shader(Custom/UIGlow) var(--accent); }
```

Properties: `width height min-* max-*` (mm, `auto`, `%`), `display`, `position` with
`top right bottom left` (negatives allowed), `anchor`, `padding margin gap`, `flex-direction
justify align grow wrap`, `color background border radius opacity scale offset icon-color`,
`font font-weight font-size text-align line-height`, `transition`, `page-transition`, `show`.
`background` takes `none`, a colour, `sprite(Name)`, `material(Name)` (an asset) or
`shader(Name)` (one generated material per shader). State rules may only change `color`,
`background`, `border`, `icon-color`, `opacity`, `scale` and `offset`; they animate with the
element's `transition`. Every property except `radius`, `font-weight`, `page-transition` and
`show` can change at runtime through a class or a bound style value.

Utility classes: `*flex-row *items-center *gap-8 *p-12 *w-full *hidden *absolute *rounded-8
*text-lg *fg-muted` and so on (steps 0 1 2 4 6 8 12 16 24 32 48 64; the full list is generated by
`Core/Schema/AuiUtilityClasses.cs` and documented in `USAGE.md`). They never count toward the
64 regular classes a view can switch at runtime, and only the ones a view mentions are baked.

Units are millimetres: the canvas is scaled 0.001 so a 440 wide view is 44 cm in the world.

## Layout

A flex subset: one axis per container, `grow` shares leftover space, `wrap` breaks rows,
`position: absolute` and `anchor` place out of the flow, `display: none` and `hidden` free their
space. The editor solves it once to bake the prefab (text measured with the real font over every
translation) and the view solves it again in Udon whenever a text, a class, a visibility, a
repeat count, a created element or the language changes, using the baked per-language text
sizes and measuring changed strings through uGUI's `LayoutUtility`. A test suite runs both
solvers on the same trees and requires identical rectangles. Repeats bake `max` copies and lay
out the live rows; they page rather than scroll.

## Inspector

A view's inspector shows the UdonSharp header, a summary line, `Rebuild view` / `Open .aui` /
`Open .cs` buttons, and then only the fields you declared in your own `.cs`. Every baked table
and generated field is hidden; they are outputs of the `.aui` and `.auis` files, not inputs.

## Preview

`AUI/Preview` opens a small window and puts a live copy of one view into the open scene. Pick
a view (or turn on "Follow selection" and click a view in the scene or a `.aui` in the Project
window), switch it to Live, and every save of the `.aui`, its stylesheet, the theme, a nested
view or a language file rebuilds the copy a moment later, straight from the files on disk. No
import, no generated code, no reload, no UdonSharp; it works while Unity is not the focused
window. The copy sits where the view's scene instance is (the instance is hidden in the scene
view meanwhile) or in front of the scene camera, is never saved with the scene, and does not
mark the scene dirty.

Buttons do nothing in the preview, so what the prefab hides at rest is reachable from the
window: a dropdown per `pages` group, a checkbox per `select` popup and per `modal`, and a
language dropdown. Templates and slots are parked and do not show; bound style values show
their defaults.
With "Follow edits" on (the default), a save inside a page shows that page and draws its tab
selected; a save inside an `<option>` opens that select. Hover and pressed looks, transitions
and clicks still need the real prefab in play mode.

"Build prefab" runs the normal pipeline for that view. "Auto-build prefabs" (also
`AUI/Auto-build Views`) pauses the automatic builds so a save never triggers a script reload
while you prototype; saved views queue up and build when it is turned back on or a rebuild is
asked for.

## Menu

| Item | What it does |
|---|---|
| `AUI/Preview` | the live preview window (see *Preview*) |
| `AUI/Auto-build Views` | toggle; off = saved views queue instead of building |
| `AUI/Rebuild All Views` | queues every `.aui` and builds them, even when unchanged |
| `AUI/Rebuild Language Table` | bakes the merged language files onto the scene's service |
| `AUI/Rebuild Input Panel` | authors the keyboard click target |
| `AUI/Bake Fonts` | builds the atlases and rebuilds views |
| `AUI/Validate` | compiles every view and prints all diagnostics |
| `AUI/Create Settings`, `Create missing program assets`, `Generate Sprites`, `Reimport Icons` | housekeeping |

Right-click in the Project window: `Create/AUI/View`, `View with Stylesheet` and `Stylesheet` make
a `.aui` (with the `name` attribute set to the file name you type), its sibling `.auis`, or a
`.auis` alone.

## How the build works

An asset importer cannot write source files or add a component whose class does not exist yet,
so the `.aui` importer only compiles and records diagnostics. A post-import queue then writes
`Name.g.cs` when its text changed (which recompiles), and after the reload creates the program
asset, compiles UdonSharp, and builds the prefab under a temporary scene root before saving it.
Children build before parents. The queue lives in `SessionState` and resumes after reloads.

Every element index in the generated code comes from the same `AuiIndexMap` walk the builder
uses, so the constants and the baked arrays agree by construction. A repeat template's subtree
is one block of elements per row.

A save costs one compile: the importer's result is kept, validated by the hashes of the files
it read, and reused by the queue. Text measurements are cached for the session, the workspace
scan only repeats when a view or icon is added, removed or moved, and the language table is
only rebuilt when a language file changes. A `.g.cs` is rewritten, and the domain reloaded,
only when the generated text differs.

## Diagnostics

Messages are `[AUI] path:line:col code: text`, clickable in the console. Codes are stable:
0xx markup syntax, 1xx markup semantics, 2xx stylesheet, 3xx languages, 4xx layout, 5xx build,
6xx fonts. The list is `Core/Diagnostics/AuiCodes.cs`.

## Tests

`Tests/Editor` holds NUnit edit-mode tests for the parser, stylesheet, cascade, analyzer, layout,
language table and code generator (golden file under `Tests/Editor/Golden`), plus parity suites
that run the Udon-side layout solver and cascade against the compile-time ones on the same
fixtures, and tests for modals, components, templates and utilities. Run them from the Test
Runner. The gallery under `Samples~/Demo/Views/Gallery` (a hub with nine nested pages: texts,
controls, lists, layout, styles, theme, runtime creation, languages) exercises the runtime
features together and carries a self-test; `Samples~/Demo/README.md` lists what each page shows.

## Known issues

- Rebuilding a prefab that a scene instance currently uses logs two `ArgumentNullException:
  unityObject` errors from UdonSharp's serializer during the reimport. Measured to be harmless:
  the prefab, its instance and the Udon heap are all correct afterwards.
- A hand-written `Name.cs` that references generated members before `Name.g.cs` exists blocks the
  compile that would generate it. Create the `.aui` first and let the scaffold appear, or run
  `AUI/Rebuild All Views` from a state that compiles.
- Han unification: a CJK character present in Japanese, Korean and Chinese renders with the
  Japanese glyph shape.
- Only the first four `args` of a text are substituted.
- The VS Code extension's bundled server must be rebuilt (`tools~/vscode-aui/install.ps1`) after
  the compiler changes; an old server shows errors for `display`, `modal`, `template` and the
  `*utility` classes that the Unity importer accepts.
- Runtime text is re-measured through `LayoutUtility` because TextMeshPro's own measurement
  calls are not exposed to Udon; a text on an inactive page is measured when the page opens.
- Font weight cannot change at runtime, and a bound `width: {w}` is a string parsed in Udon
  (`"12"`, `"50%"`, `"auto"`).

## Not verified

Everything below compiles, passes the edit-mode tests and runs under play mode in the editor,
where these things cannot be seen:

- VRCTween transitions (inert in ClientSim; the code applies final states instantly there and
  probes for tweens in the real client).
- The VRChat keyboard opening on the input box's second click, on desktop and in VR.
- Laser hover enter/exit reliability on Quest.
- `OnLanguageChanged` being raised by the live client, and PlayerData persistence of the pick.
- Text legibility of the default sizes at 2 m, and atlas memory on Quest.
- The cost of a runtime solve on Quest hardware (measured only in the editor's Udon interpreter:
  about 2 ms for a 42-element view with five nested views).
- Backdrop clicks on a modal and `shader()` backgrounds in the live client.
