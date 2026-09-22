# Icons

AUI bundles 45 icons. Use one by file name, without the extension:

```aui
<icon name="sliders-horizontal" size="22" class="accent"/>
<button onclick="Close" icon="x" class="ghost"/>
<tab page="audio" key="settings.audio" icon="headset"/>
```

An unknown name is error AUI119, with a "did you mean" suggestion.

Icons are tinted by the `icon-color` style property, which inherits, so setting it on a panel
tints every icon inside it.

```auis
.accent { color: var(--accent); icon-color: var(--accent); }
```

## The bundled set

42 of these are stock [Lucide](https://lucide.dev) icons; each name below links to its Lucide page.

| | | | |
|---|---|---|---|
| [`arrow-up-down`](https://lucide.dev/icons/arrow-up-down) | [`bookmark`](https://lucide.dev/icons/bookmark) | `bookmark-filled` | [`check`](https://lucide.dev/icons/check) |
| [`chevron-down`](https://lucide.dev/icons/chevron-down) | [`chevron-left`](https://lucide.dev/icons/chevron-left) | [`chevron-right`](https://lucide.dev/icons/chevron-right) | [`chevron-up`](https://lucide.dev/icons/chevron-up) |
| [`chevrons-left`](https://lucide.dev/icons/chevrons-left) | [`chevrons-right`](https://lucide.dev/icons/chevrons-right) | [`circle-question-mark`](https://lucide.dev/icons/circle-question-mark) | [`clock`](https://lucide.dev/icons/clock) |
| `discord` | [`download`](https://lucide.dev/icons/download) | [`external-link`](https://lucide.dev/icons/external-link) | [`eye`](https://lucide.dev/icons/eye) |
| [`folder-open`](https://lucide.dev/icons/folder-open) | [`globe`](https://lucide.dev/icons/globe) | [`hard-drive`](https://lucide.dev/icons/hard-drive) | [`headset`](https://lucide.dev/icons/headset) |
| [`heart`](https://lucide.dev/icons/heart) | `heart-filled` | [`house`](https://lucide.dev/icons/house) | [`info`](https://lucide.dev/icons/info) |
| [`layout-grid`](https://lucide.dev/icons/layout-grid) | [`library-big`](https://lucide.dev/icons/library-big) | [`lock`](https://lucide.dev/icons/lock) | [`monitor`](https://lucide.dev/icons/monitor) |
| [`pencil`](https://lucide.dev/icons/pencil) | [`plus`](https://lucide.dev/icons/plus) | [`refresh-cw`](https://lucide.dev/icons/refresh-cw) | [`search`](https://lucide.dev/icons/search) |
| [`shirt`](https://lucide.dev/icons/shirt) | [`shopping-bag`](https://lucide.dev/icons/shopping-bag) | [`sliders-horizontal`](https://lucide.dev/icons/sliders-horizontal) | [`smartphone`](https://lucide.dev/icons/smartphone) |
| [`sparkles`](https://lucide.dev/icons/sparkles) | [`tag`](https://lucide.dev/icons/tag) | [`trash-2`](https://lucide.dev/icons/trash-2) | [`triangle-alert`](https://lucide.dev/icons/triangle-alert) |
| [`user`](https://lucide.dev/icons/user) | [`users`](https://lucide.dev/icons/users) | [`wand-sparkles`](https://lucide.dev/icons/wand-sparkles) | [`x`](https://lucide.dev/icons/x) |
| [`zap`](https://lucide.dev/icons/zap) | | | |

Three are not stock Lucide:

- **`bookmark-filled`** and **`heart-filled`** are filled variants, hand-modified from the Lucide
  originals. Useful as the "on" state of a toggle beside the hollow version.
- **`discord`** is a [Simple Icons](https://simpleicons.org) glyph: a solid filled path rather than
  a stroke, so `stroke-width` does not apply to it.

## Adding your own

Put white-stroke SVGs in a folder and list it in `AuiSettings.iconFolders`. Those folders are
searched **before** the package's own, so an icon of the same name overrides a bundled one.

Match Lucide's conventions and yours will sit correctly beside the bundled set:

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none"
     stroke="#FFFFFF" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
  <path d="..."/>
</svg>
```

The stroke must be white, because the colour comes from `icon-color` at runtime. Anything
pre-coloured cannot be tinted.

Run `AUI/Reimport Icons` after adding files; the importer forces them to Textured Sprite.

## Swapping an icon at runtime

```csharp
SetIcon(GetElement("#fav"), "heart-filled");
```

The icon must be **baked into the view**: either used in its markup, or preloaded on the root.

```aui
<view name="Card" icons="heart heart-filled check">
```

Otherwise the sprite reference was never included and nothing happens.

## Next

- [Elements](/reference/elements#icon) for the `<icon>` attributes.
- [Style properties](/reference/properties#colour-and-surface) for `icon-color`.
- [Sprites](/reference/sprites) for the 9-slice backgrounds.
