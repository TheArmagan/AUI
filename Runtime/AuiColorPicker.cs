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
    /// delegates and no exposed PointerEventData, so a uGUI press arrives with no coordinates. The
    /// position therefore comes from the one uGUI component that turns the pointer into a number by
    /// itself: a <see cref="Slider"/>. Its OnPointerDown / OnDrag read the event's own position, so
    /// its value is exactly where VRChat's laser (or the desktop crosshair) is, on any controller.
    ///   * The HUE BAR is covered by one invisible vertical slider: its value is the hue.
    ///   * The SQUARE is covered by <see cref="ROWS"/> invisible horizontal sliders stacked bottom
    ///     to top. The pressed row's value is the saturation (continuous); the row the pointer is
    ///     in is the brightness (ROWS steps). Each row's PointerEnter calls its own _R&lt;i&gt; event,
    ///     so the row under the pointer is known during a drag as well.
    ///
    /// WHY NOT THE AIM RAY ANY MORE (2026-09-29). This used to intersect the tracked hand's ray
    /// with the canvas plane. In the real client the VR laser does not leave the hand along
    /// <c>TrackingData.rotation * forward</c>: the angle between them is unexposed and differs per
    /// controller, and at arm's length a few tens of degrees put the computed point a metre from
    /// the laser. The symptom was a picker that only answered when the player's finger touched it,
    /// the one distance at which the direction stops mattering. The desktop head ray IS the
    /// crosshair, so it is kept there for the hover dot and nothing else.
    ///
    /// The press and release still arrive through EventTriggers on the same objects (Down / Up),
    /// listed AFTER the slider so the slider has already moved when <see cref="_Down"/> reads it.
    /// uGUI sends the Up and the drags to the object that took the Down even when the pointer has
    /// left it, so a drag that ends outside the picker still ends.
    ///
    /// IT RUNS PER FRAME ONLY ON DESKTOP, AND ONLY WHILE HOVERED, for the hover dot. Everything
    /// else is event driven. AuiView's contract is that nothing in it runs per frame; this is the
    /// same escape hatch AuiInputPanel and AuiAttach use.
    ///
    /// THE COLOUR IS KEPT AS HSV, and only converted on the way out. A value set from code arrives
    /// as RGB and is converted once; the picker never round-trips its own state through RGB,
    /// because a grey has no hue and the cursor would jump to red on every repaint.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class AuiColorPicker : UdonSharpBehaviour
    {
        /// <summary>
        /// Rows over the square, and so brightness steps. The builder makes exactly this many and
        /// wires row i's PointerEnter to "_R" + i; the methods below must cover 0..ROWS-1.
        /// </summary>
        public const int ROWS = 32;

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
        /// <summary>The invisible pointer sliders over the square, bottom row first. <see cref="ROWS"/> of them.</summary>
        [HideInInspector] public Slider[] rows;
        /// <summary>The invisible vertical pointer slider over the hue bar.</summary>
        [HideInInspector] public Slider hueSlider;
        /// <summary>min-value: the darkest brightness offered. The square's bottom edge is this, not black.</summary>
        [HideInInspector] public float minValue;
        [HideInInspector] public bool interactable = true;

        private const int PART_NONE = 0, PART_SQUARE = 1, PART_HUE = 2;

        // The picked colour.
        private float _h, _s = 1f, _v = 1f;
        private bool _painted;

        // What the pointer is over, from the Enter events: a part, and for the square which row.
        private int _overPart;
        private int _overRow = -1;

        private bool _dragging;
        private int _dragPart;
        /// <summary>The row that took the press: its slider is the one uGUI keeps dragging.</summary>
        private int _dragRow = -1;
        private Color _lastReported;

        // Desktop hover dot: where the head ray lands, in each part's local space.
        private bool _haveHit;
        private int _hitPart;
        private Vector2 _squareLocal, _hueLocal;

        private void Start()
        {
            if (!_painted) Paint();
            HideHover();
            enabled = false;
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
            // The sliders go with it, or a disabled picker would still move them under the laser.
            if (rows != null)
                for (int i = 0; i < rows.Length; i++)
                    if (rows[i] != null) rows[i].interactable = on;
            if (hueSlider != null) hueSlider.interactable = on;
            if (!on && _dragging) { _dragging = false; Report(true); }
            if (!on) HideHover();
        }

        // ================================================================ pointer events (EventTrigger, slider)

        public void _HueEnter() { Over(PART_HUE, -1); }

        public void _Exit()
        {
            // Moving from one row to the next is Exit then Enter in the same frame; Enter restores it.
            _overPart = PART_NONE;
            if (_dragging) return;
            HideHover();
            enabled = false;
        }

        public void _Down()
        {
            if (!interactable || _overPart == PART_NONE) return;
            _dragging = true;
            _dragPart = _overPart;
            _dragRow = _overRow;
            // The slider already took this press (it is listed before the trigger), so its value
            // is where the pointer is, whether or not it changed and raised _Moved.
            ApplyFromSliders();
            Report(false);
        }

        /// <summary>A pointer slider's value changed: the press, or a frame of a drag.</summary>
        public void _Moved()
        {
            if (!_dragging) return;
            ApplyFromSliders();
            if (GetColor() != _lastReported) Report(false);
        }

        public void _Up()
        {
            if (!_dragging) return;
            _dragging = false;
            ApplyFromSliders();
            Report(true);
            if (_overPart == PART_NONE) { HideHover(); enabled = false; }
        }

        private void Over(int part, int row)
        {
            _overPart = part;
            if (row >= 0) _overRow = row;
            // During a drag the brightness follows the row under the pointer.
            if (_dragging && _dragPart == PART_SQUARE && row >= 0) _Moved();
            // The hover dot needs a ray only the desktop crosshair gives exactly.
            VRCPlayerApi local = Networking.LocalPlayer;
            enabled = local != null && local.IsValid() && !local.IsUserInVR();
        }

        // One PointerEnter per row. Udon events carry no argument, so each row names its own.
        public void _R0() { Over(PART_SQUARE, 0); }
        public void _R1() { Over(PART_SQUARE, 1); }
        public void _R2() { Over(PART_SQUARE, 2); }
        public void _R3() { Over(PART_SQUARE, 3); }
        public void _R4() { Over(PART_SQUARE, 4); }
        public void _R5() { Over(PART_SQUARE, 5); }
        public void _R6() { Over(PART_SQUARE, 6); }
        public void _R7() { Over(PART_SQUARE, 7); }
        public void _R8() { Over(PART_SQUARE, 8); }
        public void _R9() { Over(PART_SQUARE, 9); }
        public void _R10() { Over(PART_SQUARE, 10); }
        public void _R11() { Over(PART_SQUARE, 11); }
        public void _R12() { Over(PART_SQUARE, 12); }
        public void _R13() { Over(PART_SQUARE, 13); }
        public void _R14() { Over(PART_SQUARE, 14); }
        public void _R15() { Over(PART_SQUARE, 15); }
        public void _R16() { Over(PART_SQUARE, 16); }
        public void _R17() { Over(PART_SQUARE, 17); }
        public void _R18() { Over(PART_SQUARE, 18); }
        public void _R19() { Over(PART_SQUARE, 19); }
        public void _R20() { Over(PART_SQUARE, 20); }
        public void _R21() { Over(PART_SQUARE, 21); }
        public void _R22() { Over(PART_SQUARE, 22); }
        public void _R23() { Over(PART_SQUARE, 23); }
        public void _R24() { Over(PART_SQUARE, 24); }
        public void _R25() { Over(PART_SQUARE, 25); }
        public void _R26() { Over(PART_SQUARE, 26); }
        public void _R27() { Over(PART_SQUARE, 27); }
        public void _R28() { Over(PART_SQUARE, 28); }
        public void _R29() { Over(PART_SQUARE, 29); }
        public void _R30() { Over(PART_SQUARE, 30); }
        public void _R31() { Over(PART_SQUARE, 31); }

        /// <summary>
        /// The colour from where uGUI put the sliders. Saturation from the slider that took the
        /// press (uGUI keeps dragging that one, and it measures only x); brightness from the row
        /// under the pointer now, or the pressed row if the pointer has left the square.
        /// </summary>
        private void ApplyFromSliders()
        {
            if (_dragPart == PART_HUE)
            {
                if (hueSlider == null) return;
                // Just under 1: hue 1 is red again, and the cursor would jump to the bottom on repaint.
                _h = Mathf.Clamp(hueSlider.value, 0f, 0.9999f);
            }
            else if (_dragPart == PART_SQUARE)
            {
                if (rows == null || _dragRow < 0 || _dragRow >= rows.Length || rows[_dragRow] == null) return;
                _s = Mathf.Clamp01(rows[_dragRow].value);
                int row = _overPart == PART_SQUARE && _overRow >= 0 ? _overRow : _dragRow;
                int n = rows.Length;
                // The bottom row is min-value and the top row full brightness: both ends reachable.
                float f = n > 1 ? (float)row / (n - 1) : 1f;
                _v = minValue + Mathf.Clamp01(f) * (1f - minValue);
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

        // ================================================================ desktop hover dot

        private void Update()
        {
            if (view == null || square == null) { enabled = false; return; }
            SampleHead();
            if (_haveHit && interactable) PaintHover(); else HideHover();
        }

        /// <summary>
        /// Where the desktop crosshair lands: the head ray against the picker's plane, kept only on
        /// one of the two parts. Never used in VR (see the class comment), and never for the colour.
        /// </summary>
        private void SampleHead()
        {
            _haveHit = false;
            _hitPart = PART_NONE;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || !local.IsValid() || local.IsUserInVR()) return;

            VRCPlayerApi.TrackingData t = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 origin = t.position;
            Vector3 dir = t.rotation * Vector3.forward;
            if (dir.sqrMagnitude < 0.0001f) return;

            Vector3 n = square.forward;
            float denom = Vector3.Dot(n, dir);
            // Edge-on: a player looking along the wall hits nothing, which is the right answer.
            if (denom > -0.0001f && denom < 0.0001f) return;
            float dist = Vector3.Dot(n, square.position - origin) / denom;
            // Behind the pointer: the plane is infinite and extends back through the player.
            if (dist <= 0f) return;

            Vector3 hit = origin + dir * dist;
            Vector3 sl = square.InverseTransformPoint(hit);
            Vector3 hl = hueBar != null ? hueBar.InverseTransformPoint(hit) : Vector3.zero;
            Vector2 sq = new Vector2(sl.x, sl.y);
            Vector2 hu = new Vector2(hl.x, hl.y);

            if (Inside(square, sq, 2f)) _hitPart = PART_SQUARE;
            else if (hueBar != null && Inside(hueBar, hu, 6f)) _hitPart = PART_HUE;
            if (_hitPart == PART_NONE) return;

            _haveHit = true;
            _squareLocal = sq;
            _hueLocal = hu;
        }

        /// <summary>Inside the rect, with a margin in mm so a crosshair at the edge still counts.</summary>
        private bool Inside(RectTransform rt, Vector2 p, float margin)
        {
            Rect r = rt.rect;
            return p.x >= r.xMin - margin && p.x <= r.xMax + margin && p.y >= r.yMin - margin && p.y <= r.yMax + margin;
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
