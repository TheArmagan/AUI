# Recipes

Short, self-contained pieces to copy.

## A badge on a corner

```aui
<panel id="inbox" class="card">
  <text key="inbox.title"/>
  <panel class="badge" style="position: absolute; top: -6; right: -6">
    <text text="{unread}"/>
  </panel>
</panel>
```

```auis
.badge {
  background: sprite(Rounded24) var(--accent);
  color: var(--base);
  padding: 1 9;
  min-height: 20;
  align: center;
  justify: center;
  font-size: 13;
}
```

```csharp
Unread = count > 0 ? count.ToString() : "";
SetActiveElem(GetElement("#inbox"), count > 0);
```

A text draws no background of its own, so a badge is a panel wrapping a text.

## A progress bar

```aui
<panel class="bar"><panel class="fill" style="width: {progress}"/></panel>
```

```auis
.bar  { background: sprite(Rounded4) var(--chip);   height: 8; }
.fill { background: sprite(Rounded4) var(--accent); height: 8;
        transition: background 0.3s out-quad; }
```

```csharp
Progress = Mathf.RoundToInt(100f * value) + "%";   // value is 0..1
```

A bound style value is a string for a length. The transition means a colour change animates, and
the width follows the layout solve.

## A confirm dialog

```aui
<button onclick="AskDelete" key="ui.delete" icon="trash-2" class="danger"/>

<modal id="confirm" enabled="{asking}" onclose="Cancelled" show="fade-scale">
  <panel class="row">
    <icon name="triangle-alert" class="warn" size="22"/>
    <text key="ui.sure" class="section"/>
  </panel>
  <text key="ui.sureHint" class="hint" wrap="true" style="max-width: 260"/>
  <panel class="row">
    <spacer/>
    <button onclick="CloseConfirm" key="ui.no" class="ghost"/>
    <button onclick="DoDelete" key="ui.yes" class="danger"/>
  </panel>
</modal>
```

```csharp
public void AskDelete() { Asking = true; }
public void DoDelete()  { /* ... */ CloseConfirm(); }
public void Cancelled() { }
```

## A leaderboard

```aui
<repeat id="board" max="10" onselect="Pick" class="card" style="gap: 4">
  <button class="line" selected="{item.picked}">
    <panel class="badge"><text text="{item.rank}"/></panel>
    <text text="{item.name}"  style="min-width: 130; max-width: 130; text-align: left"/>
    <spacer/>
    <text text="{item.score}" style="min-width: 70; text-align: right" class="accent"/>
  </button>
  <empty><text key="board.empty" class="hint"/></empty>
</repeat>
```

```csharp
private void Refresh(string[] names, int[] scores, int n)
{
    SetAutoRelayout(false);
    BoardCount = Mathf.Min(n, BOARD_MAX);
    for (int i = 0; i < BoardCount; i++)
    {
        SetBoardRank(i, (i + 1).ToString());
        SetBoardName(i, names[i]);
        SetBoardScore(i, scores[i].ToString());
    }
    SetAutoRelayout(true);
}
```

The pinned column widths are what make the rows align.

## A chat log from a pool

```aui
<view name="Chat" width="400" height="300">
  <panel id="log" class="card" style="gap: 4; min-height: 200"/>
  <template id="line" max="16">
    <panel class="row" style="gap: 8">
      <text text="{item.who}" class="accent" style="min-width: 56"/>
      <text text="{item.msg}" style="max-width: 260"/>
    </panel>
  </template>
</view>
```

```csharp
public void Add(string who, string msg)
{
    int e = CreateLine(E_LOG);
    if (e < 0) return;              // pool exhausted; drop or recycle the oldest
    SetLineWho(e, who);
    SetLineMsg(e, msg);
    MoveElement(e, E_LOG, 0);       // newest first
}
```

## A tab bar as a sidebar

```aui
<panel class="row" style="align: stretch; grow: 1">
  <tabs bind="{page}" class="side" onchange="PageChanged">
    <tab page="home" key="nav.home" icon="house"/>
    <tab page="shop" key="nav.shop" icon="tag"/>
  </tabs>
  <pages id="main" mode="crossfade">
    <page id="home"> ... </page>
    <page id="shop"> ... </page>
  </pages>
</panel>
```

```auis
.side { flex-direction: column; gap: 4; width: 150; align: stretch; }
tab   { justify: start; padding: 8 12; }
pages { grow: 1; }
```

## A toggle that looks like a heart

```aui
<toggle bind="{fav}" icon="heart" onchange="FavChanged" class="fav"/>
```

```auis
.fav           { min-width: 28; min-height: 28; padding: 5;
                 background: sprite(Rounded8) transparent; }
.fav:selected  { background: sprite(Rounded8) transparent; icon-color: var(--bad); }
```

A toggle's icon is its "on" mark and is hidden while off, so the `:selected` rule is all the
styling it needs.

## Showing a panel from a world interaction

```csharp
public class PanelOpener : UdonSharpBehaviour
{
    public AuiView panel;

    public override void Interact()
    {
        if (panel == null) return;
        if (panel.IsVisible()) panel.Hide(); else panel.Show();
    }
}
```

Taking `AuiView` rather than the concrete class means this script works with any panel.

## A "back" button for the whole view

```csharp
public void Back_()
{
    if (!Back()) Hide();     // Back() closes an open select or the top modal
}
```

## Re-theming from a preset

```csharp
public void PresetOcean()
{
    lang.SetStyleVariable("base",   "#0C1418");
    lang.SetStyleVariable("card",   "#152836");
    lang.SetStyleVariable("chip",   "#24455A");
    lang.SetStyleVariable("accent", "#5AC8E0");
    lang.SetStyleVariable("text",   "#DCEAF0");
    lang.SetStyleVariable("muted",  "#7FA0AE");
}
```

Through `lang`, every view in the scene follows. Through the view's own `SetStyleVariable`, only
that view and its children do.

## Next

- [The gallery](/examples/gallery-pages) for larger worked examples.
- [Changing a view at runtime](/guide/runtime) for the API these use.
