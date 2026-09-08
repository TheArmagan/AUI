# Utility classes

A small built-in set of style-only classes, written with a leading star so they never collide with
your own names.

```aui
<panel class="card *flex-row *items-center *gap-8 *p-12">
  <text class="*fg-muted *text-sm">hint</text>
  <spacer/>
  <button class="*w-64" icon="x"/>
</panel>
```

## What a star class is

- **Style only.** It gets no `C_` constant and `GetElements(".*p-8")` returns nothing.
- **Its own budget.** 256 star classes, separate from the 64 regular classes a view may see
  (AUI214 if you exceed it, AUI211 for the regular budget).
- **Overridable.** They sit in the Utility tier, between the theme and your sheets, so any rule of
  yours wins.
- **Only what you use is baked.** A view bakes the utilities its markup mentions, plus anything
  listed in `<view utilities="...">`.

You can define your own: `.*name { ... }` in any sheet.

::: tip Adding one at runtime
`AddClassNamed(elem, "*p-8")` only works if the view baked that utility. If the markup never
mentions it, list it on the root: `<view utilities="p-4 p-8">` (without the star there).
:::

## Spacing steps

Every spacing utility uses these twelve steps:

`0` `1` `2` `4` `6` `8` `12` `16` `24` `32` `48` `64`

| Pattern | Sets |
|---|---|
| `*gap-N` | `gap` |
| `*p-N` | `padding` on all sides |
| `*px-N` / `*py-N` | horizontal / vertical padding |
| `*pt-N` `*pr-N` `*pb-N` `*pl-N` | one side of padding |
| `*m-N` | `margin` on all sides |
| `*mx-N` / `*my-N` | horizontal / vertical margin |
| `*mt-N` `*mr-N` `*mb-N` `*ml-N` | one side of margin |
| `*w-N` / `*h-N` | fixed width / height |
| `*top-N` `*right-N` `*bottom-N` `*left-N` | one edge |

So `*p-12` is 12 mm of padding, `*gap-8` an 8 mm gap.

## Layout

| Class | Sets |
|---|---|
| `*flex-row` | `flex-direction: row` |
| `*flex-col` | `flex-direction: column` |
| `*wrap` / `*nowrap` | `wrap: true` / `false` |
| `*grow` / `*grow-0` | `grow: 1` / `0` |
| `*items-start` `*items-center` `*items-end` `*items-stretch` | `align` |
| `*justify-start` `*justify-center` `*justify-end` | `justify` |
| `*justify-between` | `justify: space-between` |
| `*w-full` / `*h-full` | `100%` |
| `*w-auto` / `*h-auto` | `auto` |
| `*hidden` / `*flex` | `display: none` / `flex` |
| `*absolute` / `*static` | `position` |
| `*inset-0` | absolute, all four edges at 0 |
| `*center` | `anchor: center` |

## Visual

| Class | Sets |
|---|---|
| `*rounded-4` `*rounded-8` `*rounded-12` `*rounded-16` `*rounded-24` | `radius` (bake only) |
| `*opacity-0` `*opacity-25` `*opacity-50` `*opacity-75` `*opacity-100` | `opacity` |
| `*scale-90` `*scale-100` `*scale-110` | `scale` |
| `*bg-transparent` | `background: transparent` |
| `*bg-none` | `background: none` |

## Text

| Class | Sets |
|---|---|
| `*text-left` `*text-center` `*text-right` | `text-align` |
| `*text-sm` | `font-size: 14` |
| `*text-base` | `font-size: 18` |
| `*text-lg` | `font-size: 22` |
| `*text-xl` | `font-size: 26` |
| `*font-regular` | `font-weight: 400` |
| `*font-medium` | `font-weight: 500` |
| `*font-semibold` | `font-weight: 600` |
| `*font-bold` | `font-weight: 700` |

The four `*font-*` classes are **bake only**, like `font-weight` itself. Adding one at runtime
does nothing and warns AUI212.

## Palette

Each maps to a theme variable, with the default theme's value as a fallback so the utility still
resolves in a view compiled without the theme.

| Class | Sets |
|---|---|
| `*bg-base` `*bg-nav` `*bg-surface` `*bg-panel` `*bg-card` `*bg-chip` `*bg-accent` | `background: sprite(Rounded8) var(--name)` |
| `*fg-text` `*fg-muted` `*fg-accent` `*fg-base` | `color` and `icon-color` |

Because they read variables, they follow a [runtime re-theme](/guide/theming) like anything else.

## When to use them

Utilities suit one-off adjustments where naming a class would be noise: nudging a gap, pinning a
width, muting a hint. Anything you write three times, or anything that carries meaning
(`.setting`, `.leaderboard-row`), deserves a real class in a sheet.

## Next

- [Style properties](/reference/properties) for what each of these sets.
- [Styling](/guide/styling) for tiers and the cascade.
