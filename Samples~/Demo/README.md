# AUI demo

A settings panel with three tabs (sliders, toggles, a dropdown, a text input and the language
picker) in English, Turkish and Japanese.

Import the sample from the Package Manager, or copy `Views/` to `Assets/AUI/Demo/` and `lang/`
to `Assets/AUI/lang/`. Then:

1. `AUI/Create Settings` if the project has none.
2. `AUI/Bake Fonts`.
3. Let the importer build `SettingsDemo.prefab` (watch the console for `[AUI] built ...`).
4. `AUI/Rebuild Language Table` and `AUI/Rebuild Input Panel` in the scene you place it in.
5. Drop `SettingsDemo.prefab` into the scene, 1.5 m up, facing the player.

`SettingsDemo.cs` ends with `_Test*` methods that can be sent as custom events from the editor
to switch languages and pages without a player.
