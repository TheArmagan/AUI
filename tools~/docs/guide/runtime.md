---
description: The mutation API - showing and hiding elements, text and icons, classes, style values, position, templates and pools, and modals.
---

# Changing a view at runtime

A view carries its layout and its stylesheet as integer tables and re-runs them in Udon. That is
what lets a baked panel change shape in-world without parsing anything there.

Most of the time you set a [binding](/guide/bindings) and you are done. This page is everything
bindings cannot express.

## Addressing an element

Generated `E_ID` constants are element indices. The selector helpers return the same integers, so
code that is not written against one specific view still works:

```csharp
int card = GetElement("#card");        // -1 when there is no such id
int[] chips = GetElements(".chip");    // by effective runtime class
int[] buttons = GetElements("button"); // by tag
```

## Showing and hiding

```csharp
SetActiveElem(E_DETAILS, false);   // hides it and frees its space; siblings move up
SetDisplay(E_DETAILS, DISPLAY_NONE);
```

This is the code equivalent of `if="{flag}"`. Use the binding when the condition is part of your
state, and `SetActiveElem` when it is not.

## Text and icons

```csharp
SetText(card, "any string");        // escaped for you
SetIcon(GetElement("#close"), "check");
SetBackground(card, "Rounded16");
SetMaterial(card, "GlowHot");       // or null to clear
string s = GetText(card);
```

An icon, sprite or material must be **known to the view**: either used somewhere in its markup or
sheets, or preloaded on the root element.

```aui
<view name="Panel" icons="check star" sprites="Rounded16" materials="GlowHot" utilities="p-4 p-8">
```

Anything not known that way cannot be swapped in, because the asset reference was never baked.

::: warning Text you set by hand is overwritten on a language change
`_Retranslate` repaints every keyed text. Re-apply your own strings in `OnRetranslate`.
:::

## Classes

Every class any of the view's sheets defines gets a `C_NAME` constant:

```csharp
AddClass(E_CARD, C_WARM);
RemoveClass(E_CARD, C_WARM);
ToggleClass(E_CARD, C_WARM);
bool on = HasClass(E_CARD, C_WARM);

AddClassNamed(E_CARD, "warm");     // by name, for generic code
SetClasses(E_CARD, "card warm");   // replaces the API-added set
```

Changing a class re-resolves that element through the cascade and re-resolves its descendants, so
inherited properties follow, and applies each changed property through the element's own
`transition`.

You can also mix bindings into the class attribute, which is often tidier than calling `AddClass`:

```aui
<panel class="card {tone}"/>
```

```csharp
Tone = "warm";        // now class="card warm"
Tone = "";            // back to just card
```

## Style values

Set one property on one element, without a class:

```csharp
SetOpacity(card, 0.5f);
SetColor(card, Color.red);            // text colour
SetBackgroundColor(card, c);
SetIconColor(card, c);
SetScale(card, 1.1f);
SetFontSize(card, 22f);
SetTextAlign(card, 1);                // 0 left, 1 center, 2 right
SetStyleValue(card, PROP_GAP, new Vector4(12, 0, 0, 0));   // any cascade property by id
```

A value set this way holds until the next class change on that element re-resolves it.

Elements with an id get a `CanvasGroup` at bake, so `SetOpacity` always has something to fade.

The declarative equivalent is a **bound style value**, which is usually what you want:

```aui
<panel class="fill" style="width: {progress}"/>
<panel id="stage" style="flex-direction: {dir}; justify: {justify}; gap: {gap}"/>
```

```csharp
Progress = "50%";     // lengths and enums are strings
Gap = "16";
```

## Position and size

```csharp
SetPosition(E_BADGE, -6f, -6f, float.NaN, float.NaN);  // left, top, right, bottom; NaN = unset
SetSize(E_CARD, 120f, 40f);
```

`SetPosition` needs the element to be `position: absolute` (or anchored).

Read the solved rectangle back:

```csharp
float x = LayoutX(E_CARD), w = LayoutW(E_CARD);
```

## Batching

Every mutation marks the layout dirty; solves are coalesced to one per frame. For a burst:

```csharp
SetAutoRelayout(false);
/* many changes */
SetAutoRelayout(true);     // one solve
```

A setter schedules the solve for the next frame; `Relayout()` runs it now if anything is dirty,
`RelayoutNow()` runs it now regardless.

## Creating elements

Nothing is instantiated in-world. A `<template>` bakes `max` parked copies; `CreateElement` links
one under a container. A `pool` and a `<slot>` are the same idea with less markup.

```aui
<view name="Chat" width="400" height="300" pool="text:8 panel:4">
  <panel id="log" class="card"/>
  <template id="line" max="32">
    <panel class="row">
      <icon name="user"/>
      <text text="{item.who}" class="muted"/>
      <text text="{item.msg}"/>
    </panel>
  </template>
  <slot id="cards" views="PlayerCard" max="8"/>
</view>
```

```csharp
int line = CreateLine(E_LOG);        // generated per template; -1 when all 32 are in use
SetLineWho(line, "you");             // {item.x} setters take the created element
SetLineMsg(line, "hello");

int note = CreateElementByTag("text", E_LOG, "muted", 0);  // from the pool, with classes, at index 0
SetText(note, "(new message)");

AuiView card = CreateCardsPlayerCard(E_LOG);   // a parked nested view; cast to PlayerCard

MoveElement(line, E_LOG, 0);          // reorder or reparent
DestroyElement(note);                 // back to the pool
```

Rules worth knowing:

- A `<template>` must be a direct child of `<view>`; instances can go under any container.
- Only `panel`, `text`, `icon` and `spacer` can be pooled. Anything with wiring needs a template.
- Handlers inside a template receive the **instance element**, and `InstanceElem(T_LINE, row)` /
  `InstanceRow(T_LINE, elem)` convert between the two.
- `CreateElement` returns `-1` when the pool is exhausted. Always check.
- Batch a burst of creates with `SetAutoRelayout(false)`.

## Modals

A `<modal>` sits directly under `<view>`, takes no space in the flow, and draws over a dimming
backdrop.

```aui
<modal id="confirm" enabled="{asking}" onclose="ResetCancelled">
  <text key="settings.reset.sure"/>
  <panel class="row">
    <button onclick="DoReset" key="yes" class="primary"/>
    <button onclick="CloseConfirm" key="no"/>
  </panel>
</modal>
```

```csharp
public void AskReset() { Asking = true; }   // the binding opens it
public void DoReset()  { /* ... */ CloseConfirm(); }

OpenConfirm(); CloseConfirm(); bool open = IsConfirmOpen();
Back();        // closes an open select, else the top dismissable modal; true if it consumed
```

A click on the backdrop closes it unless `dismissable="false"`. Style the backdrop with
`modal::backdrop { background: rgba(0,0,0,0.6); }`. Later modals stack on top; a select's dropdown
always draws above a modal.

## Showing the view itself

```csharp
Show(); Hide(); Toggle();
ShowInstant(); HideInstant();
bool v = IsVisible();
```

The transition is the `show` style property or the `show` attribute on `<view>`
(`none | fade | fade-scale | fade-slide`). `Hide()` leaves an open modal open behind the scenes, so
the view returns the way you left it.

## Two things that cannot change in-world

Udon has no API for them:

- **A text's font weight.** A class that sets one is applied at bake; if it could only ever be
  reached at runtime you get AUI212.
- **New materials.** `shader()` materials are created at bake time. You can swap between baked
  ones with `SetMaterial`.

## Cost

A roughly 40-element view solves in about a millisecond in the editor's Udon interpreter, once per
frame at most, and only when marked dirty. A view nothing touches costs nothing.

## Next

- [Theming](/guide/theming) for changing the palette everywhere at once.
- [View API](/reference/view-api) for the complete signature list.
- [The gallery](/examples/gallery-pages#runtime) for a worked example of templates and modals.
