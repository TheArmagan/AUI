# Troubleshooting

Symptoms, in rough order of how often they come up. Every diagnostic code is listed in
[diagnostics](/reference/diagnostics).

## Nothing appears after saving a `.aui`

Read the console. Diagnostics are `[AUI] path:line:col code: message` and are clickable. Errors
stop the build.

`AUI/Validate` compiles every view and prints everything, including warnings.

## `Name.g.cs` did not update

The build queue only runs when the project compiles. A C# error anywhere blocks it, and the most
common one is a handler with the wrong signature after you changed the markup.

Fix the compile error, then save the `.aui` again or run `AUI/Rebuild All Views`.

## The prefab did not change

Its bake stamp matched, so the builder skipped it. The stamp covers the generated code, every
solved rect (per language), the resolved styles, the runtime rule table and the builder version,
so a real change always rebuilds; `AUI/Rebuild All Views` forces one anyway.

## Boxes instead of letters

The glyph is not in the baked atlases. Run `AUI/Bake Fonts`.

If it is a **player name** or another runtime string in a script you do not control, make sure
`AuiSettings.boundTextSample` still contains a CJK character so the dynamic fallback atlas is set
up and the text box is measured tall enough.

## Every text shows `??`

The scene's language table is older than the views, or there is no language service at all.

The build rebakes the table at the end of every drain **if** the scene already has an
`AUI Language Service`. If it has none, run `AUI/Rebuild Language Table` once for that scene.

## A key shows as its own name

That key is missing from the default language file. The view falls back to showing the key text
itself so you can see which one it is. Add it to `en.json` (or whatever your default is).

## Text does not fit

Give the element a `max-width`, or `wrap="true"` with a `max-width`, or a shorter
`boundTextSample` in the settings if it is bound text.

Remember keyed text is measured against its **widest translation across every language**, so adding
a long German string can reflow a panel that was fine in English.

## `Foo.cs` fails with a duplicate field

You declared a field called `_<binding>`. The generated backing field owns that name. Rename
yours.

Similarly, a binding whose PascalCase name collides with a member of `AuiView` is reported as an
error asking you to pick another name.

## A build takes seconds

Read the drain line:

```
[AUI] drain: 3 view(s), 1 built, 412 ms (scan 0, compile 96 with 2 cached, udonsharp 210, build 106)
```

`udonsharp` is paid once per drain and only when a view is new. `build` is the prefab work per
view. If `scan` is large, something is invalidating the workspace scan repeatedly.

## Nothing is clickable in the client, but it works in the editor

The root canvas lost its `VRCUiShape` or its collider (a rebuild restores both), or a parent
transform is scaling the view.

## A wrist view never appears

Outside VR there is no wrist: the view sits in its `attach-corner` instead. In VR it shows only
while the hand is within 0.6 m of the head and the player is looking at it, so raise the hand
into view. `wristRange`, `wristShowAngle` and `wristHideAngle` on the view's
[`AuiAttach`](/reference/services#auiattach) component widen that. The view also has to be shown:
one with `start-hidden` stays hidden until `Show()`.

## `SetAttach` does nothing

The console says the view was built without attach. Add `attach="none"` to its `<view>` and let it
rebuild: the component that moves the view is baked into the prefab and cannot be added in-world.

## An attached view blocks my laser

It has at least one control, so its collider is on. A display-only attached view has its collider
turned off. Either drop the controls, or move the view out of the way with `attach-corner`,
`attach-distance` or the offsets on its `AuiAttach` component.

## An attached view stays where its parent put it

It is nested inside another view, which also reports AUI123. Only an outermost view can follow
the player; place it in the scene on its own.

## The inspector shows hundreds of arrays

The view was built by an older version of the package. Rebuild it and the per-view inspector takes
over again.

## Two `ArgumentNullException: unityObject` lines per rebuild

UdonSharp's serializer reacting to a loaded prefab being reimported. Harmless.

## An `<input>` does nothing

The scene needs an `AUI Input Panel`: run `AUI/Rebuild Input Panel`.

In the real client it takes two clicks by design. The first parks the invisible target in front of
the player, the second opens VRChat's keyboard. There is no Udon API to open it directly.

## A class I add at runtime does nothing

Two possible causes:

- **The class has no rule.** A class used only from code still needs a rule in some sheet,
  otherwise there is nothing to apply.
- **It only sets bake-only properties.** `radius`, `font-weight`, `page-transition` and `show`
  cannot change in-world; this is warning AUI212.

For a star class the view never mentions in markup, list it on the root so it gets baked:
`<view utilities="p-4 p-8">`.

## An icon or sprite will not swap

`SetIcon`, `SetBackground` and `SetMaterial` can only use assets baked into the view: ones its
markup or sheets mention, plus anything preloaded on the root element
(`<view icons="check star" sprites="Rounded16" materials="GlowHot">`).

## `CreateElement` returns -1

The pool or template is exhausted; every parked copy is in use. Raise `max`, or destroy instances
you no longer need. Always check for `-1`.

## Position or references reset after a rebuild

This only happened before the stable-id builder. Do `Prefab > Revert All` on the instance once, set
things again, and they will stick.

## Still stuck

Ask in the [Discord](https://discord.gg/armagan). Bring the console line, the `.aui` and what you
expected.
