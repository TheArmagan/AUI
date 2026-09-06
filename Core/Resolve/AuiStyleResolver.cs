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
                ApplyRule(rule, rs.Values, rs.Sources, d, false);
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

            return rs;
        }

        private void ApplyRule(AuiStyleRule rule, Dictionary<string, string> values, Dictionary<string, AuiDeclaration> sources, AuiDiagnosticBag d, bool isState)
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
                case AuiPropKind.Length: { AuiLength l; if (!AuiLength.TryParse(v, out l)) err = "expected a length (12, 12mm, 50%, auto)"; break; }
                case AuiPropKind.Box: { AuiBox b; if (!AuiBox.TryParse(v, out b)) err = "expected 1 to 4 lengths"; break; }
                case AuiPropKind.Number: { float f; if (!AuiNum.TryParse(v, out f)) err = "expected a number"; break; }
                case AuiPropKind.Color: { AuiColor c; if (!AuiColor.TryParse(v, out c)) err = "expected a color (#RRGGBB, #RRGGBBAA, rgba(), transparent)"; break; }
                case AuiPropKind.Background: { AuiBackground b; if (!AuiBackground.TryParse(v, out b)) err = "expected none | color | sprite(Name) color"; break; }
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
                if (s.Tier == AuiStyleTier.Theme) continue;
                foreach (AuiStyleRule r in s.Rules)
                    foreach (AuiSelector sel in r.Selectors)
                    {
                        if (sel.Class == null || _usedClasses.Contains(sel.Class)) continue;
                        d.Items.Add(new AuiDiagnostic(AuiSeverity.Warning, AuiCodes.UnusedClass, "Class '." + sel.Class + "' is not used by this view.", s.File, r.Line, r.Column, sel.Class.Length + 1));
                    }
            }
        }
    }
}
