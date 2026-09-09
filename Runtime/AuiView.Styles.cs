using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDK3.Components;

namespace Armagan.AUI
{
    /// <summary>
    /// The runtime cascade: the stylesheet rules baked as flat tables (Core/Resolve/
    /// AuiRuleTable.cs, whose constants this file mirrors) are re-resolved in-world, so a class
    /// added or removed at runtime, or bound through class="{x}", restyles the element and its
    /// descendants exactly as the compiler would have.
    ///
    /// RESOLUTION IS A FORWARD WALK. Rules come pre-sorted by tier, specificity and order; an
    /// element inherits the inheritable properties from its parent, then every matching rule of
    /// its tag bucket overwrites, so the last match wins without sorting. State rules (:hover
    /// and friends) are collected per element and applied on top by ApplyState: baseline the
    /// properties any state touches, then the matching state's declarations.
    ///
    /// CLASSES ARE BIT MASKS. 64 classes per view, two ints per element: the static mask from
    /// the markup, one mask per class="{binding}" slot, an API mask (AddClass), and a removed
    /// mask (RemoveClass of a static or bound class), combined into the effective mask.
    ///
    /// NOTHING REPAINTS AT START. The prefab is baked with the resolved look; the first
    /// resolution only fills the caches the state machine and later class changes read.
    /// </summary>
    public partial class AuiView
    {
        // ---- mirrors of AuiRuleTable
        public const int PROP_MATERIAL = 8, PROP_FONT_SIZE = 9, PROP_FONT = 10, PROP_TEXT_ALIGN = 11, PROP_LINE_HEIGHT = 12, PROP_DISPLAY = 13, PROP_POSITION = 14,
            PROP_WIDTH = 15, PROP_HEIGHT = 16, PROP_MIN_WIDTH = 17, PROP_MIN_HEIGHT = 18, PROP_MAX_WIDTH = 19, PROP_MAX_HEIGHT = 20, PROP_ANCHOR = 21,
            PROP_PADDING = 22, PROP_MARGIN = 23, PROP_GAP = 24, PROP_FLEX_DIRECTION = 25, PROP_JUSTIFY = 26, PROP_ALIGN = 27, PROP_GROW = 28, PROP_WRAP = 29,
            PROP_TOP = 30, PROP_RIGHT = 31, PROP_BOTTOM = 32, PROP_LEFT = 33, PROP_TRANSITION = 34;
        public const int PROP_COUNT = 35;
        public const int ANIM_COUNT = 7;
        private const int MaxStateRules = 16;

        [Header("Cascade tables (baked by the AUI builder; see AuiRuleTable)")]
        [HideInInspector] public string[] classNames;
        [HideInInspector] public string[] tagNames;
        [HideInInspector] public int[] ruleTag, ruleClass, ruleNode, ruleState, ruleDeclStart, ruleDeclCount;
        [HideInInspector] public int[] declProp, declRef;
        [HideInInspector] public Vector4[] declValue;
        [HideInInspector] public Sprite[] declSprite;
        [HideInInspector] public Material[] declMaterial;
        [HideInInspector] public int[] bucketStart, bucketCount, bucketRules;
        [HideInInspector] public int[] tsStart, tsCount, tsProp, tsEase;
        [HideInInspector] public float[] tsDur;
        [HideInInspector] public int[] inheritedProps;
        [HideInInspector] public int[] elemTag, elemNode, elemSubtreeEnd, elemClass0, elemClass1;
        [HideInInspector] public int classSlots = 1;
        [HideInInspector] public string[] starNames;   // referenced *classes, with the star
        [HideInInspector] public int[] ruleStar;       // per rule: star class id or -1
        [HideInInspector] public int[] elemStar;       // per element: STAR_WORDS words of static star classes
        public const int STAR_WORDS = 8, STAR_BASE = 1000;

        // ---- star class masks (same roles as the regular ones, STAR_WORDS words each)
        private int[] _starApi, _starRemoved, _starDyn, _starEff;
        // ---- style="prop: {binding}" overrides: highest tier, applied after the rules
        private Vector4[] _ov;
        private bool[] _ovSet;
        private float[] _ovRaw;     // [e * PROP_COUNT * 4 + p * 4 + i]: raw components of a bound box/offset
        private int[] _ovCount;     // components written so far, for box expansion

        // ---- runtime
        private bool _stylesReady;
        private Vector4[] _resolved;        // [e * PROP_COUNT + p]
        private int[] _resolvedRef;
        private bool[] _resolvedSet;
        // Where each resolved value came from: a declaration index, or one of the SRC_ markers. A
        // variable change rewrites declarations in place, so a property whose source declaration
        // changed (or whose parent's inherited value did) is updated without walking the cascade.
        private int[] _resolvedSrc;
        private const int SRC_NONE = -1, SRC_INHERITED = -2, SRC_OVERRIDE = -3, SRC_DIRECT = -4;
        private int[] _stateRules;          // [e * MaxStateRules + i]
        private int[] _stateRuleCount;
        private int[] _api0, _api1, _removed0, _removed1, _dyn0, _dyn1, _eff0, _eff1;
        private bool[] _cssHidden;
        private bool[] _touched;            // scratch, PROP_COUNT
        private Vector4[] _oldVal;          // scratch, PROP_COUNT
        private int[] _oldRef;
        private bool[] _oldSet;
        private VRCTweenHandle[] _chan;     // [e * ANIM_COUNT + anim]
        private float[] _fontSize;          // last font size applied per element (TMP's getter is not exposed to Udon)

        private float EffectiveFontSize(int e)
        {
            int b = e * PROP_COUNT;
            return _resolvedSet[b + PROP_FONT_SIZE] ? _resolved[b + PROP_FONT_SIZE].x : _resolvedSet[b + PROP_FONT] ? _resolved[b + PROP_FONT].x : 18f;
        }

        // ================================================================ init

        private void _AuiStylesInit()
        {
            _stylesReady = false;
            int n = elemObjects == null ? 0 : elemObjects.Length;
            _chan = new VRCTweenHandle[Mathf.Max(1, n * ANIM_COUNT)];
            if (ruleTag == null || elemTag == null || elemTag.Length != n || elemSubtreeEnd == null || elemSubtreeEnd.Length != n) return;
            _resolved = new Vector4[n * PROP_COUNT];
            _resolvedRef = new int[n * PROP_COUNT];
            _resolvedSet = new bool[n * PROP_COUNT];
            _resolvedSrc = new int[n * PROP_COUNT];
            _stateRules = new int[n * MaxStateRules];
            _stateRuleCount = new int[n];
            int slots = classSlots > 0 ? classSlots : 1;
            _api0 = new int[n]; _api1 = new int[n]; _removed0 = new int[n]; _removed1 = new int[n];
            _dyn0 = new int[n * slots]; _dyn1 = new int[n * slots];
            _eff0 = new int[n]; _eff1 = new int[n];
            _cssHidden = new bool[n];
            _touched = new bool[PROP_COUNT];
            _oldVal = new Vector4[PROP_COUNT]; _oldRef = new int[PROP_COUNT]; _oldSet = new bool[PROP_COUNT];
            for (int e = 0; e < n; e++) { _eff0[e] = elemClass0[e]; _eff1[e] = elemClass1[e]; }
            _fontSize = new float[n];
            _starApi = new int[n * STAR_WORDS]; _starRemoved = new int[n * STAR_WORDS]; _starDyn = new int[n * slots * STAR_WORDS]; _starEff = new int[n * STAR_WORDS];
            if (elemStar != null && elemStar.Length == n * STAR_WORDS) for (int i = 0; i < elemStar.Length; i++) _starEff[i] = elemStar[i];
            _ov = new Vector4[n * PROP_COUNT]; _ovSet = new bool[n * PROP_COUNT]; _ovRaw = new float[n * PROP_COUNT * 4]; _ovCount = new int[n * PROP_COUNT];
            _stylesReady = true;
            for (int e = 0; e < n; e++) ResolveElem(e);
            for (int e = 0; e < n; e++)
            {
                _cssHidden[e] = IsSet(e, PROP_DISPLAY) && _resolved[e * PROP_COUNT + PROP_DISPLAY].x > 0.5f;
                _fontSize[e] = EffectiveFontSize(e);
            }
        }

        // ================================================================ classes

        /// <summary>Class id by name, or -1. A star class ("*p-8") gives STAR_BASE + its star id.</summary>
        public int ClassId(string name)
        {
            if (name == null) return -1;
            if (name.Length > 0 && name[0] == '*')
            {
                if (starNames == null) return -1;
                for (int i = 0; i < starNames.Length; i++) if (starNames[i] == name) return STAR_BASE + i;
                return -1;
            }
            if (classNames == null) return -1;
            for (int i = 0; i < classNames.Length; i++) if (classNames[i] == name) return i;
            return -1;
        }

        public bool HasClass(int elem, int classId)
        {
            if (!_stylesReady || elem < 0 || elem >= _eff0.Length || classId < 0) return false;
            if (classId >= STAR_BASE)
            {
                int s = classId - STAR_BASE;
                return s < 256 && (_starEff[elem * STAR_WORDS + (s >> 5)] & (1 << (s & 31))) != 0;
            }
            if (classId < 32) return (_eff0[elem] & (1 << classId)) != 0;
            if (classId < 64) return (_eff1[elem] & (1 << (classId - 32))) != 0;
            return false;
        }

        public bool HasClassNamed(int elem, string name) { return HasClass(elem, ClassId(name)); }

        public void AddClass(int elem, int classId)
        {
            if (!_started) { _auiStashI = elem; _auiStashI2 = classId; _AuiStart(); elem = _auiStashI; classId = _auiStashI2; }
            if (!_stylesReady || elem < 0 || elem >= _api0.Length || classId < 0) return;
            if (classId >= STAR_BASE)
            {
                int s = classId - STAR_BASE;
                if (s >= 256) return;
                int w = elem * STAR_WORDS + (s >> 5), bit = 1 << (s & 31);
                _starApi[w] |= bit; _starRemoved[w] &= ~bit;
                ClassesChanged(elem);
                return;
            }
            if (classId >= 64) return;
            if (classId < 32) { _api0[elem] |= 1 << classId; _removed0[elem] &= ~(1 << classId); }
            else { _api1[elem] |= 1 << (classId - 32); _removed1[elem] &= ~(1 << (classId - 32)); }
            ClassesChanged(elem);
        }

        public void RemoveClass(int elem, int classId)
        {
            if (!_started) { _auiStashI = elem; _auiStashI2 = classId; _AuiStart(); elem = _auiStashI; classId = _auiStashI2; }
            if (!_stylesReady || elem < 0 || elem >= _api0.Length || classId < 0) return;
            if (classId >= STAR_BASE)
            {
                int s = classId - STAR_BASE;
                if (s >= 256) return;
                int w = elem * STAR_WORDS + (s >> 5), bit = 1 << (s & 31);
                _starApi[w] &= ~bit; _starRemoved[w] |= bit;
                ClassesChanged(elem);
                return;
            }
            if (classId >= 64) return;
            if (classId < 32) { _api0[elem] &= ~(1 << classId); _removed0[elem] |= 1 << classId; }
            else { _api1[elem] &= ~(1 << (classId - 32)); _removed1[elem] |= 1 << (classId - 32); }
            ClassesChanged(elem);
        }

        public void ToggleClass(int elem, int classId)
        {
            if (HasClass(elem, classId)) RemoveClass(elem, classId); else AddClass(elem, classId);
        }

        public void AddClassNamed(int elem, string name) { AddClass(elem, ClassId(name)); }
        public void RemoveClassNamed(int elem, string name) { RemoveClass(elem, ClassId(name)); }
        public void ToggleClassNamed(int elem, string name) { ToggleClass(elem, ClassId(name)); }

        /// <summary>Replace the classes added through the API with a space-separated list (static and bound classes stay).</summary>
        private int[] _maskScratch = new int[STAR_WORDS];

        public void SetClasses(int elem, string names)
        {
            if (!_started) { _auiStashI = elem; _auiStashS = names; _AuiStart(); elem = _auiStashI; names = _auiStashS; }
            if (!_stylesReady || elem < 0 || elem >= _api0.Length) return;
            int m0 = 0, m1 = 0;
            for (int w = 0; w < STAR_WORDS; w++) _maskScratch[w] = 0;
            ParseMask(names, ref m0, ref m1, _maskScratch);
            _api0[elem] = m0; _api1[elem] = m1;
            _removed0[elem] &= ~m0; _removed1[elem] &= ~m1;
            for (int w = 0; w < STAR_WORDS; w++) { _starApi[elem * STAR_WORDS + w] = _maskScratch[w]; _starRemoved[elem * STAR_WORDS + w] &= ~_maskScratch[w]; }
            ClassesChanged(elem);
        }

        /// <summary>Generated code: one class="{binding}" token's current value.</summary>
        public void SetClassSlot(int elem, int slot, string names)
        {
            if (!_started) { _auiStashI = elem; _auiStashI2 = slot; _auiStashS = names; _AuiStart(); elem = _auiStashI; slot = _auiStashI2; names = _auiStashS; }
            if (!_stylesReady || elem < 0 || elem >= _api0.Length) return;
            int slots = classSlots > 0 ? classSlots : 1;
            if (slot < 0 || slot >= slots) return;
            int m0 = 0, m1 = 0;
            for (int w = 0; w < STAR_WORDS; w++) _maskScratch[w] = 0;
            ParseMask(names, ref m0, ref m1, _maskScratch);
            _dyn0[elem * slots + slot] = m0; _dyn1[elem * slots + slot] = m1;
            _removed0[elem] &= ~m0; _removed1[elem] &= ~m1;
            int db = (elem * slots + slot) * STAR_WORDS;
            for (int w = 0; w < STAR_WORDS; w++) { _starDyn[db + w] = _maskScratch[w]; _starRemoved[elem * STAR_WORDS + w] &= ~_maskScratch[w]; }
            ClassesChanged(elem);
        }

        private void ParseMask(string names, ref int m0, ref int m1, int[] star)
        {
            if (string.IsNullOrEmpty(names)) return;
            string[] toks = names.Split(' ');
            for (int i = 0; i < toks.Length; i++)
            {
                string t = toks[i].Trim();
                if (t.Length == 0) continue;
                int id = ClassId(t);
                if (id < 0) { WarnOnce("class:" + t, "class '" + t + "' is not known to this view (a *utility must be referenced in the markup or in <view utilities=\"...\">)."); continue; }
                if (id >= STAR_BASE) { int s = id - STAR_BASE; if (s < 256) star[s >> 5] |= 1 << (s & 31); continue; }
                if (id < 32) m0 |= 1 << id; else if (id < 64) m1 |= 1 << (id - 32);
            }
        }

        private void ClassesChanged(int elem)
        {
            int slots = classSlots > 0 ? classSlots : 1;
            int m0 = elemClass0[elem] | _api0[elem], m1 = elemClass1[elem] | _api1[elem];
            for (int s = 0; s < slots; s++) { m0 |= _dyn0[elem * slots + s]; m1 |= _dyn1[elem * slots + s]; }
            m0 &= ~_removed0[elem]; m1 &= ~_removed1[elem];
            bool changed = m0 != _eff0[elem] || m1 != _eff1[elem];
            _eff0[elem] = m0; _eff1[elem] = m1;
            for (int w = 0; w < STAR_WORDS; w++)
            {
                int sw = (elemStar != null && elemStar.Length > elem * STAR_WORDS + w ? elemStar[elem * STAR_WORDS + w] : 0) | _starApi[elem * STAR_WORDS + w];
                for (int s = 0; s < slots; s++) sw |= _starDyn[(elem * slots + s) * STAR_WORDS + w];
                sw &= ~_starRemoved[elem * STAR_WORDS + w];
                if (sw != _starEff[elem * STAR_WORDS + w]) changed = true;
                _starEff[elem * STAR_WORDS + w] = sw;
            }
            if (!changed) return;
            ReResolve(elem);
        }

        // ================================================================ style="prop: {binding}"

        /// <summary>Generated code: a float-typed bound style value (number, or one offset component).</summary>
        public void SetStyleBindingF(int elem, int prop, int index, float value)
        {
            if (!_started) { _auiStashI = elem; _auiStashI2 = prop; _auiStashF = value; _AuiStart(); elem = _auiStashI; prop = _auiStashI2; value = _auiStashF; }
            if (!_stylesReady || elem < 0 || elem >= elemTag.Length || prop < 0 || prop >= PROP_COUNT) return;
            int r = (elem * PROP_COUNT + prop) * 4;
            if (index < 0 || index > 3) index = 0;
            _ovRaw[r + index] = value;
            if (index + 1 > _ovCount[elem * PROP_COUNT + prop]) _ovCount[elem * PROP_COUNT + prop] = index + 1;
            CommitOverride(elem, prop);
        }

        /// <summary>Generated code: a colour-typed bound style value (color, icon-color, border, background).</summary>
        public void SetStyleBindingC(int elem, int prop, int index, Color value)
        {
            if (!_started) { _auiStashI = elem; _auiStashI2 = prop; _auiStashC = value; _AuiStart(); elem = _auiStashI; prop = _auiStashI2; value = _auiStashC; }
            if (!_stylesReady || elem < 0 || elem >= elemTag.Length || prop < 0 || prop >= PROP_COUNT) return;
            int r = (elem * PROP_COUNT + prop) * 4;
            _ovRaw[r] = value.r; _ovRaw[r + 1] = value.g; _ovRaw[r + 2] = value.b; _ovRaw[r + 3] = value.a;
            _ovCount[elem * PROP_COUNT + prop] = 4;
            CommitOverride(elem, prop);
        }

        /// <summary>Generated code: a string-typed bound style value: a length ("12", "50%", "auto"), one box edge, or an enum name.</summary>
        public void SetStyleBindingS(int elem, int prop, int index, string value)
        {
            if (!_started) { _auiStashI = elem; _auiStashI2 = prop; _auiStashS = value; _AuiStart(); elem = _auiStashI; prop = _auiStashI2; value = _auiStashS; }
            if (!_stylesReady || elem < 0 || elem >= elemTag.Length || prop < 0 || prop >= PROP_COUNT) return;
            int r = (elem * PROP_COUNT + prop) * 4;
            if (prop == PROP_PADDING || prop == PROP_MARGIN)
            {
                float mm = 0f; int kind = SIZE_MM;
                if (!ParseLength(value, ref mm, ref kind)) return;
                if (index < 0 || index > 3) index = 0;
                _ovRaw[r + index] = mm;
                if (index + 1 > _ovCount[elem * PROP_COUNT + prop]) _ovCount[elem * PROP_COUNT + prop] = index + 1;
            }
            else if (IsLengthProp(prop))
            {
                float mm = 0f; int kind = SIZE_MM;
                if (!ParseLength(value, ref mm, ref kind)) return;
                _ovRaw[r] = mm; _ovRaw[r + 1] = kind;
                _ovCount[elem * PROP_COUNT + prop] = 2;
            }
            else
            {
                int ord = EnumOrdinal(prop, value);
                if (ord < 0) { WarnOnce("enum:" + prop + ":" + value, "'" + value + "' is not a value of style property " + prop + "."); return; }
                _ovRaw[r] = ord;
                _ovCount[elem * PROP_COUNT + prop] = 1;
            }
            CommitOverride(elem, prop);
        }

        private static bool IsLengthProp(int prop)
        {
            return prop == PROP_FONT_SIZE || prop == PROP_GAP || (prop >= PROP_WIDTH && prop <= PROP_MAX_HEIGHT) || (prop >= PROP_TOP && prop <= PROP_LEFT);
        }

        /// <summary>"12", "-4", "12mm", "50%", "auto" -> (mm or percent value, SIZE_* kind). Mirrors Core's AuiLength.TryParse.</summary>
        public bool ParseLength(string s, ref float value, ref int kind)
        {
            if (s == null) return false;
            s = s.Trim();
            if (s.Length == 0) return false;
            if (s == "auto") { value = 0f; kind = SIZE_AUTO; return true; }
            kind = SIZE_MM;
            if (s.EndsWith("%")) { kind = SIZE_PCT; s = s.Substring(0, s.Length - 1); }
            else if (s.EndsWith("mm")) s = s.Substring(0, s.Length - 2);
            float f = 0f;
            if (!ParseFloatInvariant(s, ref f)) return false;
            value = f;
            return true;
        }

        /// <summary>A culture-free "-12.5" parser (the player's locale must not turn "0.5" into a failure).</summary>
        public bool ParseFloatInvariant(string s, ref float result)
        {
            if (s == null || s.Length == 0) return false;
            int i = 0;
            bool neg = false;
            if (s[0] == '-') { neg = true; i = 1; } else if (s[0] == '+') i = 1;
            float whole = 0f, frac = 0f, scale = 1f;
            bool any = false, dot = false;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '.') { if (dot) return false; dot = true; continue; }
                if (c < '0' || c > '9') return false;
                any = true;
                if (!dot) whole = whole * 10f + (c - '0');
                else { scale *= 0.1f; frac += (c - '0') * scale; }
            }
            if (!any) return false;
            result = neg ? -(whole + frac) : whole + frac;
            return true;
        }

        private int EnumOrdinal(int prop, string v)
        {
            if (v == null) return -1;
            v = v.Trim();
            if (prop == PROP_DISPLAY) return v == "none" ? 1 : v == "flex" ? 0 : -1;
            if (prop == PROP_POSITION) return v == "absolute" ? 1 : v == "static" ? 0 : -1;
            if (prop == PROP_FLEX_DIRECTION) return v == "row" ? 1 : v == "column" ? 0 : -1;
            if (prop == PROP_WRAP) return v == "true" ? 1 : v == "false" ? 0 : -1;
            if (prop == PROP_TEXT_ALIGN) return v == "left" ? 0 : v == "center" ? 1 : v == "right" ? 2 : -1;
            if (prop == PROP_JUSTIFY) return v == "start" ? JUSTIFY_START : v == "center" ? JUSTIFY_CENTER : v == "end" ? JUSTIFY_END : v == "space-between" ? JUSTIFY_BETWEEN : -1;
            if (prop == PROP_ALIGN) return v == "start" ? ALIGN_START : v == "center" ? ALIGN_CENTER : v == "end" ? ALIGN_END : v == "stretch" ? ALIGN_STRETCH : -1;
            if (prop == PROP_ANCHOR) return v == "none" ? ANCHOR_NONE : v == "fill" ? ANCHOR_FILL : v == "top" ? ANCHOR_TOP : v == "bottom" ? ANCHOR_BOTTOM : v == "left" ? ANCHOR_LEFT : v == "right" ? ANCHOR_RIGHT : v == "center" ? ANCHOR_CENTER : -1;
            return -1;
        }

        /// <summary>Turn the raw components into the property's Vector4 (box shorthand expansion), mark the override and re-resolve.</summary>
        private void CommitOverride(int elem, int prop)
        {
            int b = elem * PROP_COUNT + prop;
            int r = b * 4;
            int n = _ovCount[b];
            Vector4 v;
            if (prop == PROP_PADDING || prop == PROP_MARGIN)
            {
                float a = _ovRaw[r], c2 = _ovRaw[r + 1], c3 = _ovRaw[r + 2], c4 = _ovRaw[r + 3];
                if (n <= 1) v = new Vector4(a, a, a, a);
                else if (n == 2) v = new Vector4(a, c2, a, c2);
                else if (n == 3) v = new Vector4(a, c2, c3, c2);
                else v = new Vector4(a, c2, c3, c4);
            }
            else v = new Vector4(_ovRaw[r], _ovRaw[r + 1], _ovRaw[r + 2], _ovRaw[r + 3]);
            _ov[b] = v;
            _ovSet[b] = true;
            ReResolve(elem);
        }

        // ================================================================ resolution

        private bool IsSet(int e, int prop)
        {
            return _resolvedSet[e * PROP_COUNT + prop];
        }

        /// <summary>Resolve one element's normal-state values from its parent and the rule buckets; collect its state rules.</summary>
        private void ResolveElem(int e)
        {
            int b = e * PROP_COUNT;
            int p = layParent != null && e < layParent.Length ? layParent[e] : -1;
            for (int i = 0; i < PROP_COUNT; i++) { _resolvedSet[b + i] = false; _resolvedRef[b + i] = -1; _resolved[b + i] = Vector4.zero; _resolvedSrc[b + i] = SRC_NONE; }
            if (p >= 0)
                for (int k = 0; k < inheritedProps.Length; k++)
                {
                    int prop = inheritedProps[k];
                    int pb = p * PROP_COUNT + prop;
                    if (!_resolvedSet[pb]) continue;
                    _resolved[b + prop] = _resolved[pb]; _resolvedRef[b + prop] = _resolvedRef[pb]; _resolvedSet[b + prop] = true; _resolvedSrc[b + prop] = SRC_INHERITED;
                }
            _stateRuleCount[e] = 0;
            int tag = elemTag[e];
            if (tag < 0 || tag >= bucketStart.Length) return;
            int node = elemNode[e];
            int start = bucketStart[tag], count = bucketCount[tag];
            for (int i = start; i < start + count; i++)
            {
                int r = bucketRules[i];
                int cls = ruleClass[r];
                if (cls >= 0 && !HasClass(e, cls)) continue;
                int st = ruleStar != null && r < ruleStar.Length ? ruleStar[r] : -1;
                if (st >= 0 && (_starEff[e * STAR_WORDS + (st >> 5)] & (1 << (st & 31))) == 0) continue;
                if (ruleNode[r] >= 0 && ruleNode[r] != node) continue;
                if (ruleState[r] != STATE_NORMAL)
                {
                    int c = _stateRuleCount[e];
                    if (c < MaxStateRules) { _stateRules[e * MaxStateRules + c] = r; _stateRuleCount[e] = c + 1; }
                    continue;
                }
                int ds = ruleDeclStart[r], dc = ruleDeclCount[r];
                for (int k = ds; k < ds + dc; k++)
                {
                    int prop = declProp[k];
                    // The font shorthand resets font-size (mirrors AuiStyleResolver.ApplyRule): the
                    // FontSize getter prefers the longhand, so an inherited or earlier font-size
                    // would otherwise beat a later `font: 500 18` forever.
                    if (prop == PROP_FONT) { _resolvedSet[b + PROP_FONT_SIZE] = false; _resolvedRef[b + PROP_FONT_SIZE] = -1; _resolvedSrc[b + PROP_FONT_SIZE] = SRC_NONE; }
                    _resolved[b + prop] = declValue[k]; _resolvedRef[b + prop] = declRef[k]; _resolvedSet[b + prop] = true; _resolvedSrc[b + prop] = k;
                }
            }
            // style="prop: {binding}" values sit in the attribute tier: they win over every rule.
            if (_ovSet != null)
                for (int q = 0; q < PROP_COUNT; q++)
                    if (_ovSet[b + q]) { _resolved[b + q] = _ov[b + q]; _resolvedSet[b + q] = true; _resolvedSrc[b + q] = SRC_OVERRIDE; }
        }

        /// <summary>Re-resolve an element and its descendants, applying only what changed. Created template instances linked under it follow.</summary>
        private void ReResolve(int root)
        {
            if (!_stylesReady) return;
            ReResolveSubtree(root);
            // Instances created at runtime live outside root's contiguous subtree; re-resolve the ones linked below it.
            int reps = repeatFirst == null ? 0 : repeatFirst.Length;
            for (int r = 0; r < reps; r++)
            {
                if (!IsTemplate(r)) continue;
                for (int i = 0; i < repeatMax[r]; i++)
                {
                    if (!IsInstanceLive(r, i)) continue;
                    int inst = InstanceElem(r, i);
                    if (inst > root && !(inst >= root && inst < elemSubtreeEnd[root]) && DescendsFrom(inst, root)) ReResolveSubtree(inst);
                }
            }
        }

        private bool DescendsFrom(int e, int root)
        {
            for (int p = layParent[e]; p >= 0; p = layParent[p]) if (p == root) return true;
            return false;
        }

        private void ReResolveSubtree(int root)
        {
            ReResolveRange(root, elemSubtreeEnd[root]);
        }

        /// <summary>Every element, parked template instances and modal scrims included: a variable changed.</summary>
        private void ReResolveAll()
        {
            if (!_stylesReady) return;
            ReResolveRange(0, elemTag.Length);
        }

        private bool _instantResolve;   // set while _AuiStart applies the service's theme variables: no tween into the start look

        private void ReResolveRange(int start, int end)
        {
            bool anim = !_instantResolve && Tweens();
            for (int e = start; e < end; e++)
            {
                int b = e * PROP_COUNT;
                for (int i = 0; i < PROP_COUNT; i++) { _oldVal[i] = _resolved[b + i]; _oldRef[i] = _resolvedRef[b + i]; _oldSet[i] = _resolvedSet[b + i]; }
                ResolveElem(e);
                bool layoutChanged = false;
                for (int prop = 0; prop < PROP_COUNT; prop++)
                {
                    bool set = _resolvedSet[b + prop];
                    if (set == _oldSet[prop] && _resolvedRef[b + prop] == _oldRef[prop] && (!set || _resolved[b + prop] == _oldVal[prop])) continue;
                    if (prop == PROP_TRANSITION) continue;
                    Vector4 v = set ? _resolved[b + prop] : DefaultValue(e, prop);
                    int rf = set ? _resolvedRef[b + prop] : -1;
                    float dur = 0f; int ease = 0;
                    if (anim) TransitionFor(e, prop, ref dur, ref ease);
                    if (ApplyValue(e, prop, v, rf, dur, ease, set)) layoutChanged = true;
                }
                if (layoutChanged) MarkLayoutDirty();
                int id = elemSignal[e];
                if (id >= 0 && _stateRuleCount[e] > 0) ApplyState(id);
            }
        }

        /// <summary>What a property falls back to when no rule sets it any more.</summary>
        private Vector4 DefaultValue(int e, int prop)
        {
            if (prop == PROP_OPACITY || prop == PROP_SCALE) return new Vector4(1, 0, 0, 0);
            if (prop == PROP_COLOR) return new Vector4(1, 1, 1, 1);
            if (prop == PROP_FONT_SIZE || prop == PROP_FONT) return new Vector4(18, SIZE_MM, 0, 0);
            if (prop == PROP_ALIGN) return new Vector4(ALIGN_STRETCH, 0, 0, 0);
            return Vector4.zero;   // transparent colours, zero offsets/boxes, unset lengths, flex/static/none/start/column
        }

        /// <summary>Duration and ease for a property change on an element, from its resolved transition set.</summary>
        private void TransitionFor(int e, int prop, ref float dur, ref int ease)
        {
            dur = 0f; ease = 0;
            int b = e * PROP_COUNT + PROP_TRANSITION;
            if (!_resolvedSet[b]) return;
            int set = _resolvedRef[b];
            if (set < 0 || tsStart == null || set >= tsStart.Length) return;
            int start = tsStart[set], count = tsCount[set];
            for (int i = start; i < start + count; i++)
            {
                if (tsProp[i] == prop) { dur = tsDur[i]; ease = tsEase[i]; return; }
                if (tsProp[i] == -1) { dur = tsDur[i]; ease = tsEase[i]; }
            }
        }

        // ================================================================ apply

        /// <summary>Push one property value into the element. Returns true when the layout must re-solve.</summary>
        private bool ApplyValue(int e, int prop, Vector4 v, int rf, float dur, int easeId, bool set)
        {
            VRCTweenEase ease = (VRCTweenEase)easeId;
            int anim = prop >= PROP_BG && prop <= PROP_OFFSET ? prop : -1;
            if (anim >= 0)
            {
                int ch = e * ANIM_COUNT + anim;
                if (ch < _chan.Length) _chan[ch].Kill();
                VRCTweenHandle h = Tween(e, prop, v, dur, ease);
                if (ch < _chan.Length) _chan[ch] = h;
                return false;
            }
            if (prop == PROP_SPRITE)
            {
                if (elemBg[e] != null && rf >= 0 && declSprite != null && rf < declSprite.Length && declSprite[rf] != null) elemBg[e].sprite = declSprite[rf];
                return false;
            }
            if (prop == PROP_MATERIAL)
            {
                if (elemBg[e] != null && declMaterial != null && rf >= 0 && rf < declMaterial.Length) elemBg[e].material = declMaterial[rf];
                else if (elemBg[e] != null) elemBg[e].material = null;
                return false;
            }
            if (prop == PROP_FONT_SIZE || prop == PROP_FONT)
            {
                TextMeshProUGUI t = elemText[e];
                if (t == null) return false;
                float size = EffectiveFontSize(e);
                // TMP's fontSize getter is not exposed to Udon; the last applied size is tracked instead.
                if (Mathf.Abs(_fontSize[e] - size) < 0.01f) return false;
                _fontSize[e] = size;
                // The registry exposes these setters on TMP_Text, not on TextMeshProUGUI: cast.
                ((TMP_Text)t).fontSize = size;
                if (_layoutReady && layKind[e] == LK_TEXT) _textDirty[e] = true;
                return true;
            }
            if (prop == PROP_TEXT_ALIGN)
            {
                if (elemText[e] != null) ((TMP_Text)elemText[e]).alignment = v.x > 1.5f ? TextAlignmentOptions.Right : v.x > 0.5f ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
                return false;
            }
            if (prop == PROP_LINE_HEIGHT)
            {
                if (elemText[e] != null) ((TMP_Text)elemText[e]).lineSpacing = v.x > 0 ? (v.x - 1f) * 100f : 0f;
                return false;
            }
            if (prop == PROP_DISPLAY)
            {
                bool hidden = v.x > 0.5f;
                if (_cssHidden[e] == hidden) return false;
                _cssHidden[e] = hidden;
                if (elemObjects[e] != null) elemObjects[e].SetActive(!hidden && (_shown == null || _shown[e]));
                return true;
            }
            if (!_layoutReady) return false;
            // ---- layout inputs
            if (prop == PROP_POSITION || prop == PROP_ANCHOR)
            {
                if (prop == PROP_ANCHOR) layAnchor[e] = (int)v.x;
                int b = e * PROP_COUNT;
                bool abs = (_resolvedSet[b + PROP_POSITION] && _resolved[b + PROP_POSITION].x > 0.5f) || layAnchor[e] != ANCHOR_NONE;
                layFlags[e] = abs ? (layFlags[e] | LF_ABSOLUTE) : (layFlags[e] & ~LF_ABSOLUTE);
                return true;
            }
            if (prop >= PROP_WIDTH && prop <= PROP_MAX_HEIGHT)
            {
                int slot = 6 * e + (prop - PROP_WIDTH);   // WIDTH HEIGHT MIN_WIDTH MIN_HEIGHT MAX_WIDTH MAX_HEIGHT match SZ_ order
                laySizeKind[slot] = set ? (int)v.y : SIZE_UNSET;
                laySizeVal[slot] = v.x;
                return true;
            }
            if (prop >= PROP_TOP && prop <= PROP_LEFT)
            {
                int slot = 4 * e + (prop - PROP_TOP);
                layEdgeKind[slot] = set ? (int)v.y : SIZE_UNSET;
                layEdgeVal[slot] = v.x;
                return true;
            }
            if (prop == PROP_PADDING) { layPad[4 * e] = v.x; layPad[4 * e + 1] = v.y; layPad[4 * e + 2] = v.z; layPad[4 * e + 3] = v.w; return true; }
            if (prop == PROP_MARGIN) { layMargin[4 * e] = v.x; layMargin[4 * e + 1] = v.y; layMargin[4 * e + 2] = v.z; layMargin[4 * e + 3] = v.w; return true; }
            if (prop == PROP_GAP) { layGap[e] = v.x; return true; }
            if (prop == PROP_GROW) { layGrow[e] = Mathf.Max(0f, v.x); return true; }   // the bake clamps too; a negative grow would starve its siblings
            if (prop == PROP_JUSTIFY) { layJustify[e] = (int)v.x; return true; }
            if (prop == PROP_ALIGN) { layAlign[e] = (int)v.x; return true; }
            if (prop == PROP_FLEX_DIRECTION) { layFlags[e] = v.x > 0.5f ? (layFlags[e] | LF_ROW) : (layFlags[e] & ~LF_ROW); return true; }
            if (prop == PROP_WRAP) { layFlags[e] = v.x > 0.5f ? (layFlags[e] | LF_WRAP) : (layFlags[e] & ~LF_WRAP); return true; }
            return false;
        }

        /// <summary>The popup element of a select header, or -1.</summary>
        private int SelectPopupOf(int e)
        {
            if (selectElem == null) return -1;
            for (int s = 0; s < selectElem.Length; s++) if (selectElem[s] == e) return selectPopupElem[s];
            return -1;
        }

        private VRCTweenHandle Tween(int e, int prop, Vector4 v, float dur, VRCTweenEase ease)
        {
            VRCTweenHandle none = new VRCTweenHandle();
            if (prop == PROP_BG || prop == PROP_COLOR || prop == PROP_ICON || prop == PROP_BORDER)
            {
                Graphic g = prop == PROP_BG ? (Graphic)elemBg[e] : prop == PROP_COLOR ? (Graphic)elemText[e] : prop == PROP_ICON ? (Graphic)elemIcon[e] : (Graphic)elemBorder[e];
                Color c = new Color(v.x, v.y, v.z, v.w);
                // A slider's fill is its "color" too (the value label may not exist). Set, not tweened:
                // one channel per property, and the fill has no channel of its own.
                if (prop == PROP_COLOR && elemSliders[e] != null && elemSliders[e].fillRect != null)
                {
                    Image fill = elemSliders[e].fillRect.GetComponent<Image>();
                    if (fill != null) fill.color = c;
                }
                // A select's popup is baked with the header's background; it has no node, so no rule
                // reaches it: the header's background change is copied onto it, instantly.
                if (prop == PROP_BG && elemKind[e] == KIND_SELECT)
                {
                    int pe = SelectPopupOf(e);
                    if (pe >= 0 && elemBg[pe] != null) elemBg[pe].color = c;
                }
                if (g == null) return none;
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
            return none;
        }

        // ================================================================ states

        private bool HasStateRules(int e, int state)
        {
            if (!_stylesReady) return false;
            int c = _stateRuleCount[e];
            for (int i = 0; i < c; i++) if (ruleState[_stateRules[e * MaxStateRules + i]] == state) return true;
            return false;
        }

        /// <summary>Any state rule of the element mentions offset (its base position must retarget after a layout move).</summary>
        private bool HasOffsetState(int e)
        {
            if (!_stylesReady) return false;
            if (_resolvedSet[e * PROP_COUNT + PROP_OFFSET]) return true;
            int c = _stateRuleCount[e];
            for (int i = 0; i < c; i++)
            {
                int r = _stateRules[e * MaxStateRules + i];
                int ds = ruleDeclStart[r], dc = ruleDeclCount[r];
                for (int k = ds; k < ds + dc; k++) if (declProp[k] == PROP_OFFSET) return true;
            }
            return false;
        }

        /// <summary>Apply the element's current pointer state: baseline every property any state touches, then the state's declarations.</summary>
        protected void ApplyState(int id)
        {
            if (!_stylesReady || id < 0 || id >= _signalCount) return;
            int e = sigElem[id];
            int state = ResolveState(id);
            bool anim = Tweens();
            int c = _stateRuleCount[e];
            if (c == 0) return;
            for (int p = 0; p < PROP_COUNT; p++) _touched[p] = false;
            for (int i = 0; i < c; i++)
            {
                int r = _stateRules[e * MaxStateRules + i];
                int ds = ruleDeclStart[r], dc = ruleDeclCount[r];
                for (int k = ds; k < ds + dc; k++) _touched[declProp[k]] = true;
            }
            int b = e * PROP_COUNT;
            for (int p = 0; p < PROP_COUNT; p++)
            {
                if (!_touched[p]) continue;
                float dur = 0f; int ease = 0;
                if (anim) TransitionFor(e, p, ref dur, ref ease);
                bool set = _resolvedSet[b + p];
                ApplyValue(e, p, set ? _resolved[b + p] : DefaultValue(e, p), set ? _resolvedRef[b + p] : -1, dur, ease, set);
            }
            if (state == STATE_NORMAL) return;
            for (int i = 0; i < c; i++)
            {
                int r = _stateRules[e * MaxStateRules + i];
                if (ruleState[r] != state) continue;
                int ds = ruleDeclStart[r], dc = ruleDeclCount[r];
                for (int k = ds; k < ds + dc; k++)
                {
                    float dur = 0f; int ease = 0;
                    if (anim) TransitionFor(e, declProp[k], ref dur, ref ease);
                    ApplyValue(e, declProp[k], declValue[k], declRef[k], dur, ease, true);
                }
            }
        }

        private int ResolveState(int id)
        {
            int e = sigElem[id];
            if (_disabled[id]) return STATE_DISABLED;
            if (_press[id] && HasStateRules(e, STATE_PRESSED)) return STATE_PRESSED;
            if (_selected[id]) return STATE_SELECTED;
            if (_hover[id] && HasStateRules(e, STATE_HOVER)) return STATE_HOVER;
            return STATE_NORMAL;
        }

        // ================================================================ element style ops

        /// <summary>Set one style property from code (the same ids as the cascade); it stays until the next class change on the element.</summary>
        public void SetStyleValue(int elem, int prop, Vector4 value)
        {
            if (!_started) { _auiStashI = elem; _auiStashI2 = prop; _AuiStart(); elem = _auiStashI; prop = _auiStashI2; }
            if (!_stylesReady || elem < 0 || elem >= elemTag.Length || prop < 0 || prop >= PROP_COUNT) return;
            int b = elem * PROP_COUNT + prop;
            _resolved[b] = value; _resolvedSet[b] = true; _resolvedSrc[b] = SRC_DIRECT;
            float dur = 0f; int ease = 0;
            if (Tweens()) TransitionFor(elem, prop, ref dur, ref ease);
            if (ApplyValue(elem, prop, value, _resolvedRef[b], dur, ease, true)) MarkLayoutDirty();
        }

        public void SetOpacity(int elem, float alpha) { SetStyleValue(elem, PROP_OPACITY, new Vector4(alpha, 0, 0, 0)); }
        public void SetColor(int elem, Color c) { SetStyleValue(elem, PROP_COLOR, new Vector4(c.r, c.g, c.b, c.a)); }
        public void SetBackgroundColor(int elem, Color c) { SetStyleValue(elem, PROP_BG, new Vector4(c.r, c.g, c.b, c.a)); }
        public void SetIconColor(int elem, Color c) { SetStyleValue(elem, PROP_ICON, new Vector4(c.r, c.g, c.b, c.a)); }
        public void SetScale(int elem, float scale) { SetStyleValue(elem, PROP_SCALE, new Vector4(scale, 0, 0, 0)); }
        public void SetFontSize(int elem, float size) { SetStyleValue(elem, PROP_FONT_SIZE, new Vector4(size, SIZE_MM, 0, 0)); }
        public void SetTextAlign(int elem, int align) { SetStyleValue(elem, PROP_TEXT_ALIGN, new Vector4(align, 0, 0, 0)); }
    }
}
