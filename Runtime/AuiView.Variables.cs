using UnityEngine;

namespace Armagan.AUI
{
    /// <summary>
    /// :root variables at runtime. The bake substitutes var() into every declaration, but a
    /// declaration built from a variable also keeps its raw text (declTemplate, baked from
    /// AuiRuleTable.DeclTemplate). SetStyleVariable rewrites the variable's value, re-substitutes
    /// and re-encodes those declarations in-world and re-resolves every element through the
    /// cascade, so a theme swap is one call per variable; the language service fans one call out
    /// to every registered view and repeats it for views that start later.
    ///
    /// Values are the stylesheet's own text ("#E0A86A", "rgba(224,168,106,0.5)", "12", "50%",
    /// "row"); a value that does not parse for a property leaves that declaration as it was and
    /// logs once. Elements written through SetStyleValue are re-resolved like a class change would.
    /// </summary>
    public partial class AuiView
    {
        [Header("Root variables (baked by the AUI builder; see AuiRuleTable)")]
        [HideInInspector] public string[] varNames;      // "--accent", sorted
        [HideInInspector] public string[] varValues;     // the current value per variable: baked, then whatever was set
        [HideInInspector] public string[] declTemplate;  // per declaration: the raw value when it uses var(), else ""

        private static string NormVarName(string name)
        {
            if (name == null) return "";
            name = name.Trim();
            return name.StartsWith("--") ? name : "--" + name;
        }

        /// <summary>Index of a :root variable ("accent" or "--accent"), or -1 when no sheet of this view defines it.</summary>
        public int StyleVariableId(string varName)
        {
            if (varNames == null) return -1;
            string n = NormVarName(varName);
            for (int i = 0; i < varNames.Length; i++) if (varNames[i] == n) return i;
            return -1;
        }

        /// <summary>The variable's current value (its stylesheet text), or null when unknown.</summary>
        public string GetStyleVariable(string varName)
        {
            int id = StyleVariableId(varName);
            return id < 0 || varValues == null || id >= varValues.Length ? null : varValues[id];
        }

        /// <summary>
        /// Set a :root variable for this view and its nested views. Every declaration that reads it
        /// (through var(--name) or var(--name, fallback)) is re-encoded on the next frame and the
        /// properties that came from those declarations are updated, through each element's own
        /// transition. Any number of writes in one frame cost one pass; FlushStyleVariables() applies
        /// them at once.
        /// </summary>
        public void SetStyleVariable(string varName, string value)
        {
            if (!_started) { _auiStashS = varName; _auiStashS2 = value; _AuiStart(); varName = _auiStashS; value = _auiStashS2; }
            if (StyleVariableId(varName) < 0)
                WarnOnce("var:" + varName, "no stylesheet of this view defines the variable '" + NormVarName(varName) + "'.");
            else
                _ApplyStyleVariable(varName, value);
            // Nested views bake their own copy of the theme: forward so the whole tree follows.
            if (elemChildView == null) return;
            for (int e = 0; e < elemChildView.Length; e++)
                if (elemChildView[e] != null) elemChildView[e].SetStyleVariable(varName, value);
        }

        /// <summary>A colour value, written as #RRGGBBAA.</summary>
        public void SetStyleVariableColor(string varName, Color c)
        {
            SetStyleVariable(varName, ColorToHex(c));
        }

        /// <summary>A number value (opacity, scale, a length in mm), written without a locale.</summary>
        public void SetStyleVariableFloat(string varName, float value)
        {
            SetStyleVariable(varName, FormatInvariant(value));
        }

        /// <summary>This view only, no warning for an unknown name: the language service's fan-out and the start-time catch-up.</summary>
        public void _ApplyStyleVariable(string varName, string value)
        {
            if (!_started) { _auiStashS = varName; _auiStashS2 = value; _AuiStart(); varName = _auiStashS; value = _auiStashS2; }
            int id = StyleVariableId(varName);
            if (id < 0 || varValues == null || id >= varValues.Length) return;
            if (value == null) value = "";
            if (varValues[id] == value) return;
            varValues[id] = value;
            if (!_stylesReady || declTemplate == null || declTemplate.Length != declValue.Length) return;
            // Coalesce: a preset writes ten variables and a slider writes one per frame; both cost one pass.
            if (_varChanged == null || _varChanged.Length != varNames.Length) _varChanged = new bool[varNames.Length];
            _varChanged[id] = true;
            _varsDirty = true;
            if (_instantResolve) return;   // _AuiStart flushes once after the service's list
            if (!_varFlushScheduled) { _varFlushScheduled = true; SendCustomEventDelayedFrames(nameof(_AuiFlushStyleVariables), 1); }
        }

        private bool _varsDirty, _varFlushScheduled;
        private bool[] _varChanged;         // per variable: written since the last flush
        private bool[] _declChanged;        // per declaration: rewritten by the last flush
        private bool[] _propChanged;        // per property: some changed declaration sets it
        private int[] _declVarStart, _declVarCount, _declVarIds;   // per declaration: the variables its template reads
        private float _lastFlushTime = -10f;
        /// <summary>Flushes closer together than this (a dragged slider) apply instantly; a lone write (a preset click) tweens.</summary>
        private const float FlushTweenGap = 0.25f;

        public void _AuiFlushStyleVariables() { _varFlushScheduled = false; FlushStyleVariables(); }

        /// <summary>Apply the variable writes made since the last flush now instead of on the next frame.</summary>
        public void FlushStyleVariables()
        {
            if (!_varsDirty || !_stylesReady) return;
            _varsDirty = false;
            bool changed = ReencodeVariableDecls();
            for (int i = 0; i < _varChanged.Length; i++) _varChanged[i] = false;
            if (!changed) return;
            float now = Time.time;
            ApplyChangedDecls(now - _lastFlushTime < FlushTweenGap);
            _lastFlushTime = now;
        }

        // ================================================================ re-encoding

        /// <summary>Which variables each templated declaration reads, so a flush only re-substitutes the declarations a changed variable touches.</summary>
        private void IndexDeclVars()
        {
            int n = declTemplate.Length;
            _declVarStart = new int[n]; _declVarCount = new int[n];
            int total = 0;
            for (int k = 0; k < n; k++) if (!string.IsNullOrEmpty(declTemplate[k])) total += CountVarRefs(declTemplate[k]);
            _declVarIds = new int[Mathf.Max(1, total)];
            int at = 0;
            for (int k = 0; k < n; k++)
            {
                _declVarStart[k] = at;
                string t = declTemplate[k];
                if (string.IsNullOrEmpty(t)) continue;
                int i = 0;
                while (i < t.Length)
                {
                    int open = t.IndexOf("var(", i);
                    if (open < 0) break;
                    int close = t.IndexOf(')', open);
                    if (close < 0) break;
                    string inner = t.Substring(open + 4, close - open - 4);
                    int comma = inner.IndexOf(',');
                    int id = StyleVariableId(comma >= 0 ? inner.Substring(0, comma) : inner);
                    if (id >= 0) _declVarIds[at++] = id;
                    i = close + 1;
                }
                _declVarCount[k] = at - _declVarStart[k];
            }
        }

        private int CountVarRefs(string t)
        {
            int c = 0, i = 0;
            while (i < t.Length) { int open = t.IndexOf("var(", i); if (open < 0) break; c++; i = open + 4; }
            return c;
        }

        private bool DeclReadsChangedVar(int k)
        {
            int s = _declVarStart[k], c = _declVarCount[k];
            for (int i = s; i < s + c; i++) if (_varChanged[_declVarIds[i]]) return true;
            return false;
        }

        /// <summary>Re-substitute and re-encode every declaration that reads a changed variable, marking the ones whose value changed. True when any did.</summary>
        private bool ReencodeVariableDecls()
        {
            if (_declChanged == null || _declChanged.Length != declValue.Length) _declChanged = new bool[declValue.Length];
            if (_propChanged == null) _propChanged = new bool[PROP_COUNT];
            if (_declVarStart == null || _declVarStart.Length != declTemplate.Length) IndexDeclVars();
            for (int p = 0; p < PROP_COUNT; p++) _propChanged[p] = false;
            bool changed = false;
            for (int k = 0; k < declTemplate.Length; k++)
            {
                _declChanged[k] = false;
                string t = declTemplate[k];
                if (string.IsNullOrEmpty(t) || !DeclReadsChangedVar(k)) continue;
                string v = SubstituteVars(t);
                Vector4 enc = Vector4.zero;
                if (!EncodeStyleValue(declProp[k], v, ref enc))
                {
                    WarnOnce("decl:" + k, "'" + v + "' (from '" + t + "') is not a value for style property " + declProp[k] + "; the declaration keeps its previous value.");
                    continue;
                }
                if (declValue[k] == enc) continue;
                declValue[k] = enc;
                _declChanged[k] = true;
                _propChanged[declProp[k]] = true;
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Push the rewritten declarations into the elements without re-running the cascade: a
        /// property is updated when its source declaration changed, or when it inherits and the
        /// parent's value did (parents come first in element order). State rules read the
        /// declarations directly, so an element in a state re-applies it when one of them changed.
        /// </summary>
        private void ApplyChangedDecls(bool instant)
        {
            int n = elemTag.Length;
            bool anim = !_instantResolve && !instant && Tweens();
            for (int e = 0; e < n; e++)
            {
                int b = e * PROP_COUNT;
                int par = layParent != null && e < layParent.Length ? layParent[e] : -1;
                bool layoutChanged = false, touched = false;
                for (int p = 0; p < PROP_COUNT; p++)
                {
                    if (!_propChanged[p]) continue;
                    int src = _resolvedSrc[b + p];
                    Vector4 nv;
                    if (src >= 0) { if (!_declChanged[src]) continue; nv = declValue[src]; }
                    else if (src == SRC_INHERITED && par >= 0) { nv = _resolved[par * PROP_COUNT + p]; }
                    else continue;
                    if (nv == _resolved[b + p]) continue;
                    _resolved[b + p] = nv;
                    touched = true;
                    if (p == PROP_TRANSITION) continue;
                    float dur = 0f; int ease = 0;
                    if (anim) TransitionFor(e, p, ref dur, ref ease);
                    if (ApplyValue(e, p, nv, _resolvedRef[b + p], dur, ease, true)) layoutChanged = true;
                }
                if (layoutChanged) MarkLayoutDirty();
                int id = elemSignal[e];
                if (id < 0 || _stateRuleCount[e] == 0) continue;
                if (!touched && !StateDeclsChanged(e)) continue;
                ApplyState(id);
            }
        }

        private bool StateDeclsChanged(int e)
        {
            int c = _stateRuleCount[e];
            for (int i = 0; i < c; i++)
            {
                int r = _stateRules[e * MaxStateRules + i];
                int ds = ruleDeclStart[r], dc = ruleDeclCount[r];
                for (int k = ds; k < ds + dc; k++) if (_declChanged[k]) return true;
            }
            return false;
        }

        /// <summary>Replace var(--x) and var(--x, fallback) from varNames/varValues; mirrors AuiRuleTable.Substitute.</summary>
        private string SubstituteVars(string template)
        {
            if (template == null || template.IndexOf("var(") < 0) return template;
            string v = template;
            int guard = 0;
            while (true)
            {
                int at = v.IndexOf("var(");
                if (at < 0) break;
                // The MATCHING ')', not the first one: a fallback like rgba(0,0,0,1) carries its own.
                // One char[] for the scan: string[i] compiles to ToCharArray(i, 1)[0] under UdonSharp.
                char[] chars = v.ToCharArray();
                int close = -1, depth = 0;
                for (int k = at + 3; k < chars.Length; k++)
                {
                    char ch = chars[k];
                    if (ch == '(') depth++;
                    else if (ch == ')') { depth--; if (depth == 0) { close = k; break; } }
                }
                if (close < 0) break;
                string inner = v.Substring(at + 4, close - at - 4);
                string vn = inner, fallback = null;
                int comma = inner.IndexOf(',');
                if (comma >= 0) { vn = inner.Substring(0, comma); fallback = inner.Substring(comma + 1).Trim(); }
                int id = StyleVariableId(vn.Trim());
                string rep = "";
                if (id >= 0) rep = varValues[id];
                else if (fallback != null) rep = fallback;
                // Splice and rescan so a fallback that is itself a var() resolves on the next pass.
                v = v.Substring(0, at) + rep + v.Substring(close + 1);
                if (++guard > 64) break;
            }
            return v;
        }

        /// <summary>The Udon half of AuiRuleTable.Encode: one property's stylesheet text into its Vector4 encoding.</summary>
        private bool EncodeStyleValue(int prop, string v, ref Vector4 r)
        {
            if (v == null) return false;
            v = v.Trim();
            if (v.Length == 0) return false;
            r = Vector4.zero;
            if (prop == PROP_BG)
            {
                // sprite() / material() / shader() halves are their own declarations and never come from a variable: skip them.
                bool any = false;
                while (v.StartsWith("sprite(") || v.StartsWith("material(") || v.StartsWith("shader("))
                {
                    int cl = v.IndexOf(')');
                    if (cl < 0) return false;
                    v = v.Substring(cl + 1).Trim();
                    any = true;
                }
                if (v.Length == 0) { if (!any) return false; r = new Vector4(1, 1, 1, 1); return true; }
                return ParseColor(v, ref r);
            }
            if (prop == PROP_COLOR || prop == PROP_ICON || prop == PROP_BORDER) return ParseColor(v, ref r);
            if (prop == PROP_OPACITY || prop == PROP_SCALE || prop == PROP_GROW || prop == PROP_LINE_HEIGHT)
            {
                float f = 0f;
                if (!ParseFloatInvariant(v, ref f)) return false;
                r.x = f;
                return true;
            }
            if (prop == PROP_OFFSET)
            {
                string[] parts = SplitSpaces(v);
                float x = 0f, y = 0f;
                if (parts.Length != 2 || !ParseFloatInvariant(parts[0], ref x) || !ParseFloatInvariant(parts[1], ref y)) return false;
                r.x = x; r.y = y;
                return true;
            }
            if (prop == PROP_FONT)
            {
                // Only the size half is runtime-changeable; the weight is baked into the font asset.
                string[] parts = SplitSpaces(v);
                float size = 0f;
                if (parts.Length < 2 || !ParseFloatInvariant(parts[1], ref size)) return false;
                r.x = size; r.y = SIZE_MM;
                return true;
            }
            if (IsLengthProp(prop))
            {
                float mm = 0f; int kind = SIZE_MM;
                if (!ParseLength(v, ref mm, ref kind)) return false;
                r.x = mm; r.y = kind;
                return true;
            }
            if (prop == PROP_PADDING || prop == PROP_MARGIN)
            {
                string[] parts = SplitSpaces(v);
                if (parts.Length < 1 || parts.Length > 4) return false;
                float[] f = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    float mm = 0f; int kind = SIZE_MM;
                    if (!ParseLength(parts[i], ref mm, ref kind)) return false;
                    f[i] = mm;
                }
                if (parts.Length == 1) r = new Vector4(f[0], f[0], f[0], f[0]);
                else if (parts.Length == 2) r = new Vector4(f[0], f[1], f[0], f[1]);
                else if (parts.Length == 3) r = new Vector4(f[0], f[1], f[2], f[1]);
                else r = new Vector4(f[0], f[1], f[2], f[3]);
                return true;
            }
            if (prop == PROP_TRANSITION || prop == PROP_SPRITE || prop == PROP_MATERIAL) return false;
            int ord = EnumOrdinal(prop, v);
            if (ord < 0) return false;
            r.x = ord;
            return true;
        }

        private string[] SplitSpaces(string v)
        {
            string[] raw = v.Split(' ');
            int n = 0;
            for (int i = 0; i < raw.Length; i++) if (raw[i].Trim().Length > 0) n++;
            string[] parts = new string[n];
            int at = 0;
            for (int i = 0; i < raw.Length; i++) { string t = raw[i].Trim(); if (t.Length > 0) parts[at++] = t; }
            return parts;
        }

        // ================================================================ colours and numbers as text

        /// <summary>#RGB, #RGBA, #RRGGBB, #RRGGBBAA, rgb(), rgba(), transparent, none, white, black. Mirrors Core's AuiColor.TryParse.</summary>
        public bool ParseColor(string s, ref Vector4 c)
        {
            if (s == null) return false;
            s = s.Trim();
            if (s.Length == 0) return false;
            if (s == "transparent" || s == "none") { c = Vector4.zero; return true; }
            if (s == "white") { c = new Vector4(1, 1, 1, 1); return true; }
            if (s == "black") { c = new Vector4(0, 0, 0, 1); return true; }
            if (s.StartsWith("#"))
            {
                // One char[] per parse: string[i] compiles to ToCharArray(i, 1)[0] under UdonSharp,
                // an allocation per nibble on a path a slider handler drives every frame.
                char[] h = s.ToCharArray();
                int n = h.Length - 1;
                if (n != 3 && n != 4 && n != 6 && n != 8) return false;
                bool shortForm = n <= 4;
                int channels = shortForm ? n : n / 2;
                float r = 0f, g = 0f, b = 0f, a = 1f;
                for (int i = 0; i < channels; i++)
                {
                    int hi = HexNibble(h[1 + (shortForm ? i : 2 * i)]);
                    int lo = shortForm ? hi : HexNibble(h[2 + 2 * i]);
                    if (hi < 0 || lo < 0) return false;
                    float v = (hi * 16 + lo) / 255f;
                    if (i == 0) r = v; else if (i == 1) g = v; else if (i == 2) b = v; else a = v;
                }
                c = new Vector4(r, g, b, a);
                return true;
            }
            if (s.StartsWith("rgb"))
            {
                int o = s.IndexOf('(');
                int cl = s.LastIndexOf(')');
                if (o < 0 || cl < o) return false;
                string[] parts = s.Substring(o + 1, cl - o - 1).Split(',');
                if (parts.Length != 3 && parts.Length != 4) return false;
                float[] f = new float[4];
                f[3] = 1f;
                for (int i = 0; i < parts.Length; i++)
                {
                    float x = 0f;
                    if (!ParseFloatInvariant(parts[i].Trim(), ref x)) return false;
                    f[i] = i < 3 ? x / 255f : x;
                }
                c = new Vector4(f[0], f[1], f[2], f[3]);
                return true;
            }
            return false;
        }

        private static int HexNibble(char ch)
        {
            if (ch >= '0' && ch <= '9') return ch - '0';
            if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
            if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
            return -1;
        }

        private const string HexDigits = "0123456789ABCDEF";

        /// <summary>A colour as the stylesheet writes it: #RRGGBBAA. One char[] and one string per call.</summary>
        public string ColorToHex(Color c)
        {
            char[] digits = HexDigits.ToCharArray();
            char[] o = new char[9];
            o[0] = '#';
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255), g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255), a = Mathf.Clamp(Mathf.RoundToInt(c.a * 255f), 0, 255);
            o[1] = digits[r >> 4]; o[2] = digits[r & 15];
            o[3] = digits[g >> 4]; o[4] = digits[g & 15];
            o[5] = digits[b >> 4]; o[6] = digits[b & 15];
            o[7] = digits[a >> 4]; o[8] = digits[a & 15];
            return new string(o);
        }

        /// <summary>A number with up to three decimals and a '.' whatever the player's locale (ParseFloatInvariant reads it back).</summary>
        public string FormatInvariant(float v)
        {
            bool neg = v < 0f;
            if (neg) v = -v;
            int whole = Mathf.FloorToInt(v);
            int frac = Mathf.RoundToInt((v - whole) * 1000f);
            if (frac >= 1000) { whole++; frac -= 1000; }
            string s = whole.ToString();
            if (frac > 0)
            {
                string f = frac.ToString();
                while (f.Length < 3) f = "0" + f;
                int end = f.Length;
                while (end > 0 && f[end - 1] == '0') end--;
                s += "." + f.Substring(0, end);
            }
            return neg ? "-" + s : s;
        }
    }
}
