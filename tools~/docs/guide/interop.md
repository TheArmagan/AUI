# Talking to other Udon scripts

Both directions are typed references. No `SetProgramVariable`, no string event names.

## UI to script

Declare a normal public field in the view's `.cs` half and set it in the inspector:

```csharp
namespace Armagan.AUI.Generated
{
    public partial class SettingsPanel : AuiView
    {
        public WorldBrightness brightness;

        public void BrightnessChanged(float value)
        {
            if (brightness != null) brightness.SetPercent(value);
        }
    }
}
```

::: warning A prefab cannot reference a scene object
Set the field on the **scene instance**, not on the prefab asset. Unity records it as an instance
override. This is a Unity rule, not an AUI one.
:::

## Script to UI

Hold the generated class and set its properties:

```csharp
public class WorldBrightness : UdonSharpBehaviour
{
    public SettingsPanel panel;

    private void Start()
    {
        if (panel != null) panel.Brightness = 70f;
    }
}
```

Setters from code repaint every bound element and **never** call the handler, so this cannot loop
back into `BrightnessChanged`.

You do not have to worry about start order: every public entry point starts the view first, so
writing to a panel that has not started yet, or that sits on a hidden page, works.

## A worked pair

```aui
<view name="SettingsPanel" width="440" height="300">
  <panel class="setting">
    <text key="settings.brightness"/>
    <spacer/>
    <slider bind="{brightness}" min="0" max="100" step="1"
            format-key="settings.percent" onchange="BrightnessChanged"/>
  </panel>
  <panel class="setting">
    <text key="settings.solid"/>
    <spacer/>
    <toggle bind="{solid}" icon="check" onchange="SolidChanged"/>
  </panel>
</view>
```

```csharp
public partial class SettingsPanel : AuiView
{
    public WorldBrightness world;

    protected override void OnStart()
    {
        if (world == null) return;
        Brightness = world.GetPercent();   // the slider moves; no handler fires
        Solid      = world.IsSolid();
    }

    public void BrightnessChanged(float value) { if (world != null) world.SetPercent(value); }
    public void SolidChanged(bool value)       { if (world != null) world.SetSolid(value); }
}
```

The shape to copy: `OnStart` pulls the world's current state into the bindings, and the handlers
push player changes back out.

## Scripts that must not know the view

A library script that should work with any panel can take an `AuiView` and use the base API:

```csharp
public class PanelToggler : UdonSharpBehaviour
{
    public AuiView view;

    public override void Interact()
    {
        if (view == null) return;
        if (view.IsVisible()) view.Hide(); else view.Show();
    }
}
```

Available on the base class without knowing the concrete view: `Show`, `Hide`, `Toggle`,
`IsVisible`, `Back`, `GetElement("#id")`, `SetText`, `SetIcon`, `AddClassNamed`, `SetActiveElem`,
`SetRepeatCount`, `CreateElementByTag`, `SetStyleVariable`, `Relayout`, and the rest of
[the view API](/reference/view-api).

For anything view-specific, use the generated class. That is the point of generating it.

## Custom events

A view is a normal UdonSharpBehaviour, so `SendCustomEvent` works:

```csharp
view.SendCustomEvent("_Retranslate");   // repaint after a language change
view.SendCustomEvent("_TestRun");       // your own test entry point
```

Useful from editor scripts and from Udon Graph. From UdonSharp, prefer the typed call.

## Networking

Nothing about a view is networked. Every generated behaviour is `BehaviourSyncMode.None`.

If two players should see the same panel state, sync it in **your** script and push it into the
view on each client:

```csharp
[UdonSynced] private int _stock;

public override void OnDeserialization()
{
    if (panel != null) panel.Stock = _stock.ToString();
}
```

That keeps the sync surface small and under your control, which is what you want in Udon anyway.

## Next

- [The logic half](/guide/the-logic-half) for the view's own code.
- [View API](/reference/view-api) for what the base class offers.
- [Testing](/guide/testing) for driving a view without a headset.
