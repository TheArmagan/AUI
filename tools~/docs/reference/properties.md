---
description: The 37 properties a stylesheet may set, with their values, theme defaults, and which ones inherit, animate in a state rule, or apply only at bake.
---

# Style properties

The 37 properties a `.auis` rule or a `style=""` attribute may set. The source of truth is
`Core/Schema/AuiSchema.cs`; the "theme" column is what `Theme/Default.auis` sets, which is what you
actually see if you set nothing.

Units are millimetres unless stated. Three flags matter:

- **Inherited** — passes down to children.
- **Animatable** — may appear in a `:state` rule and be tweened. Only these seven can.
- **Bake only** — applied by the prefab builder; cannot change in-world (AUI212).

## Size

| Property | Values | Notes |
|---|---|---|
| `width` | number, `Nmm`, `N%`, `auto` | percent is of the parent's content box |
| `height` | number, `Nmm`, `N%`, `auto` | |
| `min-width` | number, `Nmm`, `N%`, `auto` | |
| `min-height` | number, `Nmm`, `N%`, `auto` | |
| `max-width` | number, `Nmm`, `N%`, `auto` | the usual fix for text that does not fit |
| `max-height` | number, `Nmm`, `N%`, `auto` | |

Without an explicit size an element is content-sized. See [layout](/guide/layout#sizing).

## Placement

| Property | Values | Default | Notes |
|---|---|---|---|
| `display` | `flex`, `none` | `flex` | `none` removes the element **and its space** |
| `position` | `static`, `absolute` | `static` | `absolute` leaves the flow |
| `anchor` | `none`, `fill`, `top`, `bottom`, `left`, `right`, `center` | `none` | absolute placement against the parent; `fill` equals `absolute` with four zero edges |
| `top` | number, `Nmm`, `N%` | | edge from the parent's content box, for `position: absolute`. May be negative |
| `right` | number, `Nmm`, `N%` | | |
| `bottom` | number, `Nmm`, `N%` | | |
| `left` | number, `Nmm`, `N%` | | |

Two opposite edges with no size stretch the element between them. An explicit size wins over the
opposite edge (AUI403). An edge on a non-absolute element is ignored (AUI404). `display` or
`position` on the root view is AUI405.

## Flow

| Property | Values | Default | Theme |
|---|---|---|---|
| `flex-direction` | `row`, `column` | `column` | `.row` and `.grid` set `row` |
| `justify` | `start`, `center`, `end`, `space-between` | `start` | main axis |
| `align` | `start`, `center`, `end`, `stretch` | `stretch` | cross axis; `.row` sets `center` |
| `grow` | number | `0` | `spacer` sets `1` |
| `wrap` | `true`, `false` | `false` | row containers only; `.grid` sets `true` |
| `gap` | number | `0` | `view` 12, `panel` 8 |
| `padding` | 1 to 4 numbers | `0` | `view` 16, `.card` 12 |
| `margin` | 1 to 4 numbers | `0` | |

`padding` and `margin` take CSS order: `all`, then `vertical horizontal`, then
`top horizontal bottom`, then `top right bottom left`.

## Colour and surface

| Property | Values | Inherited | Animatable | Theme |
|---|---|---|---|---|
| `color` | colour | yes | yes | `var(--text)` |
| `background` | `none`, colour, `sprite(N) c`, `material(N) c`, `shader(N) c` | | yes | per element |
| `border` | colour or `none` | | yes | `input`, `select`, `modal` |
| `icon-color` | colour | yes | yes | `var(--text)` |
| `opacity` | number | | yes | `1` |
| `scale` | number | | yes | `1` |
| `offset` | `x y` | | yes | for state displacement |
| `radius` | number | | | **bake only**; picks Rounded4/8/12/16/24 |

Colours: `#RGB`, `#RGBA`, `#RRGGBB`, `#RRGGBBAA`, `rgb()`, `rgba()`, `transparent`, `white`,
`black`, or `var(--name)`.

`background` combines a sprite with a material or shader: the sprite stays the texture and the
colour is the vertex tint.

```auis
.card  { background: sprite(Rounded8) var(--card); }
.plain { background: #1E1610; }
.none  { background: none; }
.glow  { background: sprite(Rounded8) shader(Custom/UIGlow) var(--accent); }
.hot   { background: material(GlowHot); }
```

`shader(Name)` bakes one material per shader under `Assets/AUI/Generated/Materials`.
`material(Name)` uses any Material asset of that name. Materials are shared assets: a property you
change on one is seen by every element using it.

`opacity` needs a `CanvasGroup`, which is added automatically for elements with an id.

## Text

| Property | Values | Inherited | Theme |
|---|---|---|---|
| `font` | `weight size`, e.g. `600 18` | yes | `view` `400 18` |
| `font-weight` | `400`..`700`, `regular`, `medium`, `semibold`, `bold` | yes | **bake only** |
| `font-size` | number | yes | |
| `text-align` | `left`, `center`, `right` | yes | |
| `line-height` | number | yes | a multiplier |

`font` sets both at once. Its **weight half is bake only**, its size half is not, so
`SetFontSize` works but nothing can change a weight in-world: Udon cannot swap a font asset.

## Transitions

| Property | Values |
|---|---|
| `transition` | `prop duration ease`, comma-separated |

```auis
button {
  transition: background 0.12s out-quad, color 0.12s out-quad, scale 0.1s out-back;
}
```

Duration is `0.12s` or `120ms`. `all` is allowed as the property. The ease defaults to `out-quad`.
A malformed value is AUI209.

### Eases

`linear`, and `in-` / `out-` / `in-out-` prefixed forms of:

`quad`, `cubic`, `quart`, `sine`, `expo`, `back`, `bounce`, `elastic`

That is 25 names in total. They come from VRCTween, which is inert under ClientSim, so transitions
only animate in the real client.

## Transitions of a view and its pages

| Property | Values | Notes |
|---|---|---|
| `show` | `none`, `fade`, `fade-scale`, `fade-slide` | on `view`; **bake only**. The `show` attribute overrides it |
| `page-transition` | `none`, `fade`, `slide-left`, `slide-right`, `slide-up`, `slide-down`, `scale` | on `page`; **bake only** |

## What may appear in a state rule

Only these seven, because they are the ones Udon can animate:

`color`, `background`, `border`, `icon-color`, `opacity`, `scale`, `offset`

Anything else in a `:hover`, `:pressed`, `:selected` or `:disabled` rule is AUI210.

```auis
button:hover   { background: var(--accent); color: var(--base); scale: 1.03; }
.lift:hover    { offset: 0 -3; }
button:disabled { opacity: 0.4; }
```

## What inherits

`color`, `icon-color`, `font`, `font-weight`, `font-size`, `text-align`, `line-height`

Setting `color` on a panel tints every text and icon inside it.

## What is bake only

`radius`, `font-weight` (and the weight half of `font`), `page-transition`, `show`

## Bindable style values

In a `style=""` attribute a declaration may be entirely `{binding}` tokens:

```aui
<panel style="width: {progress}"/>
<panel style="offset: {x} {y}"/>
<panel style="padding: {v} {h}"/>
```

The C# type follows the property's kind:

| Kind | C# type |
|---|---|
| Number, Offset | `float` |
| Colour, Background | `Color` |
| Length, Box, Enum | `string` |

Mixing a binding with literal text (`width: {n}mm`) is AUI213, and sheet rules cannot contain
bindings at all.

## Variables

Declared only in a `:root` block, read with `var(--name)` or `var(--name, fallback)`:

```auis
:root { --accent: #E0A86A; --pad: 12; }
.card { padding: var(--pad); background: sprite(Rounded8) var(--card); }
```

Collected from every `:root` in tier order, so a later sheet redefining one changes every rule that
reads it. Undefined is AUI203. See [theming](/guide/theming).

## Next

- [Utility classes](/reference/utilities) for the built-in star classes.
- [Styling](/guide/styling) for selectors, tiers and the cascade.
- [Layout](/guide/layout) for how the size and flow properties interact.
