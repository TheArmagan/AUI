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
| `onsubmit` | input | `void Name(string value)` |
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
`Assets/AUI/Demo/Gallery`, placed in `Demo.unity`). It nests five feature views on tabbed pages,
logs every child event, and its `RunTests` method writes to each child through the typed API and
checks what rendered: bound text, escaping, placeholders, slider value and label, toggle, select,
disabled and enabled buttons, child `Emit` bubbling, repeat count and visibility, state flags,
page selection and a language round trip. Press *Run tests* in play mode, or send `_TestRun` to
the gallery's UdonBehaviour; each result also reaches the console as `[AUI gallery] PASS name`.
Extend it when you add a runtime feature.

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
- The inspector shows hundreds of arrays: the view was built by an older version; rebuild it and
  the per-view inspector takes over.
- Two `ArgumentNullException: unityObject` lines per rebuild: UdonSharp's serializer reacting to a
  loaded prefab being reimported. Harmless.
- Position or references reset after a rebuild: only happened before the stable-id builder;
  Prefab > Revert All on the instance once, set them again, and they will stick.
