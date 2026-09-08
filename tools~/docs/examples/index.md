# Examples

The package ships a **Demo** sample with two things in it: a settings panel, and a ten-page gallery
that is also the runtime test suite.

Import it from the Package Manager, or copy `Views/` to `Assets/AUI/Demo/` and `lang/` to
`Assets/AUI/Lang/`.

## What is in the sample

| | |
|---|---|
| [Settings panel](/examples/settings-panel) | a three-tab settings dialog with sliders, a toggle, a dropdown, a text input, the language picker and a confirm modal, in English, Turkish and Japanese. Paired with a world script to show both directions of communication |
| [The gallery](/examples/gallery-pages) | a hub with a sidebar of tabs and one nested view per page, each demonstrating one part of AUI |
| [Recipes](/examples/recipes) | short self-contained snippets to copy |

## Running it

1. Import the sample.
2. `AUI/Create Settings` if the project has none, then `AUI/Bake Fonts`.
3. Let the importer build the prefabs. Watch for `[AUI] built ...`; children build before parents.
4. `AUI/Rebuild Input Panel` in the scene you place them in. The language table is rebaked by the
   build itself once the scene has an `AUI Language Service`; `AUI/Rebuild Language Table` creates
   one.
5. Drop `SettingsDemo.prefab` and `AuiGallery.prefab` into the scene, about 1.2 m up and facing
   the player.

![The gallery running in the editor](/img/gallery-hub.png){.shot}

## The gallery is a test suite

Its `RunTests` method (also the `_TestRun` custom event) drives every child page through its typed
API and checks what actually rendered, logging `[AUI gallery] PASS name` per check.

Press **Run tests** in play mode after an update to confirm your setup is healthy. See
[testing](/guide/testing).

## Reading the sources

The gallery is the best-commented AUI code there is, and every page is deliberately small. If you
are looking for how to do something specific, the [gallery page index](/examples/gallery-pages) maps
each feature to the file that demonstrates it.
