# AUI

A UI engine for VRChat worlds written in UdonSharp. You write a view in an HTML-like file and a
small CSS-like stylesheet; the editor compiles them into a plain uGUI prefab with generated
UdonSharp wiring. Every string is a translation key, the player's language switches in-game
without a rebuild, and nothing is parsed at runtime.

Package id `rest.armagan.aui`, namespace `Armagan.AUI`. Targets Unity 2022.3 and VRChat Worlds
SDK 3.10.4 or newer.

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

1. `AUI/Create Settings` makes `Assets/AUI/AuiSettings.asset` and `Assets/AUI/lang/`.
2. Put one `<code>.json` per language in `Assets/AUI/lang/` (see *Languages*).
3. `AUI/Bake Fonts` builds the font atlases for exactly the characters those files use.
4. `AUI/Rebuild Language Table` and `AUI/Rebuild Input Panel` create the two scene services.

## Write a view

`Assets/AUI/Demo/SettingsDemo.aui`:

```xml
<view name="SettingsDemo" width="440" height="330">
  <panel class="row">
    <icon name="sliders-horizontal" size="22"/>
    <text key="demo.title" class="title"/>
    <spacer/>
    <button id="close" icon="x" class="ghost" onclick="Close"/>
  </panel>
  <tabs bind="{page}">
    <tab page="general" key="demo.general"/>
    <tab page="audio" key="demo.audio"/>
  </tabs>
  <pages id="main">
    <page id="general">
      <panel class="row"><text key="demo.brightness"/><spacer/>
        <slider bind="{brightness}" min="0" max="100" step="1" format-key="demo.volfmt" onchange="BrightnessChanged"/></panel>
      <panel class="row"><text key="demo.solid"/><spacer/><toggle bind="{solid}" icon="check" onchange="SolidChanged"/></panel>
    </page>
    <page id="audio">
      <panel class="row"><text key="demo.nick"/><spacer/><input bind="{nick}" placeholder-key="demo.nickph" onsubmit="NickSubmitted"/></panel>
      <AuiLanguagePicker/>
    </page>
  </pages>
  <panel class="row"><text key="demo.signedIn" args="{playerName}" class="muted"/><spacer/><button onclick="Greet" key="demo.greet"/></panel>
</view>
```

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
    public void BrightnessChanged(float value) { }
    public void SolidChanged(bool value) { }
    public void NickSubmitted(string value) { PlayerName = value; }   // escaped before it reaches any label
    public void Greet() { }
}
```

The property setters fan out to every element that uses the binding; a string is escaped once
(a zero-width space after every `<`) so untrusted text can never open a rich-text tag. A control
bound to `{x}` writes `X` back when the player uses it, then calls the handler; setting `X` from
code moves the control silently. Repeats use methods because Udon has no per-row objects:
`RowsCount = 3; SetRowsName(0, "first");`.

Two more author hooks: `OnRetranslate()` runs after a language switch, `Emit("picked")` in a
nested view runs the parent's `on-picked="Handler"`.

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
use the base API: `Show()`, `Hide()`, `SetSelected(id, on)`, `SetRepeatCount(i, n)`, or send
`_Retranslate`. For the generated properties the concrete class is the API; that is the point of
generating it.

## Languages

`Assets/AUI/lang/en.json`:

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
| `icon` | `name` (Lucide file name), `size` | |
| `spacer` | `size` (fixed) or none (grows) | |
| `button` | `key`, `icon`, `disabled="{b}"`, `selected="{b}"` | `onclick`: `void H()` |
| `toggle` | `bind="{b}"`, `key`, `icon` | `onchange`: `void H(bool value)` |
| `slider` | `bind="{f}"`, `min`, `max`, `step`, `format-key`, `decimals` | `onchange`: `void H(float value)` |
| `input` | `bind="{s}"`, `placeholder-key`, `maxlength` | `onsubmit`: `void H(string value)` |
| `select` | `bind="{i}"`, `max`; children `<option value key/>` | `onchange`: `void H(int value)` |
| `tabs` / `tab` | `bind="{i}"`, `for`; `<tab page key icon/>` | `onchange`: `void H(int page)` |
| `pages` / `page` | `id`, `mode=crossfade\|sequential`, `default` | |
| `repeat` | `id`, `max` (1..64), one template child, optional `<empty>` | `onselect`: `void H(int row)`; handlers inside get `int row` first |
| `Name` | a nested `Name.aui`; attributes become the child's properties; `on-<event>` | `void H()` |

Common attributes: `id`, `class`, `style`, `if="{b}"` (active), `hidden`. Pointer handlers
`onhover`, `onleave`, `onpress`, `onrelease` on anything clickable. Inside a repeat template
bindings are `{item.name}`.

## Stylesheet

A view uses, in cascade order: the package theme (`Theme/Default.auis`), `<link href>` or the
sibling `Name.auis`, `<style>` blocks, then `style=""`. Selectors are `tag`, `.class`,
`tag.class`, any of those with `:hover`, `:pressed`, `:selected` or `:disabled`, and `:root` for
variables. No descendant selectors: styles are baked per element.

```
:root { --accent: #E0A86A; }
.card { background: sprite(Rounded8) var(--card); padding: 12; gap: 8; }
button:hover { background: var(--accent); scale: 1.03; }
```

Properties: `width height min-* max-*` (mm, `auto`, `%`), `anchor`, `padding margin gap`,
`flex-direction justify align grow wrap`, `color background border radius opacity scale offset
icon-color`, `font font-weight font-size text-align line-height`, `transition`,
`page-transition`, `show`. State rules may only change `color`, `background`, `border`,
`icon-color`, `opacity`, `scale` and `offset`; they animate with the element's `transition`.

Units are millimetres: the canvas is scaled 0.001 so a 440 wide view is 44 cm in the world.

## Layout

A flex subset resolved at compile time: one axis per container, `grow` shares leftover space,
`wrap` breaks rows, `anchor` places absolutely. Text is measured with the real font over every
translation so a box fits its widest language; text and bound content shrink first when a row
would overflow. Repeats bake `max` copies and page rather than scroll.

## Inspector

A view's inspector shows the UdonSharp header, a summary line, `Rebuild view` / `Open .aui` /
`Open .cs` buttons, and then only the fields you declared in your own `.cs`. Every baked table
and generated field is hidden; they are outputs of the `.aui` and `.auis` files, not inputs.

## Menu

| Item | What it does |
|---|---|
| `AUI/Rebuild All Views` | queues every `.aui` and builds them, even when unchanged |
| `AUI/Rebuild Language Table` | bakes the merged language files onto the scene's service |
| `AUI/Rebuild Input Panel` | authors the keyboard click target |
| `AUI/Bake Fonts` | builds the atlases and rebuilds views |
| `AUI/Validate` | compiles every view and prints all diagnostics |
| `AUI/Create Settings`, `Create missing program assets`, `Generate Sprites`, `Reimport Icons` | housekeeping |

## How the build works

An asset importer cannot write source files or add a component whose class does not exist yet,
so the `.aui` importer only compiles and records diagnostics. A post-import queue then writes
`Name.g.cs` when its text changed (which recompiles), and after the reload creates the program
asset, compiles UdonSharp, and builds the prefab under a temporary scene root before saving it.
Children build before parents. The queue lives in `SessionState` and resumes after reloads.

Every element index in the generated code comes from the same `AuiIndexMap` walk the builder
uses, so the constants and the baked arrays agree by construction. A repeat template's subtree
is one block of elements per row.

## Diagnostics

Messages are `[AUI] path:line:col code: text`, clickable in the console. Codes are stable:
0xx markup syntax, 1xx markup semantics, 2xx stylesheet, 3xx languages, 4xx layout, 5xx build,
6xx fonts. The list is `Core/Diagnostics/AuiCodes.cs`.

## Tests

`Tests/Editor` holds NUnit edit-mode tests for the parser, stylesheet, cascade, analyzer, layout,
language table and code generator (golden file under `Tests/Editor/Golden`). Run them from the
Test Runner.

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

## Not verified

Everything below compiles, passes the edit-mode tests and runs under play mode in the editor,
where these things cannot be seen:

- VRCTween transitions (inert in ClientSim; the code applies final states instantly there and
  probes for tweens in the real client).
- The VRChat keyboard opening on the input box's second click, on desktop and in VR.
- Laser hover enter/exit reliability on Quest.
- `OnLanguageChanged` being raised by the live client, and PlayerData persistence of the pick.
- Text legibility of the default sizes at 2 m, and atlas memory on Quest.
