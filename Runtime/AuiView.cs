using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDK3.Components;

namespace Armagan.AUI
{
    /// <summary>
    /// Base class of every compiled AUI view. The compiler generates a partial subclass per
    /// .aui file (Foo.g.cs) and the author writes the other half (Foo.cs). This class owns the
    /// mechanics that are the same for every view: the element tables the prefab builder
    /// fills, pointer-state styles, show/hide and page transitions, repeats, selects, the
    /// input box, translation refresh and signal dispatch. Generated code only adds the
    /// typed surface: properties for bindings, handler calls, per-text placeholder rendering.
    ///
    /// EVERYTHING IS INDEXED BY RUNTIME ELEMENT INDEX. Core's AuiIndexMap assigns one index to
    /// every baked GameObject (a repeat template's subtree gets one block per instance), and
    /// the builder fills every elem* array to that size. Entries are null where an element
    /// has no such component; every use checks for null.
    ///
    /// NOTHING HERE RUNS IN Update. Pointer, click and value events arrive through AuiSignal;
    /// delayed steps are SendCustomEventDelayedSeconds with sequence guards, because VRCTween
    /// does not animate at all under ClientSim and an inactive object stops its tweens. The
    /// behaviour's own GameObject is never deactivated; only `content` and pages are, and only
    /// in a snap step after the transition, never mid-fade.
    ///
    /// A FIELD ANOTHER BEHAVIOUR WRITES MUST BE PUBLIC AND SERIALIZED. UdonSharp exports a
    /// symbol only for such fields, and SetProgramVariable against an unexported symbol is a
    /// silent no-op. inputResult / inputResultId are written by AuiInputPanel; the tables are
    /// written by the builder at edit time. None of them may become [NonSerialized].
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class AuiView : UdonSharpBehaviour
    {
        // ---- signal kinds
        public const int SIG_CLICK = 0, SIG_ENTER = 1, SIG_EXIT = 2, SIG_DOWN = 3, SIG_UP = 4, SIG_VALUE = 5;
        // ---- visual states; index into styleRange (sig * STATE_COUNT + state)
        public const int STATE_NORMAL = 0, STATE_HOVER = 1, STATE_PRESSED = 2, STATE_SELECTED = 3, STATE_DISABLED = 4, STATE_COUNT = 5;
        // ---- style row properties
        public const int PROP_BG = 0, PROP_COLOR = 1, PROP_ICON = 2, PROP_BORDER = 3, PROP_OPACITY = 4, PROP_SCALE = 5, PROP_OFFSET = 6, PROP_SPRITE = 7;
        // ---- element kinds (what a click means)
        public const int KIND_NONE = 0, KIND_BUTTON = 1, KIND_TOGGLE = 2, KIND_TAB = 3, KIND_OPTION = 4, KIND_INPUT = 5, KIND_SELECT = 6, KIND_SLIDER = 7, KIND_SCRIM = 8;
        // ---- transitions
        public const int TRANS_NONE = 0, TRANS_FADE = 1, TRANS_FADE_SCALE = 2, TRANS_FADE_SLIDE = 3, TRANS_SLIDE_LEFT = 4, TRANS_SLIDE_RIGHT = 5, TRANS_SLIDE_UP = 6, TRANS_SLIDE_DOWN = 7, TRANS_SCALE = 8;
        public const int PAGE_CROSSFADE = 0, PAGE_SEQUENTIAL = 1;

        [Header("Wiring (baked by the AUI builder)")]
        public AuiLanguageService lang;
        public AuiInputPanel inputPanel;
        public GameObject content;
        public CanvasGroup contentGroup;
        public RectTransform contentRect;
        [HideInInspector] public string auiBakeHash;

        [Header("Element tables, index = runtime element index")]
        public GameObject[] elemObjects;
        public RectTransform[] elemRects;
        public CanvasGroup[] elemGroups;
        public Image[] elemBg;
        public Image[] elemBorder;
        public Image[] elemIcon;
        public TextMeshProUGUI[] elemText;
        public Button[] elemButtons;
        public Slider[] elemSliders;
        public Vector2[] elemBasePos;
        public int[] elemSignal;   // signal id or -1
        public int[] elemKey;      // translation key index or -1 (text label, input placeholder, slider format)
        public int[] elemKind;     // KIND_*

        [Header("Signals")]
        public int[] sigElem;
        public AuiSignal[] signals;

        [Header("State style rows")]
        public int[] styleRangeStart;  // [sig * STATE_COUNT + state]
        public int[] styleRangeCount;
        public int[] styleProp;
        public Vector4[] styleValue;
        public Sprite[] styleSprite;
        public float[] styleDur;
        public int[] styleEase;        // VRCTweenEase as int
        public int[] styleChannel;     // one tween handle slot per (element, property)
        public int channelCount;

        [Header("View transition")]
        public int showKind = TRANS_FADE_SCALE;
        public float showDur = 0.18f;
        public float hideDur = 0.14f;
        public Vector2 slideFrom = new Vector2(0f, -24f);
        public float scaleFrom = 0.94f;
        public bool startHidden;

        [Header("Pages")]
        public int[] pageGroupStart;   // index into pageElem
        public int[] pageGroupCount;
        public int[] pageGroupMode;
        public int[] pageGroupDefault;
        public int[] pageElem;         // element index of each page root
        public int[] pageTabSig;       // signal id of the tab for each page, or -1
        public int[] pageKind;         // TRANS_* per page
        public float pageDur = 0.18f;

        [Header("Repeats")]
        public int[] repeatFirst;      // element index of instance 0's root
        public int[] repeatStride;     // elements per instance
        public int[] repeatMax;
        public GameObject[] repeatEmpty;

        [Header("Selects")]
        public int[] selectElem;       // header element (button)
        public int[] selectPopupElem;  // popup root element (has CanvasGroup)
        public int[] selectOptionFirst;
        public int[] selectOptionCount;
        public int[] selectScrimSig;

        [Header("Written by AuiInputPanel")]
        [HideInInspector] public string inputResult;
        [HideInInspector] public int inputResultId = -1;

        [Header("Nesting (baked)")]
        [Tooltip("The view this one is embedded in, if any. Emit() reports to it.")]
        public AuiView parentView;

        // ---- runtime
        private bool[] _hover, _press, _selected, _disabled, _toggleOn;
        private VRCTweenHandle[] _chan;
        private VRCTweenHandle _fadeH, _scaleH, _slideH;
        private int _seq, _snapSeq;
        private bool _visible, _started;
        private int _signalCount;
        private int[] _pageCur, _pagePending, _pageSeq;
        private int[] _repeatCount;
        private int[] _selectValue;
        private int _openSelect = -1;
        private int _selectSeq;
        private bool _tweensCached, _tweensOk;
        private string[] _inputValue;
        private bool _broken;

        // ================================================================ lifecycle

        private void Start()
        {
            _AuiStart();
        }

        /// <summary>Idempotent; a parent view may call it before Start to wire a child early.</summary>
        public void _AuiStart()
        {
            if (_started) return;
            _started = true;

            int n = elemObjects == null ? 0 : elemObjects.Length;
            _signalCount = sigElem == null ? 0 : sigElem.Length;

            // ARRAYS FROM THE UDON HEAP ARE EMPTY, NOT NULL. Every elem* table must be exactly
            // n long or an index below silently reads garbage; a builder that skipped one is
            // the only way that happens, so say so once and refuse to run.
            if (Bad(elemRects == null ? -1 : elemRects.Length, n) || Bad(elemGroups == null ? -1 : elemGroups.Length, n) ||
                Bad(elemBg == null ? -1 : elemBg.Length, n) || Bad(elemBorder == null ? -1 : elemBorder.Length, n) ||
                Bad(elemIcon == null ? -1 : elemIcon.Length, n) || Bad(elemText == null ? -1 : elemText.Length, n) ||
                Bad(elemButtons == null ? -1 : elemButtons.Length, n) || Bad(elemSliders == null ? -1 : elemSliders.Length, n) ||
                Bad(elemBasePos == null ? -1 : elemBasePos.Length, n) || Bad(elemSignal == null ? -1 : elemSignal.Length, n) ||
                Bad(elemKey == null ? -1 : elemKey.Length, n) || Bad(elemKind == null ? -1 : elemKind.Length, n))
            {
                Debug.LogError("[AUI] " + name + ": element tables are not all " + n + " long. Rebuild the view (AUI/Rebuild All Views).");
                _broken = true;
                return;
            }
            _inputValue = new string[n];
            for (int i = 0; i < n; i++) _inputValue[i] = "";
            _hover = new bool[_signalCount];
            _press = new bool[_signalCount];
            _selected = new bool[_signalCount];
            _disabled = new bool[_signalCount];
            _toggleOn = new bool[n];
            _chan = new VRCTweenHandle[channelCount > 0 ? channelCount : 1];

            int groups = pageGroupStart == null ? 0 : pageGroupStart.Length;
            _pageCur = new int[groups];
            _pagePending = new int[groups];
            _pageSeq = new int[groups];
            for (int g = 0; g < groups; g++) { _pageCur[g] = -1; _pagePending[g] = -1; }

            int repeats = repeatFirst == null ? 0 : repeatFirst.Length;
            _repeatCount = new int[repeats];
            for (int r = 0; r < repeats; r++) _repeatCount[r] = repeatMax[r];

            int selects = selectElem == null ? 0 : selectElem.Length;
            _selectValue = new int[selects];
            for (int s = 0; s < selects; s++)
            {
                _selectValue[s] = 0;
                if (elemObjects[selectPopupElem[s]] != null) elemObjects[selectPopupElem[s]].SetActive(false);
            }

            if (lang != null) lang.Register(this);
            _Retranslate();

            for (int s = 0; s < _signalCount; s++) ApplyState(s);
            for (int g = 0; g < groups; g++)
            {
                for (int p = 0; p < pageGroupCount[g]; p++) PageHideInstant(pageElem[pageGroupStart[g] + p]);
                SelectPage(g, pageGroupDefault[g], false);
            }
            for (int s = 0; s < selects; s++) RenderSelect(s);

            if (startHidden) HideInstant(); else ShowInstant();
            _AfterStart();
        }

        /// <summary>Generated code applies initial binding values here.</summary>
        protected virtual void _AfterStart()
        {
        }

        private bool Bad(int length, int n)
        {
            return length != n;
        }

        private bool Tweens()
        {
            if (_tweensCached) return _tweensOk;
            if (lang == null || !lang.ProbeDone()) return false; // not decided yet: instant, do not cache
            _tweensOk = lang.TweensAvailable();
            _tweensCached = true;
            return _tweensOk;
        }

        // ================================================================ visibility

        public void Show()
        {
            _seq++;
            if (content == null) return;
            content.SetActive(true);
            if (contentGroup != null) { contentGroup.blocksRaycasts = true; contentGroup.interactable = true; }
            _visible = true;
            Enter(contentGroup, contentRect, showKind, showDur, slideFrom, scaleFrom, contentRect != null ? BasePos(contentRect) : Vector2.zero, ref _fadeH, ref _scaleH, ref _slideH);
            ScheduleSnap(showDur);
            OnShown();
        }

        public void Hide()
        {
            _seq++;
            if (content == null || !_visible) return;
            if (contentGroup != null) { contentGroup.blocksRaycasts = false; contentGroup.interactable = false; }
            _visible = false;
            CloseSelect();
            ResetPointerStates();
            Exit(contentGroup, contentRect, showKind, hideDur, slideFrom, scaleFrom, contentRect != null ? BasePos(contentRect) : Vector2.zero, ref _fadeH, ref _scaleH, ref _slideH);
            ScheduleSnap(hideDur);
            OnHidden();
        }

        public void ShowInstant()
        {
            _seq++;
            if (content == null) return;
            KillViewTweens();
            content.SetActive(true);
            SnapShown(contentGroup, contentRect, contentRect != null ? BasePos(contentRect) : Vector2.zero);
            _visible = true;
            OnShown();
        }

        public void HideInstant()
        {
            _seq++;
            if (content == null) return;
            KillViewTweens();
            CloseSelect();
            ResetPointerStates();
            SnapHidden(contentGroup);
            content.SetActive(false);
            _visible = false;
            OnHidden();
        }

        public void Toggle()
        {
            if (_visible) Hide(); else Show();
        }

        public bool IsVisible()
        {
            return _visible;
        }

        protected virtual void OnShown()
        {
        }

        protected virtual void OnHidden()
        {
        }

        private void ScheduleSnap(float delay)
        {
            _snapSeq = _seq;
            SendCustomEventDelayedSeconds(nameof(_SnapView), delay + 0.02f);
        }

        /// <summary>Trails every view transition: the final state is applied whether or not the tween ran.</summary>
        public void _SnapView()
        {
            if (_snapSeq != _seq) return;
            KillViewTweens();
            if (content == null) return;
            if (_visible) SnapShown(contentGroup, contentRect, contentRect != null ? BasePos(contentRect) : Vector2.zero);
            else
            {
                SnapHidden(contentGroup);
                content.SetActive(false);
            }
        }

        private void KillViewTweens()
        {
            _fadeH.Kill();
            _scaleH.Kill();
            _slideH.Kill();
        }

        private Vector2 BasePos(RectTransform r)
        {
            // The content rect's baked position is its authoring position; the builder leaves it at zero.
            return Vector2.zero;
        }

        // ================================================================ transitions (shared primitives)

        private void Enter(CanvasGroup g, RectTransform r, int kind, float dur, Vector2 slide, float scale, Vector2 basePos,
                           ref VRCTweenHandle fadeH, ref VRCTweenHandle scaleH, ref VRCTweenHandle slideH)
        {
            fadeH.Kill(); scaleH.Kill(); slideH.Kill();
            if (kind == TRANS_NONE || !Tweens() || g == null)
            {
                SnapShown(g, r, basePos);
                return;
            }
            Vector2 from = SlideVector(kind, slide);
            g.alpha = 0f;
            fadeH = VRCTween.TweenFade(g, 1f, dur, VRCTweenEase.OutQuad);
            if (r != null && (kind == TRANS_FADE_SCALE || kind == TRANS_SCALE))
            {
                r.localScale = new Vector3(scale, scale, 1f);
                scaleH = VRCTween.TweenScale(r, Vector3.one, dur, VRCTweenEase.OutBack);
            }
            if (r != null && (kind == TRANS_FADE_SLIDE || kind >= TRANS_SLIDE_LEFT && kind <= TRANS_SLIDE_DOWN))
            {
                r.anchoredPosition = basePos + from;
                slideH = VRCTween.TweenAnchorPos(r, basePos, dur, VRCTweenEase.OutCubic);
            }
        }

        private void Exit(CanvasGroup g, RectTransform r, int kind, float dur, Vector2 slide, float scale, Vector2 basePos,
                          ref VRCTweenHandle fadeH, ref VRCTweenHandle scaleH, ref VRCTweenHandle slideH)
        {
            fadeH.Kill(); scaleH.Kill(); slideH.Kill();
            if (kind == TRANS_NONE || !Tweens() || g == null)
            {
                SnapHidden(g);
                return;
            }
            fadeH = VRCTween.TweenFade(g, 0f, dur, VRCTweenEase.InQuad);
            if (r != null && (kind == TRANS_FADE_SCALE || kind == TRANS_SCALE))
                scaleH = VRCTween.TweenScale(r, new Vector3(scale, scale, 1f), dur, VRCTweenEase.InQuad);
            if (r != null && (kind == TRANS_FADE_SLIDE || kind >= TRANS_SLIDE_LEFT && kind <= TRANS_SLIDE_DOWN))
                slideH = VRCTween.TweenAnchorPos(r, basePos + SlideVector(kind, slide), dur, VRCTweenEase.InCubic);
        }

        private Vector2 SlideVector(int kind, Vector2 configured)
        {
            if (kind == TRANS_SLIDE_LEFT) return new Vector2(40f, 0f);
            if (kind == TRANS_SLIDE_RIGHT) return new Vector2(-40f, 0f);
            if (kind == TRANS_SLIDE_UP) return new Vector2(0f, -40f);
            if (kind == TRANS_SLIDE_DOWN) return new Vector2(0f, 40f);
            return configured;
        }

        private void SnapShown(CanvasGroup g, RectTransform r, Vector2 basePos)
        {
            if (g != null) { g.alpha = 1f; g.blocksRaycasts = true; g.interactable = true; }
            if (r != null) { r.localScale = Vector3.one; r.anchoredPosition = basePos; }
        }

        private void SnapHidden(CanvasGroup g)
        {
            if (g != null) { g.alpha = 0f; g.blocksRaycasts = false; g.interactable = false; }
        }

        // ================================================================ signals

        /// <summary>Called by AuiSignal. Pointer kinds update state styles; click/value go to generated dispatch.</summary>
        public void _OnSignal(int id, int kind)
        {
            if (id < 0 || id >= _signalCount) return;
            if (kind == SIG_ENTER) _hover[id] = true;
            else if (kind == SIG_EXIT) { _hover[id] = false; _press[id] = false; }
            else if (kind == SIG_DOWN) _press[id] = true;
            else if (kind == SIG_UP) _press[id] = false;

            if (kind != SIG_CLICK && kind != SIG_VALUE)
            {
                ApplyState(id);
                _Dispatch(id, kind);
                return;
            }
            if (_disabled[id]) return;

            int e = sigElem[id];
            int k = elemKind != null && e < elemKind.Length ? elemKind[e] : KIND_NONE;
            if (kind == SIG_CLICK)
            {
                if (k == KIND_TOGGLE) { _toggleOn[e] = !_toggleOn[e]; SetSelected(id, _toggleOn[e]); }
                else if (k == KIND_TAB) { ClickTab(id); }
                else if (k == KIND_OPTION) { ClickOption(e); }
                else if (k == KIND_INPUT) { if (inputPanel != null) inputPanel.OpenText(_inputValue[e], this, e); }
                else if (k == KIND_SELECT) { ToggleSelect(e); }
                else if (k == KIND_SCRIM) { CloseSelect(); }
            }
            _Dispatch(id, kind);
        }

        /// <summary>Generated: maps a signal to the author's handler.</summary>
        protected virtual void _Dispatch(int id, int kind)
        {
        }

        private void ResetPointerStates()
        {
            for (int i = 0; i < _signalCount; i++)
            {
                if (!_hover[i] && !_press[i]) continue;
                _hover[i] = false;
                _press[i] = false;
                ApplyState(i);
            }
        }

        // ================================================================ state styles

        public bool GetToggle(int elem)
        {
            return elem >= 0 && elem < _toggleOn.Length && _toggleOn[elem];
        }

        public void SetToggle(int elem, bool on)
        {
            if (elem < 0 || elem >= _toggleOn.Length) return;
            _toggleOn[elem] = on;
            int id = elemSignal[elem];
            if (id >= 0) SetSelected(id, on);
        }

        public void SetSelected(int id, bool on)
        {
            if (id < 0 || id >= _signalCount) return;
            _selected[id] = on;
            ApplyState(id);
        }

        public bool IsSelected(int id)
        {
            return id >= 0 && id < _signalCount && _selected[id];
        }

        public void SetEnabled(int id, bool on)
        {
            if (id < 0 || id >= _signalCount) return;
            _disabled[id] = !on;
            int e = sigElem[id];
            if (elemButtons != null && elemButtons[e] != null) elemButtons[e].interactable = on;
            if (elemSliders != null && elemSliders[e] != null) elemSliders[e].interactable = on;
            ApplyState(id);
        }

        public void SetEnabledElem(int elem, bool on)
        {
            if (elem < 0 || elemSignal == null || elem >= elemSignal.Length) return;
            SetEnabled(elemSignal[elem], on);
        }

        public void SetActiveElem(int elem, bool on)
        {
            if (elemObjects == null || elem < 0 || elem >= elemObjects.Length || elemObjects[elem] == null) return;
            elemObjects[elem].SetActive(on);
        }

        private int ResolveState(int id)
        {
            if (_disabled[id]) return STATE_DISABLED;
            if (_press[id] && HasRows(id, STATE_PRESSED)) return STATE_PRESSED;
            if (_selected[id]) return STATE_SELECTED;
            if (_hover[id] && HasRows(id, STATE_HOVER)) return STATE_HOVER;
            return STATE_NORMAL;
        }

        private bool HasRows(int id, int state)
        {
            int r = id * STATE_COUNT + state;
            return styleRangeCount != null && r < styleRangeCount.Length && styleRangeCount[r] > 0;
        }

        protected void ApplyState(int id)
        {
            if (styleRangeStart == null || styleRangeStart.Length < (id + 1) * STATE_COUNT) return;
            int state = ResolveState(id);
            int e = sigElem[id];
            bool anim = Tweens();
            // Baseline first (normal rows), then the state's overrides on top. A property the
            // state does not mention returns to normal, which is what makes exit/up restore.
            ApplyRows(e, id * STATE_COUNT + STATE_NORMAL, anim);
            if (state != STATE_NORMAL) ApplyRows(e, id * STATE_COUNT + state, anim);
        }

        private void ApplyRows(int e, int range, bool anim)
        {
            int start = styleRangeStart[range];
            int count = styleRangeCount[range];
            for (int i = start; i < start + count; i++)
            {
                int ch = styleChannel[i];
                if (ch >= 0 && ch < _chan.Length) _chan[ch].Kill();
                float d = anim ? styleDur[i] : 0f;
                VRCTweenHandle h = ApplyProp(e, i, d);
                if (ch >= 0 && ch < _chan.Length) _chan[ch] = h;
            }
        }

        private VRCTweenHandle ApplyProp(int e, int row, float dur)
        {
            VRCTweenHandle none = new VRCTweenHandle();
            int prop = styleProp[row];
            Vector4 v = styleValue[row];
            VRCTweenEase ease = (VRCTweenEase)styleEase[row];
            if (prop == PROP_BG || prop == PROP_COLOR || prop == PROP_ICON || prop == PROP_BORDER)
            {
                Graphic g = prop == PROP_BG ? (Graphic)elemBg[e] : prop == PROP_COLOR ? (Graphic)elemText[e] : prop == PROP_ICON ? (Graphic)elemIcon[e] : (Graphic)elemBorder[e];
                if (g == null) return none;
                Color c = new Color(v.x, v.y, v.z, v.w);
                if (dur <= 0f) { g.color = c; return none; }
                return VRCTween.TweenColor(g, c, dur, ease);
            }
            if (prop == PROP_OPACITY)
            {
                CanvasGroup cg = elemGroups[e];
                if (cg == null) return none;
                if (dur <= 0f) { cg.alpha = v.x; return none; }
                return VRCTween.TweenFade(cg, v.x, dur, ease);
            }
            if (prop == PROP_SCALE)
            {
                RectTransform r = elemRects[e];
                if (r == null) return none;
                Vector3 s = new Vector3(v.x, v.x, 1f);
                if (dur <= 0f) { r.localScale = s; return none; }
                return VRCTween.TweenScale(r, s, dur, ease);
            }
            if (prop == PROP_OFFSET)
            {
                RectTransform r = elemRects[e];
                if (r == null) return none;
                Vector2 p = elemBasePos[e] + new Vector2(v.x, -v.y);
                if (dur <= 0f) { r.anchoredPosition = p; return none; }
                return VRCTween.TweenAnchorPos(r, p, dur, ease);
            }
            if (prop == PROP_SPRITE)
            {
                if (elemBg[e] != null) elemBg[e].sprite = styleSprite[row];
            }
            return none;
        }

        // ================================================================ pages

        private void ClickTab(int id)
        {
            if (pageTabSig == null) return;
            for (int p = 0; p < pageTabSig.Length; p++)
            {
                if (pageTabSig[p] != id) continue;
                int g = GroupOfPage(p);
                if (g >= 0) SelectPage(g, p - pageGroupStart[g], true);
                return;
            }
        }

        private int GroupOfPage(int pageIndex)
        {
            for (int g = 0; g < pageGroupStart.Length; g++)
                if (pageIndex >= pageGroupStart[g] && pageIndex < pageGroupStart[g] + pageGroupCount[g]) return g;
            return -1;
        }

        public int GetPage(int group)
        {
            return group >= 0 && group < _pageCur.Length ? _pageCur[group] : -1;
        }

        /// <summary>Show one page of a group. <paramref name="notify"/> = call the generated hook (false when set from code).</summary>
        public void SelectPage(int group, int page, bool notify)
        {
            if (pageGroupStart == null || group < 0 || group >= pageGroupStart.Length) return;
            if (page < 0 || page >= pageGroupCount[group]) return;
            if (page == _pageCur[group]) return;
            int old = _pageCur[group];
            _pageCur[group] = page;
            _pageSeq[group]++;
            int start = pageGroupStart[group];
            if (old >= 0)
            {
                if (pageTabSig[start + old] >= 0) SetSelected(pageTabSig[start + old], false);
                PageExit(start + old);
            }
            if (pageTabSig[start + page] >= 0) SetSelected(pageTabSig[start + page], true);
            if (pageGroupMode[group] == PAGE_SEQUENTIAL && old >= 0 && Tweens())
            {
                _pagePending[group] = page;
                SendCustomEventDelayedSeconds(nameof(_PageEnterPending), pageDur);
            }
            else PageEnter(start + page);
            SendCustomEventDelayedSeconds(nameof(_PageSnap), pageDur + 0.02f);
            if (notify) _OnPageChanged(group, page);
        }

        /// <summary>Generated: forwards to the author's tabs onchange handler and updates the binding.</summary>
        protected virtual void _OnPageChanged(int group, int page)
        {
        }

        public void _PageEnterPending()
        {
            for (int g = 0; g < _pagePending.Length; g++)
            {
                int p = _pagePending[g];
                if (p < 0) continue;
                _pagePending[g] = -1;
                if (_pageCur[g] == p) PageEnter(pageGroupStart[g] + p);
            }
        }

        public void _PageSnap()
        {
            for (int g = 0; g < pageGroupStart.Length; g++)
            {
                if (_pagePending[g] >= 0) continue; // still waiting for the sequential enter
                int start = pageGroupStart[g];
                for (int p = 0; p < pageGroupCount[g]; p++)
                {
                    int e = pageElem[start + p];
                    bool cur = p == _pageCur[g];
                    if (cur) SnapShown(elemGroups[e], elemRects[e], elemBasePos[e]);
                    else PageHideInstant(e);
                }
            }
        }

        private void PageEnter(int pageIndex)
        {
            int e = pageElem[pageIndex];
            if (elemObjects[e] != null) elemObjects[e].SetActive(true);
            VRCTweenHandle a = new VRCTweenHandle(), b = new VRCTweenHandle(), c = new VRCTweenHandle();
            Enter(elemGroups[e], elemRects[e], pageKind[pageIndex], pageDur, new Vector2(0f, -24f), 0.96f, elemBasePos[e], ref a, ref b, ref c);
        }

        private void PageExit(int pageIndex)
        {
            int e = pageElem[pageIndex];
            if (elemGroups[e] != null) { elemGroups[e].blocksRaycasts = false; elemGroups[e].interactable = false; }
            VRCTweenHandle a = new VRCTweenHandle(), b = new VRCTweenHandle(), c = new VRCTweenHandle();
            Exit(elemGroups[e], elemRects[e], pageKind[pageIndex], pageDur, new Vector2(0f, -24f), 0.96f, elemBasePos[e], ref a, ref b, ref c);
        }

        private void PageHideInstant(int e)
        {
            if (elemObjects[e] != null) VRCTween.KillAllTweens(elemObjects[e]);
            SnapHidden(elemGroups[e]);
            if (elemRects[e] != null) { elemRects[e].localScale = Vector3.one; elemRects[e].anchoredPosition = elemBasePos[e]; }
            if (elemObjects[e] != null) elemObjects[e].SetActive(false);
        }

        // ================================================================ repeats

        public int GetRepeatCount(int repeat)
        {
            return repeat >= 0 && repeat < _repeatCount.Length ? _repeatCount[repeat] : 0;
        }

        public void SetRepeatCount(int repeat, int count)
        {
            if (repeatFirst == null || repeat < 0 || repeat >= repeatFirst.Length) return;
            int max = repeatMax[repeat];
            if (count < 0) count = 0;
            if (count > max) count = max;
            _repeatCount[repeat] = count;
            for (int i = 0; i < max; i++)
            {
                GameObject go = elemObjects[repeatFirst[repeat] + i * repeatStride[repeat]];
                if (go != null) go.SetActive(i < count);
            }
            if (repeatEmpty != null && repeatEmpty[repeat] != null) repeatEmpty[repeat].SetActive(count == 0);
        }

        /// <summary>Element index of a template node in a given row.</summary>
        public int RowElem(int repeat, int row, int offset)
        {
            return repeatFirst[repeat] + row * repeatStride[repeat] + offset;
        }

        // ================================================================ selects

        public int GetSelect(int select)
        {
            return select >= 0 && select < _selectValue.Length ? _selectValue[select] : -1;
        }

        /// <summary>Set the chosen option without firing the handler.</summary>
        public void SetSelect(int select, int index)
        {
            if (selectElem == null || select < 0 || select >= selectElem.Length) return;
            if (index < 0) index = 0;
            if (index >= selectOptionCount[select]) index = selectOptionCount[select] - 1;
            _selectValue[select] = index;
            RenderSelect(select);
        }

        private void RenderSelect(int select)
        {
            int header = selectElem[select];
            int first = selectOptionFirst[select];
            for (int i = 0; i < selectOptionCount[select]; i++)
            {
                int oe = first + i;
                int sid = elemSignal[oe];
                if (sid >= 0) SetSelected(sid, i == _selectValue[select]);
            }
            if (selectOptionCount[select] <= 0) return;
            int chosen = first + _selectValue[select];
            if (elemText[header] != null && elemText[chosen] != null) elemText[header].text = elemText[chosen].text;
        }

        public void SetSelectOptionLabel(int select, int index, string label)
        {
            if (selectElem == null || select < 0 || select >= selectElem.Length) return;
            if (index < 0 || index >= selectOptionCount[select]) return;
            int oe = selectOptionFirst[select] + index;
            if (elemText[oe] != null) elemText[oe].text = Escape(label);
            RenderSelect(select);
        }

        public void SetSelectOptionCount(int select, int count)
        {
            if (selectElem == null || select < 0 || select >= selectElem.Length) return;
            int first = selectOptionFirst[select];
            for (int i = 0; i < selectOptionCount[select]; i++)
                if (elemObjects[first + i] != null) elemObjects[first + i].SetActive(i < count);
        }

        private void ToggleSelect(int headerElem)
        {
            int s = SelectOfHeader(headerElem);
            if (s < 0) return;
            if (_openSelect == s) { CloseSelect(); return; }
            CloseSelect();
            _openSelect = s;
            _selectSeq++;
            int pe = selectPopupElem[s];
            if (elemObjects[pe] != null) elemObjects[pe].SetActive(true);
            VRCTweenHandle a = new VRCTweenHandle(), b = new VRCTweenHandle(), c = new VRCTweenHandle();
            Enter(elemGroups[pe], elemRects[pe], TRANS_FADE_SLIDE, 0.12f, new Vector2(0f, 8f), 1f, elemBasePos[pe], ref a, ref b, ref c);
        }

        public void CloseSelect()
        {
            if (_openSelect < 0) return;
            int pe = selectPopupElem[_openSelect];
            _openSelect = -1;
            _selectSeq++;
            if (elemObjects[pe] != null) VRCTween.KillAllTweens(elemObjects[pe]);
            SnapHidden(elemGroups[pe]);
            if (elemRects[pe] != null) elemRects[pe].anchoredPosition = elemBasePos[pe];
            if (elemObjects[pe] != null) elemObjects[pe].SetActive(false);
        }

        private int SelectOfHeader(int headerElem)
        {
            for (int s = 0; s < selectElem.Length; s++) if (selectElem[s] == headerElem) return s;
            return -1;
        }

        private void ClickOption(int optionElem)
        {
            for (int s = 0; s < selectElem.Length; s++)
            {
                int first = selectOptionFirst[s];
                if (optionElem < first || optionElem >= first + selectOptionCount[s]) continue;
                _selectValue[s] = optionElem - first;
                RenderSelect(s);
                CloseSelect();
                _OnSelectChanged(s, _selectValue[s]);
                return;
            }
        }

        /// <summary>Generated: forwards to the author's select onchange handler and updates the binding.</summary>
        protected virtual void _OnSelectChanged(int select, int index)
        {
        }

        // ================================================================ input

        /// <summary>AuiInputPanel writes inputResult / inputResultId then calls this.</summary>
        public virtual void _OnInputResult()
        {
        }

        public virtual void _OnInputCancelled()
        {
        }

        /// <summary>Shows a bound value in an input box, or its translated placeholder when empty.</summary>
        protected void RenderInput(int e, string value)
        {
            if (value == null) value = "";
            _inputValue[e] = value;
            if (elemText[e] == null) return;
            if (value.Length == 0) elemText[e].text = elemKey[e] >= 0 && lang != null ? lang.Get(elemKey[e]) : "";
            else elemText[e].text = value;
        }

        // ================================================================ nesting

        /// <summary>Author code in a nested view raises a named event; the parent's on-&lt;name&gt; handler runs.</summary>
        protected void Emit(string eventName)
        {
            if (parentView != null) parentView._OnChildEvent(this, eventName);
        }

        /// <summary>Generated in the parent: routes a child's Emit to the author's handler.</summary>
        public virtual void _OnChildEvent(AuiView child, string eventName)
        {
        }

        // ================================================================ i18n

        /// <summary>Repaints every keyed text. Generated overrides re-render texts with placeholders after calling base.</summary>
        public virtual void _Retranslate()
        {
            if (lang == null || elemKey == null || elemText == null) return;
            for (int e = 0; e < elemKey.Length; e++)
            {
                if (elemKey[e] < 0 || elemText[e] == null) continue;
                int k = elemKind != null ? elemKind[e] : KIND_NONE;
                if (k == KIND_INPUT || k == KIND_SLIDER) continue; // rendered by generated code with their value
                elemText[e].text = lang.Get(elemKey[e]);
            }
            if (selectElem != null && _selectValue != null)
                for (int s = 0; s < selectElem.Length; s++) RenderSelect(s);
        }

        /// <summary>Insert a zero-width space after every '&lt;' so untrusted text can never open a rich-text tag.
        /// TextMeshProUGUI.richText is not exposed to Udon, and &lt;noparse&gt; can be closed from inside.</summary>
        public string Escape(string s)
        {
            if (s == null) return "";
            if (s.IndexOf("<", 0) < 0) return s;
            return s.Replace("<", "<​");
        }

        public string Fill1(string t, string n0, string v0)
        {
            return t.Replace(n0, v0);
        }

        public string Fill2(string t, string n0, string v0, string n1, string v1)
        {
            return t.Replace(n0, v0).Replace(n1, v1);
        }

        public string Fill3(string t, string n0, string v0, string n1, string v1, string n2, string v2)
        {
            return t.Replace(n0, v0).Replace(n1, v1).Replace(n2, v2);
        }

        public string Fill4(string t, string n0, string v0, string n1, string v1, string n2, string v2, string n3, string v3)
        {
            return t.Replace(n0, v0).Replace(n1, v1).Replace(n2, v2).Replace(n3, v3);
        }

        public string FormatNumber(float v, int decimals)
        {
            if (decimals <= 0) return Mathf.RoundToInt(v).ToString();
            if (decimals == 1) return v.ToString("0.0");
            if (decimals == 2) return v.ToString("0.00");
            return v.ToString("0.000");
        }

        private void OnDestroy()
        {
            if (content != null) VRCTween.KillAllTweens(content);
            KillViewTweens();
        }
    }
}
