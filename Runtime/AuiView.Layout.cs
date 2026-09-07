using UnityEngine;
using UnityEngine.UI;

namespace Armagan.AUI
{
    /// <summary>
    /// The runtime half of the layout engine: a port of Core's AuiLayoutSolver that runs in
    /// Udon over the flat lay* tables the builder bakes (see Core/Resolve/AuiLayoutTables.cs,
    /// whose constants this file mirrors). It re-solves the flex layout in-world whenever a
    /// text, a repeat count, a visibility, a language, a class or a style value changes.
    ///
    /// NO RECURSION, NO ALLOCATION PER SOLVE. Element indices are pre-order (parent &lt; child),
    /// so measure is a reverse loop and place a forward loop over plain arrays allocated once
    /// in _AuiLayoutInit. Scratch arrays are fields.
    ///
    /// TEXT IS MEASURED BY uGUI, NOT BY TMP DIRECTLY. TMP's GetPreferredValues is not exposed
    /// to Udon; LayoutUtility.GetPreferredWidth/Height(RectTransform) are, and TextMeshProUGUI
    /// implements ILayoutElement, so that is the measurement path for a text a setter changed.
    /// Every other size comes from the baked tables (per language for keyed text).
    ///
    /// ONE SOLVE PER FRAME. Setters call MarkLayoutDirty; the solve runs one frame later via
    /// SendCustomEventDelayedFrames, or at once from RelayoutNow. SetAutoRelayout(false)
    /// parks the dirty flag until Relayout() or SetAutoRelayout(true).
    /// </summary>
    public partial class AuiView
    {
        // ---- mirrors of AuiLayoutTables
        public const int LK_CONTAINER = 0, LK_TEXT = 1, LK_LEAF = 2, LK_REPEAT = 3, LK_PAGES = 4, LK_SELECT = 5, LK_NESTED = 6, LK_IGNORE = 7, LK_EMPTY = 8;
        public const int LF_ROW = 1, LF_WRAP = 2, LF_ABSOLUTE = 4, LF_DISPLAY_NONE = 8, LF_HIDDEN_START = 16, LF_RUNTIME_TEXT = 32, LF_SHRINKABLE = 64,
            LF_TEXT_WRAP = 128, LF_SPACER = 256, LF_CONTENT_TEXT = 512, LF_CONTENT_ICON = 1024, LF_CONTENT_ICON_ONLY = 2048;
        public const int ANCHOR_NONE = 0, ANCHOR_FILL = 1, ANCHOR_TOP = 2, ANCHOR_BOTTOM = 3, ANCHOR_LEFT = 4, ANCHOR_RIGHT = 5, ANCHOR_CENTER = 6;
        public const int JUSTIFY_START = 0, JUSTIFY_CENTER = 1, JUSTIFY_END = 2, JUSTIFY_BETWEEN = 3;
        public const int ALIGN_START = 0, ALIGN_CENTER = 1, ALIGN_END = 2, ALIGN_STRETCH = 3;
        public const int SIZE_UNSET = 0, SIZE_MM = 1, SIZE_PCT = 2, SIZE_AUTO = 3;
        public const int SZ_W = 0, SZ_H = 1, SZ_MINW = 2, SZ_MINH = 3, SZ_MAXW = 4, SZ_MAXH = 5;
        public const int EDGE_TOP = 0, EDGE_RIGHT = 1, EDGE_BOTTOM = 2, EDGE_LEFT = 3;
        public const int DISPLAY_FLEX = 0, DISPLAY_NONE = 1;
        private const float IconMm = 18f;
        private const float MinShrunk = 24f;

        [Header("Layout tables (baked by the AUI builder; see AuiLayoutTables)")]
        [HideInInspector] public bool layoutEnabled;
        [HideInInspector] public int layLangCount = 1;
        [HideInInspector] public float layRootW, layRootH;
        [HideInInspector] public int[] layParent, layFirstChild, layNextSibling, layKind, layFlags, layJustify, layAlign, layAnchor, layRepeat, laySizeKind, layEdgeKind;
        [HideInInspector] public float[] layGrow, layGap, layPad, layMargin, laySizeVal, layEdgeVal, layIntrinsicW, layIntrinsicH, layLangW, layLangH, layContentW, layContentH;
        [HideInInspector] public AuiView[] elemChildView;
        [Tooltip("Log the time of every layout solve.")]
        public bool debugLayout;

        // ---- runtime state (allocated once)
        private bool _layoutReady;
        private bool _layoutDirty;
        private bool _autoRelayout = true;
        private int _layoutPasses;
        private int _layLang;
        private float _rootW, _rootH;
        private bool[] _shown;         // display / hidden / if gate; pages and repeat rows are handled by their own tables
        private bool[] _textDirty;
        private bool[] _placed;
        private float[] _rawW, _rawH;  // current raw sizes (baked per language, or measured)
        private float[] _lw, _lh;      // measured intrinsic after override and clamp
        private float[] _availW, _inner;
        private float[] _rx, _ry, _rw, _rh;   // local rect, relative to the parent's top-left, y down
        private float[] _ax, _ay;             // absolute, relative to the view
        private float[] _fm, _fc;             // scratch: main / cross per flow child
        private int[] _flow;                  // scratch: flow child indices
        private int _flowCount;
        private int _solveCount;

        // ================================================================ init

        /// <summary>Called from _AuiStart after the element tables were validated.</summary>
        private void _AuiLayoutInit()
        {
            _layoutReady = false;
            if (!layoutEnabled || layParent == null || elemObjects == null) return;
            int n = elemObjects.Length;
            if (layParent.Length != n || layKind.Length != n || layFlags.Length != n || laySizeKind.Length != 6 * n || layEdgeKind.Length != 4 * n ||
                layPad.Length != 4 * n || layMargin.Length != 4 * n || layIntrinsicW.Length != n || layLangW.Length != n * layLangCount)
            {
                Debug.LogWarning("[AUI] " + name + ": layout tables do not match the element count; runtime layout is off. Rebuild the view.");
                return;
            }
            _shown = new bool[n]; _textDirty = new bool[n]; _placed = new bool[n];
            _rawW = new float[n]; _rawH = new float[n]; _lw = new float[n]; _lh = new float[n];
            _availW = new float[n]; _inner = new float[n];
            _rx = new float[n]; _ry = new float[n]; _rw = new float[n]; _rh = new float[n];
            _ax = new float[n]; _ay = new float[n];
            _fm = new float[n]; _fc = new float[n]; _flow = new int[n];
            _rootW = layRootW; _rootH = layRootH;
            for (int e = 0; e < n; e++) _shown[e] = (layFlags[e] & LF_HIDDEN_START) == 0;
            _layLang = 0;
            LoadLanguageSizes();
            _layoutReady = true;
            if (debugLayout)
            {
                int containers = 0, texts = 0;
                for (int e = 0; e < n; e++) { if (layKind[e] == LK_CONTAINER) containers++; if ((layFlags[e] & LF_RUNTIME_TEXT) != 0) texts++; }
                Debug.Log("[AUI] layout: " + name + " " + n + " elements, " + containers + " containers, " + texts + " runtime texts, " + layLangCount + " languages");
            }
        }

        /// <summary>Copy the current language's baked sizes into the working raw arrays (texts a setter changed keep their measured size).</summary>
        private void LoadLanguageSizes()
        {
            int n = _rawW.Length;
            int L = layLangCount > 0 ? layLangCount : 1;
            int l = _layLang < 0 ? 0 : _layLang >= L ? L - 1 : _layLang;
            for (int e = 0; e < n; e++)
            {
                if (layKind[e] == LK_TEXT && (layFlags[e] & LF_RUNTIME_TEXT) != 0 && !_textDirty[e] && _solveCount > 0) continue;
                _rawW[e] = layLangW[e * L + l];
                _rawH[e] = layLangH[e * L + l];
            }
        }

        // ================================================================ public API

        /// <summary>Schedule a solve for the next frame (coalesced). Every setter that can move a rect calls this.</summary>
        public void MarkLayoutDirty()
        {
            if (!_layoutReady || _layoutDirty) return;
            _layoutDirty = true;
            if (_autoRelayout && _started) SendCustomEventDelayedFrames(nameof(_AuiSolveLayout), 1);
        }

        /// <summary>
        /// A nested view that started while its page was hidden could not measure its bound texts
        /// (TMP measures nothing on an inactive object), so it solved with the baked sample sizes and
        /// kept the dirty bits. The host calls this when the page enters or the view shows: one solve
        /// on the next frame, whatever the flag says, so those texts are measured now that they are active.
        /// </summary>
        public void _AuiWake()
        {
            if (!_started || !_layoutReady) return;
            _layoutDirty = true;
            if (_autoRelayout) SendCustomEventDelayedFrames(nameof(_AuiSolveLayout), 1);
        }

        /// <summary>Wake every nested view under root (root itself included when it hosts one).</summary>
        private void WakeChildViews(int root)
        {
            if (elemChildView == null || elemSubtreeEnd == null || root < 0 || root >= elemSubtreeEnd.Length) return;
            int end = elemSubtreeEnd[root];
            for (int e = root; e < end && e < elemChildView.Length; e++)
                if (elemChildView[e] != null) elemChildView[e]._AuiWake();
        }

        /// <summary>Force a solve now if anything is dirty (also used after a batch under SetAutoRelayout(false)).</summary>
        public void Relayout()
        {
            EnsureStarted();
            if (!_layoutReady) return;
            _layoutDirty = true;
            _AuiSolveLayout();
        }

        /// <summary>Solve now, dirty or not.</summary>
        public void RelayoutNow()
        {
            EnsureStarted();
            if (!_layoutReady) return;
            _layoutDirty = true;
            _AuiSolveLayout();
        }

        /// <summary>false parks the dirty flag (batch many changes, then Relayout()); true re-enables and solves once if dirty.</summary>
        public void SetAutoRelayout(bool on)
        {
            _autoRelayout = on;
            if (on && _layoutDirty && _layoutReady && _started) _AuiSolveLayout();
        }

        public bool IsAutoRelayout()
        {
            return _autoRelayout;
        }

        /// <summary>display: DISPLAY_FLEX (0) shows the element in the flow; DISPLAY_NONE (1) removes it and its space.</summary>
        public void SetDisplay(int elem, int display)
        {
            if (!_started) { _auiStashI = elem; _auiStashI2 = display; _AuiStart(); elem = _auiStashI; display = _auiStashI2; }
            SetActiveElem(elem, display == DISPLAY_FLEX);
        }

        /// <summary>Make the element position: absolute at the given edges from the parent's content box; float.NaN leaves an edge unset. Negatives allowed.</summary>
        public void SetPosition(int elem, float left, float top, float right, float bottom)
        {
            if (!_started) { _auiStashI = elem; _AuiStart(); elem = _auiStashI; }
            if (!_layoutReady || elem < 0 || elem >= layFlags.Length) return;
            layFlags[elem] = layFlags[elem] | LF_ABSOLUTE;
            SetEdge(4 * elem + EDGE_LEFT, left);
            SetEdge(4 * elem + EDGE_TOP, top);
            SetEdge(4 * elem + EDGE_RIGHT, right);
            SetEdge(4 * elem + EDGE_BOTTOM, bottom);
            MarkLayoutDirty();
        }

        private void SetEdge(int slot, float v)
        {
            if (float.IsNaN(v)) { layEdgeKind[slot] = SIZE_UNSET; layEdgeVal[slot] = 0; }
            else { layEdgeKind[slot] = SIZE_MM; layEdgeVal[slot] = v; }
        }

        /// <summary>Explicit size in mm; float.NaN returns an axis to auto.</summary>
        public void SetSize(int elem, float width, float height)
        {
            if (!_started) { _auiStashI = elem; _AuiStart(); elem = _auiStashI; }
            if (!_layoutReady || elem < 0 || elem >= layFlags.Length) return;
            if (float.IsNaN(width)) { laySizeKind[6 * elem + SZ_W] = SIZE_UNSET; laySizeVal[6 * elem + SZ_W] = 0; }
            else { laySizeKind[6 * elem + SZ_W] = SIZE_MM; laySizeVal[6 * elem + SZ_W] = width; }
            if (float.IsNaN(height)) { laySizeKind[6 * elem + SZ_H] = SIZE_UNSET; laySizeVal[6 * elem + SZ_H] = 0; }
            else { laySizeKind[6 * elem + SZ_H] = SIZE_MM; laySizeVal[6 * elem + SZ_H] = height; }
            MarkLayoutDirty();
        }

        /// <summary>Write a text and let the solver re-measure it (generated code and SetText go through here).</summary>
        public void SetElemText(int elem, string value)
        {
            if (!_started) { _auiStashI = elem; _auiStashS = value; _AuiStart(); elem = _auiStashI; value = _auiStashS; }
            if (elemText == null || elem < 0 || elem >= elemText.Length || elemText[elem] == null) return;
            elemText[elem].text = value;
            if (!_layoutReady) return;
            // Any text written at runtime is re-measured, bound or literal; the runtime-text flag
            // only records which ones the compiler expected to change.
            if (layKind[elem] == LK_TEXT) { _textDirty[elem] = true; MarkLayoutDirty(); }
        }

        /// <summary>Solved local rect of an element (x, y from the parent's top-left, y down, mm).</summary>
        public float LayoutX(int elem) { return _rx != null && elem >= 0 && elem < _rx.Length ? _rx[elem] : 0; }
        public float LayoutY(int elem) { return _ry != null && elem >= 0 && elem < _ry.Length ? _ry[elem] : 0; }
        public float LayoutW(int elem) { return _rw != null && elem >= 0 && elem < _rw.Length ? _rw[elem] : 0; }
        public float LayoutH(int elem) { return _rh != null && elem >= 0 && elem < _rh.Length ? _rh[elem] : 0; }
        public bool LayoutPlaced(int elem) { return _placed != null && elem >= 0 && elem < _placed.Length && _placed[elem]; }

        /// <summary>Size a host view reserves for this nested one: its declared &lt;view width height&gt; (what the bake used), else its measured content.</summary>
        public float GetLayoutIntrinsicW() { return layRootW > 0 ? layRootW : (_lw != null && _lw.Length > 0 ? _lw[0] : 0); }
        public float GetLayoutIntrinsicH() { return layRootH > 0 ? layRootH : (_lh != null && _lh.Length > 0 ? _lh[0] : 0); }

        /// <summary>A host view gives its nested child the rect it solved for it.</summary>
        public void _AuiSetLayoutSize(float w, float h)
        {
            EnsureStarted();
            if (!_layoutReady) return;
            if (Mathf.Abs(w - _rootW) < 0.01f && Mathf.Abs(h - _rootH) < 0.01f) return;
            _rootW = w; _rootH = h;
            _layoutDirty = true;
            _AuiSolveLayout();
        }

        /// <summary>A nested child's intrinsic size changed; re-solve the host (bounded per frame).</summary>
        public void _AuiChildResized()
        {
            if (!_layoutReady || _layoutPasses > 2) return;
            _layoutPasses++;
            _layoutDirty = true;
            _AuiSolveLayout();
        }

        // ================================================================ solve

        /// <summary>Test harness entry: run measure and place over the tables only, touching no components. counts = rows per repeat, or null for max.</summary>
        public void _AuiSolveTablesOnly(int[] counts)
        {
            if (_repeatCount == null || _repeatCount.Length != (repeatMax == null ? 0 : repeatMax.Length))
            {
                int reps = repeatMax == null ? 0 : repeatMax.Length;
                _repeatCount = new int[reps];
                for (int r = 0; r < reps; r++) _repeatCount[r] = repeatMax[r];
            }
            if (counts != null) for (int r = 0; r < _repeatCount.Length && r < counts.Length; r++) _repeatCount[r] = counts[r];
            if (!_layoutReady) _AuiLayoutInit();
            if (!_layoutReady) return;
            Measure();
            Place();
            _solveCount++;
        }

        public void _AuiSolveLayout()
        {
            if (!_layoutReady || !_layoutDirty) return;
            _layoutDirty = false;
            float t0 = debugLayout ? Time.realtimeSinceStartup : 0f;
            MeasureDirtyTexts();
            Measure();
            Place();
            ApplyLayout();
            _solveCount++;
            _layoutPasses = 0;
            if (debugLayout) Debug.Log("[AUI] layout: " + name + " solved in " + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0.00") + " ms");
        }

        private bool Hidden(int e)
        {
            if (e == 0) return false;
            if (!_shown[e]) return true;
            return _cssHidden != null && _cssHidden[e];
        }

        private bool IsFlowChild(int c)
        {
            int k = layKind[c];
            if (k == LK_IGNORE || k == LK_EMPTY) return false;
            if (Hidden(c)) return false;
            return (layFlags[c] & LF_ABSOLUTE) == 0;
        }

        private float SizeMm(int e, int slot)
        {
            return laySizeKind[6 * e + slot] == SIZE_MM ? laySizeVal[6 * e + slot] : 0;
        }

        private bool SizeSet(int e, int slot)
        {
            int k = laySizeKind[6 * e + slot];
            return k == SIZE_MM || k == SIZE_PCT;
        }

        private bool SizeIsSetAtAll(int e, int slot)
        {
            return laySizeKind[6 * e + slot] != SIZE_UNSET;
        }

        private float ResolveSize(int e, int slot, float parent, float fallback)
        {
            int k = laySizeKind[6 * e + slot];
            if (k == SIZE_MM) return laySizeVal[6 * e + slot];
            if (k == SIZE_PCT) return parent * laySizeVal[6 * e + slot] / 100f;
            return fallback;
        }

        private float ClampMinMax(int e, float v, int minSlot, int maxSlot)
        {
            if (laySizeKind[6 * e + minSlot] == SIZE_MM) v = Mathf.Max(v, laySizeVal[6 * e + minSlot]);
            if (laySizeKind[6 * e + maxSlot] == SIZE_MM) v = Mathf.Min(v, laySizeVal[6 * e + maxSlot]);
            return v;
        }

        private float PadH(int e) { return layPad[4 * e + 1] + layPad[4 * e + 3]; }
        private float PadV(int e) { return layPad[4 * e] + layPad[4 * e + 2]; }
        private float MarH(int e) { return layMargin[4 * e + 1] + layMargin[4 * e + 3]; }
        private float MarV(int e) { return layMargin[4 * e] + layMargin[4 * e + 2]; }

        // ---------------------------------------------------------------- text

        private void MeasureDirtyTexts()
        {
            int n = _textDirty.Length;
            for (int e = 0; e < n; e++)
            {
                if (!_textDirty[e] || elemText[e] == null) continue;
                if (!elemText[e].gameObject.activeInHierarchy) continue;   // measured on the solve after it activates
                RectTransform rt = (RectTransform)elemText[e].transform;
                bool wrap = (layFlags[e] & LF_TEXT_WRAP) != 0;
                float maxW = laySizeKind[6 * e + SZ_MAXW] == SIZE_MM ? laySizeVal[6 * e + SZ_MAXW] : (laySizeKind[6 * e + SZ_W] == SIZE_MM ? laySizeVal[6 * e + SZ_W] : 0);
                // preferredWidth is the unwrapped width; preferredHeight is measured at the rect's
                // current width, so a wrapped text is first given the width it wraps at (Core wraps
                // at max-width / width, else the width the last solve gave it).
                float w = LayoutUtility.GetPreferredWidth(rt);
                if (wrap)
                {
                    float wrapW = maxW > 0 ? maxW : _rw[e];
                    if (wrapW > 0 && Mathf.Abs(rt.sizeDelta.x - wrapW) > 0.01f)
                    {
                        rt.sizeDelta = new Vector2(wrapW, rt.sizeDelta.y);
                        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                    }
                }
                float h = LayoutUtility.GetPreferredHeight(rt);
                if (h <= 0f) continue;   // TMP not regenerated yet: keep the previous size and the dirty bit
                if (maxW > 0 && w > maxW) w = maxW;
                _rawW[e] = w + 2f;
                _rawH[e] = h + 1f;
                _textDirty[e] = false;
            }
        }

        // ---------------------------------------------------------------- measure

        private void Measure()
        {
            int n = layParent.Length;
            int L = layLangCount > 0 ? layLangCount : 1;
            int lang = _layLang < 0 ? 0 : _layLang >= L ? L - 1 : _layLang;

            // availW top-down: what Core's Measure(n, availW) passes to each child.
            for (int e = 0; e < n; e++)
            {
                int p = layParent[e];
                float avail = p < 0 ? (e == 0 ? _rootW : 0) : _inner[p];
                _availW[e] = avail;
                float own = SizeIsSetAtAll(e, SZ_W) && laySizeKind[6 * e + SZ_W] != SIZE_AUTO ? ResolveSize(e, SZ_W, avail, avail) : avail;
                _inner[e] = own > 0 ? Mathf.Max(0, own - PadH(e)) : 0;
            }

            for (int e = n - 1; e >= 0; e--)
            {
                int kind = layKind[e];
                if (kind == LK_IGNORE) { _lw[e] = 0; _lh[e] = 0; continue; }
                if (Hidden(e)) { _lw[e] = 0; _lh[e] = 0; continue; }
                float w = 0, h = 0;
                int flags = layFlags[e];
                if (kind == LK_TEXT) { w = _rawW[e]; h = _rawH[e]; }
                else if (kind == LK_LEAF)
                {
                    if ((flags & LF_SPACER) != 0)
                    {
                        int p = layParent[e];
                        bool parentRow = p >= 0 && (layFlags[p] & LF_ROW) != 0;
                        w = parentRow ? _rawW[e] : 0;
                        h = parentRow ? 0 : _rawH[e];
                    }
                    else { w = _rawW[e]; h = _rawH[e]; }
                }
                else if (kind == LK_NESTED)
                {
                    AuiView child = elemChildView != null && e < elemChildView.Length ? elemChildView[e] : null;
                    float cw = _rawW[e], ch = _rawH[e];
                    if (child != null) { cw = child.GetLayoutIntrinsicW(); ch = child.GetLayoutIntrinsicH(); }
                    w = SizeIsSetAtAll(e, SZ_W) && laySizeKind[6 * e + SZ_W] == SIZE_MM ? laySizeVal[6 * e + SZ_W] : (laySizeKind[6 * e + SZ_W] == SIZE_PCT ? 0 : cw);
                    h = SizeIsSetAtAll(e, SZ_H) && laySizeKind[6 * e + SZ_H] == SIZE_MM ? laySizeVal[6 * e + SZ_H] : (laySizeKind[6 * e + SZ_H] == SIZE_PCT ? 0 : ch);
                }
                else if (kind == LK_REPEAT) MeasureRepeat(e, _inner[e], ref w, ref h);
                else if (kind == LK_PAGES)
                {
                    for (int c = layFirstChild[e]; c >= 0; c = layNextSibling[c]) { w = Mathf.Max(w, _lw[c]); h = Mathf.Max(h, _lh[c]); }
                    w += PadH(e); h += PadV(e);
                }
                else MeasureContainer(e, kind, flags, lang, L, _inner[e], ref w, ref h);

                if (laySizeKind[6 * e + SZ_W] == SIZE_MM) w = laySizeVal[6 * e + SZ_W];
                if (laySizeKind[6 * e + SZ_H] == SIZE_MM) h = laySizeVal[6 * e + SZ_H];
                w = ClampMinMax(e, w, SZ_MINW, SZ_MAXW);
                h = ClampMinMax(e, h, SZ_MINH, SZ_MAXH);
                _lw[e] = w;
                _lh[e] = h;
            }
        }

        private void MeasureContainer(int e, int kind, int flags, int lang, int L, float availW, ref float w, ref float h)
        {
            bool row = (flags & LF_ROW) != 0;
            float gap = layGap[e];
            float main = 0, cross = 0;
            int count = 0;
            if (row && (flags & LF_WRAP) != 0 && availW > 0)
            {
                float x = 0, y = 0, lineH = 0, widest = 0;
                for (int c = layFirstChild[e]; c >= 0; c = layNextSibling[c])
                {
                    if (!IsFlowChild(c)) continue;
                    float cw = _lw[c] + MarH(c), ch = _lh[c] + MarV(c);
                    if (x > 0 && x + cw > availW + 0.01f) { x = 0; y += lineH + gap; lineH = 0; }
                    x += cw + gap;
                    lineH = Mathf.Max(lineH, ch);
                    widest = Mathf.Max(widest, x - gap);
                    count++;
                }
                w = Mathf.Min(availW, widest) + PadH(e);
                h = y + lineH + PadV(e);
                return;
            }
            for (int c = layFirstChild[e]; c >= 0; c = layNextSibling[c])
            {
                if (!IsFlowChild(c)) continue;
                float cw = _lw[c] + MarH(c), ch = _lh[c] + MarV(c);
                if (row) { main += cw; cross = Mathf.Max(cross, ch); }
                else { main += ch; cross = Mathf.Max(cross, cw); }
                count++;
            }
            if (count > 1) main += gap * (count - 1);
            float lw = layContentW[e * L + lang], lh = layContentH[e * L + lang];
            if (kind == LK_SELECT)
            {
                main = lw + gap + IconMm;
                cross = Mathf.Max(lh, IconMm);
            }
            else if ((flags & LF_CONTENT_TEXT) != 0)
            {
                bool icon = (flags & LF_CONTENT_ICON) != 0;
                if (row) { main += lw + (icon ? IconMm + gap : 0); cross = Mathf.Max(cross, Mathf.Max(lh, icon ? IconMm : 0)); }
                else { main += lh; cross = Mathf.Max(cross, lw); }
            }
            else if ((flags & LF_CONTENT_ICON_ONLY) != 0)
            {
                main += IconMm; cross = Mathf.Max(cross, IconMm);
            }
            if (row) { w = main + PadH(e); h = cross + PadV(e); }
            else { w = cross + PadH(e); h = main + PadV(e); }
        }

        private void RepeatRowSizes(int e, int r, ref float iw, ref float ih, ref int count)
        {
            int first = repeatFirst[r], stride = repeatStride[r];
            count = _repeatCount[r];
            iw = 0; ih = 0;
            int rows = count > 0 ? count : 1;   // row 0 sizes the empty list's template rect, as Core does
            for (int i = 0; i < rows; i++)
            {
                int root = first + i * stride;
                if (root < 0 || root >= _lw.Length) continue;
                iw = Mathf.Max(iw, _lw[root] + MarH(root));
                ih = Mathf.Max(ih, _lh[root] + MarV(root));
            }
        }

        private void MeasureRepeat(int e, float availW, ref float w, ref float h)
        {
            int r = layRepeat[e];
            if (r < 0 || repeatFirst == null || r >= repeatFirst.Length) { w = PadH(e); h = PadV(e); return; }
            float iw = 0, ih = 0; int count = 0;
            RepeatRowSizes(e, r, ref iw, ref ih, ref count);
            float gap = layGap[e];
            int flags = layFlags[e];
            bool row = (flags & LF_ROW) != 0, wrap = (flags & LF_WRAP) != 0;
            if (count <= 0)
            {
                // No rows: the box is what <empty> needs (it fills the content box), so its message has room.
                float ew = 0, eh = 0;
                for (int c = layFirstChild[e]; c >= 0; c = layNextSibling[c])
                    if (layKind[c] == LK_EMPTY) { ew = _lw[c]; eh = _lh[c]; }
                w = ew + PadH(e); h = eh + PadV(e);
            }
            else if (row && !wrap) { w = iw * count + gap * (count - 1) + PadH(e); h = ih + PadV(e); }
            else if (row)
            {
                float innerW = availW > 0 ? availW - PadH(e) : 0;
                int perLine = innerW > 0 ? Mathf.Max(1, Mathf.FloorToInt((innerW + gap) / (iw + gap))) : count;
                int lines = (count + perLine - 1) / perLine;
                w = (innerW > 0 ? innerW : iw * count + gap * (count - 1)) + PadH(e);
                h = ih * lines + gap * (lines - 1) + PadV(e);
            }
            else { w = iw + PadH(e); h = ih * count + gap * (count - 1) + PadV(e); }
        }

        // ---------------------------------------------------------------- place

        private void SetRect(int c, float x, float y, float w, float h, int parent)
        {
            _rx[c] = x; _ry[c] = y; _rw[c] = w; _rh[c] = h;
            _ax[c] = (parent >= 0 ? _ax[parent] : 0) + x;
            _ay[c] = (parent >= 0 ? _ay[parent] : 0) + y;
            _placed[c] = true;
        }

        private void Place()
        {
            int n = layParent.Length;
            for (int e = 0; e < n; e++) _placed[e] = false;
            float rw = _rootW > 0 ? _rootW : _lw[0];
            float rh = _rootH > 0 ? _rootH : _lh[0];
            SetRect(0, 0, 0, rw, rh, -1);

            for (int e = 0; e < n; e++)
            {
                int kind = layKind[e];
                if (kind == LK_IGNORE || !_placed[e]) continue;
                if (Hidden(e)) continue;
                float cx = layPad[4 * e + 3], cy = layPad[4 * e];
                float cw = Mathf.Max(0, _rw[e] - PadH(e)), ch = Mathf.Max(0, _rh[e] - PadV(e));
                if (layFirstChild[e] < 0 && kind != LK_REPEAT) continue;
                if (kind == LK_PAGES)
                {
                    for (int c = layFirstChild[e]; c >= 0; c = layNextSibling[c]) if (layKind[c] != LK_IGNORE) SetRect(c, cx, cy, cw, ch, e);
                    continue;
                }
                if (kind == LK_REPEAT) { PlaceRepeat(e, cx, cy, cw, ch); continue; }
                if (kind == LK_SELECT) continue;   // options live in the popup; PlacePopups handles it

                _flowCount = 0;
                for (int c = layFirstChild[e]; c >= 0; c = layNextSibling[c])
                {
                    if (layKind[c] == LK_IGNORE) continue;
                    if (IsFlowChild(c)) { _flow[_flowCount++] = c; continue; }
                    if (Hidden(c)) { SetRect(c, cx, cy, 0, 0, e); continue; }
                    if (layKind[c] == LK_EMPTY) continue;
                    PlaceAbsolute(c, e, cx, cy, cw, ch);
                }
                if (_flowCount == 0) continue;
                bool row = (layFlags[e] & LF_ROW) != 0;
                if (row && (layFlags[e] & LF_WRAP) != 0) PlaceWrap(e, cx, cy, cw, ch);
                else PlaceFlow(e, row, cx, cy, cw, ch);
            }
        }

        private void PlaceAbsolute(int c, int e, float cx, float cy, float cw, float ch)
        {
            int anchor = layAnchor[c];
            bool fill = anchor == ANCHOR_FILL;
            int tk = layEdgeKind[4 * c + EDGE_TOP], rk = layEdgeKind[4 * c + EDGE_RIGHT], bk = layEdgeKind[4 * c + EDGE_BOTTOM], lk = layEdgeKind[4 * c + EDGE_LEFT];
            float tv = layEdgeVal[4 * c + EDGE_TOP], rv = layEdgeVal[4 * c + EDGE_RIGHT], bv = layEdgeVal[4 * c + EDGE_BOTTOM], lv = layEdgeVal[4 * c + EDGE_LEFT];
            if (fill)
            {
                if (tk == SIZE_UNSET) { tk = SIZE_MM; tv = 0; }
                if (rk == SIZE_UNSET) { rk = SIZE_MM; rv = 0; }
                if (bk == SIZE_UNSET) { bk = SIZE_MM; bv = 0; }
                if (lk == SIZE_UNSET) { lk = SIZE_MM; lv = 0; }
            }
            bool hasT = tk != SIZE_UNSET, hasR = rk != SIZE_UNSET, hasB = bk != SIZE_UNSET, hasL = lk != SIZE_UNSET;
            float t = hasT ? (tk == SIZE_PCT ? ch * tv / 100f : tv) : 0;
            float rr = hasR ? (rk == SIZE_PCT ? cw * rv / 100f : rv) : 0;
            float b = hasB ? (bk == SIZE_PCT ? ch * bv / 100f : bv) : 0;
            float l = hasL ? (lk == SIZE_PCT ? cw * lv / 100f : lv) : 0;

            float w, h;
            int wk = laySizeKind[6 * c + SZ_W], hk = laySizeKind[6 * c + SZ_H];
            if (wk == SIZE_MM || wk == SIZE_PCT) w = wk == SIZE_PCT ? cw * laySizeVal[6 * c + SZ_W] / 100f : laySizeVal[6 * c + SZ_W];
            else if (hasL && hasR) w = cw - l - rr;
            else w = _lw[c];
            if (hk == SIZE_MM || hk == SIZE_PCT) h = hk == SIZE_PCT ? ch * laySizeVal[6 * c + SZ_H] / 100f : laySizeVal[6 * c + SZ_H];
            else if (hasT && hasB) h = ch - t - b;
            else h = _lh[c];
            w = ClampMinMax(c, Mathf.Max(0, w), SZ_MINW, SZ_MAXW);
            h = ClampMinMax(c, Mathf.Max(0, h), SZ_MINH, SZ_MAXH);

            float x, y;
            if (hasL) x = cx + l;
            else if (hasR) x = cx + cw - rr - w;
            else if (anchor == ANCHOR_TOP || anchor == ANCHOR_BOTTOM || anchor == ANCHOR_CENTER) x = cx + (cw - w) / 2;
            else if (anchor == ANCHOR_RIGHT) x = cx + cw - w;
            else x = cx;
            if (hasT) y = cy + t;
            else if (hasB) y = cy + ch - b - h;
            else if (anchor == ANCHOR_LEFT || anchor == ANCHOR_RIGHT || anchor == ANCHOR_CENTER) y = cy + (ch - h) / 2;
            else if (anchor == ANCHOR_BOTTOM) y = cy + ch - h;
            else y = cy;
            SetRect(c, x, y, w, h, e);
        }

        private bool CanShrink(int c)
        {
            int wk = laySizeKind[6 * c + SZ_W];
            if (wk == SIZE_MM || wk == SIZE_PCT) return false;
            return (layFlags[c] & LF_SHRINKABLE) != 0;
        }

        private void PlaceFlow(int e, bool row, float cx, float cy, float cw, float ch)
        {
            float gap = layGap[e];
            float mainAvail = row ? cw : ch;
            float crossAvail = row ? ch : cw;
            int k = _flowCount;
            float used = gap * (k - 1);
            float growSum = 0;
            for (int i = 0; i < k; i++)
            {
                int c = _flow[i];
                int mainSlot = row ? SZ_W : SZ_H, crossSlot = row ? SZ_H : SZ_W;
                float mainIntrinsic = row ? _lw[c] : _lh[c];
                float crossIntrinsic = row ? _lh[c] : _lw[c];
                _fm[i] = laySizeKind[6 * c + mainSlot] == SIZE_PCT ? mainAvail * laySizeVal[6 * c + mainSlot] / 100f : mainIntrinsic;
                _fc[i] = laySizeKind[6 * c + crossSlot] == SIZE_PCT ? crossAvail * laySizeVal[6 * c + crossSlot] / 100f : crossIntrinsic;
                _fm[i] += row ? MarH(c) : MarV(c);
                _fc[i] += row ? MarV(c) : MarH(c);
                used += _fm[i];
                growSum += layGrow[c];
            }
            float leftover = mainAvail - used;
            if (leftover < -0.5f)
            {
                float shrinkable = 0f;
                if (row)
                    for (int i = 0; i < k; i++)
                        if (CanShrink(_flow[i]) && _fm[i] > MinShrunk) shrinkable += _fm[i] - MinShrunk;
                float need = -leftover;
                if (shrinkable > 0f)
                {
                    float take = Mathf.Min(need, shrinkable);
                    for (int i = 0; i < k; i++)
                    {
                        if (!CanShrink(_flow[i]) || _fm[i] <= MinShrunk) continue;
                        float share = (_fm[i] - MinShrunk) / shrinkable;
                        _fm[i] -= take * share;
                    }
                    leftover += take;
                }
                if (leftover < 0) leftover = 0;
            }
            if (growSum > 0 && leftover > 0)
            {
                for (int i = 0; i < k; i++)
                {
                    float g = layGrow[_flow[i]];
                    if (g > 0) _fm[i] += leftover * g / growSum;
                }
                leftover = 0;
            }

            float cursor = 0, between = gap;
            int justify = layJustify[e];
            if (justify == JUSTIFY_CENTER) cursor = leftover / 2;
            else if (justify == JUSTIFY_END) cursor = leftover;
            else if (justify == JUSTIFY_BETWEEN && k > 1) between = gap + leftover / (k - 1);

            int align = layAlign[e];
            for (int i = 0; i < k; i++)
            {
                int c = _flow[i];
                float mT = layMargin[4 * c], mR = layMargin[4 * c + 1], mB = layMargin[4 * c + 2], mL = layMargin[4 * c + 3];
                float mCross = row ? mT + mB : mL + mR;
                float mMain = row ? mL + mR : mT + mB;
                bool stretch = align == ALIGN_STRETCH && laySizeKind[6 * c + (row ? SZ_H : SZ_W)] == SIZE_UNSET;
                float crossSize = stretch ? crossAvail - mCross : _fc[i] - mCross;
                float crossPos = 0;
                float crossFree = crossAvail - crossSize - mCross;
                if (align == ALIGN_CENTER) crossPos = crossFree / 2;
                else if (align == ALIGN_END) crossPos = crossFree;
                float mainSize = _fm[i] - mMain;
                if (row) SetRect(c, cx + cursor + mL, cy + crossPos + mT, mainSize, crossSize, e);
                else SetRect(c, cx + crossPos + mL, cy + cursor + mT, crossSize, mainSize, e);
                cursor += _fm[i] + between;
            }
        }

        private void PlaceWrap(int e, float cx, float cy, float cw, float ch)
        {
            float gap = layGap[e];
            float x = 0, y = 0, lineH = 0;
            for (int i = 0; i < _flowCount; i++)
            {
                int c = _flow[i];
                float mT = layMargin[4 * c], mL = layMargin[4 * c + 3];
                float w = _lw[c] + MarH(c), h = _lh[c] + MarV(c);
                if (x > 0 && x + w > cw + 0.01f) { x = 0; y += lineH + gap; lineH = 0; }
                SetRect(c, cx + x + mL, cy + y + mT, _lw[c], _lh[c], e);
                x += w + gap;
                lineH = Mathf.Max(lineH, h);
            }
        }

        private void PlaceRepeat(int e, float cx, float cy, float cw, float ch)
        {
            int r = layRepeat[e];
            if (r < 0 || repeatFirst == null || r >= repeatFirst.Length) return;
            float iw = 0, ih = 0; int count = 0;
            RepeatRowSizes(e, r, ref iw, ref ih, ref count);
            int first = repeatFirst[r], stride = repeatStride[r];
            int root0 = first;
            float mT = layMargin[4 * root0], mR = layMargin[4 * root0 + 1], mB = layMargin[4 * root0 + 2], mL = layMargin[4 * root0 + 3];
            iw -= mL + mR; ih -= mT + mB;   // RepeatRowSizes added the margins
            int flags = layFlags[e];
            bool row = (flags & LF_ROW) != 0, wrap = (flags & LF_WRAP) != 0;
            float gap = layGap[e];
            if (!row && laySizeKind[6 * root0 + SZ_W] == SIZE_UNSET) iw = Mathf.Max(iw, cw - (mL + mR));
            int rows = count > 0 ? count : 1;
            float x = 0, y = 0, lineH = 0;
            for (int i = 0; i < rows; i++)
            {
                if (row && wrap && x > 0 && x + iw + mL + mR > cw + 0.01f) { x = 0; y += lineH + gap; lineH = 0; }
                int root = first + i * stride;
                if (i < count || count == 0) SetRect(root, cx + x + mL, cy + y + mT, iw, ih, e);
                if (row) { x += iw + mL + mR + gap; lineH = Mathf.Max(lineH, ih + mT + mB); }
                else y += ih + mT + mB + gap;
            }
            // Rows beyond the count are inactive; their subtrees stay unplaced. <empty> fills the content box.
            for (int c = layFirstChild[e]; c >= 0; c = layNextSibling[c])
                if (layKind[c] == LK_EMPTY) SetRect(c, cx, cy, cw, ch, e);
        }

        // ---------------------------------------------------------------- apply

        private void ApplyLayout()
        {
            int n = layParent.Length;
            for (int e = 1; e < n; e++)
            {
                if (!_placed[e] || Hidden(e) || layKind[e] == LK_IGNORE) continue;
                RectTransform rt = elemRects[e];
                if (rt == null) continue;
                Vector2 size = new Vector2(_rw[e], _rh[e]);
                Vector2 pos = new Vector2(_rx[e] + _rw[e] * 0.5f, -(_ry[e] + _rh[e] * 0.5f));
                Vector2 basePos = elemBasePos[e];
                bool moved = Mathf.Abs(basePos.x - pos.x) > 0.01f || Mathf.Abs(basePos.y - pos.y) > 0.01f;
                bool resized = Mathf.Abs(rt.sizeDelta.x - size.x) > 0.01f || Mathf.Abs(rt.sizeDelta.y - size.y) > 0.01f;
                if (!moved && !resized) continue;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                if (resized) rt.sizeDelta = size;
                if (moved)
                {
                    // The base position is what offset tweens and page snaps return to; the
                    // rect follows unless a state offset is live, in which case ApplyState retargets it.
                    elemBasePos[e] = pos;
                    int id = elemSignal[e];
                    if (id >= 0 && HasOffsetState(e)) ApplyState(id);
                    else rt.anchoredPosition = pos;
                }
                if (layKind[e] == LK_NESTED && elemChildView != null && elemChildView[e] != null)
                    elemChildView[e]._AuiSetLayoutSize(_rw[e], _rh[e]);
            }
            PlacePopups();
        }

        /// <summary>A select's popup follows its header: below it, or above when there is no room, clamped into the view (same rule as the builder).</summary>
        private void PlacePopups()
        {
            if (selectElem == null) return;
            float viewW = _rootW > 0 ? _rootW : layRootW, viewH = _rootH > 0 ? _rootH : layRootH;
            for (int s = 0; s < selectElem.Length; s++)
            {
                int he = selectElem[s], pe = selectPopupElem[s];
                if (!_placed[he] || Hidden(he) || elemRects[pe] == null) continue;
                RectTransform prt = elemRects[pe];
                float popupH = prt.sizeDelta.y;
                float hx = _ax[he], hy = _ay[he], hw = _rw[he], hh = _rh[he];
                float py = hy + hh + 4f;
                if (py + popupH > viewH - 4f) py = hy - 4f - popupH;
                if (py < 4f) py = 4f;
                float px = Mathf.Clamp(hx, 4f, Mathf.Max(4f, viewW - hw - 4f));
                Vector2 pos = new Vector2(px + hw * 0.5f, -(py + popupH * 0.5f));
                Vector2 size = new Vector2(hw, popupH);
                bool moved = Mathf.Abs(elemBasePos[pe].x - pos.x) > 0.01f || Mathf.Abs(elemBasePos[pe].y - pos.y) > 0.01f;
                bool resized = Mathf.Abs(prt.sizeDelta.x - size.x) > 0.01f;
                if (!moved && !resized) continue;
                prt.sizeDelta = size;
                prt.anchoredPosition = pos;
                prt.anchoredPosition3D = new Vector3(pos.x, pos.y, -1f);
                elemBasePos[pe] = pos;
                if (resized)
                {
                    int first = selectOptionFirst[s];
                    for (int i = 0; i < selectOptionCount[s]; i++)
                    {
                        RectTransform ort = elemRects[first + i];
                        if (ort == null) continue;
                        ort.sizeDelta = new Vector2(hw - 8f, ort.sizeDelta.y);
                        ort.anchoredPosition = new Vector2(4f + (hw - 8f) * 0.5f, ort.anchoredPosition.y);
                        elemBasePos[first + i] = ort.anchoredPosition;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- hooks from AuiView.cs

        /// <summary>Language changed: swap the baked per-language sizes in and re-solve.</summary>
        private void _AuiLayoutLanguageChanged()
        {
            if (!_layoutReady) return;
            int l = lang != null ? lang.GetLanguage() : 0;
            if (l == _layLang && _solveCount > 0) return;
            _layLang = l;
            LoadLanguageSizes();
            MarkLayoutDirty();
        }

        /// <summary>Visibility of an element changed through SetActiveElem / SetDisplay.</summary>
        private void _AuiLayoutShown(int elem, bool on)
        {
            if (!_layoutReady || elem < 0 || elem >= _shown.Length) return;
            if (_shown[elem] == on) return;
            _shown[elem] = on;
            // Texts that were inactive at their last solve still carry their dirty bit; the
            // solve this schedules measures them now that they are active.
            MarkLayoutDirty();
        }
    }
}
