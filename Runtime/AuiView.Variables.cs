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
        /// (through var(--name) or var(--name, fallback)) is re-encoded and the whole tree re-resolves,
        /// through each element's own transition.
        /// </summary>
        public void SetStyleVariable(string varName, string value)
        {
            if (!_started) { _auiStashS = varName; _auiStashS2 = value; _AuiStart(); varName = _auiStashS; value = _auiStashS2; }
            if (StyleVariableId(varName) < 0)
                Debug.LogWarning("[AUI] " + name + ": no stylesheet of this view defines the variable '" + NormVarName(varName) + "'.");
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
            if (ReencodeVariableDecls()) ReResolveAll();
        }

        // ================================================================ re-encoding

        /// <summary>Re-substitute and re-encode every declaration built from a variable. True when any value changed.</summary>
        private bool ReencodeVariableDecls()
        {
            bool changed = false;
            for (int k = 0; k < declTemplate.Length; k++)
            {
                string t = declTemplate[k];
                if (string.IsNullOrEmpty(t)) continue;
                string v = SubstituteVars(t);
                Vector4 enc = Vector4.zero;
                if (!EncodeStyleValue(declProp[k], v, ref enc))
                {
                    Debug.LogWarning("[AUI] " + name + ": '" + v + "' (from '" + t + "') is not a value for style property " + declProp[k] + "; the declaration keeps its previous value.");
                    continue;
                }
                if (declValue[k] == enc) continue;
                declValue[k] = enc;
                changed = true;
            }
            return changed;
        }

        /// <summary>Replace var(--x) and var(--x, fallback) from varNames/varValues; mirrors AuiRuleTable.Substitute.</summary>
        private string SubstituteVars(string template)
        {
            if (template == null || template.IndexOf("var(") < 0) return template;
            string result = "";
            int i = 0, guard = 0;
            while (i < template.Length)
            {
                int at = template.IndexOf("var(", i);
                if (at < 0) { result += template.Substring(i); break; }
                result += template.Substring(i, at - i);
                int close = template.IndexOf(')', at);
                if (close < 0) break;
                string inner = template.Substring(at + 4, close - at - 4);
                string vn = inner, fallback = null;
                int comma = inner.IndexOf(',');
                if (comma >= 0) { vn = inner.Substring(0, comma); fallback = inner.Substring(comma + 1).Trim(); }
                int id = StyleVariableId(vn);
                if (id >= 0) result += varValues[id];
                else if (fallback != null) result += fallback;
                i = close + 1;
                if (++guard > 64) break;
            }
            return result;
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
            if (s[0] == '#')
            {
                string h = s.Substring(1);
                int n = h.Length;
                if (n != 3 && n != 4 && n != 6 && n != 8) return false;
                bool shortForm = n <= 4;
                int channels = shortForm ? n : n / 2;
                float[] ch = new float[4];
                ch[3] = 1f;
                for (int i = 0; i < channels; i++)
                {
                    int hi = HexNibble(h[shortForm ? i : 2 * i]);
                    int lo = shortForm ? hi : HexNibble(h[2 * i + 1]);
                    if (hi < 0 || lo < 0) return false;
                    ch[i] = (hi * 16 + lo) / 255f;
                }
                c = new Vector4(ch[0], ch[1], ch[2], ch[3]);
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

        private static string ToHex2(float channel)
        {
            int v = Mathf.Clamp(Mathf.RoundToInt(channel * 255f), 0, 255);
            return HexDigits[v >> 4].ToString() + HexDigits[v & 15].ToString();
        }

        /// <summary>A colour as the stylesheet writes it: #RRGGBBAA.</summary>
        public string ColorToHex(Color c)
        {
            return "#" + ToHex2(c.r) + ToHex2(c.g) + ToHex2(c.b) + ToHex2(c.a);
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
