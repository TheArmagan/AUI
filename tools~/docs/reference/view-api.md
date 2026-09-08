---
description: The AuiView surface your code calls - lifecycle hooks, elements, text, classes, style values, layout, pages, repeats, selects, modals and runtime creation.
---

# View API

What `AuiView`, the base class of every view, offers your code. Most of it takes an **element
index** (`int elem`) or a **signal id** (`int id`); the generated half gives you named constants
(`E_TITLE`, `S_CLOSE`, `C_PRIMARY`, `BOARD_MAX`, `T_LINE`, `M_CONFIRM`) so you rarely type a raw
number.

For the properties and setters generated from **your** markup, see
[generated code](/reference/generated-code).

## Lifecycle

```csharp
protected virtual void OnStart()        // once, after wiring and initial binding values
protected virtual void OnRetranslate()  // after a language change, once keyed text has repainted
protected virtual void OnShown()        // after Show()
protected virtual void OnHidden()       // after Hide()
```

Override these instead of using `Start`. `OnStart` runs after the view has built its tables and
applied every binding's initial value.

Every public entry point starts the view first, so another script may write to a view that has not
started yet, including one on a hidden page.

## Visibility

```csharp
void Show();          void Hide();          void Toggle();
void ShowInstant();   void HideInstant();
bool IsVisible();
```

The transition comes from the `show` style property or the `show` attribute on `<view>`.
`Hide()` leaves an open modal open behind the scenes.

## Finding elements

```csharp
int   GetElement(string selector);        // "#id", ".class", "tag"; -1 when not found
int[] GetElements(string selector);
int[] GetElementsByTag(string tag);
int   ElementCount();
int   TagId(string tag);
```

Star classes are style-only, so `GetElements(".*p-8")` returns nothing.

## Text, icons and images

```csharp
void   SetText(int elem, string value);   // escaped for you
string GetText(int elem);
void   SetIcon(int elem, string iconName);
void   SetBackground(int elem, string spriteName);
void   SetMaterial(int elem, string materialName);   // null clears it
Sprite IconSprite(string iconName);
int    RefId(string refName);
```

An icon, sprite or material must be baked into the view: used in its markup or sheets, or
preloaded on the root (`<view icons="..." sprites="..." materials="...">`).

## Showing and hiding elements

```csharp
void SetActiveElem(int elem, bool on);      // hides and frees the space
void SetDisplay(int elem, int display);     // DISPLAY_FLEX | DISPLAY_NONE
bool IsElementShown(int elem);
```

## Classes

```csharp
int  ClassId(string name);
bool HasClass(int elem, int classId);
void AddClass(int elem, int classId);
void RemoveClass(int elem, int classId);
void ToggleClass(int elem, int classId);

bool HasClassNamed(int elem, string name);
void AddClassNamed(int elem, string name);
void RemoveClassNamed(int elem, string name);
void ToggleClassNamed(int elem, string name);

void SetClasses(int elem, string names);           // replaces the API-added set
void SetClassSlot(int elem, int slot, string names);
```

A class change re-resolves the element and its descendants through the cascade, applying each
changed property through that element's own `transition`.

## Style values

```csharp
void SetStyleValue(int elem, int prop, Vector4 value);   // any property by id, e.g. PROP_GAP
void SetOpacity(int elem, float alpha);
void SetColor(int elem, Color c);
void SetBackgroundColor(int elem, Color c);
void SetIconColor(int elem, Color c);
void SetScale(int elem, float scale);
void SetFontSize(int elem, float size);
void SetTextAlign(int elem, int align);   // 0 left, 1 center, 2 right
```

A value set this way holds until the next class change on that element re-resolves it.

## Style variables

```csharp
int    StyleVariableId(string varName);        // "accent" or "--accent"; -1 if unknown
string GetStyleVariable(string varName);
void   SetStyleVariable(string varName, string value);
void   SetStyleVariableColor(string varName, Color c);
void   SetStyleVariableFloat(string varName, float value);
void   FlushStyleVariables();
```

Applies to this view and the views nested inside it. Writes coalesce to one re-encode pass per
frame. For the whole world, go through the language service instead. See
[theming](/guide/theming).

## Layout

```csharp
void MarkLayoutDirty();
void Relayout();        void RelayoutNow();
void SetAutoRelayout(bool on);   bool IsAutoRelayout();

void SetPosition(int elem, float left, float top, float right, float bottom);  // NaN = unset
void SetSize(int elem, float width, float height);

float LayoutX(int elem);   float LayoutY(int elem);
float LayoutW(int elem);   float LayoutH(int elem);
bool  LayoutPlaced(int elem);
float GetLayoutIntrinsicW();   float GetLayoutIntrinsicH();

public bool debugLayout;    // logs every solve and its duration
```

Solves are coalesced to one per frame. Wrap a burst of changes in
`SetAutoRelayout(false)` / `SetAutoRelayout(true)`.

## Controls

```csharp
bool GetToggle(int elem);
void SetToggle(int elem, bool on);
void SetSelected(int id, bool on);      // by signal id
bool IsSelected(int id);
void SetEnabled(int id, bool on);       // by signal id
void SetEnabledElem(int elem, bool on);
```

Prefer the generated binding properties; these are for code that is not written against one
specific view.

## Pages

```csharp
int  GetPage(int group);
void SelectPage(int group, int page, bool notify);
```

Generated per group: `GetMainPage()`, `SelectMainPage(int)`, and `MAIN_COUNT` / `MAIN_<PAGEID>`.

## Repeats

```csharp
int  GetRepeatCount(int repeat);
void SetRepeatCount(int repeat, int count);   // clamped to max; toggles <empty>
int  RowElem(int repeat, int row, int offset);
```

Generated per repeat: `BoardCount`, `SetBoardX(int row, T value)`, `BOARD_MAX`.

## Selects

```csharp
int  GetSelect(int select);
void SetSelect(int select, int index);        // no handler fired
void SetSelectOptionLabel(int select, int index, string label);
void SetSelectOptionCount(int select, int count);
void CloseSelect();
```

Generated: `SetQualityOptionLabel(int, string)`, `SetQualityOptionCount(int)`.

## Modals

```csharp
int  ModalCount();
bool IsModalOpen(int modal);
int  TopModal();                                  // topmost open, or -1
void SetModal(int modal, bool open, bool notify);
bool Back();      // closes an open select, else the top dismissable modal; true if it consumed
```

Generated per modal: `OpenConfirm()`, `CloseConfirm()`, `IsConfirmOpen()`, `M_CONFIRM`.

`Back()` is what you wire a world's "back" affordance to.

## Creating elements

```csharp
int  CreateElement(int template, int parent, int index);   // -1 index appends; -1 return = pool empty
int  CreateElementByTag(string tag, int parent, string classes, int index);
int  PoolTemplate(string tag);
void DestroyElement(int elem);        // parks it for reuse
void MoveElement(int elem, int newParent, int index);

AuiView CreateView(int template, int parent, int index);
AuiView CreateViewNamed(string slot, string viewName, int parent);
void    DestroyView(AuiView child);

int  InstanceRow(int template, int elem);
int  InstanceElem(int template, int row);
bool IsInstanceLive(int template, int row);
int  TemplateOf(int elem);
int  InstanceRowOfView(int template, AuiView child);
int  InstanceElemOfView(int template, AuiView child);
```

Generated per template: `CreateLine(int parent)` and `SetLineX(int elem, T value)`, plus `T_LINE`.

Always check for `-1`. See [creating elements](/guide/runtime#creating-elements).

## Tree

```csharp
int ElementParent(int elem);
int ChildCount(int parent);
int ChildAt(int parent, int index);

RectTransform GetRect(int elem);     // drive uGUI yourself
GameObject    GetObject(int elem);
Image         GetBackgroundImage(int elem);
```

## Nested views

```csharp
protected void Emit(string eventName);
protected void EmitString(string eventName, string value);
protected void EmitInt(string eventName, int value);
protected void EmitFloat(string eventName, float value);
protected void EmitBool(string eventName, bool value);

public virtual void _OnChildEvent(AuiView child, string eventName);
```

The host gets a typed `child<ViewName>` field per nested view. See
[components](/guide/components).

## Text helpers

```csharp
string Escape(string s);              // makes untrusted text safe for TMP rich text
string FormatNumber(float v, int decimals);
string Fill1(string t, string n0, string v0);   // Fill1..Fill4 fill {placeholder}s
```

`Escape` is applied automatically to every string binding and to `SetText`, so you rarely call it.

## Constants

Available on every view:

| Group | Names |
|---|---|
| Signals | `SIG_CLICK`, `SIG_ENTER`, `SIG_EXIT`, `SIG_DOWN`, `SIG_UP`, `SIG_VALUE` |
| States | `STATE_NORMAL`, `STATE_HOVER`, `STATE_PRESSED`, `STATE_SELECTED`, `STATE_DISABLED` |
| Kinds | `KIND_BUTTON`, `KIND_TOGGLE`, `KIND_TAB`, `KIND_OPTION`, `KIND_INPUT`, `KIND_SELECT`, `KIND_SLIDER`, `KIND_MODAL_SCRIM` |
| Transitions | `TRANS_NONE`, `TRANS_FADE`, `TRANS_FADE_SCALE`, `TRANS_FADE_SLIDE`, `TRANS_SLIDE_*`, `TRANS_SCALE` |
| Pages | `PAGE_CROSSFADE`, `PAGE_SEQUENTIAL` |
| Display | `DISPLAY_FLEX`, `DISPLAY_NONE` |
| Properties | `PROP_BG`, `PROP_COLOR`, `PROP_ICON`, `PROP_OPACITY`, `PROP_SCALE`, `PROP_GAP`, ... |

## Fields you may read

`lang` (the `AuiLanguageService`), `inputPanel`, `content`, `contentRect`. Everything else on the
base class is builder-owned wiring.

## Next

- [Generated code](/reference/generated-code) for what your markup adds to this.
- [Services and settings](/reference/services) for `AuiLanguageService` and friends.
- [Changing a view at runtime](/guide/runtime) for how these fit together.
