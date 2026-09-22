# Handlers

A handler attribute names a method on your view. The value must be a plain C# identifier
(AUI109 if it is not), and the method is scaffolded into `Name.cs` for you the first time.

```aui
<button onclick="Buy" key="shop.buy"/>
```

```csharp
public void Buy() { }
```

## Every signature

| Attribute | On | Signature |
|---|---|---|
| `onclick` | `button` | `void Name()` |
| `onhover`, `onleave`, `onpress`, `onrelease` | `button`, `toggle` | `void Name()` |
| `onchange` | `toggle` | `void Name(bool value)` |
| `onchange` | `slider` | `void Name(float value)` |
| `onchange`, `oninput` | `colorpicker` | `void Name(Color value)` |
| `onchange` | `select` | `void Name(int index)` |
| `onchange` | `tabs`, `pages` | `void Name(int page)` |
| `onsubmit` | `input` | `void Name(string value)` |
| `onsubmit` | `input type="url"` | `void Name(VRCUrl value)` |
| `onselect` | `repeat` | `void Name(int row)` |
| `onopen`, `onclose` | `modal` | `void Name()` |
| `on-<event>` | a nested view | matches the child's `<event type>` |

Inside a `<repeat>` or `<template>`, **the row comes first**:

| Where | Signature |
|---|---|
| `onclick` on a button in a repeat | `void Name(int row)` |
| `onchange` on a toggle in a repeat | `void Name(int row, bool value)` |
| `onchange` on a slider in a repeat | `void Name(int row, float value)` |

The value is always the **new** value, and the bound property has already been updated when your
handler runs.

## Pointer events

`onhover`, `onleave`, `onpress` and `onrelease` fire on any clickable. They are for behaviour, not
appearance: hover and pressed **looks** belong in the stylesheet as `:hover` and `:pressed` rules,
which cost nothing at runtime.

```aui
<button onclick="Pick" onhover="Preview" onleave="Unpreview" key="shop.pick"/>
```

```csharp
public void Preview()   { if (preview != null) preview.Show(); }
public void Unpreview() { if (preview != null) preview.Hide(); }
```

## Handlers and bindings together

A control that is both bound and handled writes the binding first, then calls you:

```aui
<toggle bind="{mute}" key="audio.mute" onchange="MuteChanged"/>
```

```csharp
public void MuteChanged(bool value)
{
    // Mute == value already
    if (audio != null) audio.SetMuted(value);
}
```

So a handler does not need to store the value; read the property whenever you need it.

## Handlers you did not write

If a handler named in markup has no matching method, the build stops with AUI502 and the console
line points at the view. The fix is to add the method with the right signature; the VS Code
extension offers a quick fix that writes the stub into your `.cs`.

If you rename a handler in the markup, the old method stays in your `.cs` (nothing deletes your
code) and a new empty one is scaffolded. Move your body across and delete the old one.

::: warning A wrong signature blocks the whole queue
The build queue only runs when the project compiles. A handler with the wrong signature is a
compile error, so saving more `.aui` files appears to do nothing. Fix the C# error first, then
save the view again or run `AUI/Rebuild All Views`.
:::

## Events from nested views

A child view raises an event; the parent handles it with `on-<event>`:

```aui
<PlayerCard nick="{name}" score="{pts}" on-kicked="PlayerKicked"/>
```

```csharp
// in PlayerCard.cs
public void Kick() { EmitInt("kicked", Score); }

// in the parent
public void PlayerKicked(int score) { }
```

The payload type comes from the child's `<event name="kicked" type="int"/>` declaration. See
[components](/guide/components#typed-events).

## Next

- [Bindings](/guide/bindings) for the values handlers work with.
- [Lists](/guide/lists) for row handlers.
- [Components](/guide/components) for parent and child views.
