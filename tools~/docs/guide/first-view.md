---
description: A greeting panel end to end - the markup, the translation keys, the files the build produces, and the code that drives it.
---

# Your first view

A greeting panel with a button, end to end. It assumes you have done the
[one-time setup](/guide/installation#one-time-project-setup).

## Create the file

Right-click a folder in the Project window, `Create > AUI > View`, and type `Hello`.

You are asked for the name first because a view's `name` attribute must equal its file stem and
must be a valid C# identifier. `View with Stylesheet` also creates `Hello.auis` next to it.

## Write the markup

`Hello.aui`:

```aui
<view name="Hello" width="300" height="140">
  <text key="hello.title" class="title"/>
  <text key="hello.greeting" args="{playerName}"/>
  <panel class="row">
    <spacer/>
    <button onclick="Wave" key="hello.wave" icon="hand"/>
  </panel>
</view>
```

Reading it line by line:

- `width` and `height` are in **millimetres**. A view is a world-space panel, and 300 x 140 mm is
  a comfortable reading size at arm's length.
- `key="hello.title"` is a translation key, not text. `class="title"` comes from the built-in
  theme, which makes it 26 mm and semibold.
- `args="{playerName}"` fills the `{playerName}` placeholder in that key's string. The braces
  declare a **binding**, which becomes a C# property.
- `class="row"` is another theme class: horizontal, centred, 12 mm gap.
- `<spacer/>` grows to fill the leftover space, pushing the button to the right.
- `onclick="Wave"` names a method you are about to write. `icon="hand"` is a bundled Lucide icon.

## Add the translation keys

`Assets/AUI/Lang/en.json`:

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


## Save, and watch the console

```
[AUI] wrote Assets/UI/Hello.g.cs
[AUI] scaffolded Assets/UI/Hello.cs
... script reload ...
[AUI] built Assets/UI/Hello.prefab
```

Your one file has become five, all next to each other:

| File | What it is |
|---|---|
| `Hello.aui` | your markup |
| `Hello.g.cs` | generated. Constants, binding properties, dispatch. Never edit it. |
| `Hello.cs` | scaffolded once, with an empty method per handler. **Your code goes here.** |
| `Hello.asset` | the UdonSharp program asset, pointing at `Hello.cs` |
| `Hello.prefab` | the built panel |

The reload in the middle is unavoidable: an asset importer cannot write a `.cs` file and then add
a component of that not-yet-existing class in one pass. See [how it works](/guide/how-it-works).

## Write the logic

`Hello.cs` was scaffolded with one empty method per handler:

```csharp
using UnityEngine;
using VRC.SDKBase;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    public partial class Hello : AuiView
    {
        public void Wave()
        {
        }
    }
}
```

Fill it in, and set the binding:

```csharp
public partial class Hello : AuiView
{
    protected override void OnStart()
    {
        PlayerName = Networking.LocalPlayer.displayName;
    }

    public void Wave()
    {
        PlayerName = "waving " + PlayerName;
    }
}
```

`PlayerName` exists because `{playerName}` appeared in the markup. Setting it repaints every
element that uses it. `OnStart` is AUI's hook, called once after the view has wired itself up;
use it rather than `Start`.

::: tip Player names are escaped for you
String bindings are escaped on the way in: a `<` becomes `<` plus a zero-width space, so a player
called `<color=red>Bob` renders literally instead of colouring your panel. See
[bindings](/guide/bindings#escaping).
:::

## Put it in the scene

Drag `Hello.prefab` into the scene, about 1.2 m up and facing the player spawn.

Its inspector shows only the fields **you** declared in `Hello.cs`. All the baked wiring is
hidden, with a summary line and *Rebuild view*, *Open .aui* and *Open .cs* buttons instead.

## Iterate

Save the `.aui` and the prefab rebuilds. You do not need to touch the scene instance.

For fast iteration, open `AUI/Preview`: it builds the view straight from the files on disk a moment
after you save, with no import, no codegen and no prefab, and it works while Unity is unfocused.
See [menu and windows](/reference/menu#preview).

## What to read next

- [The logic half](/guide/the-logic-half) for what the generated class gives you.
- [Bindings](/guide/bindings) for the binding types and two-way controls.
- [Styling](/guide/styling) to stop relying on the theme's classes.
