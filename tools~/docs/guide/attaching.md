---
description: Tie a view to the local player with the attach attribute - a wrist panel in VR, a head-locked corner readout, a panel that floats in front and follows, or one placed in front once.
---

# Attaching a view to the player

A view normally sits where you put its prefab in the scene. `attach` on `<view>` ties it to the
local player instead. Everything here is local: each player sees their own copy move with
themselves, and nothing is synced.

```aui
<view name="Hud" width="150" height="80" attach="wrist-left" attach-corner="bottom-left">
  <text text="{credits}"/>
</view>
```

## The modes

| `attach` | What the view does |
|---|---|
| `wrist-left`, `wrist-right` | sits at that hand in VR and shows while the player looks at it. On desktop it sits in `attach-corner` |
| `corner` | head-locked in a corner of the player's view, for everyone |
| `follow` | floats in front at eye level, and recentres only after the player turns or walks away |
| `front` | placed straight ahead once, when the view is shown, then stays in the world |
| `none` | not attached. Keeps the component so code can attach it later |

Two more attributes go with it:

| Attribute | Values | Applies to |
|---|---|---|
| `attach-corner` | `bottom-left` (default), `bottom-right`, `top-left`, `top-right` | `corner`, and a wrist view on desktop |
| `attach-distance` | metres from the head, 0.2 to 3, default 0.6 | `corner`, `follow`, `front` |

## Wrist

```aui
<view name="Hud" width="150" height="80" attach="wrist-left" attach-corner="bottom-left">
```

The view sits a few centimetres above the hand and always faces the player's head. It appears
while the hand is within 0.6 m of the head and the player is looking at it (within 24 degrees of
the gaze), and disappears again past 36 degrees. The gap between the two angles stops it
flickering at the edge. In practice: raise the hand and glance at it, like a watch.

It uses the hand's tracked position and never its rotation. The orientation of hand tracking data
differs between controllers, and avatar bones differ between rigs, so a panel glued to a hand axis
would sit differently for every player.

A player who is not in VR has no wrist, so the same view sits in its `attach-corner`.

Sizes are still millimetres. A wrist view is small, so give it small type: a 150 by 80 view with
the default 18 mm body text holds very little.

## Corner

```aui
<view name="Status" width="150" height="80" attach="corner" attach-corner="top-right" attach-distance="0.6">
```

Head-locked: 27 degrees sideways and 15 degrees up or down from the centre of the player's view,
at `attach-distance`, lying parallel to the view so it reads as a flat rectangle rather than one
turned toward the eye. 27 and 15 degrees is about a third of the way in on a desktop view; raise
`cornerAngles` on the `AuiAttach` component (37 and 19 reach the corner of a 16:9 view for a panel
18 cm wide at 0.55 m) if it covers what the player is looking at. Head-locked panels are tiring in VR over a long session, which
is why the wrist modes only use the corner on desktop.

## Follow

```aui
<view name="Welcome" width="440" height="300" attach="follow" attach-distance="1.2" start-hidden="true">
```

The view floats `attach-distance` ahead, 0.12 m below eye level, and stays level: it follows the
direction the player faces, not where they tilt their head. It holds still until the player has
turned more than 38 degrees away from it or walked more than 0.45 m, then glides to the new spot.
Small head movements therefore never move it, and a button on it stays where the laser is
pointing.

## Front

```aui
<view name="Notice" width="440" height="300" attach="front" attach-distance="0.8" start-hidden="true">
```

`Show()` and `ShowInstant()` place the view straight ahead of the player, facing them, and then it
stays where it was put. The next `Show()` places it again. This is the placement the input panel
uses for the keyboard target.

## From code

```csharp
SetAttach(ATTACH_FOLLOW);              // ATTACH_NONE, ATTACH_WRIST_LEFT, ATTACH_WRIST_RIGHT,
                                       // ATTACH_CORNER, ATTACH_FOLLOW, ATTACH_FRONT
SetAttachCorner(CORNER_TOP_RIGHT);
SetAttachDistance(1.2f);               // clamped to 0.2 .. 3
SetAttachOffset(new Vector3(0f, 0.09f, 0.03f));   // wrist: x right, y up, z toward the head
int mode = GetAttach();
```

These only work on a view whose markup has `attach`. The attribute is what makes the builder add
the [`AuiAttach`](/reference/services#auiattach) component to the prefab, and a component cannot
be added in-world. A view that starts in the world and gets attached later declares that up front:

```aui
<view name="Panel" width="440" height="300" attach="none">
```

Without it, `SetAttach` logs one warning and does nothing.

## Tuning

The numbers on this page are defaults. They are public fields on the view's `AuiAttach` component:
the wrist offset, range and angles, the corner angles, and the follow drop, angle, slack and
speed. Set them in the inspector, on the prefab or on a scene instance. A rebuild rewrites only
the mode, the corner and the distance, which come from the markup, and leaves the rest alone.
The [field table](/reference/services#auiattach) lists them.

## Lasers

An attached view with no controls has its collider turned off, so it never stops a laser aimed
past it. A view with buttons keeps its collider, and on a wrist it is on only while the view is
visible.

AUI cannot choose which hand's laser reaches a view. A wrist view with buttons can therefore be
pressed by the hand it sits on as well as by the other one.

## Cost

An attached view runs a small update every frame while it is shown and moving with the player.
A hidden view, `attach="none"`, and a `front` view after its placement run nothing.

## Nested views

Only an outermost view can be attached. A view used as a tag inside another view has no canvas of
its own to move, so the builder drops its attach and the host reports
[AUI123](/reference/diagnostics). Place the attached view in the scene on its own, next to the
view that would have hosted it, and wire the two through a
[typed reference](/guide/interop).

## Not verified yet

Attach compiles and its markup checks are tested, and that is all so far. It has not been run in
the real client. Treat as unproven: where the wrist view lands and whether the glance thresholds
feel right, lasers on a wrist view with buttons, jitter of corner and wrist views, how follow
feels at its defaults, and the per-frame cost on Quest.

## Next

- [Elements: `<view>`](/reference/elements#view) for the attribute table.
- [View API: attach](/reference/view-api#attach) for the signatures and constants.
- [Services: AuiAttach](/reference/services#auiattach) for the tuning fields.
