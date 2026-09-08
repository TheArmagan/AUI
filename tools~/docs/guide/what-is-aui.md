# What AUI is

AUI turns an HTML-like view file and a CSS-like stylesheet into a Unity uGUI prefab with
UdonSharp behaviours on it. You write the markup, AUI writes the wiring.

```aui
<view name="Hello" width="300" height="140">
  <text key="hello.title" class="title"/>
  <button onclick="Wave" key="hello.wave" icon="hand"/>
</view>
```

Saving that file produces three things next to it: `Hello.g.cs` (generated, never edited by hand),
`Hello.cs` (scaffolded once, where your logic goes) and `Hello.prefab` (the built panel). Drop the
prefab in your scene.

## The two ideas behind it

**Everything is baked.** Parsing, the stylesheet cascade and the flex layout all happen in the
Unity editor. The markup never ships to the world. What ships is a plain uGUI hierarchy plus a few
integer tables, and the view re-runs those tables in Udon when something changes. That is why a
view can restyle and re-lay-out itself in-world without ever parsing a string there.

**Every visible string is a translation key.** Not an option you switch on later, but the default
path. Players change language in-game and every live view repaints; fonts are baked containing
exactly the glyphs your language files use.

## What it is not

It is **not a runtime UI framework**. No element is created from markup in-world. A `<repeat>`
bakes a fixed number of rows and you show as many as you need; a `<template>` bakes parked copies
you link in and out. If a panel might show 12 rows, 12 rows exist in the prefab from the start.

It is **not networked**. Every generated behaviour is `BehaviourSyncMode.None` and nothing about a
view goes on the wire. Syncing is your world's job; a view is a local display of whatever state you
give it.

It is **not a general layout engine**. It implements a small flex subset: direction, gap, padding,
margin, justify, align, grow, wrap, min/max sizing, and absolute positioning. There is no grid, no
z-index, no floats.

## What you get for that

- **Typed code in both directions.** Each binding becomes a C# property and each handler a method
  with a real signature, on a generated partial class. Another Udon script holds a reference to
  `SettingsPanel` and writes `panel.Brightness = 70f`. No `SetProgramVariable`, no strings.
- **Diagnostics before you press play.** Unknown tags, unknown properties, missing translation
  keys, a handler whose signature does not match, a class no sheet defines, layout overflow. Each
  has a stable code and a file, line and column.
- **A cheap runtime.** A roughly 40-element view re-solves in about a millisecond in the editor's
  Udon interpreter, and only when something marked it dirty, at most once per frame.

## Is it right for your world?

It suits panels: settings menus, shops, leaderboards, info kiosks, admin tools. Anything with a
fixed shape, a known maximum number of rows, and text that should not be English-only.

It suits less well anything whose structure is genuinely unknown until runtime and unbounded, or
anything needing per-player synced UI state, since you would be building that layer yourself
anyway.

## Where to go next

- [Installation](/guide/installation) to get it into a project.
- [Your first view](/guide/first-view) for the full walkthrough of one panel.
- [How it works](/guide/how-it-works) if you want the pipeline before the recipes.
- [Elements](/reference/elements) if you would rather read the vocabulary.
