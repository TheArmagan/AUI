# Menu and windows

Everything AUI adds to the Unity editor.

## The AUI menu

| Item | What it does |
|---|---|
| `AUI/Preview` | opens the live preview window (see below) |
| `AUI/Rebuild All Views` | queues every `.aui` and builds the ones whose prefab is out of date, plus the views that nest one of those |
| `AUI/Force Rebuild All Views` | queues every `.aui` and builds all of them, even when unchanged |
| `AUI/Rebuild Selected Views` | force-rebuilds the views the selection stands for (see below), then the views that nest them |
| `AUI/Bake Fonts` | bakes atlases for exactly the glyphs the language files use, then rebuilds views |
| `AUI/Auto-build Views` | a toggle. Off means saved views queue instead of building |
| `AUI/Build Status` | opens the build status window (see below) |
| `AUI/Rebuild Language Table` | merges the language files onto the scene's service, creating it if absent |
| `AUI/Rebuild Input Panel` | (re)builds the invisible keyboard targets for `<input>`: the text field and VRChat's URL field |
| `AUI/Validate` | compiles every view and prints every diagnostic |
| `AUI/Create Settings` | creates and selects `Assets/AUI/AuiSettings.asset` |
| `AUI/Create missing program assets` | creates the sibling UdonSharp program asset each view needs |
| `AUI/Open Package Folder` | reveals the resolved package root |
| `AUI/Generate Sprites` | regenerates the package's 9-slice PNGs |
| `AUI/Reimport Icons` | reimports the SVG icon set with the right import settings |

The ones you will use routinely are **Preview**, **Rebuild All Views**, **Rebuild Selected
Views**, **Bake Fonts** and **Validate**.

`Rebuild All Views` decides per view from its bake hash, which covers everything that shapes the
prefab (generated code, layout, styles, keys, the rule table, the builder version). Use
`Force Rebuild All Views` only when you suspect a prefab is stale without its hash knowing it.

::: tip Auto-build off
Useful while restructuring several views at once: saves queue up instead of triggering builds and
domain reloads, and the preview window shows how many are waiting. Turn it back on, or run
`Rebuild All Views`, when you are done.
:::

## Rebuilding one view

`AUI/Rebuild Selected Views` is also on two context menus:

| Where | Item | Selection it accepts |
|---|---|---|
| Project window | `AUI/Rebuild View` | a `.aui`, any of its generated files (`Name.prefab`, `Name.cs`, `Name.g.cs`, `Name.asset`), or a folder (every view under it) |
| Hierarchy | `AUI/Rebuild View` | any object with a view on it or above it |

The selected views are rebuilt even when their hash matches. Any view that nests a rebuilt one is
rebuilt after it, because the parent's prefab holds overrides on objects inside the child that the
child's rebuild replaced. Nothing else in the queue is forced. The view inspector's **Rebuild
view** and the preview window's **Build prefab** do the same for one view.

## Build status

`AUI/Build Status` shows the build queue and the last drain, and survives domain reloads.

- **Queue**: every view waiting, whether it is forced, its attempt count and why it is still
  waiting (generated source compiling, a child prefab missing, a failed build).
- **Last drain**: per view, built / skipped (hash matched) / waiting / failed, with the time of each
  phase of a build (load, tree, tables, finish, audit, save, revert) and **Rebuild**, **Prefab**
  and **Open** buttons.
- The toolbar has Rebuild All, Force Rebuild All, Rebuild Selected and the Auto-build toggle.

## After-build hooks

Code that writes its own wiring into an AUI prefab or its scene instance loses it when the view is
rebuilt. Mark a static method with `[AuiAfterBuild]` and AUI calls it once the queue is empty:

```csharp
using Armagan.AUI.Editor;
using UnityEngine;

static class MyWallHooks
{
    // Runs only when one of these views was actually rebuilt.
    [AuiAfterBuild("ResultCard", "AuthorCard", Order = 0)]
    static void RewireWall(AuiBuildReport report)
    {
        if (GameObject.Find("Wall") == null) return;   // not in this scene
        MyWallBuilder.Rebuild();
    }
}
```

- The method is static and takes one `AuiBuildReport` (or nothing). `report.BuiltViews` and
  `report.BuiltPaths` list what was rebuilt since the hooks last ran; `report.AnyBuilt(...)` tests
  for names.
- With no view names the hook runs after any build. A view that was skipped because its hash
  matched does not count as built.
- Hooks run once per finished build, after the language table, never between the two halves of a
  build that waits for a domain reload. Lower `Order` runs first.
- A hook that throws is logged and the others still run. A hook that starts another AUI build does
  not re-enter the hooks; that build's views are reported the next time they run.
- Guard on the scene: a hook runs in whatever scene is open.

For code that is not a static method, `AuiBuildQueue.Built` is the same report as an event.

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
[AUI] drain: 3 view(s), 1 built, 412 ms (scan 0, compile 96 with 2 cached, udonsharp 210, build 106 of which batched import 60, language table 6, hooks 0)
```

`udonsharp` is paid once per drain and only when a view is new; `build` is the prefab work, and
`batched import` the part of it Unity spends importing the saved prefabs and updating every prefab
and scene instance that nests them. Each `built` line carries the same split per view.

## Next

- [How it works](/guide/how-it-works) for what the build is doing between those log lines.
- [Testing](/guide/testing) for driving views without a headset.
- [The VS Code extension](/tooling/vscode) for the editing side.
