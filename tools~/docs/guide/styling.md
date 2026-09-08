# Styling

A stylesheet is a `.auis` file. It looks like CSS with a smaller vocabulary, and it is resolved in
the editor rather than in-world.

```auis
:root {
  --brand: #E0A86A;
}

.card {
  background: sprite(Rounded8) var(--panel);
  padding: 12;
  gap: 8;
}

button:hover {
  background: var(--brand);
  color: var(--base);
}
```

Units are **millimetres** unless stated. `12` and `12mm` are the same thing.

## Which sheets a view uses

In this order, each tier overriding the one before:

1. **Theme** — the package's `Theme/Default.auis`, always applied.
2. **Utilities** — the built-in star classes (`*p-8`, `*flex-row`, ...). Only the ones a view
   mentions are baked into it.
3. **Linked** — every `<link href="...">` in the view, in order. If the view links nothing, the
   sibling `Name.auis` is picked up automatically instead.
4. **Inline** — `<style>` blocks in the view file.
5. **Attribute** — a `style=""` on the element itself.

```aui
<view name="Shop" width="440" height="360">
  <link href="Shared.auis"/>
  <panel class="card" style="gap: 4"> ... </panel>
  <style>
    .card { padding: 16; }
  </style>
</view>
```

::: tip A link replaces the sibling sheet
The moment a view has one `<link>`, `Name.auis` stops being picked up automatically. That is how
the demo gallery shares one `Showcase.auis` across every page and keeps per-view rules in `<style>`
blocks.
:::

## Selectors

| Selector | Matches |
|---|---|
| `button` | every button. `panel` and `div` match each other |
| `.card` | every element with that class |
| `button.primary` | both at once |
| `*` | everything |
| `button:hover` | a state; see below |
| `modal::backdrop` | the dimming scrim behind a modal |
| `:root` | not an element: the variable block |
| `.*name` | a star class, style-only |

There are **no descendant or child combinators**. `.card text` is error AUI205. Style the child
directly with its own class instead. This is not an oversight: the runtime cascade re-resolves an
element from flat tables using its own tag and class masks, and a descendant selector cannot be
answered that way cheaply.

Within a tier, specificity is: tag 1, class 2, `tag.class` 3, plus 10 for a state. Later
declarations win ties.

## States

Four states, written as pseudo-classes:

```auis
button          { background: var(--chip); }
button:hover    { background: var(--accent); color: var(--base); }
button:pressed  { scale: 0.96; }
button:selected { background: var(--accent); }
button:disabled { opacity: 0.4; }
```

At runtime the precedence is `disabled > pressed > selected > hover > normal`. The normal values
are re-applied before a state's declarations, so leaving a state always restores every property.

**A state rule may only set these seven properties**, because they are the ones that can be
animated in Udon:

`color`, `background`, `border`, `icon-color`, `opacity`, `scale`, `offset`

Anything else in a state rule is error AUI210. In particular you cannot change padding, size or
font weight on hover.

## Transitions

```auis
button {
  transition: background 0.12s out-quad, scale 0.1s out-back;
}
button:hover { background: var(--accent); scale: 1.03; }
```

The form is `property duration ease`, comma-separated. Duration is `0.12s` or `120ms`. `all` is
allowed. The ease defaults to `out-quad`; the full list is the 25 names in
[style properties](/reference/properties#eases).

Transitions apply to any change of an animatable property, not only to states, so a colour set
from code fades too.

## Variables

Declared in a `:root` block, read with `var(--name)`:

```auis
:root {
  --ok:   #7FC97F;
  --pad:  12;
}

.card    { padding: var(--pad); }
.success { color: var(--ok); }
```

Variables are collected from every `:root` in tier order, so your sheet can redefine a theme
variable and every rule that reads it follows, including rules in the theme itself.
`var(--x, fallback)` supplies a default. An undefined variable is an error (AUI203).

Variables can be rewritten in-world with one call per variable, which is how a whole world
re-themes at once. See [theming](/guide/theming).

## Inheritance

Only these seven properties inherit from a parent element:

`color`, `icon-color`, `font`, `font-weight`, `font-size`, `text-align`, `line-height`

Everything else applies to the element it is set on. Setting `color` on a panel tints every text
and icon inside it.

## Properties applied only at bake

Four properties can be set in a sheet but cannot change in-world, because Udon has no API for them:

| Property | Why |
|---|---|
| `radius` | picks a 9-slice sprite at bake |
| `font-weight` (and the weight half of `font`) | Udon cannot swap a text's font asset |
| `page-transition` | baked into the page group |
| `show` | baked into the view |

A class that only sets these and is only ever added at runtime warns AUI212, because adding it
in-world will do nothing.

## Style on one element

```aui
<text style="max-width: 440; font-size: 15"/>
```

Use it for one-off values. Anything you write more than twice belongs in a class.

A `style=""` declaration may also be a binding, which is how a value changes at runtime:

```aui
<panel class="fill" style="width: {progress}"/>
```

The declaration must be **entirely** bindings; `width: {n}mm` mixes a binding with literal text and
is error AUI213. Sheet rules cannot contain bindings, only `style=""` can.

## Classes at runtime

Every class any of a view's sheets defines gets a `C_NAME` constant, so you can add and remove it
from code:

```csharp
AddClass(E_CARD, C_WARM);
RemoveClass(E_CARD, C_WARM);
```

A class used **only** at runtime still needs a rule somewhere, otherwise there is nothing to
apply. See [changing a view at runtime](/guide/runtime#classes).

::: warning The 64-class budget
A view sees at most 64 regular classes (AUI211). Star classes, including all the built-in
utilities, have their own separate budget of 256 and do not count toward it. If you are near the
limit, move one-off looks into `style=""` or into star classes of your own (`.*name { ... }`).
:::

## Next

- [Layout](/guide/layout) for the box model these properties feed.
- [Style properties](/reference/properties) for the complete table.
- [Utility classes](/reference/utilities) for the built-in star classes.
- [Theming](/guide/theming) for changing the palette in-world.
