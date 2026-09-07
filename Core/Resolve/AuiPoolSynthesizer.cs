using System.Collections.Generic;

namespace Armagan.AUI.Core
{
    /// <summary>
    /// Turns the two sugar forms of runtime creation into ordinary &lt;template&gt; elements
    /// appended to the root, so the analyzer, the index map and the builder only know
    /// templates:
    ///   &lt;view pool="text:16 panel:8"&gt;   -&gt; &lt;template id="pool-text" max="16" pool="text"&gt;&lt;text/&gt;&lt;/template&gt; ...
    ///   &lt;slot id="s" views="Foo Bar" max="4"/&gt; -&gt; &lt;template id="s-Foo" max="4" slot="s" view="Foo"&gt;&lt;Foo/&gt;&lt;/template&gt; ...
    /// Runs after inline components are expanded and before the cascade.
    /// </summary>
    public static class AuiPoolSynthesizer
    {
        public static readonly string[] PoolTags = { "panel", "text", "icon", "spacer" };

        public static void Synthesize(AuiMarkupDocument doc, AuiDiagnosticBag d)
        {
            if (doc.Root == null) return;
            bool changed = false;

            // ---- slots
            foreach (AuiNode n in doc.Nodes.ToArray())
            {
                if (n.Tag != "slot") continue;
                changed = true;
                AuiNode parent = n.Parent;
                int at = parent != null ? parent.Children.IndexOf(n) : -1;
                if (parent != null) parent.Children.RemoveAt(at);
                string id = n.Id;
                if (string.IsNullOrEmpty(id)) { d.Error(AuiCodes.MissingRequiredAttribute, "<slot> needs an id.", n.Line, n.Column, 5); continue; }
                string views = n.Get("views");
                if (string.IsNullOrEmpty(views)) { d.Error(AuiCodes.MissingRequiredAttribute, "<slot> needs views=\"ViewA ViewB\".", n.Line, n.Column, 5); continue; }
                string max = n.Get("max") ?? "4";
                foreach (string v in views.Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!char.IsUpper(v[0])) { d.Error(AuiCodes.BadAttributeValue, "slot views must be view names (capitalised): '" + v + "'.", n.FindAttribute("views").ValueLine, n.FindAttribute("views").ValueColumn, views.Length); continue; }
                    AuiNode t = Template(doc, id + "-" + v, max, n);
                    t.Attributes.Add(Attr("slot", id, n));
                    t.Attributes.Add(Attr("view", v, n));
                    var child = new AuiNode { Tag = v, SelfClosing = true, Parent = t, Line = n.Line, Column = n.Column, EndLine = n.EndLine, EndColumn = n.EndColumn };
                    t.Children.Add(child);
                }
            }

            // ---- pools
            string pool = doc.Root.Get("pool");
            if (!string.IsNullOrEmpty(pool))
            {
                AuiAttribute pa = doc.Root.FindAttribute("pool");
                foreach (string tok in pool.Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    int colon = tok.IndexOf(':');
                    string tag = colon < 0 ? tok : tok.Substring(0, colon);
                    string count = colon < 0 ? "8" : tok.Substring(colon + 1);
                    int cnt;
                    if (System.Array.IndexOf(PoolTags, tag) < 0)
                    {
                        d.Error(AuiCodes.BadAttributeValue, "pool: '" + tag + "' cannot be pooled; only " + string.Join(", ", PoolTags) + " can (use a <template> for anything else).", pa.ValueLine, pa.ValueColumn, pool.Length);
                        continue;
                    }
                    if (!int.TryParse(count, out cnt) || cnt < 1 || cnt > 64)
                    {
                        d.Error(AuiCodes.BadAttributeValue, "pool: '" + tok + "' needs tag:count with 1 <= count <= 64.", pa.ValueLine, pa.ValueColumn, pool.Length);
                        continue;
                    }
                    if (doc.FindById("pool-" + tag) != null) { d.Error(AuiCodes.DuplicateId, "pool lists '" + tag + "' twice.", pa.ValueLine, pa.ValueColumn, pool.Length); continue; }
                    changed = true;
                    AuiNode t = Template(doc, "pool-" + tag, count, doc.Root);
                    t.Attributes.Add(Attr("pool", tag, doc.Root));
                    var proto = new AuiNode { Tag = tag, SelfClosing = true, Parent = t, Line = doc.Root.Line, Column = doc.Root.Column, EndLine = doc.Root.Line, EndColumn = doc.Root.Column };
                    if (tag == "icon") proto.Attributes.Add(Attr("name", "circle", doc.Root));
                    t.Children.Add(proto);
                }
            }

            if (changed) AuiTemplateExpander.Reindex(doc);
        }

        private static AuiNode Template(AuiMarkupDocument doc, string id, string max, AuiNode at)
        {
            var t = new AuiNode { Tag = "template", Parent = doc.Root, Line = at.Line, Column = at.Column, EndLine = at.EndLine, EndColumn = at.EndColumn };
            t.Attributes.Add(Attr("id", id, at));
            t.Attributes.Add(Attr("max", max, at));
            doc.Root.Children.Add(t);
            return t;
        }

        private static AuiAttribute Attr(string name, string value, AuiNode at)
        {
            return new AuiAttribute { Name = name, Value = value, Line = at.Line, Column = at.Column, ValueLine = at.Line, ValueColumn = at.Column };
        }
    }
}
