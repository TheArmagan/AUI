---
description: Every tag AUI understands, with its attributes, events, allowed children and an example - view, panel, text, button, toggle, slider, input, select, tabs, pages, repeat, template, modal and the rest.
---

# Elements

Every tag AUI understands. The source of truth is `Core/Schema/AuiSchema.cs`; this page mirrors it.

## Attributes every element accepts

| Attribute | Type | Meaning |
|---|---|---|
| `id` | identifier | names the element. Generates `E_<ID>` (and `S_<ID>` if clickable). Must be unique in the view (AUI102) |
| `class` | string | space-separated class names. May contain bindings: `class="card {tone}"` |
| `style` | declarations | inline style, `prop: value; prop: value`. May contain bindings |
| `if` | `{binding}` | shows or hides the element and frees its space. Starts `true` |
| `hidden` | flag | hidden at bake, statically. **Never on an element with `icon=` or `key=`** — see below |

::: warning `hidden` breaks icons permanently
A hidden element's subtree is not laid out, at bake time included, and a clickable's icon is
positioned by the builder exactly once (the runtime solver never writes an icon's rect). An
icon-only button baked `hidden` therefore has its glyph placed against a zero-wide box and ends
up hanging outside the button's left edge, where it stays no matter what is shown or relaid out
later. A keyed one collapses to its own padding instead, because TMP measures nothing on an
inactive object.

Bake it live and let the binding switch it off on the first frame:

```xml
<button onclick="Next" icon="chevron-right" if="{hasNext}"/>
```

`hidden` remains correct on a plain `panel` or `text`, which the solver owns and re-places.
:::

## Structure

### `<view>`

The root of every `.aui` file. Exactly one per file (AUI004), and it must be the root (AUI104).

| Attribute | Values | Meaning |
|---|---|---|
| `name` | identifier | **required**; must equal the file stem (AUI103) |
| `width`, `height` | number | panel size in millimetres |
| `show` | `none`, `fade`, `fade-scale`, `fade-slide` | enter/leave transition; overrides the `show` style property |
| `start-hidden` | flag | the view starts hidden |
| `icons` | names | preload icons for `SetIcon` |
| `sprites` | names | preload sprites for `SetBackground` |
| `materials` | names | preload materials for `SetMaterial` |
| `utilities` | names | preload star classes for `AddClass` |
| `pool` | `tag:count ...` | bake spare elements for `CreateElementByTag`, e.g. `text:16 panel:8` |
| `attach` | `none`, `wrist-left`, `wrist-right`, `corner`, `follow`, `front` | ties the view to the local player. Present at all, `none` included, it bakes the [`AuiAttach`](/reference/services#auiattach) component that `SetAttach` drives. See [attaching a view](/guide/attaching) |
| `attach-corner` | `bottom-left`, `bottom-right`, `top-left`, `top-right` | where a `corner` view sits, and where a wrist view sits on desktop. Default `bottom-left`. Ignored with a warning on `follow` and `front` (AUI111) |
| `attach-distance` | number, 0.2 to 3 | metres from the head for `corner`, `follow` and `front`. Default 0.6. Outside the range is AUI111 |

```aui
<view name="Chat" width="400" height="300"
      pool="text:8 panel:4" icons="check x" utilities="p-4 opacity-50">
  ...
</view>
```

The four preload attributes exist because only baked asset references can be swapped in-world.

```aui
<view name="Hud" width="150" height="80" attach="wrist-left" attach-corner="bottom-left">
  ...
</view>
```

`attach` only works on an outermost view. A view that declares it and is then used as a tag
inside another view loses it, and the host gets AUI123.

### `<panel>` and `<div>`

A container. Aliases of each other, and a `panel` selector matches a `div` and vice versa.
Direction, gap and padding come from the stylesheet; the default is a column.

```aui
<panel class="row">
  <text key="a"/>
  <spacer/>
  <button onclick="Go" key="go"/>
</panel>
```

### `<spacer>`

Flexible space. Grows to fill what is left, which is how you push things apart.

| Attribute | Meaning |
|---|---|
| `size` | a fixed gap in mm instead of growing |

```aui
<spacer/>          <!-- grow: 1 -->
<spacer size="8"/> <!-- a fixed 8 mm gap -->
```

## Content

### `<text>`

| Attribute | Values | Meaning |
|---|---|---|
| `key` | key | a translation key |
| `text` | `{binding}` | a runtime string, escaped when set |
| `args` | `{a} {b}` | bindings filling the key's `{placeholder}`s, by name |
| `wrap` | `true`, `false` | wrap onto more lines |
| `max-lines` | number | cap the line count of a wrapped text (with `ellipsis`, the last line ends in `...`) |
| `ellipsis` | `true`, `false` | show `...` when truncated |
| `runtime` | flag | measured and re-measured as runtime text (like `text=`), for a keyed label you will `SetText` from code |
| `rich` | flag | render TextMeshPro tags (`<b>`, `<i>`, `<color=#E0A86A>`, `<size=120%>`). Off by default |

`key`, `text` and literal content are mutually exclusive (AUI115). Literal content is reported as
info AUI117, since it will never be translated. A `wrap="true"` text with no `width` or `max-width`
wraps at the width its parent gives it.

```aui
<text key="shop.title" class="title"/>
<text text="{status}"/>
<text key="shop.owned" args="{count} {itemName}"/>
<text wrap="true" max-lines="3" ellipsis="true" key="shop.blurb" style="max-width: 300"/>
<text>Debug build</text>
```

#### Rich text

Every label is baked with TMP rich text **off**, and every string a binding receives is escaped (a
zero-width space after each `<`), so nothing a player typed can open a tag. `rich="true"` turns
both off for one label:

```aui
<text rich="true" key="shop.sale"/>               <!-- "<b>50%</b> off" in the language file -->
<text rich="true" text="{bio}" wrap="true"/>      <!-- Bio = "<color=#E0A86A>VIP</color> since 2024" -->
<text rich="true">Built with <b>AUI</b></text>
```

- The flag is baked into the TMP component. Udon cannot switch `richText`, so a label is rich or
  not for the life of the prefab.
- The label's `text=` binding is stored and pushed **without** escaping. The property keeps its
  name (`Bio`); the generated setter just has no `Escape` call, and a `// rich:` comment above it
  says so.
- Only when **every** use of that binding is a rich label's `text=`. If the same binding also feeds
  a plain label, an `args=` list, a `class`, an input or a nested view's prop, the whole binding
  stays escaped and the rich label shows its tags as typed (warning AUI124). Give the rich label a
  binding of its own.
- `args=` values are always escaped, rich label or not: the translation's own markup renders, the
  values filled into it do not.
- `SetText(E_ID, s)` still escapes on a rich label. `SetRichText(E_ID, s)` writes without escaping.

::: danger Never bind a player's string to a rich label
Anything that reaches a rich label is markup. A display name, a chat line or a keyboard submission
belongs on a plain label.
:::

### `<icon>`

A bundled Lucide icon, tinted by the `icon-color` style property.

| Attribute | Meaning |
|---|---|
| `name` | the icon file name, without `.svg` (AUI119 if unknown) |
| `size` | size in mm. It wins over a stylesheet's fixed `width` / `height` for icons (the theme sets 18); an inline `style="width: ..."` on the same icon wins over `size` |

```aui
<icon name="sliders-horizontal" size="22" class="accent"/>
```

Icons from a folder listed in `AuiSettings.iconFolders` work the same way. Add the folder to the
settings BEFORE dropping SVGs into it: a file imported earlier keeps Unity's default SVG import
settings and renders as nothing until `AUI/Reimport Icons` is run.

See [icons](/reference/icons) for the full set.

### `<image>`

A sprite asset, by path relative to the `.aui`.

| Attribute | Values |
|---|---|
| `src` | path to a sprite asset |
| `mode` | `simple`, `sliced`, `fit` |

```aui
<image src="../Art/logo.png" mode="fit"/>
```

### `<rawimage>`

A texture that only exists at runtime: a `VRCImageDownloader` result, a render texture, or one
tile of a downloaded sprite sheet. Nothing is baked; the element stays invisible until a texture
is set, so a background on its parent shows as the placeholder.

| Attribute | Values |
|---|---|
| `texture` | `{binding}` of type `Texture` |
| `uv` | `{binding}` of type `Rect`: the region shown, 0 to 1 with a bottom-left origin. Starts as `(0, 0, 1, 1)` |
| `mask` | `none` (default) or `circle`: cut the image to a circle |

`texture` and `uv` take a binding; a literal is an error. Give it a `width` and `height` in the
stylesheet, otherwise it is 64 by 64. Allowed inside a `<repeat>`.

`mask="circle"` is drawn by a shader (`AUI/CircleMask`), not a `Mask` or `RectMask2D` component:
those make uGUI copy a material for every masked graphic. The circle is the element's own box, not
the `uv` crop, so a tile of a sprite sheet is centred like a whole texture. An element that is not
square gets an ellipse. The builder gives each masked element (each repeat row, each nested copy)
a material of its own under `Assets/AUI/Generated/Materials/CircleMask/<View>/`, because the shader
has to know that element's `uv`; `SetRawUv` writes it there. In the editor's play mode that write
lands in the asset file, and the next build puts it back.

```aui
<rawimage texture="{avatar}" mask="circle" style="width: 48; height: 48"/>
```

```aui
<repeat id="results" max="10" class="grid">
  <panel class="card">
    <rawimage texture="{item.thumb}" uv="{item.uv}" class="thumb"/>
  </panel>
</repeat>
```

```csharp
SetResultsThumb(i, sheet);                  // Texture
SetResultsUv(i, new Rect(0f, 0.5f, 0.5f, 0.5f));
```

The view does not own the texture: destroy a downloaded texture yourself when you replace it.

## Controls

### `<button>`

| Attribute | Meaning |
|---|---|
| `key` | label translation key |
| `icon` | an icon name shown beside the label |
| `disabled` | `{binding}` to grey it out and stop clicks |
| `selected` | `{binding}` driving the `:selected` state |

| Event | Signature |
|---|---|
| `onclick` | `void Name()` |
| `onhover`, `onleave`, `onpress`, `onrelease` | `void Name()` |

Takes children, so a button can be a whole row.

```aui
<button onclick="Buy" key="shop.buy" icon="shopping-cart"/>
<button onclick="Close" icon="x" class="ghost"/>
<button onclick="Buy" key="shop.buy" disabled="{soldOut}"/>
<button onclick="Pick" onhover="Preview" onleave="Unpreview" key="shop.pick"/>
```

### `<toggle>`

A two-state button. With only an icon it is a square; with a `key` it becomes a labelled switch.

| Attribute | Meaning |
|---|---|
| `bind` | `{bool}`, two-way |
| `key` | label |
| `icon` | the "on" mark; hidden while off |
| `disabled` | `{binding}` |

| Event | Signature |
|---|---|
| `onchange` | `void Name(bool value)` |
| `onhover`, `onleave`, `onpress`, `onrelease` | `void Name()` |

```aui
<toggle bind="{mute}" icon="check" onchange="MuteChanged"/>
<toggle bind="{mute}" key="audio.mute" onchange="MuteChanged"/>
```

### `<slider>`

| Attribute | Meaning |
|---|---|
| `bind` | `{float}`, two-way |
| `min`, `max` | range |
| `step` | increment |
| `format-key` | a key containing `{value}`, e.g. `"{value}%"` |
| `decimals` | digits in the formatted value (0 by default) |
| `disabled` | `{binding}` |

| Event | Signature |
|---|---|
| `onchange` | `void Name(float value)` |

```aui
<slider bind="{volume}" min="0" max="100" step="1"
        format-key="audio.percent" onchange="VolumeChanged"/>
```

In the theme, `background` is the track, `color` the fill and the label, `icon-color` the handle.

### `<colorpicker>`

A saturation/brightness square with a hue bar on its right. The player aims at a colour and
presses; holding and moving drags. A dot shows where the aim lands: in VR the ray starts at the
tracked hand, which is a little off VRChat's laser, so the player aims the dot rather than the
laser.

| Attribute | Meaning |
|---|---|
| `bind` | `{Color}`, two-way; starts white |
| `min-value` | the darkest brightness offered, 0 up to (not including) 1; the square's bottom edge |
| `disabled` | `{binding}` |

| Event | Signature |
|---|---|
| `oninput` | `void Name(Color value)`, every frame of a drag |
| `onchange` | `void Name(Color value)`, once when the press is released |

```aui
<colorpicker bind="{tint}" min-value="0.3" oninput="TintDragged" onchange="TintChanged"/>
<text text="{label}" style="color: {tint}"/>
```

A text that shares the binding follows the drag without a handler. Setting the property from code
moves the cursors and calls neither event. The theme sizes it 240 by 180; `gap` is the space
between the square and the bar, `background` the plate behind both. Not inside a `repeat` or
`template` (AUI112).

### `<input>`

A text box. Clicking parks an invisible target in front of the player; the second click opens
VRChat's keyboard. Needs an `AUI Input Panel` in the scene.

| Attribute | Meaning |
|---|---|
| `bind` | `{string}`, two-way (for a url input: the URL's text, for display) |
| `type` | `text` (default) or `url` |
| `placeholder-key` | translation key shown while empty |
| `maxlength` | character limit; a longer submission is cut to it (text only) |
| `disabled` | `{binding}` |

| Event | Signature |
|---|---|
| `onsubmit` | `void Name(string value)`, or `void Name(VRCUrl value)` when `type="url"` |

```aui
<input bind="{nick}" placeholder-key="profile.nickHint" maxlength="24" onsubmit="NickSubmitted"/>
<input type="url" bind="{link}" placeholder-key="profile.linkHint" onsubmit="LinkSubmitted"/>
```

A url input arms VRChat's own URL field instead of the keyboard field. That matters because Udon
cannot build a `VRCUrl` from a string: the only URL a world can load that was not baked at build
time is one a player typed into a `VRCUrlInputField`. The handler receives that `VRCUrl` (its text
lands in the bound string), and `GetInputUrl(E_LINK)` returns the last one submitted. The next click
reopens the field with it, so the player edits rather than retypes.

```csharp
public void LinkSubmitted(VRCUrl value)
{
    VRCStringDownloader.LoadUrl(value, (IUdonEventReceiver)this);
}
```

### `<select>` and `<option>`

A dropdown. Its options bake as a popup that stays inside the view and always draws above modals.

| `select` attribute | Meaning |
|---|---|
| `bind` | `{int}` index, two-way |
| `max` | bake this many option rows (1..32). The ones past the authored `<option>`s start inactive and empty, for `SetSelectOptionLabel` to fill; the popup resizes to the rows that are showing |
| `disabled` | `{binding}` |

| Event | Signature |
|---|---|
| `onchange` | `void Name(int index)` |

`<option>` takes `key` (or literal text) and must sit directly inside a `<select>`. The select's
value is the option's **index**; `value=` is accepted as a name for whoever reads the markup and is
not baked or reported. A `<select>` cannot sit inside a `<repeat>` (AUI112).

```aui
<select bind="{quality}" onchange="QualityChanged">
  <option value="low" key="quality.low"/>
  <option value="medium" key="quality.medium"/>
  <option value="high" key="quality.high"/>
</select>
```

Filling one at runtime. Labels may be set in any order up to the baked capacity; the count decides
how many rows show and resizes the popup.

```csharp
SetQualityOptionCount(3);
SetQualityOptionLabel(0, "Low");
```

## Navigation

### `<tabs>` and `<tab>`

| `tabs` attribute | Meaning |
|---|---|
| `bind` | `{int}` page index, two-way |
| `for` | the id of the `pages` it drives, when there is more than one group |

| Event | Signature |
|---|---|
| `onchange` | `void Name(int page)` |

`<tab>` takes `page` (the id of the page it shows), `key` and `icon`, and must sit inside `<tabs>`.

### `<pages>` and `<page>`

| `pages` attribute | Values |
|---|---|
| `mode` | `crossfade`, `sequential` |
| `default` | id of the page shown at start |

`<page>` needs an `id` and must sit inside `<pages>`.

```aui
<tabs bind="{page}" onchange="PageChanged">
  <tab page="general" key="settings.general" icon="monitor"/>
  <tab page="audio"   key="settings.audio"   icon="headset"/>
</tabs>
<pages id="main" mode="crossfade">
  <page id="general"> ... </page>
  <page id="audio">   ... </page>
</pages>
```

See [pages and tabs](/guide/pages-and-tabs).

## Lists and creation

### `<repeat>` and `<empty>`

Bakes `max` copies of its single template child.

| Attribute | Meaning |
|---|---|
| `max` | 1 to 64 (AUI120) |

| Event | Signature |
|---|---|
| `onselect` | `void Name(int row)` |

Exactly one template child plus an optional `<empty>` (AUI105). `<empty>` shows when the count is
0.

```aui
<repeat id="board" max="5" onselect="Pick">
  <button class="line"><text text="{item.name}"/></button>
  <empty><text key="board.empty" class="hint"/></empty>
</repeat>
```

See [lists](/guide/lists).

### `<template>`

Bakes `max` parked copies you link in at runtime with `CreateElement`. Must be a direct child of
`<view>`.

| Attribute | Meaning |
|---|---|
| `max` | how many parked copies |
| `pool` | mark it as a tag pool |
| `slot` | the slot id this template serves |
| `view` | the nested view this template holds |

```aui
<template id="line" max="32">
  <panel class="row"><text text="{item.msg}"/></panel>
</template>
```

### `<slot>`

Bakes parked instances of whole views.

| Attribute | Meaning |
|---|---|
| `views` | the view names to park |
| `max` | how many of each |

```aui
<slot id="cards" views="PlayerCard" max="8"/>
```

```csharp
AuiView card = CreateCardsPlayerCard(E_LOG);
```

See [creating elements](/guide/runtime#creating-elements).

## Composition

### `<component>` and `<prop>`

An inline template expanded at compile time, in the same file.

```aui
<component name="Stat">
  <prop name="label"/>
  <prop name="value"/>
  <prop name="tone" default="accent"/>
  <panel class="kpi {tone}">
    <text key="{label}" class="hint"/>
    <text text="{value}"/>
  </panel>
</component>

<Stat label="stats.hp" value="{hp}"/>
```

`<prop>` takes `name`, `type` (`string`, `int`, `float`, `bool`) and `default`. It is also valid
directly under `<view>`, where it declares a prop of that view for its host.

### `<event>`

Declares an event this view raises. Directly under `<view>`.

```aui
<event name="kicked" type="int"/>
```

```csharp
EmitInt("kicked", Score);
```

`type` is the payload type, or absent for none. See [components](/guide/components#typed-events).

### Nested views

Any other `.aui` is used by its name as a tag. It takes no children.

```aui
<PlayerCard nick="{name}" score="{pts}" on-kicked="PlayerKicked"/>
<AuiLanguagePicker/>
```

## Dialogs

### `<modal>`

A dialog over a dimming backdrop. Directly under `<view>`; takes no space in the flow.

| Attribute | Meaning |
|---|---|
| `enabled` | `{bool}` that opens and closes it |
| `dismissable` | `false` to stop a backdrop click closing it |
| `show` | body transition: `none`, `fade`, `fade-scale`, `fade-slide` |

| Event | Signature |
|---|---|
| `onopen`, `onclose` | `void Name()` |

```aui
<modal id="confirm" enabled="{asking}" onclose="ResetCancelled">
  <text key="settings.reset.sure"/>
  <panel class="row">
    <button onclick="DoReset" key="yes" class="primary"/>
    <button onclick="CloseConfirm" key="no"/>
  </panel>
</modal>
```

```auis
modal::backdrop { background: rgba(0, 0, 0, 0.6); }
```

## Where each element may go

Most elements go anywhere. These do not (AUI112 otherwise):

| Element | Must sit directly inside |
|---|---|
| `option` | `select` |
| `tab` | `tabs` |
| `page` | `pages` |
| `empty` | `repeat` |
| `prop` | `view` or `component` |
| `event` | `view` |
| `component` | `view` |
| `modal` | `view` |
| `template` | `view` |

## Next

- [Style properties](/reference/properties) for what a stylesheet can set.
- [Diagnostics](/reference/diagnostics) for every code named above.
- [View API](/reference/view-api) for driving these from code.
