---
description: Requirements, installing AUI from its VPM listing or a GitHub release, and the four one-time setup steps that give a project settings, languages, fonts and its scene services.
---

# Installation

Get the latest AUI from the [GitHub releases](https://github.com/TheArmagan/AUI/releases). Every
release has the same package in two forms, plus the VS Code extension:

| File | Use it for |
|---|---|
| `rest.armagan.aui-X.Y.Z.zip` | the VPM package (VCC and ALCOM install this one for you) |
| `rest.armagan.aui-X.Y.Z.unitypackage` | a plain import, without VCC |
| `aui-X.Y.Z.vsix` | the [VS Code extension](/tooling/vscode) |

Questions and bug reports go to the [Discord](https://discord.gg/armagan).

## Requirements

| | |
|---|---|
| Unity | 2022.3 (the version VRChat currently targets) |
| VRChat Worlds SDK | 3.10.4 or newer, with UdonSharp |
| TextMeshPro | included with the SDK |

AUI needs `com.vrchat.worlds` and UdonSharp. Through VCC the SDK comes in as a dependency; with
the `.unitypackage`, install the VRChat SDK **before** importing AUI, or the import gives you a
wall of missing-type errors rather than a clear message.

## Install with VCC or ALCOM (recommended)

1. Add the AUI listing. Either open
   [thearmagan.github.io/AUI](https://thearmagan.github.io/AUI/) and press **Add to VCC**, or in
   VCC / ALCOM go to Settings, Packages, Add Repository and paste:

   ```
   https://thearmagan.github.io/AUI/index.json
   ```

2. Open your world project in VCC / ALCOM, find **AUI** in the package list and add it.
3. Open the project in Unity and let it finish compiling. An `AUI` menu appears in the menu bar.

The package lands in `Packages/rest.armagan.aui`. **Updating** is the same list: pick the newer
version. Your own `.aui`, `.auis`, `.cs` and language files live under `Assets/` and are never
touched.

## Install from the .unitypackage

1. Download `rest.armagan.aui-X.Y.Z.unitypackage` from the latest
   [release](https://github.com/TheArmagan/AUI/releases/latest).
2. Open your VRChat world project, `Assets > Import Package > Custom Package`, pick the file and
   import everything. (Double-clicking the file with the project open does the same.)
3. Let Unity finish compiling. An `AUI` menu appears in the menu bar.

The files import to `Packages/rest.armagan.aui`, the same place VCC puts them. To update, import
the newer `.unitypackage` over the top. A `.unitypackage` cannot carry the `Samples~` folder in a
way Unity's package manager recognises; if **Demo** does not appear under Samples, take the
`Samples~/Demo` folder from the release `.zip`.

If the `AUI` menu does not appear, the install did not compile. Check the console for errors, and
confirm the SDK is present.

::: tip After an update
After a version that changes what the prefab builder emits, existing prefabs rebuild on their own
(the builder version is part of each prefab's bake stamp). `AUI/Rebuild All Views` runs that
check for every view at once.
:::

## One-time project setup

Four steps, once per project (and two of them once per scene).

### 1. Create the settings asset

`AUI/Create Settings` creates `Assets/AUI/AuiSettings.asset` and an `Assets/AUI/Lang/` folder. The
asset holds the language folder path, the default language code, the extra folders searched for
views and icons, and the font configuration. One per project, found by type.

### 2. Add at least one language file

Every visible string is a translation key, so a view cannot build without a default language.
Create `Assets/AUI/Lang/en.json`:

```json
{
  "_name": "English",
  "hello": {
    "title": "Hello",
    "greeting": "Hi {playerName}",
    "wave": "Wave"
  }
}
```

`_name` is what the language picker shows. Nesting is only for your convenience: the file is
flattened, so the key above is `hello.title`. The default language code is on the settings asset
(`defaultLanguage`, `en` out of the box).

See [Languages](/guide/languages) for placeholders, fallbacks and adding more languages.

### 3. Bake the fonts

`AUI/Bake Fonts` reads every language file and bakes font atlases containing exactly the glyphs
those files use, into `Assets/AUI/Fonts/`, then assigns the resulting `AuiFontSet.asset` to the
settings.

Re-run it whenever a language file gains characters the atlases do not have. Boxes instead of
letters in-world is the sign that you need to.

### 4. Create the scene services

In each scene that shows a view:

- `AUI/Rebuild Language Table` creates the `AUI Language Service` object and bakes the merged
  language files onto it.
- `AUI/Rebuild Input Panel` creates the `AUI Input Panel`, the invisible click target that
  `<input>` elements need to open VRChat's keyboard.

Views find both by name at start, because a prefab cannot hold a reference to a scene object.

::: warning A scene with views but no language service
Every text renders as `??`. The build rebakes the table after each prefab build **if** the scene
already has a service; if it has none, nothing creates one for you. Run
`AUI/Rebuild Language Table` once per scene.
:::

## Editor tooling

Optional, and worth the two minutes: the VS Code extension gives diagnostics as you type,
completion for translation keys, classes and icons, go to definition, rename across views and
language files, and quick fixes that create a missing key or handler for you.

See [the VS Code extension](/tooling/vscode) for how to install it.

## Verify the setup

Import the **Demo** sample and check it builds:

1. Import the sample (it ships with the package).
2. Watch the console for `[AUI] built ...` lines; children build before parents.
3. `AUI/Rebuild Input Panel` in the scene you place it in.
4. Drop `AuiGallery.prefab` into the scene, about 1.2 m up and facing the player.

The gallery is also the runtime test suite. In play mode, press *Run tests* and every check logs
`[AUI gallery] PASS` to the console. See [the gallery](/examples/gallery-pages).

Next: [your first view](/guide/first-view).
