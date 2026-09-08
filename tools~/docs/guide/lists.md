# Lists

A `<repeat>` bakes a fixed number of copies of its single child. At runtime you set how many are
visible and fill them in. Nothing is instantiated in-world.

```aui
<repeat id="board" max="5" class="card" style="gap: 4">
  <panel class="row">
    <text text="{item.rank}" style="min-width: 30"/>
    <text text="{item.name}"/>
    <spacer/>
    <text text="{item.score}" class="accent"/>
  </panel>
  <empty><text key="board.empty" class="hint"/></empty>
</repeat>
```

```csharp
BoardCount = 3;
SetBoardRank(0, "1");  SetBoardName(0, "Alice"); SetBoardScore(0, "120");
SetBoardRank(1, "2");  SetBoardName(1, "Bob");   SetBoardScore(1, "95");
SetBoardRank(2, "3");  SetBoardName(2, "Cara");  SetBoardScore(2, "80");
```

## The rules

- **Exactly one template child**, plus an optional `<empty>` (AUI105 otherwise).
- `max` is between 1 and 64 (AUI120). It is baked; the prefab always contains that many rows.
- `id` is required, and drives the generated names: `id="board"` gives `BoardCount` and
  `SetBoardX(int row, ...)`.
- `{item.x}` bindings become per-row setters. Using `{item.x}` outside a repeat or template is
  AUI118.
- `<empty>` is shown when the count is 0.

Choose `max` as the largest list you will realistically show. Every row exists in the prefab, so
`max="64"` on a row with ten elements is 640 baked GameObjects.

## Setting the count

```csharp
BoardCount = 3;              // the generated property: use this
SetRepeatCount(0, 3);        // or by repeat index, from code that is not view-specific
int n = GetRepeatCount(0);
```

`BOARD_MAX` is generated too, so you can clamp against the baked maximum:

```csharp
BoardCount = Mathf.Min(_players.Length, BOARD_MAX);
```

The count is clamped to `max`, the extra rows are hidden, `<empty>` is toggled, and the layout
re-solves.

## Rows that react

A row can be a `button`, so the whole row is clickable, and `onselect` on the repeat gives you the
row index:

```aui
<repeat id="board" max="5" onselect="Pick">
  <button class="line" selected="{item.picked}">
    <text text="{item.name}"/>
    <spacer/>
    <toggle bind="{item.fav}" icon="heart" onchange="FavChanged"/>
  </button>
</repeat>
```

```csharp
public void Pick(int row) { ... }
public void FavChanged(int row, bool value) { ... }
```

**Inside a repeat, the row index comes first** in every handler signature. See
[handlers](/guide/handlers).

`selected="{item.picked}"` is a per-row bool binding driving the `:selected` state, so highlighting
the chosen row is a style rule rather than code:

```auis
.line:selected { background: var(--panel); }
```

```csharp
public void Pick(int row)
{
    for (int i = 0; i < BoardCount; i++) SetBoardPicked(i, i == row);
}
```

## Filling a list efficiently

Every setter marks the layout dirty. Batch a rebuild into one solve:

```csharp
private void Rebuild()
{
    SetAutoRelayout(false);

    BoardCount = _n;
    for (int i = 0; i < _n; i++)
    {
        SetBoardRank(i, (i + 1).ToString());
        SetBoardName(i, _names[i]);
        SetBoardScore(i, _scores[i].ToString());
    }

    SetAutoRelayout(true);      // one solve
}
```

## Text width in rows

Rows look ragged unless the columns are pinned, because each cell is content-sized by default:

```aui
<text text="{item.name}"  style="min-width: 130; max-width: 130; text-align: left"/>
<text text="{item.score}" style="min-width: 70; text-align: right"/>
```

This is the single most useful trick for making a leaderboard look like a table.

## A grid of chips

A repeat with the theme's `.grid` class wraps into lines:

```aui
<repeat id="tags" max="8" onselect="PickTag" class="grid">
  <button class="chip" selected="{item.on}">
    <icon name="tag" size="14"/>
    <text text="{item.label}" style="max-width: 80"/>
  </button>
</repeat>
```

## When a fixed maximum is not enough

If you genuinely do not know the upper bound, or the rows come and go, use a `<template>` instead:
it bakes parked copies you link into the tree and remove again.

```aui
<template id="line" max="20">
  <panel class="row"><text text="{item.msg}"/></panel>
</template>
```

```csharp
int e = CreateLine(E_LOG);     // -1 when the pool is empty
SetLineMsg(e, "hello");
DestroyElement(e);             // parks it for reuse
```

The difference: a repeat is a list with a count, a template is a pool you draw from. See
[changing a view at runtime](/guide/runtime#creating-elements).

## Next

- [Handlers](/guide/handlers) for row handler signatures.
- [Changing a view at runtime](/guide/runtime) for templates, pools and slots.
- [The gallery](/examples/gallery-pages#lists) for the worked example this page is drawn from.
