using System.Collections.Generic;

namespace Armagan.AUI.Core
{
    /// <summary>What the analyzer may ask its host about the world outside this file.</summary>
    public sealed class AuiAnalysisContext
    {
        /// <summary>File stem the root's name must match; null skips that check.</summary>
        public string FileStem;
        /// <summary>Whether a nested view of this name exists; null skips the check.</summary>
        public System.Func<string, bool> ViewExists;
        /// <summary>Whether an icon of this name exists; null skips the check.</summary>
        public System.Func<string, bool> IconExists;
        /// <summary>Analyzed model of a nested view by name (declared props and events); null = unknown.</summary>
        public System.Func<string, AuiViewInfo> ChildInfo;
    }

    /// <summary>
    /// The semantic pass over a parsed document. It validates the tree against the schema
    /// and derives the model the code generator needs: bindings with inferred types, handler
    /// signatures, repeats, page groups, selects, translation keys and nested views. Like the
    /// parsers it never throws; problems are diagnostics with positions.
    /// </summary>
    public static class AuiAnalyzer
    {
        public static AuiViewInfo Analyze(AuiMarkupDocument doc, AuiAnalysisContext ctx, AuiDiagnosticBag d)
        {
            var info = new AuiViewInfo { Doc = doc };
            if (ctx == null) ctx = new AuiAnalysisContext();
            if (doc.Root == null) return info;

            AnalyzeRoot(doc, ctx, info, d);
            CollectRepeats(doc, info, d);
            CollectPageGroups(doc, info, d);
            CollectSelects(doc, info, d);
            CollectModals(doc, info, d);

            var ids = new Dictionary<string, AuiNode>();
            foreach (AuiNode n in doc.Nodes) AnalyzeNode(n, ctx, info, ids, d);

            return info;
        }

        // ---------------------------------------------------------------- root

        private static void AnalyzeRoot(AuiMarkupDocument doc, AuiAnalysisContext ctx, AuiViewInfo info, AuiDiagnosticBag d)
        {
            AuiNode root = doc.Root;
            if (root.Tag != "view")
            {
                d.Error(AuiCodes.RootMustBeView, "The root element must be <view>, not <" + root.Tag + ">.", root.Line, root.Column, root.Tag.Length + 1);
            }
            info.Name = root.Get("name");
            if (string.IsNullOrEmpty(info.Name))
                d.Error(AuiCodes.MissingRequiredAttribute, "<view> needs a name attribute; it becomes the generated class.", root.Line, root.Column, 5);
            else if (!AuiSchema.IsIdentifier(info.Name))
                d.Error(AuiCodes.BadAttributeValue, "View name '" + info.Name + "' is not a valid C# identifier.", root.FindAttribute("name").ValueLine, root.FindAttribute("name").ValueColumn, info.Name.Length);
            else if (ctx.FileStem != null && ctx.FileStem != info.Name)
                d.Error(AuiCodes.ViewNameMismatch, "View name '" + info.Name + "' must equal the file name '" + ctx.FileStem + "'.", root.FindAttribute("name").ValueLine, root.FindAttribute("name").ValueColumn, info.Name.Length);

            info.Width = NumberAttr(root, "width", 400, d);
            info.Height = NumberAttr(root, "height", 300, d);

            // <prop name type default/>: a typed binding that exists whether or not the markup uses it.
            foreach (AuiPropDecl p in doc.Props)
            {
                if (!AuiSchema.IsIdentifier(p.Name)) { d.Error(AuiCodes.BadPropDeclaration, "'" + p.Name + "' is not a valid prop name.", p.Line, p.Column, 5); continue; }
                AuiBinding existing = info.FindBinding(p.Name, null);
                if (existing != null) { d.Error(AuiCodes.DuplicateId, "Prop '" + p.Name + "' is declared twice.", p.Line, p.Column, 5); continue; }
                var b = new AuiBinding { Name = p.Name, Type = TypeOf(p.Type), IsProp = true, Default = p.Default };
                info.Bindings.Add(b);
            }
            foreach (AuiEventDecl ev in doc.Events)
            {
                if (!AuiSchema.IsIdentifier(ev.Name.Replace('-', '_'))) { d.Error(AuiCodes.BadPropDeclaration, "'" + ev.Name + "' is not a valid event name.", ev.Line, ev.Column, 6); continue; }
                if (info.FindEvent(ev.Name) != null) { d.Error(AuiCodes.DuplicateId, "Event '" + ev.Name + "' is declared twice.", ev.Line, ev.Column, 6); continue; }
                info.Events.Add(ev);
            }
        }

        public static AuiBindingType TypeOf(string type)
        {
            switch (type)
            {
                case "int": return AuiBindingType.Int;
                case "float": return AuiBindingType.Float;
                case "bool": return AuiBindingType.Bool;
            }
            return AuiBindingType.String;
        }

        private static float NumberAttr(AuiNode n, string name, float fallback, AuiDiagnosticBag d)
        {
            AuiAttribute a = n.FindAttribute(name);
            if (a == null) return fallback;
            float v;
            if (AuiNum.TryParse(a.Value, out v) && v > 0) return v;
            d.Error(AuiCodes.BadAttributeValue, "'" + name + "' must be a positive number of millimetres.", a.ValueLine, a.ValueColumn, a.Value.Length);
            return fallback;
        }

        // ---------------------------------------------------------------- structure passes

        private static void CollectRepeats(AuiMarkupDocument doc, AuiViewInfo info, AuiDiagnosticBag d)
        {
            foreach (AuiNode n in doc.Nodes)
            {
                if (n.Tag != "repeat" && n.Tag != "template") continue;
                bool tpl = n.Tag == "template";
                var r = new AuiRepeatInfo { Node = n, Id = n.Id, IsTemplate = tpl, PoolTag = n.Get("pool"), SlotId = n.Get("slot"), SlotView = n.Get("view") };
                if (string.IsNullOrEmpty(r.Id))
                {
                    d.Error(AuiCodes.MissingRequiredAttribute, "<" + n.Tag + "> needs an id; it names the generated " + (tpl ? "T_ID / CreateId / SetIdX" : "SetIdCount / SetIdX") + " members.", n.Line, n.Column, n.Tag.Length + 1);
                    r.Id = n.Tag + info.Repeats.Count;
                }
                int max;
                AuiAttribute ma = n.FindAttribute("max");
                if (ma == null || !int.TryParse(ma.Value, out max) || max < 1 || max > 64)
                {
                    d.Error(AuiCodes.RepeatMaxOutOfRange, "<" + n.Tag + "> needs max=\"N\" with 1 <= N <= 64; every instance is baked.", ma != null ? ma.Line : n.Line, ma != null ? ma.Column : n.Column, ma != null ? ma.Name.Length : n.Tag.Length + 1);
                    max = 1;
                }
                r.Max = max;
                foreach (AuiNode c in n.Children)
                {
                    if (c.Tag == "empty" && !tpl) { r.Empty = c; continue; }
                    if (r.Template == null) r.Template = c;
                    else d.Error(AuiCodes.RepeatTemplateCount, "<" + n.Tag + "> takes exactly one template child" + (tpl ? "" : " (plus an optional <empty>)") + "; wrap several in a <panel>.", c.Line, c.Column, c.Tag.Length + 1);
                }
                if (r.Template == null)
                    d.Error(AuiCodes.RepeatTemplateCount, "<" + n.Tag + "> has no template child.", n.Line, n.Column, n.Tag.Length + 1);
                if (tpl && n.Parent != null && n.Parent.Tag != "view")
                    d.Error(AuiCodes.ElementNotAllowedHere, "<template> must be a direct child of <view>; its instances are linked under any parent by CreateElement.", n.Line, n.Column, 9);
                foreach (AuiRepeatInfo outer in info.Repeats)
                    if (outer.Template != null && IsInside(n, outer.Template))
                        d.Error(AuiCodes.ElementNotAllowedHere, "A <repeat> or <template> cannot sit inside another one.", n.Line, n.Column, n.Tag.Length + 1);
                if (r.Template != null)
                    foreach (AuiNode inner in doc.Nodes)
                    {
                        if (inner == n || !IsInside(inner, r.Template)) continue;
                        if (tpl && (inner.Tag == "modal" || inner.Tag == "template" || inner.Tag == "repeat"))
                            d.Error(AuiCodes.ElementNotAllowedHere, "<" + inner.Tag + "> cannot be inside a <template>.", inner.Line, inner.Column, inner.Tag.Length + 1);
                        // A select has a popup and scrim element per NODE (AuiIndexMap.Walk adds them
                        // outside WalkRepeat), and select/pages/tabs each have per-node runtime tables
                        // and one generated setter, not one per row. A select in a row compiled clean
                        // and then threw KeyNotFoundException in the prefab builder.
                        if (inner.Tag == "select" || inner.Tag == "pages" || inner.Tag == "tabs")
                            d.Error(AuiCodes.ElementNotAllowedHere, "<" + inner.Tag + "> cannot be inside a <" + n.Tag + ">; its runtime tables are per element, not per row.", inner.Line, inner.Column, inner.Tag.Length + 1);
                    }
                info.Repeats.Add(r);
            }
        }

        /// <summary>The first node in this subtree (self included) that carries icon= / an &lt;icon&gt; / key=, or null.</summary>
        private static AuiNode FindIconOrKey(AuiNode n)
        {
            if (n.Get("icon") != null || n.Get("key") != null || n.Tag == "icon") return n;
            foreach (AuiNode c in n.Children)
            {
                AuiNode r = FindIconOrKey(c);
                if (r != null) return r;
            }
            return null;
        }

        private static bool IsInside(AuiNode n, AuiNode ancestorOrSelf)
        {
            for (AuiNode p = n; p != null; p = p.Parent) if (p == ancestorOrSelf) return true;
            return false;
        }

        private static void CollectPageGroups(AuiMarkupDocument doc, AuiViewInfo info, AuiDiagnosticBag d)
        {
            foreach (AuiNode n in doc.Nodes)
            {
                if (n.Tag != "pages") continue;
                var g = new AuiPageGroup { Node = n, Id = n.Id ?? "pages" + info.PageGroups.Count, Mode = n.Get("mode", "crossfade") };
                if (g.Mode != "crossfade" && g.Mode != "sequential")
                {
                    AuiAttribute a = n.FindAttribute("mode");
                    d.Error(AuiCodes.BadAttributeValue, "pages mode must be crossfade or sequential.", a.ValueLine, a.ValueColumn, a.Value.Length);
                    g.Mode = "crossfade";
                }
                foreach (AuiNode c in n.Children)
                {
                    if (c.Tag != "page")
                    {
                        d.Error(AuiCodes.ElementNotAllowedHere, "<pages> may only contain <page> elements.", c.Line, c.Column, c.Tag.Length + 1);
                        continue;
                    }
                    if (string.IsNullOrEmpty(c.Id))
                        d.Error(AuiCodes.MissingRequiredAttribute, "<page> needs an id so a <tab page=\"...\"> can reach it.", c.Line, c.Column, 5);
                    g.Pages.Add(c);
                }
                if (g.Pages.Count == 0)
                    d.Error(AuiCodes.ElementNotAllowedHere, "<pages> has no <page> children.", n.Line, n.Column, 6);
                string def = n.Get("default");
                g.DefaultIndex = 0;
                if (def != null)
                {
                    int idx = g.IndexOfPage(def);
                    if (idx < 0) d.Error(AuiCodes.BadAttributeValue, "default=\"" + def + "\" names no page in this group.", n.FindAttribute("default").ValueLine, n.FindAttribute("default").ValueColumn, def.Length);
                    else g.DefaultIndex = idx;
                }
                info.PageGroups.Add(g);
            }

            // Link tab strips.
            foreach (AuiNode n in doc.Nodes)
            {
                if (n.Tag != "tabs") continue;
                AuiPageGroup g = null;
                string forId = n.Get("for");
                if (forId != null)
                {
                    foreach (AuiPageGroup pg in info.PageGroups) if (pg.Id == forId) g = pg;
                    if (g == null) d.Error(AuiCodes.BadAttributeValue, "for=\"" + forId + "\" names no <pages> id.", n.FindAttribute("for").ValueLine, n.FindAttribute("for").ValueColumn, forId.Length);
                }
                else if (info.PageGroups.Count == 1) g = info.PageGroups[0];
                else if (info.PageGroups.Count == 0) d.Error(AuiCodes.PageWithoutTabs, "<tabs> found but the view has no <pages>.", n.Line, n.Column, 5);
                else d.Error(AuiCodes.MissingRequiredAttribute, "More than one <pages> in this view: <tabs> needs for=\"pagesId\".", n.Line, n.Column, 5);

                if (g != null)
                {
                    if (g.Tabs != null) d.Error(AuiCodes.ElementNotAllowedHere, "A <pages> group can have only one <tabs> strip.", n.Line, n.Column, 5);
                    g.Tabs = n;
                }
                foreach (AuiNode t in n.Children)
                {
                    if (t.Tag != "tab")
                    {
                        d.Error(AuiCodes.ElementNotAllowedHere, "<tabs> may only contain <tab> elements.", t.Line, t.Column, t.Tag.Length + 1);
                        continue;
                    }
                    string page = t.Get("page");
                    if (string.IsNullOrEmpty(page))
                    {
                        d.Error(AuiCodes.MissingRequiredAttribute, "<tab> needs page=\"id\".", t.Line, t.Column, 4);
                        continue;
                    }
                    if (g != null)
                    {
                        if (g.IndexOfPage(page) < 0)
                            d.Error(AuiCodes.TabWithoutPage, "No <page id=\"" + page + "\"> in this group.", t.FindAttribute("page").ValueLine, t.FindAttribute("page").ValueColumn, page.Length);
                        else g.TabNodes.Add(t);
                    }
                }
            }
        }

        private static void CollectSelects(AuiMarkupDocument doc, AuiViewInfo info, AuiDiagnosticBag d)
        {
            foreach (AuiNode n in doc.Nodes)
            {
                if (n.Tag != "select") continue;
                var s = new AuiSelectInfo { Node = n };
                foreach (AuiNode c in n.Children)
                {
                    if (c.Tag != "option")
                    {
                        d.Error(AuiCodes.ElementNotAllowedHere, "<select> may only contain <option> elements.", c.Line, c.Column, c.Tag.Length + 1);
                        continue;
                    }
                    s.Options.Add(c);
                }
                int max = s.Options.Count;
                AuiAttribute ma = n.FindAttribute("max");
                if (ma != null && (!int.TryParse(ma.Value, out max) || max < 1 || max > 32))
                {
                    d.Error(AuiCodes.BadAttributeValue, "select max must be 1..32.", ma.ValueLine, ma.ValueColumn, ma.Value.Length);
                    max = System.Math.Max(1, s.Options.Count);
                }
                if (max < s.Options.Count) max = s.Options.Count;
                if (max == 0)
                {
                    d.Error(AuiCodes.MissingRequiredAttribute, "<select> needs <option> children or max=\"N\" for runtime-filled options.", n.Line, n.Column, 6);
                    max = 1;
                }
                s.Max = max;
                info.Selects.Add(s);
            }
        }

        private static void CollectModals(AuiMarkupDocument doc, AuiViewInfo info, AuiDiagnosticBag d)
        {
            foreach (AuiNode n in doc.Nodes)
            {
                if (n.Tag != "modal") continue;
                var m = new AuiModalInfo { Node = n, Id = n.Id };
                if (string.IsNullOrEmpty(m.Id))
                {
                    d.Error(AuiCodes.MissingRequiredAttribute, "<modal> needs an id; it names the generated OpenId / CloseId methods.", n.Line, n.Column, 6);
                    m.Id = "modal" + info.Modals.Count;
                }
                // The builder moves the modal to the end of Content, which is the view's coordinate
                // space; a modal nested deeper would be laid out against the wrong parent.
                if (n.Parent != null && n.Parent.Tag != "view")
                    d.Error(AuiCodes.ElementNotAllowedHere, "<modal> must be a direct child of <view>.", n.Line, n.Column, 6);
                string dis = n.Get("dismissable");
                m.Dismissable = dis == null || dis != "false";
                string en = n.Get("enabled");
                if (en != null && !IsBindingValue(en)) m.StartOpen = en == "true";
                info.Modals.Add(m);
            }
        }

        // ---------------------------------------------------------------- per node

        private static void AnalyzeNode(AuiNode n, AuiAnalysisContext ctx, AuiViewInfo info, Dictionary<string, AuiNode> ids, AuiDiagnosticBag d)
        {
            AuiRepeatInfo repeat = info.RepeatContaining(n);

            // ---- id
            if (n.Id != null)
            {
                AuiNode other;
                if (ids.TryGetValue(n.Id, out other))
                    d.Error(AuiCodes.DuplicateId, "Duplicate id '" + n.Id + "' (first on line " + other.Line + ").", n.FindAttribute("id").ValueLine, n.FindAttribute("id").ValueColumn, n.Id.Length);
                else ids[n.Id] = n;
            }

            // ---- nested view
            if (n.IsComponentTag)
            {
                var nv = new AuiNestedView { Node = n, ViewName = n.Tag, FieldName = "child" + n.Tag + (n.Id != null ? AuiNaming.Pascal(n.Id) : "") };
                if (ctx.ViewExists != null && !ctx.ViewExists(n.Tag))
                    d.Error(AuiCodes.NestedViewNotFound, "No view named '" + n.Tag + "' (expected " + n.Tag + ".aui next to this file or in a component folder).", n.Line, n.Column, n.Tag.Length + 1);
                if (n.Children.Count > 0)
                    d.Error(AuiCodes.ElementNotAllowedHere, "A nested view takes no children in this version.", n.Children[0].Line, n.Children[0].Column, 1);
                // Inside a <template> (a slot) every parked instance is its own prefab copy; inside a
                // <repeat> the rows would all share one generated field, which does not work yet.
                if (repeat != null && !repeat.IsTemplate)
                    d.Error(AuiCodes.ElementNotAllowedHere, "A nested view cannot be a repeat template in this version (use a <slot> or <template>).", n.Line, n.Column, n.Tag.Length + 1);
                info.NestedViews.Add(nv);
                AuiViewInfo child = ctx.ChildInfo != null ? ctx.ChildInfo(n.Tag) : null;
                foreach (AuiAttribute a in n.Attributes)
                {
                    if (AuiSchema.IsCommonAttribute(a.Name)) { CommonAttribute(n, a, repeat, info, d); continue; }
                    if (a.Name.StartsWith("on-"))
                    {
                        AuiHandler h = AddHandler(n, a, null, repeat, info, d);
                        // A declared <event type> gives the host handler its payload parameter.
                        AuiEventDecl ev = child != null ? child.FindEvent(a.Name.Substring(3)) : null;
                        if (h != null && ev != null) h.ParamType = ev.Type;
                        if (child != null && ev == null && child.Events.Count > 0)
                            d.Warning(AuiCodes.UnknownComponentProp, "<" + n.Tag + "> declares no event '" + a.Name.Substring(3) + "' (events: " + string.Join(", ", EventNames(child)) + ").", a.Line, a.Column, a.Name.Length);
                        continue;
                    }
                    // A child that declares props rejects unknown attributes; one that does not still accepts any binding name.
                    if (child != null && child.HasDeclaredProps && child.FindBinding(a.Name, null) == null)
                    {
                        var names = new List<string>();
                        foreach (AuiBinding cb in child.Bindings) names.Add(cb.Name);
                        string hint = AuiSchema.Suggest(a.Name, names);
                        d.Error(AuiCodes.UnknownComponentProp, "<" + n.Tag + "> has no prop '" + a.Name + "'." + (hint != null ? " Did you mean '" + hint + "'?" : " Props: " + string.Join(", ", names) + "."), a.Line, a.Column, a.Name.Length);
                        continue;
                    }
                    AuiBinding childBinding = child != null ? child.FindBinding(a.Name, null) : null;
                    if (IsBindingValue(a.Value)) AddBinding(n, a, childBinding != null ? childBinding.Type : AuiBindingType.Unknown, repeat, info, d);
                    else if (childBinding != null)
                    {
                        // A literal is pasted into the child's setter as C# text (`childX.Ratio = 1.5f;`),
                        // so it has to be a literal of the child's type here, or the generated file
                        // fails to compile with an error the author cannot place.
                        string err = null;
                        float fv; int iv;
                        switch (childBinding.Type)
                        {
                            case AuiBindingType.Float: if (!AuiNum.TryParse(a.Value, out fv)) err = "a number"; break;
                            case AuiBindingType.Int: if (!int.TryParse(a.Value, out iv)) err = "an integer"; break;
                            case AuiBindingType.Bool: if (a.Value != "true" && a.Value != "false") err = "true or false"; break;
                        }
                        if (err != null)
                            d.Error(AuiCodes.BadAttributeValue, "<" + n.Tag + "> prop '" + a.Name + "' takes " + err + " or a {binding}; got '" + a.Value + "'.", a.ValueLine, a.ValueColumn, a.Value.Length);
                    }
                    // Literal values are passed to the child's setter once at Start; the type is the child's.
                }
                return;
            }

            // ---- schema
            AuiElementDef def = AuiSchema.FindElement(n.Tag);
            if (def == null)
            {
                var names = new List<string>();
                foreach (AuiElementDef e in AuiSchema.Elements) names.Add(e.Name);
                string hint = AuiSchema.Suggest(n.Tag, names);
                d.Error(AuiCodes.UnknownTag, "Unknown element <" + n.Tag + ">." + (hint != null ? " Did you mean <" + hint + ">?" : " Nested views start with a capital letter."), n.Line, n.Column, n.Tag.Length + 1);
                return;
            }
            if (n.Tag == "view" && n != info.Doc.Root)
                d.Error(AuiCodes.ElementNotAllowedHere, "<view> is only the root; use <Name/> to nest another view.", n.Line, n.Column, 5);
            if (def.AllowedParents != null)
            {
                bool ok = n.Parent != null;
                if (ok)
                {
                    ok = false;
                    foreach (string p in def.AllowedParents) if (n.Parent.Tag == p) ok = true;
                }
                if (!ok) d.Error(AuiCodes.ElementNotAllowedHere, "<" + n.Tag + "> must be directly inside <" + string.Join("> or <", def.AllowedParents) + ">.", n.Line, n.Column, n.Tag.Length + 1);
            }
            if (!def.AllowsChildren && n.Children.Count > 0)
                d.Error(AuiCodes.ElementNotAllowedHere, "<" + n.Tag + "> takes no children.", n.Children[0].Line, n.Children[0].Column, n.Children[0].Tag.Length + 1);
            if (!def.AllowsText && n.Text != null)
                d.Warning(AuiCodes.ElementNotAllowedHere, "<" + n.Tag + "> ignores literal text; put it in a <text>.", n.Line, n.Column, n.Tag.Length + 1);

            // ---- attributes
            foreach (AuiAttribute a in n.Attributes)
            {
                if (AuiSchema.IsCommonAttribute(a.Name)) { CommonAttribute(n, a, repeat, info, d); continue; }
                if (def.HasEvent(a.Name)) { AddHandler(n, a, def, repeat, info, d); continue; }
                if (!def.HasAttribute(a.Name))
                {
                    var names = new List<string>(def.Attributes);
                    names.AddRange(def.Events);
                    names.AddRange(AuiSchema.CommonAttributes);
                    string hint = AuiSchema.Suggest(a.Name, names);
                    d.Error(AuiCodes.UnknownAttribute, "<" + n.Tag + "> has no attribute '" + a.Name + "'." + (hint != null ? " Did you mean '" + hint + "'?" : ""), a.Line, a.Column, a.Name.Length);
                    continue;
                }
                ElementAttribute(n, a, repeat, info, ctx, d);
            }

            // ---- element-level checks
            switch (n.Tag)
            {
                case "text":
                    if ((n.Get("key") != null && (n.Get("text") != null || n.Text != null)) || (n.Get("text") != null && n.Text != null))
                        d.Error(AuiCodes.TextAndKeyBoth, "<text> takes key= OR text= OR literal content, not several.", n.Line, n.Column, 5);
                    else if (n.Get("key") == null && n.Get("text") == null && n.Text == null && !(n.Parent != null && n.Parent.Tag == "template" && n.Parent.Get("pool") != null))
                        d.Warning(AuiCodes.MissingRequiredAttribute, "<text> with nothing to show: give it key=, text=\"{binding}\" or literal content.", n.Line, n.Column, 5);
                    if (n.Get("args") != null && n.Get("key") == null)
                        d.Error(AuiCodes.PlaceholderNotBound, "args= only makes sense with key=; placeholders live in the translated string.", n.FindAttribute("args").Line, n.FindAttribute("args").Column, 4);
                    break;
                case "icon":
                    if (n.Get("name") == null) d.Error(AuiCodes.MissingRequiredAttribute, "<icon> needs name=\"lucide-name\".", n.Line, n.Column, 5);
                    else if (!IsBindingValue(n.Get("name")) && ctx.IconExists != null && !ctx.IconExists(n.Get("name")))
                        d.Error(AuiCodes.UnknownIcon, "No icon named '" + n.Get("name") + "'.", n.FindAttribute("name").ValueLine, n.FindAttribute("name").ValueColumn, n.Get("name").Length);
                    break;
                case "image":
                    if (n.Get("src") == null) d.Error(AuiCodes.MissingRequiredAttribute, "<image> needs src=.", n.Line, n.Column, 6);
                    break;
                case "toggle":
                case "slider":
                case "input":
                case "select":
                    if (n.Get("bind") == null)
                        d.Warning(AuiCodes.MissingRequiredAttribute, "<" + n.Tag + "> without bind=\"{name}\" has no generated property; only its handler will run.", n.Line, n.Column, n.Tag.Length + 1);
                    break;
                case "option":
                    if (n.Get("key") == null && n.Text == null)
                        d.Warning(AuiCodes.MissingRequiredAttribute, "<option> has no key= and no literal label.", n.Line, n.Column, 7);
                    break;
                case "tab":
                    if (n.Get("key") == null && n.Text == null && n.Get("icon") == null && n.Children.Count == 0)
                        d.Warning(AuiCodes.MissingRequiredAttribute, "<tab> has nothing to show; give it key=, icon= or content.", n.Line, n.Column, 4);
                    break;
                case "button":
                    if (n.Get("key") == null && n.Text == null && n.Get("icon") == null && n.Children.Count == 0)
                        d.Warning(AuiCodes.MissingRequiredAttribute, "<button> has nothing to show; give it key=, icon= or content.", n.Line, n.Column, 7);
                    break;
            }

            // ---- literal text as a translation-less label
            // An EMPTY literal has nothing to translate, so it is not worth a line. That covers
            // `<text> </text>`, which the parser trims to "", and the empty label every reserved
            // <select> row carries — which would otherwise report once per reserved row.
            if (n.Text != null && n.Text.Length > 0 && def.AllowsText && n.Get("key") == null && n.Get("text") == null && n.Tag != "empty")
            {
                // Allowed, but a translated view usually wants a key. Only an info so single-language worlds stay quiet.
                d.Info(AuiCodes.LiteralTextInTranslatedView, "Literal text '" + Shorten(n.Text) + "' will not be translated; use key=.", n.Line, n.Column, n.Tag.Length + 1);
            }
        }

        private static void CommonAttribute(AuiNode n, AuiAttribute a, AuiRepeatInfo repeat, AuiViewInfo info, AuiDiagnosticBag d)
        {
            switch (a.Name)
            {
                case "if":
                    if (!IsBindingValue(a.Value))
                    {
                        d.Error(AuiCodes.BadBindingSyntax, "if= takes a binding: if=\"{flag}\".", a.ValueLine, a.ValueColumn, a.Value.Length);
                        return;
                    }
                    AddBinding(n, a, AuiBindingType.Bool, repeat, info, d);
                    break;
                case "hidden":
                    if (a.Value != "true" && a.Value != "false" && a.Value != "")
                        d.Error(AuiCodes.BadAttributeValue, "hidden is a flag: hidden=\"true\" or just hidden. For a runtime value use if=\"{flag}\".", a.ValueLine, a.ValueColumn, a.Value.Length);
                    else if (a.Value != "false")
                    {
                        // The baker does not lay out a hidden subtree (REFERENCE section 15): an
                        // icon-only clickable bakes its glyph at -9 and nothing ever moves it, and a
                        // keyed label measures at zero width until the page re-measures. Both were
                        // shipped bugs; warn at authoring time instead of after a world upload.
                        AuiNode bad = FindIconOrKey(n);
                        if (bad != null)
                            d.Warning(AuiCodes.BadAttributeValue, "hidden on an element with icon= or key= bakes it at the wrong size (the hidden subtree is never laid out); bake it visible and use if=\"{flag}\" instead.", a.Line, a.Column, a.Name.Length);
                    }
                    break;
                case "id":
                    if (!AuiSchema.IsIdentifier(AuiNaming.Identifier(a.Value)) || a.Value.Length == 0)
                        d.Error(AuiCodes.BadAttributeValue, "id must be a plain identifier.", a.ValueLine, a.ValueColumn, a.Value.Length);
                    break;
                case "style":
                {
                    // style="width: {w}; color: {c}": each bound declaration is a typed binding of this view.
                    AuiStyleRule rule = AuiStyleParser.ParseDeclarations(a.Value, "", a.ValueLine, a.ValueColumn, null);
                    foreach (AuiDeclaration decl in rule.Declarations)
                    {
                        if (!AuiStyleResolver.IsBoundValue(decl.Value)) continue;
                        AuiPropDef def = AuiSchema.FindProp(decl.Property);
                        if (def == null || AuiStyleResolver.CheckBoundValue(def, decl.Value) != null) continue;   // reported by the resolver
                        string[] toks = decl.Value.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < toks.Length; i++)
                        {
                            AuiBindingType t;
                            switch (def.Kind)
                            {
                                case AuiPropKind.Number: case AuiPropKind.Offset: t = AuiBindingType.Float; break;
                                case AuiPropKind.Color: case AuiPropKind.Background: t = AuiBindingType.Color; break;
                                default: t = AuiBindingType.String; break;   // Length, Box (parsed in Udon: 12, 50%, auto), Enum
                            }
                            AuiBinding b = AddBindingNamed(n, a, BindingName(toks[i]), IsItemBinding(toks[i]), t, repeat, info, d);
                            if (b == null) continue;
                            AuiBindingUse use = b.Uses[b.Uses.Count - 1];
                            use.StyleProp = decl.Property;
                            use.StyleIndex = i;
                        }
                    }
                    break;
                }
                case "class":
                {
                    // class="card {tone} {extra}": literal names are static, each {binding} is a
                    // string of space-separated names that gets its own runtime slot.
                    int slot = 0;
                    foreach (string tok in a.Value.Split(new[] { ' ', '\t', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (tok[0] != '{')
                        {
                            if (tok.IndexOf('}') >= 0) d.Error(AuiCodes.BadBindingSyntax, "Class token '" + tok + "' mixes a name and a binding; separate them with a space.", a.ValueLine, a.ValueColumn, a.Value.Length);
                            continue;
                        }
                        if (!IsBindingValue(tok)) { d.Error(AuiCodes.BadBindingSyntax, "Class token '" + tok + "' is not a binding like {tone}.", a.ValueLine, a.ValueColumn, a.Value.Length); continue; }
                        AuiBinding b = AddBindingNamed(n, a, BindingName(tok), IsItemBinding(tok), AuiBindingType.String, repeat, info, d);
                        if (b != null) b.Uses[b.Uses.Count - 1].Slot = slot;
                        slot++;
                    }
                    break;
                }
            }
        }

        private static void ElementAttribute(AuiNode n, AuiAttribute a, AuiRepeatInfo repeat, AuiViewInfo info, AuiAnalysisContext ctx, AuiDiagnosticBag d)
        {
            switch (a.Name)
            {
                case "text":
                    if (!IsBindingValue(a.Value)) { d.Error(AuiCodes.BadBindingSyntax, "text= takes a binding: text=\"{name}\". For a literal, write it as content.", a.ValueLine, a.ValueColumn, a.Value.Length); return; }
                    AddBinding(n, a, AuiBindingType.String, repeat, info, d);
                    break;
                case "bind":
                {
                    if (!IsBindingValue(a.Value)) { d.Error(AuiCodes.BadBindingSyntax, "bind= takes a binding: bind=\"{name}\".", a.ValueLine, a.ValueColumn, a.Value.Length); return; }
                    AuiBindingType t = n.Tag == "toggle" ? AuiBindingType.Bool : n.Tag == "slider" ? AuiBindingType.Float : n.Tag == "input" ? AuiBindingType.String : AuiBindingType.Int;
                    AuiBinding b = AddBinding(n, a, t, repeat, info, d);
                    if (b != null)
                    {
                        if (n.Tag == "select") foreach (AuiSelectInfo s in info.Selects) if (s.Node == n) s.Binding = b;
                        if (n.Tag == "tabs") foreach (AuiPageGroup g in info.PageGroups) if (g.Tabs == n) g.Binding = b;
                    }
                    break;
                }
                case "disabled":
                case "selected":
                case "enabled":
                    if (IsBindingValue(a.Value))
                    {
                        AuiBinding b = AddBinding(n, a, AuiBindingType.Bool, repeat, info, d);
                        if (b != null && a.Name == "enabled") foreach (AuiModalInfo m in info.Modals) if (m.Node == n) m.Binding = b;
                    }
                    else if (a.Value != "true" && a.Value != "false" && a.Value != "")
                        d.Error(AuiCodes.BadAttributeValue, a.Name + " takes true, false or a {binding}.", a.ValueLine, a.ValueColumn, a.Value.Length);
                    break;
                case "dismissable":
                    if (a.Value != "true" && a.Value != "false" && a.Value != "")
                        d.Error(AuiCodes.BadAttributeValue, "dismissable is a flag: true or false.", a.ValueLine, a.ValueColumn, a.Value.Length);
                    break;
                case "key":
                case "placeholder-key":
                case "format-key":
                {
                    if (a.Value.Length == 0 || IsBindingValue(a.Value)) { d.Error(AuiCodes.BadAttributeValue, a.Name + " is a translation key such as settings.title, not a binding.", a.ValueLine, a.ValueColumn, a.Value.Length); return; }
                    var use = new AuiKeyUse { Key = a.Value, Node = n, Attribute = a };
                    string args = n.Get("args");
                    if (a.Name == "key" && args != null)
                    {
                        AuiAttribute aa = n.FindAttribute("args");
                        foreach (string item in args.Split(new[] { ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (!IsBindingValue(item)) { d.Error(AuiCodes.BadBindingSyntax, "args= is a list of bindings: args=\"{count} {name}\".", aa.ValueLine, aa.ValueColumn, aa.Value.Length); continue; }
                            AuiBinding b = AddBindingNamed(n, aa, BindingName(item), IsItemBinding(item), AuiBindingType.String, repeat, info, d);
                            if (b != null) use.Args.Add(b);
                        }
                    }
                    info.Keys.Add(use);
                    break;
                }
                case "args":
                    // handled with key
                    break;
                case "pool":
                case "slot":
                case "view":
                case "views":
                    break;   // handled by the pool synthesizer
                case "min":
                case "max":
                case "step":
                case "size":
                case "maxlength":
                case "decimals":
                case "max-lines":
                {
                    float v;
                    if (n.Tag == "repeat" || n.Tag == "template" || n.Tag == "select") break; // validated in the collect passes
                    if (!AuiNum.TryParse(a.Value, out v)) d.Error(AuiCodes.BadAttributeValue, a.Name + " must be a number.", a.ValueLine, a.ValueColumn, a.Value.Length);
                    break;
                }
                case "mode":
                    if (n.Tag == "image" && a.Value != "simple" && a.Value != "sliced" && a.Value != "fit")
                        d.Error(AuiCodes.BadAttributeValue, "image mode must be simple, sliced or fit.", a.ValueLine, a.ValueColumn, a.Value.Length);
                    break;
                case "type":
                    if (n.Tag == "input")
                    {
                        if (a.Value != "text" && a.Value != "url")
                            d.Error(AuiCodes.BadAttributeValue, "input type must be text or url.", a.ValueLine, a.ValueColumn, a.Value.Length);
                        // The URL field is VRChat's own component; its text is not ours to cut.
                        else if (a.Value == "url" && n.Get("maxlength") != null)
                            d.Warning(AuiCodes.BadAttributeValue, "maxlength is ignored on a url input; VRChat's URL field owns the text.", n.FindAttribute("maxlength").Line, n.FindAttribute("maxlength").Column, 9);
                    }
                    break;
                case "wrap":
                case "ellipsis":
                case "runtime":
                case "start-hidden":
                    if (a.Value != "true" && a.Value != "false" && a.Value != "")
                        d.Error(AuiCodes.BadAttributeValue, a.Name + " is a flag: true or false.", a.ValueLine, a.ValueColumn, a.Value.Length);
                    break;
                case "show":
                {
                    AuiPropDef p = AuiSchema.FindProp("show");
                    bool ok = false;
                    foreach (string e in p.EnumValues) if (e == a.Value) ok = true;
                    if (!ok) d.Error(AuiCodes.BadAttributeValue, "show must be one of " + string.Join(" | ", p.EnumValues) + ".", a.ValueLine, a.ValueColumn, a.Value.Length);
                    break;
                }
                case "name":
                    if (n.Tag == "icon" && IsBindingValue(a.Value)) AddBinding(n, a, AuiBindingType.String, repeat, info, d);
                    break;
                case "icon":
                    if (IsBindingValue(a.Value)) AddBinding(n, a, AuiBindingType.String, repeat, info, d);
                    else if (ctx.IconExists != null && !ctx.IconExists(a.Value))
                        d.Error(AuiCodes.UnknownIcon, "No icon named '" + a.Value + "'.", a.ValueLine, a.ValueColumn, a.Value.Length);
                    break;
                case "icons":
                    // <view icons="a b"> preloads icon sprites for SetIcon; every name must exist.
                    foreach (string icon in a.Value.Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries))
                        if (ctx.IconExists != null && !ctx.IconExists(icon))
                            d.Error(AuiCodes.UnknownIcon, "No icon named '" + icon + "' (in icons=).", a.ValueLine, a.ValueColumn, a.Value.Length);
                    break;
                case "sprites":
                case "materials":
                    break;   // resolved by the builder (a missing asset logs there)
            }
        }

        // ---------------------------------------------------------------- bindings and handlers

        public static bool IsBindingValue(string v)
        {
            return v != null && v.Length > 2 && v[0] == '{' && v[v.Length - 1] == '}';
        }

        /// <summary>&lt;input type="url"&gt;: armed on VRChat's VRCUrlInputField, submits a VRCUrl.</summary>
        public static bool IsUrlInput(AuiNode n)
        {
            return n != null && n.Tag == "input" && n.Get("type") == "url";
        }

        public static string BindingName(string v)
        {
            string inner = v.Substring(1, v.Length - 2).Trim();
            return inner.StartsWith("item.") ? inner.Substring(5) : inner;
        }

        public static bool IsItemBinding(string v)
        {
            return v.Substring(1, v.Length - 2).Trim().StartsWith("item.");
        }

        private static AuiBinding AddBinding(AuiNode n, AuiAttribute a, AuiBindingType type, AuiRepeatInfo repeat, AuiViewInfo info, AuiDiagnosticBag d)
        {
            return AddBindingNamed(n, a, BindingName(a.Value), IsItemBinding(a.Value), type, repeat, info, d);
        }

        private static AuiBinding AddBindingNamed(AuiNode n, AuiAttribute a, string name, bool isItem, AuiBindingType type, AuiRepeatInfo repeat, AuiViewInfo info, AuiDiagnosticBag d)
        {
            if (!AuiSchema.IsIdentifier(name))
            {
                d.Error(AuiCodes.BadBindingSyntax, "'" + name + "' is not a valid binding name (letters, digits, underscore).", a.ValueLine, a.ValueColumn, a.Value.Length);
                return null;
            }
            if (isItem && repeat == null)
            {
                d.Error(AuiCodes.ItemBindingOutsideRepeat, "{item." + name + "} is only valid inside a <repeat> template.", a.ValueLine, a.ValueColumn, a.Value.Length);
                return null;
            }
            AuiRepeatInfo target = isItem ? repeat : null;
            AuiBinding b = info.FindBinding(name, target);
            if (b == null)
            {
                b = new AuiBinding { Name = name, Type = type, IsItem = isItem, Repeat = target };
                if (target != null) target.ItemBindings.Add(b); else info.Bindings.Add(b);
            }
            else if (b.Type == AuiBindingType.Unknown) b.Type = type;
            else if (type != AuiBindingType.Unknown && b.Type != type)
            {
                AuiBindingUse first = b.Uses.Count > 0 ? b.Uses[0] : null;
                d.Error(AuiCodes.BindingTypeConflict, "{" + name + "} is used as " + AuiNaming.CsType(type) + " here but as " + b.CsType + (first != null ? " on line " + first.Node.Line : "") + ".", a.ValueLine, a.ValueColumn, a.Value.Length);
            }
            b.Uses.Add(new AuiBindingUse { Node = n, Attribute = a.Name, Type = type });
            return b;
        }

        private static List<string> EventNames(AuiViewInfo child)
        {
            var names = new List<string>();
            foreach (AuiEventDecl e in child.Events) names.Add(e.Name);
            return names;
        }

        private static AuiHandler AddHandler(AuiNode n, AuiAttribute a, AuiElementDef def, AuiRepeatInfo repeat, AuiViewInfo info, AuiDiagnosticBag d)
        {
            if (!AuiSchema.IsIdentifier(a.Value))
            {
                d.Error(AuiCodes.HandlerNotIdentifier, a.Name + "=\"" + a.Value + "\" must name a method (letters, digits, underscore).", a.ValueLine, a.ValueColumn, a.Value.Length);
                return null;
            }
            var h = new AuiHandler { Name = a.Value, Event = a.Name, Node = n, HasRow = repeat != null, Repeat = repeat };
            switch (a.Name)
            {
                case "onchange":
                    h.ParamType = n.Tag == "toggle" ? "bool" : n.Tag == "slider" ? "float" : "int";
                    break;
                case "onsubmit":
                    // A url input's submit is the VRCUrl itself: Udon cannot build one from a string,
                    // so handing over the text would leave the author with nothing loadable.
                    h.ParamType = IsUrlInput(n) ? "VRCUrl" : "string";
                    break;
                case "onselect":
                    h.HasRow = true; // the repeat's own row event
                    h.Repeat = null;
                    foreach (AuiRepeatInfo r in info.Repeats) if (r.Node == n) h.Repeat = r;
                    break;
                default:
                    if (a.Name.StartsWith("on-")) h.ParamType = null; // child events carry no payload in v1
                    break;
            }
            AuiHandler existing = info.FindHandler(h.Name);
            if (existing != null && (existing.ParamType != h.ParamType || existing.HasRow != h.HasRow))
            {
                d.Error(AuiCodes.BindingTypeConflict, "Handler '" + h.Name + "' is used with different signatures: " + existing.Signature + " (line " + existing.Node.Line + ") and " + h.Signature + ".", a.ValueLine, a.ValueColumn, a.Value.Length);
                return null;
            }
            info.Handlers.Add(h);
            if (repeat != null) repeat.Handlers.Add(h);
            return h;
        }

        private static string Shorten(string s)
        {
            return s.Length > 24 ? s.Substring(0, 24) + "..." : s;
        }
    }
}
