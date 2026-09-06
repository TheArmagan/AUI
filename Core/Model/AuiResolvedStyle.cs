using System.Collections.Generic;

namespace Armagan.AUI.Core
{
    /// <summary>
    /// The cascade's answer for one element: every property that applies in the normal
    /// state, plus what each pointer state changes. Values are stored as the raw strings the
    /// cascade settled on (variables already substituted) and parsed on demand through the
    /// typed getters, so the layout solver, the prefab builder and the language server all
    /// read one object.
    /// </summary>
    public sealed class AuiResolvedStyle
    {
        /// <summary>property -> value (normal state, after inheritance).</summary>
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        /// <summary>property -> the declaration that won, for go-to-definition and error positions.</summary>
        public readonly Dictionary<string, AuiDeclaration> Sources = new Dictionary<string, AuiDeclaration>();
        /// <summary>state -> (property -> value). Only states with at least one override are present.</summary>
        public readonly Dictionary<string, Dictionary<string, string>> States = new Dictionary<string, Dictionary<string, string>>();

        public bool Has(string prop)
        {
            return Values.ContainsKey(prop);
        }

        public string Get(string prop)
        {
            string v;
            return Values.TryGetValue(prop, out v) ? v : null;
        }

        public string GetState(string state, string prop)
        {
            Dictionary<string, string> d;
            string v;
            if (!States.TryGetValue(state, out d)) return null;
            return d.TryGetValue(prop, out v) ? v : null;
        }

        public bool HasStateRules
        {
            get { return States.Count > 0; }
        }

        public bool HasState(string state)
        {
            return States.ContainsKey(state);
        }

        // ---------------------------------------------------------------- typed getters

        public AuiLength Length(string prop)
        {
            AuiLength l;
            string v = Get(prop);
            return v != null && AuiLength.TryParse(v, out l) ? l : AuiLength.Unset;
        }

        public float Number(string prop, float fallback)
        {
            float f;
            string v = Get(prop);
            return v != null && AuiNum.TryParse(v, out f) ? f : fallback;
        }

        public string Enum(string prop, string fallback)
        {
            string v = Get(prop);
            return v ?? fallback;
        }

        public AuiBox Box(string prop)
        {
            AuiBox b;
            string v = Get(prop);
            return v != null && AuiBox.TryParse(v, out b) ? b : new AuiBox();
        }

        public bool Color(string prop, out AuiColor c)
        {
            string v = Get(prop);
            if (v == null) { c = AuiColor.Transparent; return false; }
            return AuiColor.TryParse(v, out c);
        }

        public AuiBackground Background
        {
            get
            {
                AuiBackground bg;
                string v = Get("background");
                if (v != null && AuiBackground.TryParse(v, out bg)) return bg;
                return new AuiBackground { IsNone = true };
            }
        }

        public AuiLength Width { get { return Length("width"); } }
        public AuiLength Height { get { return Length("height"); } }
        public AuiLength MinWidth { get { return Length("min-width"); } }
        public AuiLength MinHeight { get { return Length("min-height"); } }
        public AuiLength MaxWidth { get { return Length("max-width"); } }
        public AuiLength MaxHeight { get { return Length("max-height"); } }
        public AuiBox Padding { get { return Box("padding"); } }
        public AuiBox Margin { get { return Box("margin"); } }
        public float Gap { get { return Length("gap").Resolve(0, 0); } }
        public string Anchor { get { return Enum("anchor", "none"); } }
        public bool IsRow { get { return Enum("flex-direction", "column") == "row"; } }
        public string Justify { get { return Enum("justify", "start"); } }
        public string Align { get { return Enum("align", "stretch"); } }
        public float Grow { get { return Number("grow", 0); } }
        public bool Wrap { get { return Enum("wrap", "false") == "true"; } }
        public float Opacity { get { return Number("opacity", 1); } }
        public float Scale { get { return Number("scale", 1); } }
        public float Radius { get { return Length("radius").Resolve(0, 0); } }
        public string TextAlign { get { return Enum("text-align", "left"); } }
        public float LineHeight { get { return Number("line-height", 0); } }
        public string PageTransition { get { return Enum("page-transition", "fade"); } }
        public string Show { get { return Enum("show", "fade"); } }

        public int FontWeight
        {
            get
            {
                string f = Get("font");
                if (f != null)
                {
                    string[] parts = f.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 1) { int w = AuiNum.ParseWeight(parts[0]); if (w > 0 && Get("font-weight") == null) return w; }
                }
                int fw = AuiNum.ParseWeight(Get("font-weight"));
                return fw > 0 ? fw : 400;
            }
        }

        public float FontSize
        {
            get
            {
                string fs = Get("font-size");
                AuiLength l;
                if (fs != null && AuiLength.TryParse(fs, out l)) return l.Value;
                string f = Get("font");
                if (f != null)
                {
                    string[] parts = f.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    float v;
                    if (parts.Length >= 2 && AuiNum.TryParse(parts[1], out v)) return v;
                    if (parts.Length == 1 && AuiNum.TryParse(parts[0], out v)) return v;
                }
                return 18f;
            }
        }

        public List<AuiTransition> Transitions
        {
            get
            {
                var list = new List<AuiTransition>();
                string t = Get("transition");
                if (t == null || t == "none") return list;
                foreach (string item in t.Split(','))
                {
                    AuiTransition tr;
                    string err;
                    if (AuiTransition.TryParse(item, out tr, out err)) list.Add(tr);
                }
                return list;
            }
        }

        /// <summary>Duration and ease that apply when <paramref name="prop"/> changes between states; false when no transition covers it.</summary>
        public bool TransitionFor(string prop, out float duration, out string ease)
        {
            duration = 0;
            ease = "out-quad";
            bool found = false;
            foreach (AuiTransition t in Transitions)
            {
                if (t.Property == prop || t.Property == "all")
                {
                    duration = t.Duration;
                    ease = t.Ease;
                    found = true;
                    if (t.Property == prop) return true;
                }
            }
            return found;
        }
    }
}
