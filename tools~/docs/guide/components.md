# Components

Two ways to reuse a piece of UI: an **inline component**, expanded at compile time within one file,
and a **nested view**, a real separate `.aui` with its own behaviour.

## Inline components

Declare a template once and use it as a tag in the same file. It is expanded before anything else
looks at the tree, so the clones are ordinary elements and cost nothing at runtime.

```aui
<view name="Stats" width="420" height="200">
  <component name="Stat">
    <prop name="label"/>
    <prop name="value"/>
    <prop name="icon" default="zap"/>
    <prop name="tone" default="accent"/>
    <panel class="kpi">
      <panel class="row" style="gap: 6">
        <icon name="{icon}" class="{tone}" size="16"/>
        <text key="{label}" class="hint"/>
      </panel>
      <text text="{value}" class="kpi-value"/>
    </panel>
  </component>

  <panel class="row">
    <Stat id="hp"    label="stats.health" value="{hp}"    icon="heart" tone="ok"/>
    <Stat id="score" label="stats.score"  value="{score}"/>
  </panel>
</view>
```

- `{prop}` is substituted **at compile time**, anywhere: `class`, `style`, `icon`, `key`, `text`,
  `if`.
- A `<prop>` may have a `default`; without one it is required.
- The usage's `id`, `class`, `style` and `if` merge onto the clone's root.
- An id **inside** the template becomes `usageId_inner`, so two usages do not collide.
- Handlers named inside a component are the view's own methods.
- Components may use other components.
- A `{binding}` passed as a prop value is the host's binding, written through as usual.

Use an inline component for a repeated shape inside one view. It is the cheapest form of reuse
there is, because after expansion there is no evidence it existed.

## Nested views

Any `.aui` in the project (or in a folder listed in `AuiSettings.componentFolders`) can be used by
its name as a tag:

```aui
<page id="lang"><AuiLanguagePicker/></page>
<PlayerCard nick="{name}" score="{pts}" on-kicked="PlayerKicked"/>
```

A nested view is a real view: its own prefab, its own generated class, its own `.cs`. Children
build before parents.

Restrictions: a nested view takes no children of its own, and cannot be a repeat template (put it
in a `<template>` or a `<slot>` instead).

### Declared props

A view declares what it accepts, so the host gets completion and errors instead of silent
defaults:

```aui
<view name="PlayerCard" width="300" height="60">
  <prop name="nick" default="anon"/>
  <prop name="score" type="int"/>
  <event name="kicked" type="int"/>

  <panel class="row">
    <text text="{nick}"/>
    <text text="{score}"/>
    <spacer/>
    <button onclick="Kick" icon="x"/>
  </panel>
</view>
```

`type` is `string` (the default), `int`, `float` or `bool`.

In the host:

```aui
<PlayerCard nick="{name}" score="{pts}" on-kicked="PlayerKicked"/>
```

A **bound** prop takes the child's declared type. A **literal** is assigned once at start. An
attribute that is not a declared prop, on a child that declares any, is error AUI121.

### Typed events

```aui
<event name="kicked" type="int"/>
```

```csharp
// in PlayerCard.cs
public void Kick() { EmitInt("kicked", Score); }
```

```csharp
// in the host, from on-kicked="PlayerKicked"
public void PlayerKicked(int score) { }
```

`Emit(name)` for no payload, then `EmitString`, `EmitInt`, `EmitFloat` and `EmitBool`. The type in
the declaration decides the host handler's parameter.

### Reaching a child directly

The host gets a typed field for each nested view, so you can drive it without events:

```csharp
public partial class Hub : AuiView
{
    protected override void OnStart()
    {
        childPlayerCard.Nick = "Alice";
        childPlayerCard.Score = 120;
    }
}
```

The field name is `child` plus the view name, plus the usage id when there is one
(`childPlayerCardTop`).

::: warning Views inside a template have no single field
A nested view used inside a `<template>` exists once per parked instance, so there is no one field
to hold it. `CreateView` returns the instance; cast it to the child's class. See
[creating elements](/guide/runtime#creating-elements).
:::

## Which to use

| | Inline component | Nested view |
|---|---|---|
| Lives in | the same file | its own `.aui` + `.cs` |
| Cost at runtime | none, expanded away | a real view with its own behaviour |
| Own logic | no, the host's methods | yes, its own class |
| Reuse across files | no | yes |
| Can hold state | no | yes |

Start with an inline component. Promote it to a nested view when a second file needs it, or when it
wants logic of its own.

## Next

- [Handlers](/guide/handlers#events-from-nested-views) for `on-<event>` signatures.
- [Changing a view at runtime](/guide/runtime#creating-elements) for templates and slots.
- [The gallery](/examples/gallery-pages), which is a hub nesting nine feature views.
