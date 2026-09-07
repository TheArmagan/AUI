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
    public partial class AuiView : UdonSharpBehaviour
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
        [HideInInspector] public AuiLanguageService lang;
        [HideInInspector] public AuiInputPanel inputPanel;
        [HideInInspector] public GameObject content;
        [HideInInspector] public CanvasGroup contentGroup;
        [HideInInspector] public RectTransform contentRect;
        [HideInInspector] public string auiBakeHash;

        [Header("Element tables, index = runtime element index")]
        [HideInInspector] public GameObject[] elemObjects;
        [HideInInspector] public RectTransform[] elemRects;
        [HideInInspector] public CanvasGroup[] elemGroups;
        [HideInInspector] public Image[] elemBg;
        [HideInInspector] public Image[] elemBorder;
        [HideInInspector] public Image[] elemIcon;
        [HideInInspector] public TextMeshProUGUI[] elemText;
        [HideInInspector] public Button[] elemButtons;
        [HideInInspector] public Slider[] elemSliders;
        [HideInInspector] public Vector2[] elemBasePos;
        [HideInInspector] public int[] elemSignal;   // signal id or -1
        [HideInInspector] public int[] elemKey;      // translation key index or -1 (text label, input placeholder, slider format)
        [HideInInspector] public int[] elemKind;     // KIND_*

        [Header("Signals")]
        [HideInInspector] public int[] sigElem;
        [HideInInspector] public AuiSignal[] signals;

        [Header("View transition")]
        [HideInInspector] public int showKind = TRANS_FADE_SCALE;
        [HideInInspector] public float showDur = 0.18f;
        [HideInInspector] public float hideDur = 0.14f;
        [HideInInspector] public Vector2 slideFrom = new Vector2(0f, -24f);
        [HideInInspector] public float scaleFrom = 0.94f;
        [HideInInspector] public bool startHidden;

        [Header("Pages")]
        [HideInInspector] public int[] pageGroupStart;   // index into pageElem
        [HideInInspector] public int[] pageGroupCount;
        [HideInInspector] public int[] pageGroupMode;
        [HideInInspector] public int[] pageGroupDefault;
        [HideInInspector] public int[] pageElem;         // element index of each page root
        [HideInInspector] public int[] pageTabSig;       // signal id of the tab for each page, or -1
        [HideInInspector] public int[] pageKind;         // TRANS_* per page
        [HideInInspector] public float pageDur = 0.18f;

        [Header("Repeats")]
        [HideInInspector] public int[] repeatFirst;      // element index of instance 0's root
        [HideInInspector] public int[] repeatStride;     // elements per instance
        [HideInInspector] public int[] repeatMax;
        [HideInInspector] public GameObject[] repeatEmpty;

        [Header("Selects")]
        [HideInInspector] public int[] selectElem;       // header element (button)
        [HideInInspector] public int[] selectPopupElem;  // popup root element (has CanvasGroup)
        [HideInInspector] public int[] selectOptionFirst;
        [HideInInspector] public int[] selectOptionCount;
        [HideInInspector] public int[] selectScrimSig;

        [Header("Written by AuiInputPanel")]
        [HideInInspector] public string inputResult;
        [HideInInspector] public int inputResultId = -1;

        [Header("Nesting (baked)")]
        [Tooltip("The view this one is embedded in, if any. Emit() reports to it.")]
        [HideInInspector] public AuiView parentView;

        // ---- runtime
        private bool[] _hover, _press, _selected, _disabled, _toggleOn;
        private VRCTweenHandle _fadeH, _scaleH, _slideH;
        private int _seq, _snapSeq;
        private bool _visible;
        /// <summary>Set at the top of _AuiStart; generated setters read it to park a pre-start write.</summary>
        protected bool _started;
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
            _AuiLayoutInit();
            _AuiStylesInit();
            _AuiModalsInit();
            _AuiCreateInit();

            // A PREFAB CANNOT REFERENCE SCENE OBJECTS, so a view placed from its prefab arrives with
            // lang and inputPanel null. Both live on well-known, always-active objects; Find once.
            if (lang == null)
            {
                GameObject svc = GameObject.Find("AUI Language Service");
                if (svc != null) lang = svc.GetComponent<AuiLanguageService>();
            }
            if (inputPanel == null)
            {
                GameObject ip = GameObject.Find("AUI Input Panel");
                if (ip != null) inputPanel = ip.GetComponent<AuiInputPanel>();
            }
            if (lang != null)
            {
                lang.Register(this);
                // Theme variables set through the service before this view started: apply them now, instantly.
                int vc = lang.StyleVariableCount();
                _instantResolve = true;
                for (int k = 0; k < vc; k++) _ApplyStyleVariable(lang.StyleVariableName(k), lang.StyleVariableValue(k));
                _instantResolve = false;
            }
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
            OnStart();
            // The prefab is baked for the widest translation; the first solve lays the current
            // language and the initial binding values out before the first frame is drawn.
            if (_layoutReady)
            {
                _layoutDirty = true;
                _AuiSolveLayout();
            }
        }

        /// <summary>
        /// A view on a non-default page (or any inactive object) has not had its Start yet when a
        /// parent writes to it, and its runtime arrays are still null. Every public entry and every
        /// generated setter starts the view first; _AuiStart is idempotent, so the later Start is a no-op.
        ///
        /// UDON HAS NO CALL STACK FOR PARAMETERS. A method's arguments live in heap slots, so when
        /// _AuiStart runs OnStart and OnStart writes the same property, the outer call's `value`
        /// is overwritten by the inner one. The EnsureStartedX helpers park the outer value in a
        /// field before starting and hand it back after; the field is only written while the view
        /// is not yet started, which is exactly once, so the inner calls cannot touch it. The
        /// outcome is the intuitive one: a write made before Start survives OnStart's defaults.
        /// </summary>
        public void EnsureStarted()
        {
            if (!_started) _AuiStart();
        }

        protected int _auiStashI, _auiStashI2;
        protected bool _auiStashB;
        protected float _auiStashF;
        protected string _auiStashS, _auiStashS2;
        protected Color _auiStashC;

        protected string EnsureStartedS(string v) { if (_started) return v; _auiStashS = v; _AuiStart(); return _auiStashS; }
        protected Color EnsureStartedC(Color v) { if (_started) return v; _auiStashC = v; _AuiStart(); return _auiStashC; }
        protected int EnsureStartedI(int v) { if (_started) return v; _auiStashI = v; _AuiStart(); return _auiStashI; }
        protected float EnsureStartedF(float v) { if (_started) return v; _auiStashF = v; _AuiStart(); return _auiStashF; }
        protected bool EnsureStartedB(bool v) { if (_started) return v; _auiStashB = v; _AuiStart(); return _auiStashB; }

        /// <summary>Generated code applies initial binding values here.</summary>
        protected virtual void _AfterStart()
        {
        }

        /// <summary>Author hook: runs once after the view is wired and its initial values applied.</summary>
        protected virtual void OnStart()
        {
        }

        /// <summary>Author hook: runs whenever the language changes (after the keyed texts repaint).</summary>
        protected virtual void OnRetranslate()
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
            EnsureStarted();
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
            EnsureStarted();
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
            // Raycasts back on at the START of the enter: the previous Exit/SnapHidden switched them
            // off, and a popup that only tweens alpha would look open and take no clicks.
            g.blocksRaycasts = true;
            g.interactable = true;
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
            if (_broken || id < 0 || id >= _signalCount) return;
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
                else if (k == KIND_MODAL_SCRIM) { ClickModalScrim(e); }
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
            elem = EnsureStartedI(elem);
            return elem >= 0 && elem < _toggleOn.Length && _toggleOn[elem];
        }

        public void SetToggle(int elem, bool on)
        {
            if (!_started) { _auiStashI = elem; _auiStashB = on; _AuiStart(); elem = _auiStashI; on = _auiStashB; }
            if (elem < 0 || elem >= _toggleOn.Length) return;
            _toggleOn[elem] = on;
            int id = elemSignal[elem];
            if (id >= 0) SetSelected(id, on);
        }

        public void SetSelected(int id, bool on)
        {
            if (!_started) { _auiStashI = id; _auiStashB = on; _AuiStart(); id = _auiStashI; on = _auiStashB; }
            if (id < 0 || id >= _signalCount) return;
            _selected[id] = on;
            ApplyState(id);
        }

        public bool IsSelected(int id)
        {
            id = EnsureStartedI(id);
            return id >= 0 && id < _signalCount && _selected[id];
        }

        public void SetEnabled(int id, bool on)
        {
            if (!_started) { _auiStashI = id; _auiStashB = on; _AuiStart(); id = _auiStashI; on = _auiStashB; }
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
            _AuiLayoutShown(elem, on);
        }

        // State resolution and application live in AuiView.Styles.cs (runtime cascade).

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
            group = EnsureStartedI(group);
            return group >= 0 && group < _pageCur.Length ? _pageCur[group] : -1;
        }

        /// <summary>Show one page of a group. <paramref name="notify"/> = call the generated hook (false when set from code).</summary>
        public void SelectPage(int group, int page, bool notify)
        {
            if (!_started) { _auiStashI = group; _auiStashI2 = page; _auiStashB = notify; _AuiStart(); group = _auiStashI; page = _auiStashI2; notify = _auiStashB; }
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
            MarkLayoutDirty();   // texts on the page that changed while it was inactive get measured now
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
            repeat = EnsureStartedI(repeat);
            return repeat >= 0 && repeat < _repeatCount.Length ? _repeatCount[repeat] : 0;
        }

        public void SetRepeatCount(int repeat, int count)
        {
            if (!_started) { _auiStashI = repeat; _auiStashI2 = count; _AuiStart(); repeat = _auiStashI; count = _auiStashI2; }
            if (repeatFirst == null || repeat < 0 || repeat >= repeatFirst.Length) return;
            if (IsTemplate(repeat)) return;   // template instances are created one by one
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
            MarkLayoutDirty();
        }

        /// <summary>Element index of a template node in a given row.</summary>
        public int RowElem(int repeat, int row, int offset)
        {
            return repeatFirst[repeat] + row * repeatStride[repeat] + offset;
        }

        // ================================================================ selects

        public int GetSelect(int select)
        {
            select = EnsureStartedI(select);
            return select >= 0 && select < _selectValue.Length ? _selectValue[select] : -1;
        }

        /// <summary>Set the chosen option without firing the handler.</summary>
        public void SetSelect(int select, int index)
        {
            if (!_started) { _auiStashI = select; _auiStashI2 = index; _AuiStart(); select = _auiStashI; index = _auiStashI2; }
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
            if (!_started) { _auiStashI = select; _auiStashI2 = index; _auiStashS = label; _AuiStart(); select = _auiStashI; index = _auiStashI2; label = _auiStashS; }
            if (selectElem == null || select < 0 || select >= selectElem.Length) return;
            if (index < 0 || index >= selectOptionCount[select]) return;
            int oe = selectOptionFirst[select] + index;
            if (elemText[oe] != null) elemText[oe].text = Escape(label);
            RenderSelect(select);
        }

        public void SetSelectOptionCount(int select, int count)
        {
            if (!_started) { _auiStashI = select; _auiStashI2 = count; _AuiStart(); select = _auiStashI; count = _auiStashI2; }
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

        // PAYLOADS CROSS THE BEHAVIOUR BOUNDARY THROUGH PUBLIC SERIALIZED FIELDS (the same rule as
        // inputResult): the parent reads child.emitS after _OnChildEvent, so these must be exported.
        [HideInInspector] public string emitS;
        [HideInInspector] public int emitI;
        [HideInInspector] public float emitF;
        [HideInInspector] public bool emitB;

        /// <summary>Author code in a nested view raises a named event; the parent's on-&lt;name&gt; handler runs.</summary>
        protected void Emit(string eventName)
        {
            if (parentView != null) parentView._OnChildEvent(this, eventName);
        }

        /// <summary>Emit with a payload; the host's handler for an &lt;event name type="string"/&gt; receives it.</summary>
        protected void EmitString(string eventName, string value) { emitS = value; Emit(eventName); }
        protected void EmitInt(string eventName, int value) { emitI = value; Emit(eventName); }
        protected void EmitFloat(string eventName, float value) { emitF = value; Emit(eventName); }
        protected void EmitBool(string eventName, bool value) { emitB = value; Emit(eventName); }

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
            _AuiLayoutLanguageChanged();
            if (_started) OnRetranslate();
        }

        /// <summary>Insert a zero-width space after every '&lt;' so untrusted text can never open a rich-text tag.
        /// TextMeshProUGUI.richText is not exposed to Udon, and &lt;noparse&gt; can be closed from inside.</summary>
        public string Escape(string s)
        {
            if (s == null) return "";
            if (s.IndexOf("<", 0) < 0) return s;
            return s.Replace("<", "<​");
        }

        // A null template (a halted language service answers null across the behaviour boundary)
        // must render as nothing, not halt this view as well.
        public string Fill1(string t, string n0, string v0)
        {
            if (t == null) return "";
            return t.Replace(n0, v0 == null ? "" : v0);
        }

        public string Fill2(string t, string n0, string v0, string n1, string v1)
        {
            if (t == null) return "";
            return t.Replace(n0, v0 == null ? "" : v0).Replace(n1, v1 == null ? "" : v1);
        }

        public string Fill3(string t, string n0, string v0, string n1, string v1, string n2, string v2)
        {
            if (t == null) return "";
            return t.Replace(n0, v0 == null ? "" : v0).Replace(n1, v1 == null ? "" : v1).Replace(n2, v2 == null ? "" : v2);
        }

        public string Fill4(string t, string n0, string v0, string n1, string v1, string n2, string v2, string n3, string v3)
        {
            if (t == null) return "";
            return t.Replace(n0, v0 == null ? "" : v0).Replace(n1, v1 == null ? "" : v1).Replace(n2, v2 == null ? "" : v2).Replace(n3, v3 == null ? "" : v3);
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
