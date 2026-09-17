# AUI demo

Two things:

- `Views/SettingsDemo.*`: a settings panel with three tabs (sliders, a toggle, a dropdown, a text
  input, the language picker) and a reset dialog, in English, Turkish and Japanese, paired with
  `DemoBrightness.cs` to show a world script talking to a view in both directions. Its sheet is
  the sibling `SettingsDemo.auis`.
- `Views/Gallery/*`: the **gallery**, a hub with a sidebar of tabs and one nested view per page.
  Every page links the shared `Showcase.auis` and keeps its one-off rules in a `<style>` block.

| Page | View | What it shows |
|---|---|---|
| Overview | `HomeDemo` | an inline `<component>`, stat tiles, a progress bar whose width is a bound style value, a once-a-second timer, a typed `navigate` event the hub routes to `SelectMainPage` |
| Texts | `TextsDemo` | keyed, placeholder, literal, wrapped (`max-lines`, `ellipsis`) and `runtime` text, the escape path, `SetTextAlign`, the size utilities |
| Controls | `ControlsDemo` | two sliders (one with `decimals`, one disabled by a binding), a dropdown, a dropdown filled at runtime through `max`, a keyboard input, a labelled toggle, a button with `onhover`/`onleave`, an `if` banner |
| Lists | `ListDemo` | a leaderboard repeat whose rows are buttons with a toggle inside (`FavChanged(int row, bool value)`), a chip grid, shuffle and sort, `<empty>`, one solve per batch |
| Layout | `LayoutDemo` | a stage whose `flex-direction`, `justify`, `align`, `wrap`, `gap` and `padding` are bound style values; `SetActiveElem`, `AddClass` for `grow`, `position: absolute`, the solved rects read back |
| Styles | `StatesDemo` | hover, pressed, selected and disabled looks from three tiers of sheet, offset, opacity, bounce and border transitions, then a card restyled by a class binding, bound `background`/`width`/`opacity`, and utilities toggled at runtime |
| Theme | `ThemeDemo` | five presets that rewrite the `:root` palette with `SetStyleVariable`, a hue slider for the accent, a density slider for `--pad`, a lift slider for `--card`; a switch chooses this page or, through the language service, every view in the world |
| Runtime | `RuntimeDemo` | a `<template>` of chat lines, a `pool` of texts, a `<template>` holding a nested `PlayerCard` whose typed `kicked` event comes back with the instance element, `MoveElement`, `DestroyElement`, a confirm `<modal>` |
| Language | `LangDemo` | the shipped picker, the service's read-backs, a sentence with three placeholders |
| Log | (the hub) | everything the pages emitted, newest first, in a repeat |

`Views/Gallery/AttachDemo.*` sits in the same folder and links the same sheet, but it is a view of
its own and not a gallery page: `attach` only works on an outermost view, and the builder drops it
from a nested one (warning `AUI123`). Its markup says `attach="none"`, which bakes the `AuiAttach`
component, and its buttons call `SetAttach` for each mode (none, left wrist, right wrist, corner,
follow, front), `SetAttachCorner` and `SetAttachDistance`. Drop `AttachDemo.prefab` into the scene
next to the gallery. The self-test below does not cover it, and attach has not been run in the
real client yet.

The hub (`AuiGallery`) also carries a self-test (`RunTests`, also reachable as the `_TestRun`
custom event) that drives each child through its typed API and checks what actually rendered:
bound text, escaping, placeholders, slider value and label, toggle, `if`, select, runtime
options, disabled and enabled buttons, child `Emit`, repeat count and visibility, a layout
re-solve, state flags, a class binding, a style variable round trip, template and nested-view
creation, page selection and a language round trip. Every result is logged in-world and to the
console as `[AUI gallery] PASS/FAIL name`. Read it as the worked example of connecting views
together.

Import the sample from the Package Manager, or copy `Views/` to `Assets/AUI/Demo/` and `lang/`
to `Assets/AUI/Lang/`. Then:

1. `AUI/Create Settings` if the project has none.
2. `AUI/Bake Fonts`.
3. Let the importer build the prefabs (watch the console for `[AUI] built ...`; children build
   before the gallery).
4. `AUI/Rebuild Input Panel` in the scene you place them in. The language table is rebaked by the
   build itself once the scene has an `AUI Language Service`; `AUI/Rebuild Language Table`
   creates one.
5. Drop `SettingsDemo.prefab` and `AuiGallery.prefab` into the scene, about 1.2 m up, facing the
   player. `AttachDemo.prefab` goes in the same way, as its own object.

`SettingsDemo.cs` ends with `_Test*` methods that can be sent as custom events from the editor
to switch languages and pages without a player; the gallery's `_TestRun` runs the whole suite.
