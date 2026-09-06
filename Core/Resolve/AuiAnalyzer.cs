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
                if (n.Tag != "repeat") continue;
                var r = new AuiRepeatInfo { Node = n, Id = n.Id };
                if (string.IsNullOrEmpty(r.Id))
                {
                    d.Error(AuiCodes.MissingRequiredAttribute, "<repeat> needs an id; it names the generated SetIdCount / SetIdX methods.", n.Line, n.Column, 7);
                    r.Id = "repeat" + info.Repeats.Count;
                }
                int max;
                AuiAttribute ma = n.FindAttribute("max");
                if (ma == null || !int.TryParse(ma.Value, out max) || max < 1 || max > 64)
                {
                    d.Error(AuiCodes.RepeatMaxOutOfRange, "<repeat> needs max=\"N\" with 1 <= N <= 64; every instance is baked.", ma != null ? ma.Line : n.Line, ma != null ? ma.Column : n.Column, ma != null ? ma.Name.Length : 7);
                    max = 1;
                }
                r.Max = max;
                foreach (AuiNode c in n.Children)
                {
                    if (c.Tag == "empty") { r.Empty = c; continue; }
                    if (r.Template == null) r.Template = c;
                    else d.Error(AuiCodes.RepeatTemplateCount, "<repeat> takes exactly one template child (plus an optional <empty>); wrap several in a <panel>.", c.Line, c.Column, c.Tag.Length + 1);
                }
                if (r.Template == null)
                    d.Error(AuiCodes.RepeatTemplateCount, "<repeat> has no template child.", n.Line, n.Column, 7);
                foreach (AuiRepeatInfo outer in info.Repeats)
                    if (outer.Template != null && IsInside(n, outer.Template))
                        d.Error(AuiCodes.ElementNotAllowedHere, "Nested <repeat> is not supported.", n.Line, n.Column, 7);
                info.Repeats.Add(r);
            }
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
                if (repeat != null)
                    d.Error(AuiCodes.ElementNotAllowedHere, "A nested view cannot be a repeat template in this version.", n.Line, n.Column, n.Tag.Length + 1);
                info.NestedViews.Add(nv);
                foreach (AuiAttribute a in n.Attributes)
                {
                    if (AuiSchema.IsCommonAttribute(a.Name)) { CommonAttribute(n, a, repeat, info, d); continue; }
                    if (a.Name.StartsWith("on-")) { AddHandler(n, a, null, repeat, info, d); continue; }
                    if (IsBindingValue(a.Value)) AddBinding(n, a, AuiBindingType.Unknown, repeat, info, d);
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
                    if (n.Get("key") != null && (n.Get("text") != null || n.Text != null))
                        d.Error(AuiCodes.TextAndKeyBoth, "<text> takes key= OR text= OR literal content, not several.", n.Line, n.Column, 5);
                    else if (n.Get("key") == null && n.Get("text") == null && n.Text == null)
                        d.Warning(AuiCodes.MissingRequiredAttribute, "<text> with nothing to show: give it key=, text=\"{binding}\" or literal content.", n.Line, n.Column, 5);
                    if (n.Get("args") != null && n.Get("key") == null)
                        d.Error(AuiCodes.PlaceholderNotBound, "args= only makes sense with key=; placeholders live in the translated string.", n.FindAttribute("args").Line, n.FindAttribute("args").Column, 4);
                    break;
                case "icon":
                    if (n.Get("name") == null) d.Error(AuiCodes.MissingRequiredAttribute, "<icon> needs name=\"lucide-name\".", n.Line, n.Column, 5);
                    else if (ctx.IconExists != null && !ctx.IconExists(n.Get("name")))
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
            if (n.Text != null && def.AllowsText && n.Get("key") == null && n.Get("text") == null && n.Tag != "empty")
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
                    break;
                case "id":
                    if (!AuiSchema.IsIdentifier(AuiNaming.Identifier(a.Value)) || a.Value.Length == 0)
                        d.Error(AuiCodes.BadAttributeValue, "id must be a plain identifier.", a.ValueLine, a.ValueColumn, a.Value.Length);
                    break;
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
                    if (IsBindingValue(a.Value)) AddBinding(n, a, AuiBindingType.Bool, repeat, info, d);
                    else if (a.Value != "true" && a.Value != "false" && a.Value != "")
                        d.Error(AuiCodes.BadAttributeValue, a.Name + " takes true, false or a {binding}.", a.ValueLine, a.ValueColumn, a.Value.Length);
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
                case "min":
                case "max":
                case "step":
                case "size":
                case "maxlength":
                case "decimals":
                case "max-lines":
                {
                    float v;
                    if (n.Tag == "repeat" || n.Tag == "select") break; // validated in the collect passes
                    if (!AuiNum.TryParse(a.Value, out v)) d.Error(AuiCodes.BadAttributeValue, a.Name + " must be a number.", a.ValueLine, a.ValueColumn, a.Value.Length);
                    break;
                }
                case "mode":
                    if (n.Tag == "image" && a.Value != "simple" && a.Value != "sliced" && a.Value != "fit")
                        d.Error(AuiCodes.BadAttributeValue, "image mode must be simple, sliced or fit.", a.ValueLine, a.ValueColumn, a.Value.Length);
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
                    if (n.Tag == "icon" && ctx.IconExists != null && !ctx.IconExists(a.Value))
                    {
                        // reported in the element-level switch
                    }
                    break;
            }
        }

        // ---------------------------------------------------------------- bindings and handlers

        public static bool IsBindingValue(string v)
        {
            return v != null && v.Length > 2 && v[0] == '{' && v[v.Length - 1] == '}';
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

        private static void AddHandler(AuiNode n, AuiAttribute a, AuiElementDef def, AuiRepeatInfo repeat, AuiViewInfo info, AuiDiagnosticBag d)
        {
            if (!AuiSchema.IsIdentifier(a.Value))
            {
                d.Error(AuiCodes.HandlerNotIdentifier, a.Name + "=\"" + a.Value + "\" must name a method (letters, digits, underscore).", a.ValueLine, a.ValueColumn, a.Value.Length);
                return;
            }
            var h = new AuiHandler { Name = a.Value, Event = a.Name, Node = n, HasRow = repeat != null, Repeat = repeat };
            switch (a.Name)
            {
                case "onchange":
                    h.ParamType = n.Tag == "toggle" ? "bool" : n.Tag == "slider" ? "float" : "int";
                    break;
                case "onsubmit":
                    h.ParamType = "string";
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
                return;
            }
            info.Handlers.Add(h);
            if (repeat != null) repeat.Handlers.Add(h);
        }

        private static string Shorten(string s)
        {
            return s.Length > 24 ? s.Substring(0, 24) + "..." : s;
        }
    }
}
