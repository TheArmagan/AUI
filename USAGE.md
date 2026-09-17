# Using AUI

How to build panels with AUI in a VRChat world. `README.md` is the overview, `REFERENCE.md` the
full specification; this is the recipe book. Every snippet here compiles against v0.1.0.

Contents

1. One-time project setup
2. Your first view
3. Writing the logic half
4. Every element, with an example
5. Bindings in depth
6. Handlers in depth
7. Languages
8. Styling
9. Lists (repeat)
10. Pages and tabs
11. Nested views and the language picker
12. Talking to other Udon scripts
13. Showing, hiding and placing views
14. Testing without a headset
15. Troubleshooting

---

## 1. One-time project setup

The package sits at `Packages/rest.armagan.aui` (a folder or a junction). Then in Unity:

1. `AUI/Create Settings` creates `Assets/AUI/AuiSettings.asset` and `Assets/AUI/Lang/`.
2. Drop at least `Assets/AUI/Lang/en.json` in (see 7). The default language code is on the
   settings asset (`defaultLanguage`, `en` by default).
3. `AUI/Bake Fonts`. This reads every language file, bakes atlases with exactly those glyphs
   into `Assets/AUI/Fonts/`, and assigns `AuiFontSet.asset` to the settings. Re-run it whenever
   a language file gains characters the atlases do not have (a box glyph in-world is the sign).
4. In each scene that shows views: `AUI/Rebuild Language Table` (creates `AUI Language Service`)
   and `AUI/Rebuild Input Panel` (creates `AUI Input Panel`). Views find both by name.

Optional but worth it: run `tools~/vscode-aui/install.ps1` once to get squiggles, completion,
hover and go-to-definition for `.aui` and `.auis` files in VS Code.

## 2. Your first view

Right-click `Assets/UI` in the Project window, `Create > AUI > View`, and type `Hello`. That
writes `Hello.aui` with the `name` attribute filled in (`View with Stylesheet` adds `Hello.auis`
next to it). Then make it:

```xml
<view name="Hello" width="300" height="140">
  <text key="hello.title" class="title"/>
  <text key="hello.greeting" args="{playerName}"/>
  <panel class="row">
    <spacer/>
    <button onclick="Wave" key="hello.wave" icon="hand"/>
  </panel>
</view>
```

Add the keys to `Assets/AUI/Lang/en.json`:

```json
{
  "_name": "English",
  "hello": { "title": "Hello", "greeting": "Hi {playerName}", "wave": "Wave" }
}
```

Save. Watch the console: `[AUI] wrote Assets/UI/Hello.g.cs`, `[AUI] scaffolded Assets/UI/Hello.cs`,
a script reload, then `[AUI] built Assets/UI/Hello.prefab`. Drag the prefab into the scene. Its
inspector shows only what you declare in `Hello.cs`; everything baked is hidden.

Rules that follow from the file name: the `name` attribute must equal the file stem, and the
class is `Armagan.AUI.Generated.Hello`.

## 3. Writing the logic half

`Hello.cs` was scaffolded with one empty method per handler:

```csharp
using UnityEngine;
using VRC.SDKBase;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    public partial class Hello : AuiView
    {
        protected override void OnStart()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            PlayerName = lp != null ? lp.displayName : "you";
        }

        public void Wave()
        {
            PlayerName = PlayerName + "!";
        }
    }
}
```

What you get for free: `PlayerName` is a property whose setter re-renders every element that uses
`{playerName}` in the current language; `Wave()` runs when the button is pressed; `OnStart` runs
once after the view is wired; `OnRetranslate` runs after every language switch; `Show()`, `Hide()`,
`Toggle()`, `IsVisible()` are inherited.

Do not edit `Hello.g.cs`. It is regenerated whenever the markup changes, and the header says so.

## 4. Every element, with an example

Text, three ways:

```xml
<text key="shop.title" class="title"/>                 <!-- translated -->
<text text="{status}"/>                                 <!-- a runtime string, escaped -->
<text key="shop.owned" args="{count} {name}"/>          <!-- "You own {count} {name}" -->
<text>Debug build</text>                                <!-- literal, never translated (info AUI117) -->
```

Icons and images:

```xml
<icon name="settings" size="22"/>                       <!-- Icons/settings.svg, tinted by icon-color -->
<image src="../Art/logo.png" mode="fit"/>               <!-- a Sprite asset, path relative to the .aui -->
```

Buttons:

```xml
<button onclick="Buy" key="shop.buy" icon="cart"/>
<button onclick="Close" icon="x" class="ghost"/>
<button onclick="Buy" key="shop.buy" disabled="{soldOut}"/>
<button onclick="Pick" onhover="Preview" onleave="Unpreview" key="shop.pick"/>
```

Toggle, slider, input, select:

```xml
<toggle bind="{mute}" icon="check" onchange="MuteChanged"/>
<toggle bind="{mute}" key="audio.mute" onchange="MuteChanged"/>       <!-- labelled switch -->
<slider bind="{volume}" min="0" max="100" step="1" format-key="audio.pct" onchange="VolumeChanged"/>
<input bind="{nick}" placeholder-key="profile.nickHint" maxlength="24" onsubmit="NickSubmitted"/>
<select bind="{quality}" onchange="QualityChanged">
  <option value="low" key="quality.low"/>
  <option value="high" key="quality.high"/>
</select>
```

`format-key` points at a string containing `{value}`, e.g. `"{value}%"`. `decimals` controls the
number formatting (0 by default). An input is a visible box; clicking it parks an invisible target
in front of the player and the second click opens VRChat's keyboard. A select bakes its options as
a popup that stays inside the view.

Layout helpers:

```xml
<panel class="row"> ... </panel>       <!-- horizontal, centred, gap 12 (theme class) -->
<spacer/>                              <!-- grows to push what follows to the end -->
<spacer size="8"/>                     <!-- fixed gap -->
<panel style="anchor: fill"/>          <!-- absolute, fills the parent -->
```

## 5. Bindings in depth

`{name}` anywhere in an attribute value declares a binding; the same name in several places is one
property. The type comes from where it is used:

| Used as | Type | Property |
|---|---|---|
| `text="{s}"`, `args="{s}"`, input `bind` | string | `string S { get; set; }` |
| `if="{b}"`, `disabled="{b}"`, `selected="{b}"`, toggle `bind` | bool | `bool B` (an `if` binding starts true) |
| slider `bind` | float | `float F` |
| select `bind`, tabs `bind` | int | `int I` |

Two-way: a toggle, slider, select, tabs or input bound to `{x}` writes `X` back when the player
uses it and then calls your handler. Setting `X` from code moves the control and does not call the
handler.

Strings are escaped when set: a `<` becomes `<` plus a zero-width space, so a player name such as
`<color=red>` renders literally. The getter returns the escaped string.

Show or hide a part of a view:

```xml
<panel if="{isVip}"> ... </panel>
```
```csharp
IsVip = Networking.LocalPlayer != null && Networking.LocalPlayer.isVRCPlus;
```

## 6. Handlers in depth

A handler attribute names a method you write. Signatures:

| Attribute | On | Signature |
|---|---|---|
| `onclick`, `onhover`, `onleave`, `onpress`, `onrelease` | button, toggle, tab, option, any clickable | `void Name()` |
| `onchange` | toggle | `void Name(bool value)` |
| `onchange` | slider | `void Name(float value)` (per frame while dragging) |
| `onchange` | select, tabs, pages | `void Name(int value)` |
| `onsubmit` | input | `void Name(string value)`; `void Name(VRCUrl value)` for `type="url"` |
| `onselect` | repeat | `void Name(int row)` |
| any, inside a repeat template | | `int row` is the first parameter |
| `on-<event>` | a nested view | `void Name()` |

A missing handler is a normal C# compile error naming the method. Using one name for two events
with different signatures is a markup error (AUI108).

Slider handlers fire every frame during a drag. If the handler writes PlayerData or does anything
heavy, debounce it with `SendCustomEventDelayedSeconds`.

## 7. Languages

One file per language in `Assets/AUI/Lang/`, named by code (`en`, `tr`, `ja`, `pt-BR`), nested
objects flattened with dots, `_name` shown by the picker:

```json
{
  "_name": "Turkce",
  "shop": { "title": "Magaza", "owned": "{count} adet {name} sahibisin" },
  "audio": { "pct": "%{value}" }
}
```

Rules: the default language must have every key (error otherwise); other languages may miss keys
(warning, and the default text is used); placeholders must match the element's `args` (error);
`{value}` is reserved for slider format keys. The languages a world ships are exactly the files in
that folder; the package's own `aui.lang.*` keys are merged in for those codes.

At runtime the client language is detected when the local player joins, matched exactly, then by
base code (`pt-BR` to `pt`), then the default. A pick from the picker is saved in PlayerData and
wins on the next visit. Switch from code: `lang.SetLanguageByCode("ja")` from any view (`lang` is
the service). Every view repaints and `OnRetranslate` runs.

After adding characters (a new language, or new text with letters the atlases lack):
`AUI/Bake Fonts`. Boxes instead of glyphs mean this step was skipped.

## 8. Styling

The package theme (`Theme/Default.auis`) gives every element a look and defines the palette
variables. A view adds its own sheet in one of three ways:

- `Name.auis` next to `Name.aui` (linked automatically);
- `<link href="Shared.auis"/>` under `<view>`, one or more, which replaces the sibling;
- `<style>` blocks inside the view, and `style=""` on an element for one-offs.

```
:root { --accent: #F0B072; }              /* override a theme variable for this view */
.setting { flex-direction: row; align: center; gap: 12; padding: 6 10;
           background: sprite(Rounded8) #1A130E; transition: background 0.12s out-quad; }
.setting:hover { background: var(--card); }
.primary { background: sprite(Rounded8) var(--accent); color: var(--base); }
.primary:pressed { scale: 0.96; }
```

Selectors: `tag`, `.class`, `tag.class`, and `:hover`, `:pressed`, `:selected`, `:disabled` on
them. No descendant selectors, by design: styles are baked per element. A state may only change
color, background, border, icon-color, opacity, scale and offset, and it animates with the
element's `transition` list.

Units are millimetres. A 440 wide view is 44 cm in the world. Font sizes are in the same units;
18 is the theme's body size, readable at about two metres.

Theme classes you can use without defining them: `.title`, `.muted`, `.row`, `.grid`, `.card`,
`.chip`, `.ghost`.

## 9. Lists (repeat)

```xml
<repeat id="rows" max="8" onselect="RowPicked" class="grid">
  <button class="chip" selected="{item.current}">
    <icon name="user"/>
    <text text="{item.name}" style="max-width: 90"/>
  </button>
  <empty><text key="list.empty"/></empty>
</repeat>
```

`max` copies are baked; there is no runtime instantiation. Generated API:

```csharp
RowsCount = players.Length;                 // rows beyond the count are hidden; <empty> shows at 0
for (int i = 0; i < players.Length; i++)
{
    SetRowsName(i, players[i]);             // one Set<Id><Field>(row, value) per {item.x}
    SetRowsCurrent(i, i == mine);
}
public void RowPicked(int row) { ... }      // and any handler inside the template gets int row first
```

Give a bound text inside a row a `max-width`, otherwise it is sized for the sample string and a
grid gets one chip per line. Nested repeats are not supported.

## 10. Pages and tabs

```xml
<tabs bind="{page}" onchange="PageChanged">
  <tab page="general" key="settings.general" icon="sliders-horizontal"/>
  <tab page="audio" key="settings.audio"/>
</tabs>
<pages id="main" default="general" mode="crossfade">
  <page id="general"> ... </page>
  <page id="audio"> ... </page>
</pages>
```

Generated: `Page` (the binding), `GetMainPage()`, `SelectMainPage(int)`, constants `MAIN_GENERAL`,
`MAIN_AUDIO`, and `PageChanged(int)` is called on tab clicks (not on `SelectMainPage` from code).
With more than one `pages` in a view, `tabs` needs `for="pagesId"`. `mode="sequential"` waits for
the old page to fade out before the new one fades in. Page transitions come from the stylesheet
(`page { page-transition: slide-left; }`).

## 11. Nested views and the language picker

Any `.aui` in the project (or in `AuiSettings.componentFolders`) is a component; use it by its
name as a tag. Attributes set the child's bindings; `on-<event>` receives what the child `Emit`s.

```xml
<page id="lang"><AuiLanguagePicker/></page>
<Card title="{cardTitle}" price="12" on-bought="CardBought"/>
```

In the child (`Card.cs`): `Emit("bought")`. In the parent: `public void CardBought() { }` and a
typed field `childCard` to reach the child directly (`childCard.Title = "..."`). Children build
before parents; a nested view takes no children of its own and cannot be a repeat template.

`AuiLanguagePicker` ships with the package: one chip per language, current one highlighted, tap
to switch. Drop it anywhere.

### Declared props and typed events

A nested view can declare what it takes and what it raises, so the host gets completion and
errors instead of silent defaults:

```xml
<view name="Player" width="300" height="60">
  <prop name="nick" default="anon"/>
  <prop name="score" type="int"/>
  <event name="kicked" type="int"/>
  <panel class="row"><text text="{nick}"/><text text="{score}"/><button onclick="Kick" icon="x"/></panel>
</view>
```

```csharp
public void Kick() { EmitInt("kicked", Score); }        // in Player.cs
public void PlayerKicked(int score) { ... }              // in the host, for on-kicked="PlayerKicked"
```

`<Player nick="{name}" score="{pts}" on-kicked="PlayerKicked"/>`: a bound prop takes the child's
declared type, a literal is assigned once at start, and an attribute that is not a declared
prop is error AUI121.

### Inline components

Small repeated pieces do not need their own file. Declare a template once and use it as a tag;
it is expanded at compile time, so `{prop}` works anywhere in it (class, style, icon, text, if):

```xml
<view name="Stats" width="400" height="200">
  <component name="Stat">
    <prop name="label"/>
    <prop name="value"/>
    <prop name="tone" default="muted"/>
    <panel class="card {tone}"><text class="title">{value}</text><text class="muted">{label}</text></panel>
  </component>
  <panel class="row">
    <Stat id="hp" label="Health" value="{hp}"/>
    <Stat label="Score" value="{score}" tone="warm" class="wide"/>
  </panel>
</view>
```

The usage's `id`, `class`, `style` and `if` merge onto the clone's root; ids inside the template
become `usageId_inner`. Handlers named inside a component are the view's own methods.
Components may use other components. A `{binding}` passed as a prop value is the host's binding,
written through as usual.

## 12. Talking to other Udon scripts

Both directions are typed references, no strings, no `SetProgramVariable`.

UI to script: declare a public field in the view's `.cs`, set it in the inspector on the scene
instance (a prefab cannot reference scene objects, so it is an instance override), call it.

```csharp
public partial class SettingsDemo : AuiView
{
    public DemoBrightness brightness;                         // shows in the inspector
    public void BrightnessChanged(float value) { if (brightness != null) brightness.SetPercent(value); }
}
```

Script to UI: hold the generated class and set its properties.

```csharp
using Armagan.AUI.Generated;

public class DemoBrightness : UdonSharpBehaviour
{
    public SettingsDemo panel;
    private void Start() { if (panel != null) panel.Brightness = 70f; }   // moves the slider, no handler call
}
```

Both sides survive `AUI/Rebuild All Views`: the builder keeps the prefab's ids, so instance
overrides stay attached. The demo in `Samples~/Demo` is this exact pairing.

Writing to a view before it has started is safe. A view on a hidden page, or any inactive
object, has not had its `Start` when another script reaches it; every property setter and every
public method starts the view first, and the value you wrote survives the view's own `OnStart`
defaults. A parent view starts all of its nested children before its own `OnStart` runs, so a
child's read-back methods answer correctly from the parent's `OnStart` onward. If you need the
same guarantee for a getter (a `Size()` you wrote yourself, say), call `EnsureStarted()` on the
view first.

Two naming rules for the author half: do not declare a private field named `_<binding>` (the
generated backing field has that name and the compile fails with a duplicate), and do not use a
binding name that matches an `AuiView` member (`Name`, `Lang`, `Show`, ...; the analyzer refuses
those).

## 13. Showing, hiding and placing views

A view is a world-space canvas, 1 unit = 1 mm at the prefab's 0.001 scale. Place the instance
where a player will stand about a metre away; the panel's front faces the instance's negative Z
in the way any uGUI canvas does, so rotate the instance to face the player.

`Show()` and `Hide()` fade (and scale or slide, per `show`) and disable the content when hidden;
`ShowInstant()` / `HideInstant()` skip the animation; `start-hidden="true"` on `<view>` boots
hidden. Transitions run through VRCTween, which does nothing in the editor; there they snap.
Another script can call these on the typed reference.

### Attaching a view to the player

`attach` on `<view>` ties an outermost view to the local player instead of a place in the world.
One example per mode:

```aui
<!-- At the left hand in VR, shown while the player looks at that hand. On desktop there is no
     wrist, so the view sits in attach-corner instead. -->
<view name="Hud" width="150" height="80" attach="wrist-left" attach-corner="bottom-left">

<view name="Hud" width="150" height="80" attach="wrist-right" attach-corner="bottom-right">

<!-- Head-locked in a corner of the player's view, for everyone. -->
<view name="Status" width="150" height="80" attach="corner" attach-corner="top-right" attach-distance="0.6">

<!-- Floats in front at eye level; recentres only after the player turns or walks away. -->
<view name="Welcome" width="440" height="300" attach="follow" attach-distance="1.2" start-hidden="true">

<!-- Placed straight ahead once, on Show(), and then stays where it was put. -->
<view name="Notice" width="440" height="300" attach="front" attach-distance="0.8" start-hidden="true">

<!-- Not attached, but built with the component so code can attach it later. -->
<view name="Panel" width="440" height="300" attach="none">
```

`attach-corner` is `bottom-left`, `bottom-right`, `top-left` or `top-right`. `attach-distance` is
metres from the head, 0.2 to 3, default 0.6; it applies to `corner`, `follow` and `front`.

How each mode behaves:

- `wrist-left` / `wrist-right` use the hand's tracked position only. The view sits a few
  centimetres above the hand, always faces the head, and appears while the hand is within 0.6 m
  of the head and the player is looking at it (within 24 degrees; it hides again past 36). It does
  not read the hand's rotation, which differs between controllers and avatar rigs. A view that is
  out of sight has its canvas and collider disabled; its content and bindings are untouched.
- `corner` is head-locked: 27 degrees sideways and 15 degrees up or down from the centre of the
  view, at `attach-distance`, parallel to the view plane. `cornerAngles` on the component moves
  it further out.
- `follow` stays level (yaw only), 0.12 m below eye height. It moves only after the player has
  turned more than 38 degrees away from it or walked more than 0.45 m, then glides to the new
  spot.
- `front` is placed by `Show()` and `ShowInstant()` and is not moved again until the next one.

From code, on a view whose markup has `attach` (`attach="none"` is enough):

```csharp
SetAttach(ATTACH_FOLLOW);                 // ATTACH_NONE, _WRIST_LEFT, _WRIST_RIGHT, _CORNER, _FOLLOW, _FRONT
SetAttachCorner(CORNER_TOP_RIGHT);        // CORNER_BOTTOM_LEFT, _BOTTOM_RIGHT, _TOP_LEFT, _TOP_RIGHT
SetAttachDistance(1.2f);                  // clamped to 0.2 .. 3
SetAttachOffset(new Vector3(0f, 0.09f, 0.03f));   // wrist: metres from the hand; x right, y up, z toward the head
int mode = GetAttach();
```

The attribute is what makes the builder add the `AuiAttach` component to the prefab root. Without
it `SetAttach` logs one warning and does nothing, because a component cannot be added in-world.
The component's other numbers (the wrist offset, range and angles, the corner angles, the follow
drop, angle, slack and speed) are public fields on it; set them in the inspector on the prefab or
on a scene instance, and they survive rebuilds. The builder rewrites only the mode, the corner and
the distance.

An attached view costs a per-frame update only while it is shown and moving with the player:
nothing runs while the view is hidden, in `none`, or after a `front` placement. A view with no
controls has its root collider turned off so it never stops a laser; a view with controls keeps
it, and on a wrist it is on only while the view is visible.

A view used inside another view cannot be attached: the nested instance has no canvas of its own,
so the builder removes its `AuiAttach` and the host gets warning `AUI123`.

### Changing the layout at runtime

The layout you see in the editor is solved again in-world whenever something that affects it
changes, so a longer label pushes its neighbours, a hidden element frees its space, a list with
three rows is three rows tall, and a language switch lays out that language's widths. Nothing
is needed for that; every setter marks the view dirty and one solve runs on the next frame.

```csharp
SetActiveElem(E_DETAILS, false);          // display: none; siblings close the gap
SetDisplay(E_DETAILS, DISPLAY_FLEX);      // back in the flow
SetPosition(E_BADGE, -6f, -6f, float.NaN, float.NaN); // position: absolute at left -6, top -6
SetSize(E_CARD, 120f, float.NaN);         // explicit width, height back to auto
SetElemText(E_TITLE, "A longer title");   // the same call the generated setters use

SetAutoRelayout(false);                   // batch: many changes, one solve
for (int i = 0; i < 30; i++) SetItemsLabel(i, names[i]);
Relayout();                               // or SetAutoRelayout(true)
```

`display: none` and `hidden` remove the space; use `opacity: 0` when the slot should stay.
`position: absolute` with `top`, `right`, `bottom`, `left` (negatives allowed) pins an element
to its parent's content box. Tick `debugLayout` on a view to log every solve with its time.

**Do not put the `hidden` attribute on an element that has an `icon=` or a `key=`.** A hidden
element's subtree is not laid out, at bake time either, and a clickable's icon is positioned by
the builder exactly once — the runtime solver never writes it. So an icon-only button baked
hidden has its glyph placed against a zero-wide box, ends up hanging outside the button's left
edge, and stays there no matter what is shown or relaid out afterwards. A keyed one collapses to
its padding instead, because TMP measures nothing on an inactive object.

Bake it live and let your model push the binding false on the first frame, which is what a host
does for a nested view's props anyway:

```xml
<!-- wrong: the chevron ends up outside the button, for good -->
<button onclick="Next" icon="chevron-right" if="{hasNext}" hidden="true"/>
<!-- right: laid out at bake, switched off by the binding -->
<button onclick="Next" icon="chevron-right" if="{hasNext}"/>
```

`hidden` is still the right thing on a plain `panel` or `text` — those the solver owns and
re-places whenever they are shown.

### Changing classes at runtime

Every class a sheet of the view defines can be added or removed in-world; the element and its
descendants are restyled through the same cascade the compiler ran, with the element's
`transition` for the properties that changed.

```xml
<panel id="card" class="card {tone}">...</panel>
```

```csharp
AddClass(E_CARD, C_WARM);          // generated C_ constants, one per class
RemoveClass(E_CARD, C_CARD);       // a static class can be removed too
ToggleClass(E_CARD, C_SELECTED);
Tone = "warm big";                 // the {tone} binding: any number of names
SetClasses(E_CARD, "warm");        // replaces what AddClass added; static and bound stay
if (HasClass(E_CARD, C_WARM)) ...
```

A class changes layout too (`padding`, `width`, `display: none`, `position: absolute`, ...) and
the view re-solves on the next frame. `font-weight` cannot change at runtime (Udon has no font
setter), so a class that only exists for runtime use and sets a weight gets warning AUI212.

### Binding style values

Any style value can be a binding, and the element re-resolves when it changes:

```xml
<panel id="bar" style="width: {barWidth}; background: {barColor}; opacity: {barAlpha}; padding: {pv} {ph}"/>
```

```csharp
BarWidth = "50%";                 // lengths are strings: "12", "12mm", "50%", "auto"
BarColor = new Color(1f, 0.6f, 0.2f);   // colours are UnityEngine.Color
BarAlpha = 0.5f;                  // numbers are floats
Pv = "4"; Ph = "12";              // each box component is its own binding
```

The type follows the property: numbers and offset components are `float`, colours are `Color`,
lengths, box edges and enum values (`row`, `center`, `none`) are `string`. A bound value must be
bindings only (`width: {w}mm` is error AUI213); the bake uses the property's default until the
value arrives.

### Utility classes

A small built-in set of style-only classes, written with a star so they never collide with your
own and never count toward the 64-class budget:

```xml
<panel class="card *flex-row *items-center *gap-8 *p-12">
  <text class="*fg-muted *text-sm">hint</text>
  <spacer/>
  <button class="*w-64" icon="x"/>
</panel>
```

Spacing steps are 0 1 2 4 6 8 12 16 24 32 48 64: `*gap-N`, `*p-N *px-N *py-N *pt-N *pr-N *pb-N
*pl-N`, `*m-N` and friends, `*w-N *h-N *top-N *right-N *bottom-N *left-N`. Layout: `*flex-row
*flex-col *wrap *nowrap *grow *grow-0 *items-start|center|end|stretch *justify-start|center|end|between
*w-full *h-full *w-auto *h-auto *hidden *flex *absolute *static *inset-0 *center`. Visual:
`*rounded-4|8|12|16|24 *opacity-0|25|50|75|100 *text-left|center|right *text-sm|base|lg|xl
*font-regular|medium|semibold|bold *bg-transparent *bg-none *bg-base|nav|surface|panel|card|chip|accent
*fg-text|muted|accent|base *scale-90|100|110`. Your own sheets override them (they sit between the
theme and your sheets), you can define your own star classes with `.*name { ... }`, and only the
utilities a view mentions are baked into it. To add one at runtime that the markup never uses,
list it in `<view utilities="p-4 p-8">`; `AddClassNamed(elem, "*p-8")` then works.
`GetElements(".*p-8")` returns nothing: star classes are for styling only.

### Finding and driving elements from code

The generated `E_ID` constants are element indices; the same ints come back from the selector
helpers, so code that builds views generically does not need the constants:

```csharp
int card = GetElement("#card");            // -1 when there is no such id
int[] chips = GetElements(".chip");        // effective runtime classes
int[] buttons = GetElements("button");     // by tag

SetText(card, "Escaped for you");          // any text, bound or literal
SetIcon(GetElement("#close"), "check");    // icons the view uses, plus <view icons="check star">
SetBackground(card, "Rounded16");          // sprites the sheets mention, plus <view sprites="...">
SetOpacity(card, 0.5f); SetColor(card, Color.red); SetBackgroundColor(card, c);
SetScale(card, 1.1f); SetFontSize(card, 22f); SetTextAlign(card, 1);
SetStyleValue(card, PROP_GAP, new Vector4(12, 0, 0, 0));   // any cascade property by id

RectTransform rt = GetRect(card);          // drive uGUI yourself
GameObject go = GetObject(card);
```

Elements with an id get a CanvasGroup at bake so `SetOpacity` always has something to fade.
A value set this way stays until the next class change on that element re-resolves it.

### Re-theming at runtime

The `:root { --name: value; }` variables of the theme and of your sheets are baked into every view
with their values, and every declaration that reads one (`color: var(--accent)`) keeps its text.
Setting a variable re-encodes those declarations and re-resolves the whole view through the
cascade, so a theme swap is one call per variable and every rule, state rule and nested view
follows, each element through its own `transition`:

```csharp
SetStyleVariable("accent", "#4FC3F7");          // "accent" or "--accent"; values are stylesheet text
SetStyleVariableColor("base", new Color(0.06f, 0.08f, 0.1f));
SetStyleVariableFloat("radius-scale", 1.25f);   // numbers and lengths, written without a locale
string now = GetStyleVariable("accent");        // the current text, null when no sheet defines it

lang.SetStyleVariable("accent", "#4FC3F7");     // every registered view, and every view that starts later
```

A variable exists for a view when any of its sheets (theme included) declares it; a name no sheet
declares logs a warning. A value that does not parse for a property (`"row"` into a colour) leaves
that declaration alone and logs which one. Anything written through `SetStyleValue` or the colour
helpers is re-resolved by the change, like a class change would.

### Creating elements at runtime

Nothing is allocated in-world: a `<template>` bakes `max` parked copies of its child, and
`CreateElement` links one under any container you name. Pools and slots are the same thing with
less markup.

```xml
<view name="Chat" width="400" height="300" pool="text:8 panel:4">
  <panel id="log" class="card"/>
  <template id="line" max="32">
    <panel class="row"><icon name="user"/><text text="{item.who}" class="muted"/><text text="{item.msg}"/></panel>
  </template>
  <slot id="cards" views="PlayerCard" max="8"/>
  <button onclick="Say">Say</button>
</view>
```

```csharp
public void Say()
{
    int line = CreateLine(E_LOG);            // generated per template; -1 when all 32 are in use
    SetLineWho(line, "you");                 // {item.x} setters take the created element
    SetLineMsg(line, "hello");
    int note = CreateElementByTag("text", E_LOG, "muted", 0);   // from the pool, with classes, at index 0
    SetText(note, "(new message)");
    AuiView card = CreateCardsPlayerCard(E_LOG);                // a parked nested view; cast to PlayerCard
    MoveElement(line, E_LOG, 0);             // reorder or reparent
    DestroyElement(note);                    // back to the pool
}
```

Templates must be direct children of `<view>`; their instances can go under any container.
Handlers inside a template get the instance row (`Pick(int row)`); `InstanceElem(T_LINE, row)`
and `InstanceRow(T_LINE, elem)` convert. Only `panel`, `text`, `icon` and `spacer` can be pooled;
anything with more wiring goes in a template. `SetAutoRelayout(false)` before a burst of creates
and `Relayout()` after gives one solve for the batch.

### Backgrounds with a material or shader

```
.glow       { background: sprite(Rounded8) shader(Custom/UIGlow) var(--accent); }
.glow:hover { background: material(GlowHot); }
```

`shader(Name)` finds the shader by name and bakes one material asset for it under
`Assets/AUI/Generated/Materials` (created on first use; tweak its properties there).
`material(Name)` uses any Material asset of that name in the project. The element's colour is
still the vertex tint and the sprite is still the texture, so a shader that reads `_MainTex` and
the vertex colour behaves like the default UI shader. Materials are shared assets: a property you
set on one is seen by every element that uses it. `SetMaterial(elem, "GlowHot")` swaps at runtime
(names the sheets mention, plus `<view materials="...">`); `SetMaterial(elem, null)` clears it.

### Modals

A `<modal>` is a dialog over a dimming backdrop. It must sit directly under `<view>`, it takes
no space in the flow, and the theme centres it (`anchor: center`); `top`/`left` or another
anchor place it elsewhere.

```xml
<view name="Settings" width="400" height="300">
  <button onclick="AskReset" key="settings.reset"/>
  <modal id="confirm" enabled="{asking}" onclose="ResetCancelled">
    <text key="settings.reset.sure"/>
    <panel class="row">
      <button onclick="DoReset" key="yes" class="primary"/>
      <button onclick="CloseConfirm" key="no"/>
    </panel>
  </modal>
</view>
```

```csharp
public void AskReset() { Asking = true; }           // the binding opens it
public void DoReset()  { ...; CloseConfirm(); }      // generated Open/Close/IsOpen per modal
public override void ... Back();                     // closes an open select, then the top modal
```

A click on the backdrop closes the modal (and fires `onclose`) unless `dismissable="false"`.
`show` picks the body's transition (`fade`, `fade-scale`, `fade-slide`, `none`); the backdrop
always fades. Style the backdrop with `modal::backdrop { background: rgba(0,0,0,0.6) }`, or per
modal with `modal.dark::backdrop`. `Hide()` keeps the modal open behind the scenes, so the view
comes back the way it was. Later opened modals stack on top; a select's dropdown always draws
above a modal.

## 14. Testing without a headset

Play mode in the editor runs the whole state machine except tweens and the keyboard. Add
parameterless test methods to a view and send them as custom events from the inspector or from
editor code:

```csharp
public void _TestJapanese() { if (lang != null) lang.SetLanguageByCode("ja"); }
public void _TestAudioPage() { SelectMainPage(MAIN_AUDIO); }
```

With UnityMCP or a small editor script: `GetBackingUdonBehaviour(view).SendCustomEvent("_TestJapanese")`.
A clickable can be driven the same way through its signal: `signals[S_CLOSE]` and `_Click`.

The edit-mode tests (`Tests/Editor`) cover the compiler; run them from the Test Runner after
changing the package.

The runtime has its own suite: the gallery in `Samples~/Demo/Views/Gallery` (in this world under
`Assets/AUI/Demo/Gallery`, placed in `Demo.unity`). It nests nine feature views behind a sidebar
of tabs, logs every child event, and its `RunTests` method writes to each child through the typed
API and checks what rendered: bound text, escaping, placeholders, slider value and label, toggle,
an `if` binding, select, runtime-filled options, disabled and enabled buttons, child `Emit`
bubbling, repeat count and visibility, a layout re-solve from a bound style value, state flags, a
class binding, a `:root` variable round trip, template and nested-view creation, page selection
and a language round trip. Press *Run tests* in play mode, or send `_TestRun` to the gallery's
UdonBehaviour; each result also reaches the console as `[AUI gallery] PASS name`. Extend it when
you add a runtime feature.

## 15. Troubleshooting

- Nothing appears after saving a `.aui`: read the console for `[AUI] path:line:col`. Errors stop
  the build; `AUI/Validate` prints every diagnostic for every view.
- `Name.g.cs` did not update: the queue only runs when the project compiles. Fix the C# errors
  (usually a handler with the wrong signature) and save the `.aui` again, or `AUI/Rebuild All
  Views`.
- The prefab did not change: its bake stamp matched. `AUI/Rebuild All Views` forces it.
- Boxes instead of letters: `AUI/Bake Fonts`.
- A key shows as its own name in-world: it is missing from the default language.
- Every text shows `??`: the scene's language table is older than the views. The build rebakes it
  after every prefab build when the scene has an `AUI Language Service`; if the scene has none,
  `AUI/Rebuild Language Table` creates it.
- `Foo.cs` fails with a duplicate field: you declared `_<binding>`; the generated backing field
  owns that name. Rename yours.
- A build takes seconds: the console line `[AUI] drain: ... (scan, compile, udonsharp, build)`
  says where. `udonsharp` is paid once per drain and only when a view is new; `build` is the
  prefab work per view.
- Text of a bound string does not fit: give the element `max-width` or `wrap="true"`, or a
  shorter `boundTextSample` in the settings.
- Nothing is clickable in the real client but works in the editor: the root canvas lost its
  `VRCUiShape` or collider (a rebuild restores both), or the view is scaled by a parent.
- A wrist view never appears: outside VR there is no wrist and the view sits in `attach-corner`
  instead. In VR it shows only while the hand is within 0.6 m of the head and the player is
  looking at it; raise the hand into view. `wristRange`, `wristShowAngle` and `wristHideAngle` on
  the `AuiAttach` component widen that. The view must also be shown: a `start-hidden` view stays
  hidden until `Show()`.
- `SetAttach` does nothing and the console says the view was built without attach: add
  `attach="none"` to its `<view>` and let it rebuild. The component that moves the view is baked.
- An attached view stops lasers aimed past it: it has at least one control, so its collider is
  on. Remove the controls (a display-only attached view has its collider off), or move it out of
  the way with `attach-corner`, `attach-distance` or the component's offsets.
- An attached view stays where its host put it: it is nested in another view (warning `AUI123`).
  Only an outermost view follows the player.
- The inspector shows hundreds of arrays: the view was built by an older version; rebuild it and
  the per-view inspector takes over.
- Two `ArgumentNullException: unityObject` lines per rebuild: UdonSharp's serializer reacting to a
  loaded prefab being reimported. Harmless.
- Position or references reset after a rebuild: only happened before the stable-id builder;
  Prefab > Revert All on the instance once, set them again, and they will stick.
