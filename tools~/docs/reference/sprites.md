# Sprites

The package ships twelve generated 9-slice sprites, used as element backgrounds.

```auis
.card   { background: sprite(Rounded8) var(--card); }
.pill   { background: sprite(Rounded24) var(--chip); }
.avatar { background: sprite(Circle) var(--panel); }
```

## The set

| Sprite | Shape |
|---|---|
| `Square` | a plain rectangle, no rounding |
| `Rounded4` | 4 mm corners |
| `Rounded8` | 8 mm corners |
| `Rounded12` | 12 mm corners |
| `Rounded16` | 16 mm corners |
| `Rounded24` | 24 mm corners, effectively a pill on short elements |
| `Circle` | a circle |
| `Border4` | 4 mm corners, outline only |
| `Border8` | 8 mm corners, outline only |
| `Border12` | 12 mm corners, outline only |
| `Border16` | 16 mm corners, outline only |
| `Border24` | 24 mm corners, outline only |
| `HueBar` | the colour picker's hue strip, red at the bottom round to red at the top |
| `SatRamp` | white fading to clear, left to right (the picker's saturation layer) |
| `ValRamp` | black fading to clear, bottom to top (the picker's brightness layer) |

All are white, so the element's colour is the tint. They are 9-sliced, so a sprite stretches to any
size without distorting its corners.

## `sprite()` versus `radius`

Two ways to get a rounded background:

```auis
.a { background: sprite(Rounded8) var(--card); }
.b { background: var(--card); radius: 8; }
```

`radius` picks the matching sprite for you, snapping to the nearest of 4, 8, 12, 16 and 24. Both
are **bake only** in effect: the sprite is chosen when the prefab is built.

Use `sprite()` when you want to be explicit, `radius` when the value comes from a variable.

## Borders

The `border` property draws a separate outline image on top, using the `Border*` sprite matching
the element's corner radius:

```auis
input {
  background: sprite(Rounded8) var(--surface);
  border: var(--border);
  transition: border 0.12s out-quad;
}
input:hover { border: var(--accent); }
```

`border` is animatable, so an input can highlight on hover. `border: none` removes it.

## Swapping at runtime

```csharp
SetBackground(card, "Rounded16");
```

The sprite must be baked into the view: mentioned by one of its sheets, or preloaded on the root.

```aui
<view name="Card" sprites="Rounded16 Circle">
```

## Regenerating

`AUI/Generate Sprites` rewrites the PNGs under the package's `Sprites/` folder. They are committed,
so you only need this if you are changing the package itself.

## Next

- [Style properties](/reference/properties#colour-and-surface) for `background`, `border` and
  `radius`.
- [Icons](/reference/icons) for the icon set.
