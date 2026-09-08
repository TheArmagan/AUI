# Generated code

`Name.g.cs` is written from your markup on every save and must never be edited by hand. This page
says what it contains, so you know what you can call.

```csharp
namespace Armagan.AUI.Generated
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public partial class Shop : AuiView
    {
        // ...
    }
}
```

Your `Name.cs` is the other half of the same partial class, in the same namespace.

## Constants

| Constant | From | Used with |
|---|---|---|
| `E_<ID>` | an element with an `id` | element index: `SetText`, `AddClass`, `SetActiveElem` |
| `S_<ID>` | a clickable with an `id` | signal id: `SetEnabled`, `IsSelected` |
| `C_<CLASS>` | every class any of the view's sheets defines | `AddClass`, `RemoveClass`, `HasClass` |
| `<REPEAT>_MAX` | a `<repeat max="...">` | clamping a count |
| `<GROUP>_COUNT`, `<GROUP>_<PAGEID>` | a `<pages>` group | `SelectMainPage(MAIN_AUDIO)` |
| `T_<ID>` | a `<template>` | `CreateElement`, `InstanceRow` |
| `M_<ID>` | a `<modal>` | `IsModalOpen`, `SetModal` |

## Binding properties

One property per binding, with a backing field, a setter that repaints everything using it, and
forwarding to nested views.

```csharp
public string PlayerName { get; set; }   // escaped, then re-renders each element using it
public bool   Busy       { get; set; }   // SetEnabledElem(...)
public int    Page       { get; set; }   // SelectPage(0, value, false)
public float  Volume     { get; set; }   // moves the slider without firing the handler
public bool   Muted      { get; set; }   // SetToggle(...)
public string Nick       { get; set; }   // re-renders the input
public bool   IsVip      { get; set; }   // SetActiveElem(...)
public int    Quality    { get; set; }   // SetSelect(0, value)
```

Rules: string bindings are always escaped; every setter starts the view first if needed, so writing
before `Start` is safe; setting from code never calls your handler.

The type comes from where the binding is used. See [bindings](/guide/bindings#the-type-comes-from-where-it-is-used).

## Repeats

For `<repeat id="board" max="5">` with `{item.name}` and `{item.score}`:

```csharp
public int  BoardCount { get; set; }
public void SetBoardName(int row, string value);
public void SetBoardScore(int row, string value);
public const int BOARD_MAX = 5;
```

Row values that feed a `{placeholder}` are stored per row, so a language change can re-render them.

## Templates

For `<template id="line" max="32">`:

```csharp
public const int T_LINE = 0;
public int  CreateLine(int parent);          // -1 when the pool is empty
public void SetLineMsg(int elem, string value);   // keyed on the created element, not a row
```

A template holding a nested view returns the view instead:

```csharp
public AuiView CreateCardsPlayerCard(int parent);
```

## Pages

For `<pages id="main">` with pages `general`, `audio`, `lang`:

```csharp
public const int MAIN_COUNT   = 3;
public const int MAIN_GENERAL = 0;
public const int MAIN_AUDIO   = 1;
public const int MAIN_LANG    = 2;

public int  GetMainPage();
public void SelectMainPage(int page);
```

Plus an override that updates the bound property and calls your `onchange` handler when a **player**
changes page.

## Selects

For `<select bind="{quality}">`:

```csharp
public void SetQualityOptionLabel(int index, string label);
public void SetQualityOptionCount(int count);
```

Plus the dispatch that calls `QualityChanged(int index)`.

## Modals

For `<modal id="confirm">`:

```csharp
public const int M_CONFIRM = 0;
public void OpenConfirm();
public void CloseConfirm();
public bool IsConfirmOpen();
```

Plus dispatch to your `onopen` / `onclose` handlers.

## Nested view fields

```csharp
[HideInInspector] public AuiLanguagePicker childAuiLanguagePicker;
[HideInInspector] public PlayerCard        childPlayerCardTop;   // when the usage has id="top"
```

Assigned by the prefab builder. A nested view **inside a `<template>`** gets no field, because
there is one copy per parked instance; `CreateView` returns it instead.

## Machinery you do not call

The rest of the file is dispatch, and you only see it if you open it:

| Member | Purpose |
|---|---|
| `Render_<n>()` | pushes current values into one element (bound text, placeholders, a slider's label) |
| `_Retranslate()` | calls every render method after a language change |
| `_AfterStart()` | starts child views, applies initial binding values, reads a slider's authored value back |
| `_OnInputResult()` | routes a keyboard result to the right binding and handler |
| `_Dispatch(id, kind)` | maps a signal to your handler, with repeat-row arithmetic inlined |
| `_OnChildEvent(child, name)` | routes a nested view's event to your `on-<event>` handler |

A per-view custom editor is also emitted, which is why a view's inspector shows only your own
fields.

## What you must supply

Exactly the handlers named in `on*` attributes, with the
[signature their position implies](/guide/handlers#every-signature). They are scaffolded as empty
stubs the first time.

## Two name collisions to avoid

- A binding whose PascalCase name matches a member of `AuiView` is an error (AUI116); rename it.
- Do not declare a field called `_<binding>`; the generated backing field owns that name.

## Next

- [View API](/reference/view-api) for what the base class adds.
- [The logic half](/guide/the-logic-half) for how to write against this.
