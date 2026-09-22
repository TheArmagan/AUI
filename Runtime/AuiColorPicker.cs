using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace Armagan.AUI
{
    /// <summary>
    /// The runtime half of &lt;colorpicker&gt;: a saturation/brightness square beside a hue bar.
    /// Added under the element by the builder; reports to its view through the view's exported
    /// colorResult / colorResultElem / colorResultFinal fields and <c>AuiView._OnColorResult</c>.
    ///
    /// HOW THE POINTER POSITION IS OBTAINED, WHICH IS THE WHOLE PROBLEM. Udon has no runtime
    /// delegates and no exposed PointerEventData, so a uGUI press arrives with no coordinates at
    /// all. The two halves are therefore obtained separately:
    ///   * WHERE the pointer is: computed here, every frame while hovered or dragged, by
    ///     intersecting the player's own aim ray with the canvas plane (both hands in VR, nearest
    ///     hit wins; the head ray on desktop, which is where VRChat's crosshair points).
    ///   * WHETHER they pressed, and when they let go: uGUI's PointerDown / PointerUp, delivered by
    ///     an EventTrigger the builder wires to <see cref="_Down"/> / <see cref="_Up"/>. uGUI sends
    ///     the Up to the object that took the Down even when the pointer has left it, so a drag
    ///     that ends outside the picker still ends.
    /// This is the port of the hand-built flare colour wheel avtr.zip shipped before AUI had one.
    ///
    /// THE HOVER DOT IS NOT DECORATION. The aim ray is the HAND's, and VRChat's laser starts a
    /// little way from the tracked hand by an offset nothing exposes. The dot shows where this ray
    /// lands, so the player aims the dot rather than the laser and the offset stops mattering.
    ///
    /// IT RUNS PER FRAME ONLY WHILE IT HAS TO. AuiView's contract is that nothing in it runs per
    /// frame; this is the same escape hatch AuiInputPanel and AuiAttach use: a sibling behaviour
    /// whose `enabled` is true only between PointerEnter and PointerExit, or during a drag.
    ///
    /// THE COLOUR IS KEPT AS HSV, and only converted on the way out. A value set from code arrives
    /// as RGB and is converted once; the picker never round-trips its own state through RGB,
    /// because a grey has no hue and the cursor would jump to red on every repaint.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class AuiColorPicker : UdonSharpBehaviour
    {
        [Header("Wiring (baked by the AUI builder)")]
        [HideInInspector] public AuiView view;
        /// <summary>The runtime element index of the &lt;colorpicker&gt;, reported as colorResultElem.</summary>
        [HideInInspector] public int elem = -1;
        /// <summary>The saturation/brightness area and the hue bar, both centre-pivoted and coplanar.</summary>
        [HideInInspector] public RectTransform square;
        [HideInInspector] public RectTransform hueBar;
        /// <summary>The square's bottom layer, tinted to the pure hue.</summary>
        [HideInInspector] public Image squareBase;
        [HideInInspector] public RectTransform squareCursor;
        [HideInInspector] public RectTransform squareHover;
        [HideInInspector] public RectTransform hueCursor;
        [HideInInspector] public RectTransform hueHover;
        /// <summary>min-value: the darkest brightness offered. The square's bottom edge is this, not black.</summary>
        [HideInInspector] public float minValue;
        [HideInInspector] public bool interactable = true;

        private const int PART_NONE = 0, PART_SQUARE = 1, PART_HUE = 2;

        // The picked colour.
        private float _h, _s = 1f, _v = 1f;
        private bool _painted;

        // Where this frame's ray landed, in each part's local space.
        private bool _haveHit;
        private int _hitPart;
        private Vector2 _squareLocal, _hueLocal;

        private bool _hovered;
        private bool _dragging;
        private int _dragPart;
        private Color _lastReported;

        private void Start()
        {
            if (!_painted) Paint();
            HideHover();
            if (!_hovered && !_dragging) enabled = false;
        }

        // ================================================================ API

        /// <summary>Show <paramref name="c"/> without reporting it. Called by the view's generated setter.</summary>
        public void SetColor(Color c)
        {
            float h, s, v;
            RgbToHsv(c, out h, out s, out v);
            // A grey carries no hue: keep the one the bar is on, so the cursor does not jump to red.
            if (s > 0.0001f && v > 0.0001f) _h = h;
            _s = s;
            _v = Mathf.Clamp(v, minValue, 1f);
            Paint();
        }

        public Color GetColor() { return HsvToRgb(_h, _s, _v); }

        public void SetInteractable(bool on)
        {
            interactable = on;
            if (!on && _dragging) { _dragging = false; Report(true); }
            if (!on) HideHover();
        }

        // ================================================================ pointer events (EventTrigger)

        public void _Enter()
        {
            _hovered = true;
            enabled = true;
        }

        public void _Exit()
        {
            _hovered = false;
            if (_dragging) return;
            HideHover();
            enabled = false;
        }

        public void _Down()
        {
            if (!interactable) return;
            // The press may land before this behaviour's first Update of the hover (a desktop click
            // on a picker the crosshair just reached), so sample now rather than trust the last frame.
            Sample(false);
            if (!_haveHit || _hitPart == PART_NONE) return;
            _dragging = true;
            _dragPart = _hitPart;
            enabled = true;
            ApplyHit(_dragPart);
            Report(false);
        }

        public void _Up()
        {
            if (!_dragging) return;
            _dragging = false;
            Sample(true);
            if (_haveHit) ApplyHit(_dragPart);
            Report(true);
            if (!_hovered) { HideHover(); enabled = false; }
        }

        // ================================================================ per frame

        private void Update()
        {
            if (view == null || square == null) { enabled = false; return; }
            Sample(_dragging);

            if (_dragging)
            {
                if (_haveHit)
                {
                    ApplyHit(_dragPart);
                    Color c = GetColor();
                    if (c != _lastReported) Report(false);
                }
            }

            if (_haveHit && interactable) PaintHover(); else HideHover();
        }

        /// <summary>
        /// Intersect the aim ray(s) with the picker's plane. With <paramref name="anyPlaneHit"/>
        /// (during a drag) a hit anywhere on the plane counts and is clamped into the dragged part
        /// later; otherwise it must land on one of the two parts.
        /// </summary>
        private void Sample(bool anyPlaneHit)
        {
            _haveHit = false;
            _hitPart = PART_NONE;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || !local.IsValid() || square == null) return;

            float best = float.MaxValue;
            if (local.IsUserInVR())
            {
                TryRay(local, VRCPlayerApi.TrackingDataType.RightHand, anyPlaneHit, ref best);
                TryRay(local, VRCPlayerApi.TrackingDataType.LeftHand, anyPlaneHit, ref best);
            }
            else TryRay(local, VRCPlayerApi.TrackingDataType.Head, anyPlaneHit, ref best);
        }

        private void TryRay(VRCPlayerApi player, VRCPlayerApi.TrackingDataType part, bool anyPlaneHit, ref float best)
        {
            VRCPlayerApi.TrackingData t = player.GetTrackingData(part);
            Vector3 origin = t.position;
            Vector3 dir = t.rotation * Vector3.forward;
            if (dir.sqrMagnitude < 0.0001f) return;

            Vector3 n = square.forward;
            float denom = Vector3.Dot(n, dir);
            // Edge-on: a player looking along the wall hits nothing, which is the right answer.
            if (denom > -0.0001f && denom < 0.0001f) return;
            float dist = Vector3.Dot(n, square.position - origin) / denom;
            // Behind the pointer: the plane is infinite and extends back through the player.
            if (dist <= 0f || dist >= best) return;

            Vector3 hit = origin + dir * dist;
            Vector3 sl = square.InverseTransformPoint(hit);
            Vector3 hl = hueBar != null ? hueBar.InverseTransformPoint(hit) : Vector3.zero;
            Vector2 sq = new Vector2(sl.x, sl.y);
            Vector2 hu = new Vector2(hl.x, hl.y);

            int onPart = PART_NONE;
            if (Inside(square, sq, 2f)) onPart = PART_SQUARE;
            else if (hueBar != null && Inside(hueBar, hu, 6f)) onPart = PART_HUE;
            // A ray crossing the plane metres away is on the plane and not on the picker; adopting
            // it outside a drag would pin the hover dot to an edge while the player looked elsewhere.
            if (onPart == PART_NONE && !anyPlaneHit) return;

            best = dist;
            _haveHit = true;
            _hitPart = onPart;
            _squareLocal = sq;
            _hueLocal = hu;
        }

        /// <summary>Inside the rect, with a margin in mm so a shaky laser at the edge still counts.</summary>
        private bool Inside(RectTransform rt, Vector2 p, float margin)
        {
            Rect r = rt.rect;
            return p.x >= r.xMin - margin && p.x <= r.xMax + margin && p.y >= r.yMin - margin && p.y <= r.yMax + margin;
        }

        private void ApplyHit(int part)
        {
            if (part == PART_SQUARE)
            {
                Rect r = square.rect;
                if (r.width <= 0f || r.height <= 0f) return;
                _s = Mathf.Clamp01((_squareLocal.x - r.xMin) / r.width);
                float f = Mathf.Clamp01((_squareLocal.y - r.yMin) / r.height);
                _v = minValue + f * (1f - minValue);
            }
            else if (part == PART_HUE && hueBar != null)
            {
                Rect r = hueBar.rect;
                if (r.height <= 0f) return;
                // Just under 1: hue 1 is red again, and the cursor would jump to the bottom on repaint.
                _h = Mathf.Clamp((_hueLocal.y - r.yMin) / r.height, 0f, 0.9999f);
            }
            Paint();
        }

        private void Report(bool final)
        {
            if (view == null) return;
            Color c = GetColor();
            _lastReported = c;
            view.colorResult = c;
            view.colorResultElem = elem;
            view.colorResultFinal = final;
            view._OnColorResult();
        }

        // ================================================================ paint

        private void Paint()
        {
            _painted = true;
            if (squareBase != null) squareBase.color = HsvToRgb(_h, 1f, 1f);
            if (square != null && squareCursor != null)
            {
                Rect r = square.rect;
                float f = minValue >= 1f ? 1f : (_v - minValue) / (1f - minValue);
                squareCursor.anchoredPosition = new Vector2(r.xMin + _s * r.width, r.yMin + Mathf.Clamp01(f) * r.height);
            }
            if (hueBar != null && hueCursor != null)
            {
                Rect r = hueBar.rect;
                hueCursor.anchoredPosition = new Vector2(0f, r.yMin + _h * r.height);
            }
        }

        private void PaintHover()
        {
            // During a drag only the dragged part shows the dot; otherwise whichever part is under it.
            int part = _dragging ? _dragPart : _hitPart;
            if (squareHover != null)
            {
                bool on = part == PART_SQUARE;
                squareHover.gameObject.SetActive(on);
                if (on)
                {
                    Rect r = square.rect;
                    squareHover.anchoredPosition = new Vector2(Mathf.Clamp(_squareLocal.x, r.xMin, r.xMax), Mathf.Clamp(_squareLocal.y, r.yMin, r.yMax));
                }
            }
            if (hueHover != null && hueBar != null)
            {
                bool on = part == PART_HUE;
                hueHover.gameObject.SetActive(on);
                if (on)
                {
                    Rect r = hueBar.rect;
                    hueHover.anchoredPosition = new Vector2(0f, Mathf.Clamp(_hueLocal.y, r.yMin, r.yMax));
                }
            }
        }

        private void HideHover()
        {
            if (squareHover != null) squareHover.gameObject.SetActive(false);
            if (hueHover != null) hueHover.gameObject.SetActive(false);
        }

        // ================================================================ colour maths
        //
        // Hand-rolled: Color.HSVToRGB's Udon exposure is not something to find out the hard way (a
        // failed UdonSharp pass keeps the old program running). HsvToRgb is the twin of
        // AuiSpriteGenerator.Hsv, which paints the hue bar this reads back; change both or neither.

        public Color HsvToRgb(float h, float s, float v)
        {
            h = h - Mathf.Floor(h);
            s = Mathf.Clamp01(s);
            v = Mathf.Clamp01(v);

            float c = v * s;
            float hp = h * 6f;
            float xm = c * (1f - Mathf.Abs(hp - Mathf.Floor(hp / 2f) * 2f - 1f));
            float m = v - c;

            float r = 0f, g = 0f, b = 0f;
            if (hp < 1f) { r = c; g = xm; }
            else if (hp < 2f) { r = xm; g = c; }
            else if (hp < 3f) { g = c; b = xm; }
            else if (hp < 4f) { g = xm; b = c; }
            else if (hp < 5f) { r = xm; b = c; }
            else { r = c; b = xm; }

            return new Color(r + m, g + m, b + m, 1f);
        }

        public void RgbToHsv(Color c, out float h, out float s, out float v)
        {
            float r = c.r, g = c.g, b = c.b;
            float max = r > g ? r : g;
            if (b > max) max = b;
            float min = r < g ? r : g;
            if (b < min) min = b;

            v = max;
            float d = max - min;
            s = max <= 0.0001f ? 0f : d / max;
            if (d <= 0.0001f) { h = 0f; return; }

            float hp;
            if (max == r) hp = (g - b) / d;
            else if (max == g) hp = 2f + (b - r) / d;
            else hp = 4f + (r - g) / d;

            hp = hp / 6f;
            if (hp < 0f) hp += 1f;
            h = hp;
        }
    }
}
