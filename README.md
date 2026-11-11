# AUI

A UI engine for VRChat worlds written in UdonSharp. You write a view as an HTML-like `.aui` file
plus a small CSS-like `.auis` stylesheet, and the Unity editor compiles them into a plain uGUI
prefab with a generated UdonSharp class. Every visible string is a translation key, and the
player's language switches in-game without a rebuild.

Nothing is parsed in-world. The layout (a flex subset) and the stylesheet cascade are baked into
tables that the view re-solves in Udon, so texts, classes, visibility, style values and created
elements can still change at runtime.

- Package id `rest.armagan.aui`, runtime namespace `Armagan.AUI`.
- Unity 2022.3, VRChat Worlds SDK 3.10.4 or newer (UdonSharp and TextMeshPro come with it).
- Documentation: <https://auidocs.thearmagan.workers.dev>. Support: <https://discord.gg/armagan>.

## Install

**VCC or ALCOM.** Add this repository listing (Settings, Packages, Add Repository), then add
**AUI** to your world project:

```
https://thearmagan.github.io/AUI/index.json
```

**Without VCC.** Download `rest.armagan.aui-X.Y.Z.unitypackage` from the
[latest release](https://github.com/TheArmagan/AUI/releases/latest) and import it into a project
that already has the VRChat Worlds SDK. The release also has the VPM `.zip` and the VS Code
extension (`aui-X.Y.Z.vsix`).

Then, once per project, from the `AUI` menu:

1. `AUI/Create Settings` makes `Assets/AUI/AuiSettings.asset` and `Assets/AUI/Lang/`.
2. Put one `<code>.json` per language in `Assets/AUI/Lang/` (at least the default, `en.json`).
3. `AUI/Bake Fonts` bakes atlases holding exactly the characters those files use.
4. In each scene: `AUI/Rebuild Language Table` and `AUI/Rebuild Input Panel` create the two scene
   services views find by name.

The [installation guide](https://auidocs.thearmagan.workers.dev/guide/installation) has the
details and a check that everything works.

## A view

`Assets/UI/Hello.aui`:

```xml
<view name="Hello" width="300" height="140">
  <text key="hello.title" class="title"/>
  <text key="hello.greeting" args="{playerName}" class="muted"/>
  <button onclick="Wave" key="hello.wave" icon="sparkles"/>
</view>
```

`Assets/AUI/Lang/en.json`:

```json
{
  "_name": "English",
  "hello": { "title": "Hello", "greeting": "Hi {playerName}", "wave": "Wave" }
}
```

Saving the `.aui` builds it. After the editor's reloads there are four files next to it:
`Hello.g.cs` (generated, never edit it), `Hello.cs` (yours, scaffolded once with an empty method
per handler), `Hello.asset` (the UdonSharp program) and `Hello.prefab`, which you place in the
scene. The logic goes in `Hello.cs`; bindings are properties and handlers are methods:

```csharp
using VRC.SDKBase;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    public partial class Hello : AuiView
    {
        protected override void OnStart()
        {
            PlayerName = Networking.LocalPlayer.displayName;   // re-renders "Hi {playerName}"
        }

        public void Wave() { Hide(); }
    }
}
```

Units are millimetres (a 300 wide view is 30 cm in the world). Other scripts reach a view through
its generated class (`public Hello hello;` then `hello.PlayerName = "..."`), and the view reaches
other scripts through ordinary public fields in its `.cs` half.

`Samples~/Demo` (import it from the Package Manager's Samples tab) is a settings panel, a store
panel and a gallery of every feature, in three languages. The gallery also carries a self-test you
run in play mode.

## What it does

- Elements: `panel`, `text`, `image`, `rawimage`, `icon` (the Lucide set ships in `Icons/`),
  `button`, `toggle`, `slider`, `colorpicker`, `input`, `select`, `tabs` and `pages`, `repeat`,
  `template` and `slot` (rows and elements created at runtime from parked copies), `modal`,
  inline `component`s, and other views nested by name.
- Stylesheet: tag, class and state selectors (`:hover`, `:pressed`, `:selected`, `:disabled`),
  `:root` variables that can be changed at runtime for one view or the whole world, transitions,
  and built-in utility classes (`*flex-row *gap-8 *p-12 ...`). No descendant selectors.
- Languages: one JSON file per language, nested keys flattened, `{placeholders}`, a built-in
  language picker, right-to-left text and Arabic shaping baked at build time. The language starts
  from the VRChat client's and a pick is saved in PlayerData.
- `attach` keeps a view on the player's wrist, in a screen corner, or floating in front of them.
- Editor: a live preview window that rebuilds a copy of a view on every save without a reload,
  per-view or full rebuilds, a build status window with timings, after-build hooks for your own
  editor code, and diagnostics with stable codes (`[AUI] path:line:col AUI123: ...`).
- A VS Code extension (in each release) with diagnostics, completion for keys, classes and icons,
  go to definition, rename and quick fixes, running the same compiler as the Unity importer.

## Limits

- A text's font weight and new materials cannot change in-world: Udon has no API for them. A
  class that sets a weight is applied at bake and warns when it is only reached at runtime.
- Text is re-measured at runtime through uGUI's `LayoutUtility`, because TextMeshPro's measurement
  calls are not exposed to Udon. A text on an inactive page is measured when the page opens.
- Lists page instead of scrolling: a `repeat` bakes `max` copies (1 to 64) and lays out the live
  rows.
- Only the first four `args` of a text are substituted.
- Han unification: a character shared by Japanese, Korean and Chinese renders with the Japanese
  glyph.
- Building is not instant. Saving a view writes generated C#, which reloads the editor's scripts,
  then compiles UdonSharp and saves the prefab. A full forced rebuild of a 23-view world took 36 s
  on the author's machine; most of it is Unity updating the prefabs and scene instances that nest
  the rebuilt ones.
- Rebuilding a prefab that a scene instance uses can log two `ArgumentNullException: unityObject`
  errors from UdonSharp's serializer. The prefab, its instance and the Udon heap are correct
  afterwards.

## Documentation in this repository

- `USAGE.md`: recipes for building panels.
- `REFERENCE.md`: the full specification, for changing the package itself (pipeline, grammar,
  generated-code contract, measured platform facts, known issues).
- `tools~/docs/`: the source of the documentation site.

## Working on AUI

Clone the repository and point a directory junction from a world project's `Packages/` at it:

```
mklink /J "<world>\Packages\rest.armagan.aui" "<path to this checkout>"
```

Unity warns that the path is a symlink and turns off directory monitoring for it, so edits made
outside Unity are picked up when the editor is focused. Tests are NUnit edit-mode tests in
`Tests/Editor` (Test Runner, assembly `Armagan.AUI.Tests.Editor`); the gallery under
`Samples~/Demo/Views/Gallery` is the runtime regression suite. The VS Code extension and its
language server are under `tools~/vscode-aui` and `tools~/aui-lsp` (`install.ps1` builds and
installs both).

Releases: set `version` in `package.json`, commit, and push a tag `vX.Y.Z` with the same number.
`.github/workflows/release.yml` builds the `.zip`, the `.unitypackage` and the `.vsix`, publishes
the release, and rebuilds the VPM listing on GitHub Pages.

## Licence

Copyright (c) Kıraç Armağan Önal. Licensed under the Mozilla Public License 2.0; the full text is in
[`LICENSE`](LICENSE).

In practice: you can use AUI in any world, free or paid, and your own views, scripts and the rest
of your project stay yours under whatever terms you like. If you distribute AUI with changes to
its own files, those changed files have to stay under the MPL and their source has to be
available. This paragraph is a summary, not the licence; the `LICENSE` file is what applies.

The bundled fonts (Inter, Noto Sans) and icons (Lucide, one Simple Icons logo) are not AUI's and
keep their own licences (SIL OFL 1.1, ISC, CC0); `THIRD_PARTY_NOTICES.md` lists which file is
under which.
