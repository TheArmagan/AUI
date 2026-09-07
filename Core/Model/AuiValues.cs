using System.Globalization;

namespace Armagan.AUI.Core
{
    /// <summary>A colour with float channels 0..1. Own struct so Core never references UnityEngine.</summary>
    public struct AuiColor
    {
        public float R, G, B, A;

        public AuiColor(float r, float g, float b, float a)
        {
            R = r; G = g; B = b; A = a;
        }

        public static readonly AuiColor Transparent = new AuiColor(0, 0, 0, 0);
        public static readonly AuiColor White = new AuiColor(1, 1, 1, 1);

        public bool IsTransparent
        {
            get { return A <= 0f; }
        }

        /// <summary>#RGB, #RGBA, #RRGGBB, #RRGGBBAA, rgb(), rgba(), transparent, white, black.</summary>
        public static bool TryParse(string s, out AuiColor c)
        {
            c = Transparent;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s == "transparent" || s == "none") { c = Transparent; return true; }
            if (s == "white") { c = White; return true; }
            if (s == "black") { c = new AuiColor(0, 0, 0, 1); return true; }
            if (s[0] == '#')
            {
                string h = s.Substring(1);
                if (h.Length == 3 || h.Length == 4)
                {
                    string e = "";
                    foreach (char ch in h) e += ch.ToString() + ch;
                    h = e;
                }
                if (h.Length != 6 && h.Length != 8) return false;
                int v;
                if (!int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
                if (h.Length == 6)
                    c = new AuiColor(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
                else
                    c = new AuiColor(((v >> 24) & 255) / 255f, ((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
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
                    float x;
                    if (!AuiNum.TryParse(parts[i].Trim(), out x)) return false;
                    f[i] = i < 3 ? x / 255f : x;
                }
                c = new AuiColor(f[0], f[1], f[2], f[3]);
                return true;
            }
            return false;
        }

        public string ToHex()
        {
            int r = (int)System.Math.Round(R * 255), g = (int)System.Math.Round(G * 255), b = (int)System.Math.Round(B * 255), a = (int)System.Math.Round(A * 255);
            return a == 255 ? string.Format("#{0:X2}{1:X2}{2:X2}", r, g, b) : string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", r, g, b, a);
        }

        public override string ToString()
        {
            return ToHex();
        }
    }

    /// <summary>A size: a number of mm, auto, or a percentage of the parent.</summary>
    public struct AuiLength
    {
        public float Value;
        public bool IsAuto;
        public bool IsPercent;
        public bool IsSet;

        public static readonly AuiLength Unset = new AuiLength();
        public static readonly AuiLength Auto = new AuiLength { IsAuto = true, IsSet = true };

        public static AuiLength Mm(float v)
        {
            return new AuiLength { Value = v, IsSet = true };
        }

        public static bool TryParse(string s, out AuiLength l)
        {
            l = Unset;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s == "auto") { l = Auto; return true; }
            bool pct = s.EndsWith("%");
            if (pct) s = s.Substring(0, s.Length - 1);
            else if (s.EndsWith("mm")) s = s.Substring(0, s.Length - 2);
            float v;
            if (!AuiNum.TryParse(s, out v)) return false;
            l = new AuiLength { Value = v, IsPercent = pct, IsSet = true };
            return true;
        }

        /// <summary>Resolve against the parent's size on this axis; auto returns the fallback.</summary>
        public float Resolve(float parentSize, float fallback)
        {
            if (!IsSet || IsAuto) return fallback;
            return IsPercent ? parentSize * Value / 100f : Value;
        }

        public override string ToString()
        {
            if (!IsSet) return "unset";
            if (IsAuto) return "auto";
            return IsPercent ? Value.ToString(CultureInfo.InvariantCulture) + "%" : Value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Four edges in mm: top, right, bottom, left (CSS order).</summary>
    public struct AuiBox
    {
        public float Top, Right, Bottom, Left;
        public bool IsSet;

        public static bool TryParse(string s, out AuiBox b)
        {
            b = new AuiBox();
            if (string.IsNullOrEmpty(s)) return false;
            string[] parts = s.Trim().Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 4) return false;
            float[] v = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].EndsWith("mm") ? parts[i].Substring(0, parts[i].Length - 2) : parts[i];
                if (!AuiNum.TryParse(p, out v[i])) return false;
            }
            b.IsSet = true;
            switch (parts.Length)
            {
                case 1: b.Top = b.Right = b.Bottom = b.Left = v[0]; break;
                case 2: b.Top = b.Bottom = v[0]; b.Right = b.Left = v[1]; break;
                case 3: b.Top = v[0]; b.Right = b.Left = v[1]; b.Bottom = v[2]; break;
                default: b.Top = v[0]; b.Right = v[1]; b.Bottom = v[2]; b.Left = v[3]; break;
            }
            return true;
        }

        public float Horizontal
        {
            get { return Left + Right; }
        }

        public float Vertical
        {
            get { return Top + Bottom; }
        }
    }

    /// <summary>none | color | sprite(Name) color | material(Name) color | shader(Name) color; sprite() may combine with material()/shader().</summary>
    public struct AuiBackground
    {
        public bool IsNone;
        public string Sprite;
        /// <summary>A Material asset by name, used as the image's material (its shader draws the sprite).</summary>
        public string Material;
        /// <summary>A shader by name; the builder creates one Material asset per shader under Assets/AUI/Generated/Materials.</summary>
        public string Shader;
        public AuiColor Color;
        public bool IsSet;

        public bool HasMaterial
        {
            get { return Material != null || Shader != null; }
        }

        public static bool TryParse(string s, out AuiBackground bg)
        {
            bg = new AuiBackground();
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            bg.IsSet = true;
            if (s == "none") { bg.IsNone = true; return true; }
            bool any = false;
            while (true)
            {
                string fn = null;
                if (s.StartsWith("sprite(")) fn = "sprite";
                else if (s.StartsWith("material(")) fn = "material";
                else if (s.StartsWith("shader(")) fn = "shader";
                if (fn == null) break;
                int cl = s.IndexOf(')');
                if (cl < 0) return false;
                string arg = s.Substring(fn.Length + 1, cl - fn.Length - 1).Trim();
                if (arg.Length == 0) return false;
                if (fn == "sprite") bg.Sprite = arg;
                else if (fn == "material") { if (bg.Shader != null) return false; bg.Material = arg; }
                else { if (bg.Material != null) return false; bg.Shader = arg; }
                any = true;
                s = s.Substring(cl + 1).Trim();
            }
            if (any && s.Length == 0) { bg.Color = AuiColor.White; return true; }
            if (any && s.StartsWith("var(")) return AuiColor.TryParse(s, out bg.Color);
            return AuiColor.TryParse(s, out bg.Color);
        }
    }

    public struct AuiTransition
    {
        public string Property;
        public float Duration;
        public string Ease;

        /// <summary>Parses one "prop 0.12s out-quad" item. Ease optional (out-quad), duration optional (0.15s).</summary>
        public static bool TryParse(string item, out AuiTransition t, out string error)
        {
            t = new AuiTransition { Duration = 0.15f, Ease = "out-quad" };
            error = null;
            string[] parts = item.Trim().Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) { error = "empty transition"; return false; }
            t.Property = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.EndsWith("s") && p.Length > 1 && (char.IsDigit(p[0]) || p[0] == '.'))
                {
                    string num = p.EndsWith("ms") ? p.Substring(0, p.Length - 2) : p.Substring(0, p.Length - 1);
                    float v;
                    if (!AuiNum.TryParse(num, out v)) { error = "bad duration '" + p + "'"; return false; }
                    t.Duration = p.EndsWith("ms") ? v / 1000f : v;
                }
                else if (AuiSchema.IsEase(p))
                {
                    t.Ease = p;
                }
                else
                {
                    error = "unknown ease '" + p + "'";
                    return false;
                }
            }
            return true;
        }
    }

    public static class AuiNum
    {
        public static bool TryParse(string s, out float v)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        public static string Fmt(float v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>Font weight keyword or number to 400/500/600/700; 0 when unknown.</summary>
        public static int ParseWeight(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            switch (s.Trim().ToLowerInvariant())
            {
                case "regular": case "normal": case "400": return 400;
                case "medium": case "500": return 500;
                case "semibold": case "semi-bold": case "600": return 600;
                case "bold": case "700": return 700;
            }
            return 0;
        }
    }
}
