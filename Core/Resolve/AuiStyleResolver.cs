using System.Collections.Generic;
using System.Text;

namespace Armagan.AUI.Core
{
    /// <summary>
    /// Runs the cascade for every element of a document. Inputs are the sheets in tier order
    /// (theme, linked, inline); the element's own style="" attribute is read here. Output is
    /// one <see cref="AuiResolvedStyle"/> per node, indexed by <c>node.Index</c>.
    ///
    /// Rules of the cascade, in the order they are applied:
    ///   1. tier (theme &lt; linked &lt; inline &lt; attribute)
    ///   2. specificity (tag 1, class 2, tag.class 3; +10 for a state)
    ///   3. source order (later wins)
    /// Variables are collected from every :root in tier order and substituted before values are
    /// typed. Only color, font*, icon-color, text-align and line-height inherit.
    /// </summary>
    public sealed class AuiStyleResolver
    {
        private readonly List<AuiStyleSheet> _sheets = new List<AuiStyleSheet>();
        private readonly Dictionary<string, AuiVariable> _vars = new Dictionary<string, AuiVariable>();
        private readonly HashSet<AuiDeclaration> _reported = new HashSet<AuiDeclaration>();
        private readonly List<AuiStyleRule> _rules = new List<AuiStyleRule>();
        private readonly HashSet<string> _usedClasses = new HashSet<string>();

        public AuiStyleResolver(IEnumerable<AuiStyleSheet> sheetsInTierOrder)
        {
            int order = 0;
            foreach (AuiStyleSheet s in sheetsInTierOrder)
            {
                if (s == null) continue;
                _sheets.Add(s);
                foreach (AuiVariable v in s.Variables) _vars[v.Name] = v;
                foreach (AuiStyleRule r in s.Rules)
                {
                    r.Order = order++;
                    _rules.Add(r);
                }
            }
        }

        public IEnumerable<AuiVariable> Variables
        {
            get { return _vars.Values; }
        }

        /// <summary>Every sheet rule in cascade input order (Order assigned), for the runtime rule table.</summary>
        public IEnumerable<AuiStyleRule> Rules
        {
            get { return _rules; }
        }

        /// <summary>Classes that appear literally in a class attribute of the resolved document.</summary>
        public HashSet<string> UsedClasses
        {
            get { return _usedClasses; }
        }

        /// <summary>Substitute variables and validate one declaration without reporting (its problems were reported by Resolve). False = unusable.</summary>
        public bool TryResolveValue(AuiDeclaration decl, out string value)
        {
            value = null;
            AuiPropDef def = AuiSchema.FindProp(decl.Property);
            if (def == null || IsBoundValue(decl.Value)) return false;
            var quiet = new AuiDiagnosticBag("");
            var saved = new HashSet<AuiDeclaration>(_reported);
            string v = Substitute(decl, quiet, "");
            bool ok = v != null && Validate(def, v, decl, quiet, "");
            _reported.Clear();
            foreach (AuiDeclaration x in saved) _reported.Add(x);
            if (!ok) return false;
            value = v;
            return true;
        }

        public AuiVariable FindVariable(string name)
        {
            AuiVariable v;
            return _vars.TryGetValue(name, out v) ? v : null;
        }

        /// <summary>Every class any sheet defines (for completion and unknown-class checks).</summary>
        public HashSet<string> DefinedClasses()
        {
            var set = new HashSet<string>();
            foreach (AuiStyleSheet s in _sheets) foreach (string c in s.ClassNames()) set.Add(c);
            return set;
        }

        public AuiResolvedStyle[] Resolve(AuiMarkupDocument doc, AuiDiagnosticBag diagnostics)
        {
            var result = new AuiResolvedStyle[doc.Nodes.Count];
            for (int i = 0; i < doc.Nodes.Count; i++)
            {
                AuiNode n = doc.Nodes[i];
                AuiResolvedStyle parent = n.Parent != null ? result[n.Parent.Index] : null;
                result[i] = ResolveNode(n, parent, doc, diagnostics);
            }
            ReportClassUsage(doc, diagnostics);
            return result;
        }

        private sealed class Candidate
        {
            public AuiStyleRule Rule;
            public AuiSelector Selector;
            public int Tier;
        }

        private static int Compare(Candidate a, Candidate b)
        {
            if (a.Tier != b.Tier) return a.Tier.CompareTo(b.Tier);
            int sa = a.Selector.Specificity, sb = b.Selector.Specificity;
            if (sa != sb) return sa.CompareTo(sb);
            return a.Rule.Order.CompareTo(b.Rule.Order);
        }

        private AuiResolvedStyle ResolveNode(AuiNode node, AuiResolvedStyle parent, AuiMarkupDocument doc, AuiDiagnosticBag d)
        {
            var rs = new AuiResolvedStyle();
            string[] classes = node.Classes;
            foreach (string c in classes) _usedClasses.Add(c);

            // ---- gather
            var normal = new List<Candidate>();
            var byState = new Dictionary<string, List<Candidate>>();
            foreach (AuiStyleRule r in _rules)
            {
                foreach (AuiSelector sel in r.Selectors)
                {
                    if (!sel.Matches(node.Tag, classes)) continue;
                    var c = new Candidate { Rule = r, Selector = sel, Tier = (int)r.Sheet.Tier };
                    if (sel.State == null) normal.Add(c);
                    else
                    {
                        List<Candidate> list;
                        if (!byState.TryGetValue(sel.State, out list)) byState[sel.State] = list = new List<Candidate>();
                        list.Add(c);
                    }
                }
            }
            normal.Sort(Compare);

            // ---- inherit first, so declared values override
            if (parent != null)
            {
                foreach (AuiPropDef p in AuiSchema.Props)
                {
                    if (!p.Inherited) continue;
                    string v = parent.Get(p.Name);
                    if (v != null) { rs.Values[p.Name] = v; AuiDeclaration src; if (parent.Sources.TryGetValue(p.Name, out src)) rs.Sources[p.Name] = src; }
                }
                // "font" shorthand inherits as a unit; a child that sets only font-size must
                // not keep the parent's shorthand size. Handled by the getters' precedence.
            }

            // ---- normal state
            foreach (Candidate c in normal) ApplyRule(c.Rule, rs.Values, rs.Sources, d, false);

            string inline = node.Get("style");
            if (!string.IsNullOrEmpty(inline))
            {
                AuiAttribute a = node.FindAttribute("style");
                AuiStyleRule rule = AuiStyleParser.ParseDeclarations(inline, doc.File, a.ValueLine, a.ValueColumn, d);
                ApplyRule(rule, rs.Values, rs.Sources, d, false, rs.Bound);
            }

            // ---- states
            foreach (KeyValuePair<string, List<Candidate>> kv in byState)
            {
                kv.Value.Sort(Compare);
                var values = new Dictionary<string, string>();
                var sources = new Dictionary<string, AuiDeclaration>();
                foreach (Candidate c in kv.Value) ApplyRule(c.Rule, values, sources, d, true);
                if (values.Count > 0) rs.States[kv.Key] = values;
            }

            // ---- modal::backdrop: the scrim's own cascade (no inheritance, no states)
            if (node.Tag == "modal")
            {
                var bd = new List<Candidate>();
                foreach (AuiStyleRule r in _rules)
                    foreach (AuiSelector sel in r.Selectors)
                        if (sel.Pseudo == "backdrop" && sel.MatchesElement(node.Tag, classes))
                            bd.Add(new Candidate { Rule = r, Selector = sel, Tier = (int)r.Sheet.Tier });
                bd.Sort(Compare);
                rs.Backdrop = new AuiResolvedStyle();
                foreach (Candidate c in bd) ApplyRule(c.Rule, rs.Backdrop.Values, rs.Backdrop.Sources, d, false);
            }

            return rs;
        }

        /// <summary>A style value made of {binding} tokens (style="width: {w}").</summary>
        public static bool IsBoundValue(string v)
        {
            return v != null && v.IndexOf('{') >= 0;
        }

        /// <summary>
        /// Check a bound value: every whitespace-separated token must be a {binding}; Number,
        /// Length, Color, Background, Enum and Font-size take one, Offset two, Box one to four.
        /// Returns null when fine, else the error text.
        /// </summary>
        public static string CheckBoundValue(AuiPropDef def, string v)
        {
            string[] toks = v.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (string t in toks)
            {
                if (!AuiAnalyzer.IsBindingValue(t) || !AuiSchema.IsIdentifier(AuiAnalyzer.BindingName(t)))
                    return "a bound value is made of {binding} tokens only (no literal parts): '" + v + "'";
            }
            switch (def.Kind)
            {
                case AuiPropKind.Offset: return toks.Length == 2 ? null : "offset takes two bindings: {x} {y}";
                case AuiPropKind.Box: return toks.Length >= 1 && toks.Length <= 4 ? null : "padding/margin take one to four bindings";
                case AuiPropKind.Transition: case AuiPropKind.Font: case AuiPropKind.FontWeight:
                    return "'" + def.Name + "' cannot be bound";
            }
            if (def.BakeOnly) return "'" + def.Name + "' is applied at bake only and cannot be bound";
            return toks.Length == 1 ? null : "'" + def.Name + "' takes one binding";
        }

        private void ApplyRule(AuiStyleRule rule, Dictionary<string, string> values, Dictionary<string, AuiDeclaration> sources, AuiDiagnosticBag d, bool isState, Dictionary<string, string> bound = null)
        {
            foreach (AuiDeclaration decl in rule.Declarations)
            {
                AuiPropDef def = AuiSchema.FindProp(decl.Property);
                if (def == null) continue; // already reported by the parser
                if (isState && !def.Animatable)
                {
                    Report(decl, d, AuiCodes.NonAnimatableTransition, "'" + decl.Property + "' cannot change in a state rule; states may change color, background, border, icon-color, opacity, scale and offset.", rule.Sheet.File);
                    continue;
                }
                if (IsBoundValue(decl.Value))
                {
                    // style="prop: {binding}": the bake keeps the property's default; the runtime writes the binding.
                    if (bound == null) { Report(decl, d, AuiCodes.BadBoundValue, "Only a style=\"\" attribute may bind a value; a stylesheet rule cannot.", rule.Sheet.File); continue; }
                    string err = CheckBoundValue(def, decl.Value);
                    if (err != null) { Report(decl, d, AuiCodes.BadBoundValue, err + ".", rule.Sheet.File); continue; }
                    bound[decl.Property] = decl.Value;
                    sources[decl.Property] = decl;
                    continue;
                }
                string v = Substitute(decl, d, rule.Sheet.File);
                if (v == null) continue;
                if (!Validate(def, v, decl, d, rule.Sheet.File)) continue;
                values[decl.Property] = v;
                sources[decl.Property] = decl;
            }
        }

        private void Report(AuiDeclaration decl, AuiDiagnosticBag d, string code, string message, string file)
        {
            if (!_reported.Add(decl)) return;
            // Diagnostics for a sheet belong to that sheet's file, not the view being resolved.
            d.Items.Add(new AuiDiagnostic(AuiSeverity.Error, code, message, file, decl.Line, decl.Column, decl.Property.Length));
        }

        /// <summary>Replace var(--x) and var(--x, fallback); null when a variable is undefined.</summary>
        private string Substitute(AuiDeclaration decl, AuiDiagnosticBag d, string file)
        {
            string v = decl.Value;
            if (v.IndexOf("var(", System.StringComparison.Ordinal) < 0) return v;
            var sb = new StringBuilder();
            int i = 0;
            int guard = 0;
            while (i < v.Length)
            {
                int at = v.IndexOf("var(", i, System.StringComparison.Ordinal);
                if (at < 0) { sb.Append(v, i, v.Length - i); break; }
                sb.Append(v, i, at - i);
                int close = v.IndexOf(')', at);
                if (close < 0)
                {
                    Report(decl, d, AuiCodes.BadStyleValue, "Unterminated var() in '" + decl.Property + "'.", file);
                    return null;
                }
                string inner = v.Substring(at + 4, close - at - 4);
                string name = inner, fallback = null;
                int comma = inner.IndexOf(',');
                if (comma >= 0) { name = inner.Substring(0, comma).Trim(); fallback = inner.Substring(comma + 1).Trim(); }
                name = name.Trim();
                AuiVariable var;
                if (_vars.TryGetValue(name, out var)) sb.Append(var.Value);
                else if (fallback != null) sb.Append(fallback);
                else
                {
                    var names = new List<string>(_vars.Keys);
                    string hint = AuiSchema.Suggest(name, names);
                    Report(decl, d, AuiCodes.UndefinedVariable, "Undefined variable '" + name + "'." + (hint != null ? " Did you mean '" + hint + "'?" : ""), file);
                    return null;
                }
                i = close + 1;
                if (++guard > 64) break;
            }
            return sb.ToString();
        }

        private bool Validate(AuiPropDef def, string v, AuiDeclaration decl, AuiDiagnosticBag d, string file)
        {
            string err = null;
            switch (def.Kind)
            {
                case AuiPropKind.Length:
                {
                    AuiLength l;
                    if (!AuiLength.TryParse(v, out l)) err = "expected a length (12, 12mm, 50%, auto)";
                    else if (l.IsSet && !l.IsAuto && l.Value < 0 && !AllowsNegative(def.Name))
                    {
                        Report(decl, d, AuiCodes.LayoutNegativeSize, "'" + def.Name + "' cannot be negative (" + v + "). Only top, right, bottom, left, margin and offset may be.", file);
                        return false;
                    }
                    break;
                }
                case AuiPropKind.Box:
                {
                    AuiBox b;
                    if (!AuiBox.TryParse(v, out b)) err = "expected 1 to 4 lengths";
                    else if (def.Name != "margin" && (b.Top < 0 || b.Right < 0 || b.Bottom < 0 || b.Left < 0))
                    {
                        Report(decl, d, AuiCodes.LayoutNegativeSize, "'" + def.Name + "' cannot be negative (" + v + "). Only margin may be.", file);
                        return false;
                    }
                    break;
                }
                case AuiPropKind.Number: { float f; if (!AuiNum.TryParse(v, out f)) err = "expected a number"; break; }
                case AuiPropKind.Color:
                {
                    AuiColor c;
                    if (!AuiColor.TryParse(v, out c))
                        err = v.StartsWith("material(") || v.StartsWith("shader(") || v.StartsWith("sprite(")
                            ? "material(), shader() and sprite() are only valid in background; text is coloured by a plain color"
                            : "expected a color (#RRGGBB, #RRGGBBAA, rgba(), transparent)";
                    break;
                }
                case AuiPropKind.Background: { AuiBackground b; if (!AuiBackground.TryParse(v, out b)) err = "expected none | color | sprite(Name) color | material(Name) color | shader(Name) color"; break; }
                case AuiPropKind.FontWeight: if (AuiNum.ParseWeight(v) == 0) err = "expected regular | medium | semibold | bold | 400..700"; break;
                case AuiPropKind.Font:
                {
                    string[] parts = v.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    float size;
                    if (parts.Length != 2 || AuiNum.ParseWeight(parts[0]) == 0 || !AuiNum.TryParse(parts[1], out size)) err = "expected 'weight size', e.g. 600 18";
                    break;
                }
                case AuiPropKind.Enum:
                {
                    bool ok = false;
                    foreach (string e in def.EnumValues) if (e == v) { ok = true; break; }
                    if (!ok)
                    {
                        string hint = AuiSchema.Suggest(v, def.EnumValues);
                        err = "expected one of " + string.Join(" | ", def.EnumValues) + (hint != null ? ". Did you mean '" + hint + "'?" : "");
                    }
                    break;
                }
                case AuiPropKind.Offset:
                {
                    string[] parts = v.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    float x, y;
                    if (parts.Length != 2 || !AuiNum.TryParse(parts[0], out x) || !AuiNum.TryParse(parts[1], out y)) err = "expected 'x y'";
                    break;
                }
                case AuiPropKind.Transition:
                {
                    if (v == "none") break;
                    foreach (string item in v.Split(','))
                    {
                        AuiTransition t;
                        string e;
                        if (!AuiTransition.TryParse(item, out t, out e)) { err = e; break; }
                        AuiPropDef target = AuiSchema.FindProp(t.Property);
                        if (t.Property != "all" && (target == null || !target.Animatable)) { err = "'" + t.Property + "' is not an animatable property"; break; }
                    }
                    break;
                }
            }
            if (err == null) return true;
            Report(decl, d, AuiCodes.BadStyleValue, "Bad value '" + v + "' for '" + def.Name + "': " + err + ".", file);
            return false;
        }

        private static bool AllowsNegative(string prop)
        {
            return prop == "top" || prop == "right" || prop == "bottom" || prop == "left";
        }

        private void ReportClassUsage(AuiMarkupDocument doc, AuiDiagnosticBag d)
        {
            HashSet<string> defined = DefinedClasses();
            foreach (AuiNode n in doc.Nodes)
            {
                AuiAttribute a = n.FindAttribute("class");
                if (a == null) continue;
                foreach (string c in n.Classes)
                {
                    if (defined.Contains(c)) continue;
                    string hint = AuiSchema.Suggest(c, defined);
                    d.Warning(AuiCodes.UnknownClass, "No stylesheet defines class '" + c + "'." + (hint != null ? " Did you mean '" + hint + "'?" : ""), a.ValueLine, a.ValueColumn, a.Value.Length);
                }
            }
            foreach (AuiStyleSheet s in _sheets)
            {
                if (s.Tier == AuiStyleTier.Theme || s.Tier == AuiStyleTier.Utility) continue;   // theme classes are documented as bake-only where they set a weight
                foreach (AuiStyleRule r in s.Rules)
                    foreach (AuiSelector sel in r.Selectors)
                    {
                        if (sel.Class == null || _usedClasses.Contains(sel.Class)) continue;
                        // Info, not a warning: a class may exist only for AddClass / class="{x}" at runtime.
                        d.Items.Add(new AuiDiagnostic(AuiSeverity.Info, AuiCodes.UnusedClass, "Class '." + sel.Class + "' is not used in the markup (fine if it is added at runtime).", s.File, r.Line, r.Column, sel.Class.Length + 1));
                        // A class only reachable at runtime (AddClass, class="{x}") cannot change the font
                        // weight: Udon has no font setter. Its size half still applies.
                        foreach (AuiDeclaration decl in r.Declarations)
                        {
                            if (decl.Property != "font-weight" && decl.Property != "font") continue;
                            if (decl.Property == "font" && !SetsWeight(decl.Value)) continue;
                            d.Items.Add(new AuiDiagnostic(AuiSeverity.Warning, AuiCodes.BakeOnlyProperty, "'." + sel.Class + "' sets the font weight, which is applied at bake only; an element that gains '." + sel.Class + "' at runtime keeps its baked weight.", s.File, decl.Line, decl.Column, decl.Property.Length));
                        }
                    }
            }
        }

        private static bool SetsWeight(string fontValue)
        {
            string[] parts = (fontValue ?? "").Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 1 && AuiNum.ParseWeight(parts[0]) > 0 && AuiNum.ParseWeight(parts[0]) != 400;
        }
    }
}
