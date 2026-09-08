# The logic half

Every view is one C# class split across two files. `Name.g.cs` is generated from the markup and
rewritten on every save; `Name.cs` is scaffolded once and is yours forever. They are `partial`
halves of the same class, in namespace `Armagan.AUI.Generated`.

```csharp
namespace Armagan.AUI.Generated
{
    public partial class Shop : AuiView
    {
        // your fields, your handlers, your overrides
    }
}
```

## What the generated half gives you

For markup like this:

```aui
<view name="Shop" width="440" height="360">
  <text key="shop.title" class="title"/>
  <text key="shop.owned" args="{count}"/>
  <slider bind="{volume}" min="0" max="100" onchange="VolumeChanged"/>
  <button id="buy" onclick="Buy" key="shop.buy" disabled="{soldOut}"/>
</view>
```

you get:

```csharp
public const int E_BUY = 7;      // element index, for SetText / AddClass / ...
public const int S_BUY = 2;      // signal id, for SetEnabled / IsSelected

public string Count    { get; set; }   // repaints the text that uses it
public float  Volume   { get; set; }   // moves the slider, no handler fired
public bool   SoldOut  { get; set; }   // enables/disables the button
```

and it calls `Buy()` and `VolumeChanged(float value)` on you.

The full list of what gets emitted, section by section, is in
[generated code](/reference/generated-code).

## The rules that follow

**A binding becomes a PascalCase property.** `{playerName}` gives `PlayerName`. Setting it from
code updates every element that uses it and does **not** call your handler. A player moving the
control does both.

**A handler becomes a method you must write.** Its signature is fixed by where it is used, and the
scaffolder writes the empty stub for you. If you change the markup so a signature changes, fix the
method: the project will not compile until you do, and the queue does not run while it does not
compile. See [handlers](/guide/handlers).

**Names can collide.** A binding whose PascalCase name matches a member of `AuiView` is an error
telling you to pick another name. Also do not declare a field called `_<binding>`; the generated
backing field owns that name.

## Lifecycle hooks

Override these rather than using `Start`:

```csharp
protected override void OnStart()        { }  // once, after wiring and initial binding values
protected override void OnRetranslate()  { }  // after a language change, once texts have repainted
protected override void OnShown()        { }  // after Show()
protected override void OnHidden()       { }  // after Hide()
```

`OnStart` is the important one. It runs after the view has built its tables and applied the initial
values of every binding, which `Start` does not guarantee.

`OnRetranslate` is where you re-apply anything you wrote with `SetText` by hand, because a language
change repaints keyed text and would otherwise overwrite it.

::: tip Writing to a view before it starts
Every public entry point starts the view if it has not started yet, so another script may set
`panel.Volume = 50f` in its own `Start` even if the panel is on a hidden page. You do not need to
sequence them.
:::

## A worked example

```csharp
using UnityEngine;
using VRC.SDKBase;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    public partial class Shop : AuiView
    {
        public ShopInventory inventory;   // a normal reference to your own script

        private int _owned;

        protected override void OnStart()
        {
            _owned = 0;
            Refresh();
        }

        public void Buy()
        {
            if (inventory == null || !inventory.TryBuy()) return;
            _owned++;
            Refresh();
        }

        public void VolumeChanged(float value)
        {
            if (inventory != null) inventory.SetVolume(value);
        }

        private void Refresh()
        {
            Count   = _owned.ToString();
            SoldOut = _owned >= 5;
        }
    }
}
```

Two things worth copying from it: keep your own state in your own fields and push it into bindings
in one `Refresh` method, and null-check every reference to another behaviour, because Udon gives
you no exception you would enjoy debugging in-world.

## Test methods

A parameterless `public void _TestSomething()` can be sent as a custom event from the editor
without entering play mode logic by hand:

```csharp
public void _TestJapanese() { if (lang != null) lang.SetLanguageByCode("ja"); }
public void _TestSoldOut()  { SoldOut = true; }
```

See [testing](/guide/testing).

## Next

- [Bindings](/guide/bindings) for the type of each binding and two-way controls.
- [Handlers](/guide/handlers) for every signature.
- [Talking to other Udon scripts](/guide/interop) for wiring a view into a world.
