# Menu and windows

Everything AUI adds to the Unity editor.

## The AUI menu

| Item | What it does |
|---|---|
| `AUI/Preview` | opens the live preview window (see below) |
| `AUI/Rebuild All Views` | queues every `.aui` and builds them, even when unchanged |
| `AUI/Rebuild Language Table` | merges the language files onto the scene's service, creating it if absent |
| `AUI/Rebuild Input Panel` | (re)builds the invisible keyboard target for `<input>` |
| `AUI/Bake Fonts` | bakes atlases for exactly the glyphs the language files use, then rebuilds views |
| `AUI/Auto-build Views` | a toggle. Off means saved views queue instead of building |
| `AUI/Validate` | compiles every view and prints every diagnostic |
| `AUI/Create Settings` | creates and selects `Assets/AUI/AuiSettings.asset` |
| `AUI/Create missing program assets` | creates the sibling UdonSharp program asset each view needs |
| `AUI/Open Package Folder` | reveals the resolved package root |
| `AUI/Generate Sprites` | regenerates the package's 9-slice PNGs |
| `AUI/Reimport Icons` | reimports the SVG icon set with the right import settings |

The four you will use routinely are **Preview**, **Rebuild All Views**, **Bake Fonts** and
**Validate**.

::: tip Auto-build off
Useful while restructuring several views at once: saves queue up instead of triggering builds and
domain reloads, and the preview window shows how many are waiting. Turn it back on, or run
`Rebuild All Views`, when you are done.
:::

## Creating files

Right-click in the Project window:

| Item | Creates |
|---|---|
| `Create/AUI/View` | `Name.aui` with `name` already filled in |
| `Create/AUI/View with Stylesheet` | that, plus a sibling `Name.auis` |
| `Create/AUI/Stylesheet` | `Name.auis` alone |

You type the name first because a view's `name` attribute must equal the file stem and be a valid
C# identifier.

## The view inspector

Selecting a view prefab or its scene instance shows a compact inspector:

```
Demo.aui: 37 elements, 16 signals, build a3f19c2
```

with **Rebuild view**, **Open .aui** and **Open .cs** buttons.

Only the fields **you** declared in your `.cs` half are shown. All the baked wiring is hidden, so
the inspector stays useful for setting the scene references your handlers need.

If you instead see hundreds of arrays, the view was built by an older version. Rebuild it.

## Preview

`AUI/Preview` builds a view as plain uGUI in the open scene, straight from the files on disk, a
moment after you save: no import, no generated code, no UdonSharp, no prefab. It works while Unity
is unfocused, so you can keep the editor on one monitor and your editor on the other.

It deliberately avoids `Undo` and `SetDirty`, so a prototyping session closes without a save
prompt.

### Controls

| Section | What it offers |
|---|---|
| View | pick the view, or *Follow selection* |
| Live | *Live/Off*, *Rebuild now*, *Frame* |
| Language | preview in any shipped language, or the default |
| Follow edits | shows whichever page or popup contains the element you are editing |
| Pickable in scene | lets you click the preview in the scene view |
| Shown | force any page, select popup or modal visible |
| Prefab | *Build prefab*, and *Auto-build prefabs*, with a count of views waiting |
| Diagnostics | clickable, opening the file at the line |

It also shows the compile and build timings.

The **Shown** section is the part worth remembering: it is how you inspect a hover popup, a modal
or a page you would otherwise have to enter play mode to reach.

## Console output

Diagnostics are clickable and formatted `[AUI] path:line:col code: message`. See
[diagnostics](/reference/diagnostics).

Each build drain reports where the time went:

```
[AUI] drain: 3 view(s), 1 built, 412 ms (scan 0, compile 96 with 2 cached, udonsharp 210, build 106)
```

`udonsharp` is paid once per drain and only when a view is new; `build` is the prefab work.

## Next

- [How it works](/guide/how-it-works) for what the build is doing between those log lines.
- [Testing](/guide/testing) for driving views without a headset.
- [The VS Code extension](/tooling/vscode) for the editing side.
