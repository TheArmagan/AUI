using System.Collections.Generic;
using System.Text;

namespace Armagan.AUI.Core
{
    /// <summary>
    /// Expands inline components: every &lt;Name .../&gt; whose Name is a &lt;component&gt; of the same
    /// file is replaced by a deep clone of the component's template, with {prop} tokens in
    /// attribute values and text substituted by the usage's attribute (a literal, or a
    /// {binding} of the host view passed straight through), or the prop's default.
    ///
    /// Runs right after parsing, before the cascade and the analyzer, so everything downstream
    /// sees ordinary elements: props reach class, style values, icon names, if= and text at no
    /// runtime cost. A name that is not a local component is left alone and becomes a nested
    /// view as before.
    /// </summary>
    public static class AuiTemplateExpander
    {
        private const int MaxPasses = 8;

        public static void Expand(AuiMarkupDocument doc, AuiDiagnosticBag d)
        {
            // Reserved <select> rows are real elements too, and expanding them here means the
            // cascade, the analyzer, the index map, the layout solver and the builder all keep
            // seeing nothing but ordinary <option>s.
            if (doc.Root != null && ExpandSelectRows(doc)) Reindex(doc);
            if (doc.Root == null || doc.Components.Count == 0) { return; }
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                bool any = false;
                foreach (AuiNode n in doc.Nodes.ToArray())
                {
                    if (!n.IsComponentTag || n.Parent == null) continue;
                    AuiComponentDecl comp = doc.FindComponent(n.Tag);
                    if (comp == null) continue;
                    ExpandOne(doc, n, comp, d);
                    any = true;
                }
                Reindex(doc);
                if (!any) return;
            }
            d.Error(AuiCodes.NestedViewCycle, "Inline components nest deeper than " + MaxPasses + " levels (a component that uses itself?).", doc.Root.Line, doc.Root.Column, 4);
        }

        private static void ExpandOne(AuiMarkupDocument doc, AuiNode usage, AuiComponentDecl comp, AuiDiagnosticBag d)
        {
            int at = usage.Parent.Children.IndexOf(usage);
            if (comp.Root == null || comp.Root == usage)
            {
                usage.Parent.Children.RemoveAt(at);
                return;
            }
            if (usage.Children.Count > 0)
                d.Error(AuiCodes.ElementNotAllowedHere, "<" + usage.Tag + "> is an inline component and takes no children.", usage.Children[0].Line, usage.Children[0].Column, 1);

            // ---- values: defaults, then the usage's attributes
            var values = new Dictionary<string, string>();
            foreach (AuiPropDecl p in comp.Props) values[p.Name] = p.Default ?? DefaultFor(p.Type);
            foreach (AuiAttribute a in usage.Attributes)
            {
                if (a.Name == "id" || a.Name == "class" || a.Name == "style" || a.Name == "if" || a.Name == "hidden") continue;
                if (a.Name.StartsWith("on-"))
                {
                    d.Error(AuiCodes.UnknownComponentProp, "<" + usage.Tag + "> is an inline component; its handlers are this view's own methods, so on-* events do not apply.", a.Line, a.Column, a.Name.Length);
                    continue;
                }
                if (comp.FindProp(a.Name) == null)
                {
                    var names = new List<string>();
                    foreach (AuiPropDecl p in comp.Props) names.Add(p.Name);
                    string hint = AuiSchema.Suggest(a.Name, names);
                    d.Error(AuiCodes.UnknownComponentProp, "Component '" + usage.Tag + "' has no prop '" + a.Name + "'." + (hint != null ? " Did you mean '" + hint + "'?" : names.Count > 0 ? " Props: " + string.Join(", ", names) + "." : ""), a.Line, a.Column, a.Name.Length);
                    continue;
                }
                values[a.Name] = a.Value;
            }

            // ---- clone
            string prefix = usage.Id != null ? usage.Id + "_" : null;
            AuiNode clone = Clone(comp.Root, values, prefix, usage);
            clone.Parent = usage.Parent;
            usage.Parent.Children[at] = clone;

            // ---- merge the usage's common attributes onto the clone root
            foreach (AuiAttribute a in usage.Attributes)
            {
                if (a.Name == "id") { Set(clone, "id", a.Value, a); continue; }
                if (a.Name == "if" || a.Name == "hidden") { Set(clone, a.Name, a.Value, a); continue; }
                if (a.Name == "class")
                {
                    string cur = clone.Get("class");
                    Set(clone, "class", string.IsNullOrEmpty(cur) ? a.Value : cur + " " + a.Value, a);
                    continue;
                }
                if (a.Name == "style")
                {
                    string cur = clone.Get("style");
                    Set(clone, "style", string.IsNullOrEmpty(cur) ? a.Value : cur.TrimEnd(';', ' ') + "; " + a.Value, a);
                    continue;
                }
            }
        }

        private static string DefaultFor(string type)
        {
            switch (type)
            {
                case "int": return "0";
                case "float": return "0";
                case "bool": return "false";
            }
            return "";
        }

        private static void Set(AuiNode n, string name, string value, AuiAttribute from)
        {
            AuiAttribute a = n.FindAttribute(name);
            if (a == null)
            {
                a = new AuiAttribute { Name = name, Line = from.Line, Column = from.Column, ValueLine = from.ValueLine, ValueColumn = from.ValueColumn };
                n.Attributes.Add(a);
            }
            a.Value = value;
        }

        private static AuiNode Clone(AuiNode src, Dictionary<string, string> values, string prefix, AuiNode usage)
        {
            var n = new AuiNode
            {
                Tag = src.Tag,
                Text = src.Text != null ? Substitute(src.Text, values) : null,
                SelfClosing = src.SelfClosing,
                Line = src.Line, Column = src.Column, EndLine = src.EndLine, EndColumn = src.EndColumn,
                ExpandedFrom = usage,
            };
            foreach (AuiAttribute a in src.Attributes)
            {
                string v = Substitute(a.Value, values);
                if (a.Name == "id" && prefix != null && !string.IsNullOrEmpty(v)) v = prefix + v;
                n.Attributes.Add(new AuiAttribute { Name = a.Name, Value = v, Line = a.Line, Column = a.Column, ValueLine = a.ValueLine, ValueColumn = a.ValueColumn });
            }
            // <text>{prop}</text> given a {binding} of the host becomes a bound text, not the literal "{binding}".
            if (n.Tag == "text" && n.Text != null && n.Get("text") == null && n.Get("key") == null && AuiAnalyzer.IsBindingValue(n.Text) && AuiSchema.IsIdentifier(AuiAnalyzer.BindingName(n.Text)))
            {
                n.Attributes.Add(new AuiAttribute { Name = "text", Value = n.Text, Line = src.Line, Column = src.Column, ValueLine = src.Line, ValueColumn = src.Column });
                n.Text = null;
            }
            foreach (AuiNode c in src.Children)
            {
                AuiNode cc = Clone(c, values, prefix, usage);
                cc.Parent = n;
                n.Children.Add(cc);
            }
            return n;
        }

        /// <summary>Replace every {name} whose name is a prop; other {tokens} (host bindings) are left as they are.</summary>
        public static string Substitute(string s, Dictionary<string, string> values)
        {
            if (s == null || s.IndexOf('{') < 0) return s;
            var sb = new StringBuilder();
            int i = 0;
            while (i < s.Length)
            {
                int o = s.IndexOf('{', i);
                if (o < 0) { sb.Append(s, i, s.Length - i); break; }
                int c = s.IndexOf('}', o);
                if (c < 0) { sb.Append(s, i, s.Length - i); break; }
                string name = s.Substring(o + 1, c - o - 1).Trim();
                string v;
                sb.Append(s, i, o - i);
                if (values.TryGetValue(name, out v)) sb.Append(v);
                else sb.Append(s, o, c - o + 1);
                i = c + 1;
            }
            return sb.ToString();
        }

        /// <summary>Rebuild the document-order node list after expansion (Index == position).</summary>
        /// <summary>
        /// Gives a &lt;select max="N"&gt; its N rows.
        ///
        /// `max` is documented as reserving rows the view fills at runtime with
        /// SetSelectOptionCount / SetSelectOptionLabel, and the analyzer has always parsed and
        /// range-checked it, but nothing ever baked those rows. The index map walks a select's
        /// authored children and only those, so a select with one &lt;option&gt; and max="20" got
        /// exactly one row, and SetSelectOptionLabel then dropped every index past 0 in silence
        /// (its `index >= selectOptionCount` guard). Measured on a four-filter panel: each popup
        /// showed one row and the other nineteen writes went nowhere.
        ///
        /// A reserved row is a copy of the last authored option carrying only its class and
        /// style, so it MEASURES like its siblings, plus an empty literal text so the builder
        /// gives it a label to write into. Without one, `hasLabel` is false, no TMP is created
        /// and SetSelectOptionLabel silently writes to a null.
        /// </summary>
        private static bool ExpandSelectRows(AuiMarkupDocument doc)
        {
            bool any = false;
            foreach (AuiNode n in doc.Nodes.ToArray())
            {
                if (n.Tag != "select") continue;
                AuiAttribute ma = n.FindAttribute("max");
                if (ma == null) continue;
                int max;
                // A malformed or out-of-range max is the analyzer's error to report, not ours.
                if (!int.TryParse(ma.Value, out max) || max < 1 || max > 32) continue;

                var options = new List<AuiNode>();
                foreach (AuiNode c in n.Children) if (c.Tag == "option") options.Add(c);
                if (options.Count == 0 || options.Count >= max) continue;

                AuiNode last = options[options.Count - 1];
                n.ReservedFrom = options.Count;
                for (int i = options.Count; i < max; i++)
                {
                    var row = new AuiNode
                    {
                        Tag = "option",
                        Text = "",
                        SelfClosing = true,
                        Line = last.Line, Column = last.Column,
                        EndLine = last.EndLine, EndColumn = last.EndColumn,
                        ExpandedFrom = last,
                        Parent = n,
                    };
                    // Class and style only. Never the key, the text, the value or the id: a
                    // reserved row has no content of its own and two elements may not share an id.
                    foreach (AuiAttribute a in last.Attributes)
                    {
                        if (a.Name != "class" && a.Name != "style") continue;
                        row.Attributes.Add(new AuiAttribute
                        {
                            Name = a.Name, Value = a.Value,
                            Line = a.Line, Column = a.Column,
                            ValueLine = a.ValueLine, ValueColumn = a.ValueColumn,
                        });
                    }
                    n.Children.Add(row);
                    any = true;
                }
            }
            return any;
        }

        public static void Reindex(AuiMarkupDocument doc)
        {
            doc.Nodes.Clear();
            if (doc.Root != null) Collect(doc.Root, doc.Nodes);
            for (int i = 0; i < doc.Nodes.Count; i++) doc.Nodes[i].Index = i;
        }

        private static void Collect(AuiNode n, List<AuiNode> list)
        {
            list.Add(n);
            foreach (AuiNode c in n.Children) Collect(c, list);
        }
    }
}
