# AUI demo

Two things:

- `Views/SettingsDemo.*`: a settings panel with three tabs (sliders, toggles, a dropdown, a text
  input and the language picker) in English, Turkish and Japanese, paired with
  `DemoBrightness.cs` to show a world script talking to a view in both directions.
- `Views/Gallery/*`: the **gallery**, a hub view that nests five feature views on tabbed pages
  (texts, controls, lists, states, language), routes every child `Emit` into a log page, and
  carries a self-test (`RunTests`, also reachable as the `_TestRun` custom event) that drives each
  child through its typed API and checks what actually rendered. Seventeen checks; every result
  is logged in-world and to the console as `[AUI gallery] PASS/FAIL name`. Read it as the worked
  example of connecting views together.

Import the sample from the Package Manager, or copy `Views/` to `Assets/AUI/Demo/` and `lang/`
to `Assets/AUI/lang/`. Then:

1. `AUI/Create Settings` if the project has none.
2. `AUI/Bake Fonts`.
3. Let the importer build the prefabs (watch the console for `[AUI] built ...`; children build
   before the gallery).
4. `AUI/Rebuild Input Panel` in the scene you place them in. The language table is rebaked by the
   build itself once the scene has an `AUI Language Service`; `AUI/Rebuild Language Table`
   creates one.
5. Drop `SettingsDemo.prefab` and `AuiGallery.prefab` into the scene, about 1.2 m up, facing the
   player.

`SettingsDemo.cs` ends with `_Test*` methods that can be sent as custom events from the editor
to switch languages and pages without a player; the gallery's `_TestRun` runs the whole suite.
