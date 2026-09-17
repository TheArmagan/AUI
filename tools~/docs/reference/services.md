# Services and settings

The scene objects and assets around a view.

## AuiLanguageService

One per scene, on a GameObject named `AUI Language Service`. Views find it by name at start, because
a prefab cannot hold a reference to a scene object. Every view exposes it as `lang`.

Create it with `AUI/Rebuild Language Table`.

### Language

```csharp
int    GetLanguage();
string GetLanguageCode();
int    LanguageCount();
string LanguageName(int index);      // the native name, from the file's "_name"

void SetLanguage(int index);
void SetLanguageByCode(string code);
void ClearChoice();                  // follow the player's client language again

string Get(int key);                 // by key index; falls back to the default language
int    IndexOfKey(string key);       // slow path, for editor and debug code
```

A player's choice is persisted with VRChat's PlayerData, so it survives rejoining. Until they
choose, the client's own language is followed. Changing it repaints every registered view.

### Style variables, world-wide

```csharp
void   SetStyleVariable(string varName, string value);
string GetStyleVariable(string varName);
int    StyleVariableCount();
string StyleVariableName(int i);
string StyleVariableValue(int i);
```

Unlike the same call on a view, this reaches **every** registered view in the scene, including
views created later. See [theming](/guide/theming#everywhere-at-once).

### Other

```csharp
void Register(AuiView v);       // views register themselves
bool TweensAvailable();         // false under ClientSim, where VRCTween is inert
```

## AuiInputPanel

One per scene, on a GameObject named `AUI Input Panel`. The invisible click targets behind every
`<input>`, because no Udon API opens VRChat's keyboard directly: a `Text Panel` with a
`TMP_InputField`, and a `Url Panel` with VRChat's own `VRCUrlInputField` for `<input type="url">`.
Each is its own canvas and collider; one is enabled at a time.

Create it with `AUI/Rebuild Input Panel`. Re-run it on a scene built before the url panel existed;
the builder moves the old text field under `Text Panel` and adds the url one.

| Field | Default | Meaning |
|---|---|---|
| `distance` | `0.6` | metres along the head ray where the target is parked |
| `clickTimeout` | `3` | seconds the player has to click the second time before it dismisses |

An `<input>` therefore takes **two clicks**: the first arms the panel, the second opens the
keyboard. You do not call this class yourself.

## AuiAttach

One per attached view, on the view's own root, added by the prefab builder when the markup has
[`attach`](/reference/elements#view) (`attach="none"` included) and removed again when the
attribute goes. It moves the view's root with the local player. You do not add it by hand, and
you switch modes through the view: `SetAttach`, `SetAttachCorner`, `SetAttachDistance`,
`SetAttachOffset` ([view API](/reference/view-api#attach)).

The builder writes the mode, the corner, the distance and the wiring on every build. The fields
below are yours: set them on the prefab or on a scene instance, and a rebuild leaves them alone.

| Field | Default | Meaning |
|---|---|---|
| `wristOffset` | `(0, 0.07, 0.03)` | wrist: metres from the hand, in head space. x right, y up, z toward the head |
| `wristRange` | `0.6` | wrist: the hand must be this close to the head (metres) for the view to show |
| `wristShowAngle` | `24` | wrist: degrees between the gaze and the hand at which the view appears |
| `wristHideAngle` | `36` | wrist: degrees at which it disappears again. Larger than the show angle so it does not flicker at the edge |
| `cornerAngles` | `(27, 15)` | corner: degrees from the centre of the player's view, x sideways, y up or down |
| `followDrop` | `0.12` | follow: metres below eye level |
| `followAngle` | `38` | follow: the view recentres once the player has turned this far from it (degrees) |
| `followSlack` | `0.45` | follow: or once they have moved this far from where it was placed for (metres) |
| `followSpeed` | `5` | follow: higher is snappier, `0` jumps |

How it runs:

- It works in `PostLateUpdate`, after VRChat has finished the frame's tracking, and only while
  the view is shown. A hidden view, `none`, and a `front` view after its one placement cost
  nothing.
- A wrist view out of sight is hidden by disabling the root canvas and collider. The view's own
  `Show()` / `Hide()` state and its content are not touched.
- The wrist modes use the hand's position, never its rotation, because that differs between
  controllers and avatar rigs. Outside VR they behave as `corner`.
- The root collider is on only when the view has controls.

## AuiSettings

One per project, at `Assets/AUI/AuiSettings.asset`, found by type. Editor-only: nothing reads it at
runtime.

Create it with `AUI/Create Settings`.

| Field | Default | Meaning |
|---|---|---|
| `langFolder` | `Assets/AUI/lang` | where the language JSON files live |
| `defaultLanguage` | `en` | the language a missing translation falls back to, and the one a missing key is an error in |
| `componentFolders` | empty | extra folders searched for nested views used by tag |
| `iconFolders` | empty | extra icon folders, searched **before** the package's own |
| `extraCharacters` | empty | characters to include in the font bake beyond what the language files use |
| `unicodeRanges` | Latin, Latin Extended, Greek, Cyrillic, punctuation, currency, arrows, CJK punctuation, fullwidth, U+200B | ranges always baked |
| `fontSet` | | the baked `AuiFontSet`, assigned by `AUI/Bake Fonts` |
| `boundTextSample` | `Mmmmmmmmmmm日` | the string used to measure `text="{binding}"` elements that have no `max-width` |

::: tip Keep a CJK character in boundTextSample
It makes bound text boxes as tall as the Noto fallback's line. A box shorter than a line renders
nothing when the text is truncated, which looks like a bug and is hard to trace.
:::

## AuiFontSet

The baked fonts, at `Assets/AUI/Fonts/AuiFontSet.asset`. Produced by `AUI/Bake Fonts`; you do not
edit it by hand.

| Field | Meaning |
|---|---|
| `regular`, `medium`, `semiBold`, `bold` | one static atlas per Inter weight |
| `cjkRegular`, `cjkBold` | Noto JP, KR and SC atlases, in fallback priority order |
| `dynamicLatin`, `dynamicCjk` | dynamic atlases for runtime strings such as player names |
| `bakedCharCount`, `bakedHash` | what the last bake covered; drives the AUI600 staleness warning |

Characters Inter does not have fall back to Noto JP, then KR, then SC.

## AuiSignal

One per interactive element, added by the prefab builder. It relays a uGUI event into the view's
dispatch. You never add or configure one, but you can drive one in a test:

```csharp
signals[S_CLOSE].SendCustomEvent("_Click");
```

## Next

- [Languages](/guide/languages) for the file format and the workflow.
- [Menu and windows](/reference/menu) for the commands that create these.
- [View API](/reference/view-api) for the view side.
