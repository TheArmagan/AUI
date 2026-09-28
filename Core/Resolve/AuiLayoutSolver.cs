using System.Collections.Generic;

namespace Armagan.AUI.Core
{
    /// <summary>
    /// Resolves the flex-like layout of a view at compile time into rectangles, so the baked
    /// prefab carries plain anchors and offsets and nothing lays out at runtime.
    ///
    /// Two passes:
    ///   measure (bottom-up): every element's intrinsic size; text is measured over EVERY
    ///     language so the box fits the widest translation.
    ///   layout (top-down): the parent hands each child a rect; grow shares leftover space,
    ///     justify/align place what is left, wrap breaks rows into lines.
    ///
    /// Subset deliberately supported: one axis per container, grow (no shrink), wrap for
    /// row containers, anchor (fill/top/bottom/left/right/center) for absolute placement,
    /// percent sizes against the parent's content box. Nothing else.
    /// </summary>
    public sealed class AuiLayoutSolver
    {
        private readonly AuiMarkupDocument _doc;
        private readonly AuiResolvedStyle[] _styles;
        private readonly AuiViewInfo _info;
        private readonly AuiLayoutContext _ctx;
        private readonly AuiDiagnosticBag _d;
        private AuiLayoutNode[] _out;

        public AuiLayoutSolver(AuiMarkupDocument doc, AuiResolvedStyle[] styles, AuiViewInfo info, AuiLayoutContext ctx, AuiDiagnosticBag diagnostics)
        {
            _doc = doc;
            _styles = styles;
            _info = info;
            _ctx = ctx ?? new AuiLayoutContext();
            _d = diagnostics;
        }

        public AuiLayoutNode[] Solve()
        {
            _out = new AuiLayoutNode[_doc.Nodes.Count];
            for (int i = 0; i < _doc.Nodes.Count; i++) _out[i] = new AuiLayoutNode { Node = _doc.Nodes[i] };
            if (_doc.Root == null) return _out;

            Measure(_doc.Root, _info != null && _info.Width > 0 ? _info.Width : 0);
            AuiLayoutNode root = _out[_doc.Root.Index];
            float w = _info != null && _info.Width > 0 ? _info.Width : root.IntrinsicW;
            float h = _info != null && _info.Height > 0 ? _info.Height : root.IntrinsicH;
            Layout(_doc.Root, new AuiRect(0, 0, w, h), new AuiRect(0, 0, w, h));
            return _out;
        }

        // ---------------------------------------------------------------- helpers

        private AuiResolvedStyle S(AuiNode n)
        {
            return _styles[n.Index];
        }

        private static bool IsContainer(AuiNode n)
        {
            switch (n.Tag)
            {
                case "view": case "panel": case "div": case "button": case "toggle": case "tab": case "tabs":
                case "pages": case "page": case "option": case "empty": case "repeat": case "select":
                    return true;
            }
            return n.IsComponentTag == false && n.Children.Count > 0;
        }

        private bool IsFlagTrue(AuiNode n, string attr, bool fallback)
        {
            string v = n.Get(attr);
            if (v == null) return fallback;
            return v == "" || v == "true";
        }

        private AuiRepeatInfo RepeatOf(AuiNode n)
        {
            if (_info == null) return null;
            foreach (AuiRepeatInfo r in _info.Repeats) if (r.Node == n) return r;
            return null;
        }

        private int CountOf(AuiRepeatInfo r)
        {
            if (r == null) return 1;
            if (_ctx.RepeatCount != null) return System.Math.Max(0, System.Math.Min(r.Max, _ctx.RepeatCount(r)));
            return r.Max;
        }

        /// <summary>hidden="" (any value but "false") removes the element like display: none.</summary>
        private static bool IsHiddenAttr(AuiNode n)
        {
            string v = n.Get("hidden");
            return v != null && v != "false";
        }

        /// <summary>True when the element takes part in its parent's flow: not display none, not hidden, not absolute or anchored.</summary>
        private bool IsFlow(AuiNode c)
        {
            if (c.Tag == "modal" || c.Tag == "template") return false;   // modal: over its siblings; template: parked instances take no space
            return !S(c).IsOutOfFlow && !IsHiddenAttr(c);
        }

        private bool IsHidden(AuiNode c)
        {
            return S(c).IsDisplayNone || IsHiddenAttr(c) || c.Tag == "template";
        }

        /// <summary>Warnings about display/position/edges, reported once per node during measure.</summary>
        private void CheckPlacement(AuiNode n, AuiResolvedStyle s)
        {
            bool hasEdge = s.Top.IsSet || s.Right.IsSet || s.Bottom.IsSet || s.Left.IsSet;
            if (n.Parent == null)
            {
                if (s.IsDisplayNone || s.IsAbsolute)
                    _d.Error(AuiCodes.DisplayOnRoot, "<view> cannot have display: none or position: absolute.", n.Line, n.Column, n.Tag.Length + 1);
                return;
            }
            if (s.IsAbsolute)
            {
                if (!hasEdge && s.Anchor == "none")
                    _d.Warning(AuiCodes.AbsoluteWithoutEdges, "<" + n.Tag + "> is position: absolute without top/right/bottom/left or an anchor; it sits at the parent's content origin.", n.Line, n.Column, n.Tag.Length + 1);
                if (s.Top.IsSet && s.Bottom.IsSet && s.Height.IsSet && !s.Height.IsAuto)
                    _d.Warning(AuiCodes.EdgeConflict, "<" + n.Tag + "> sets top, bottom and height; height wins and bottom is ignored.", n.Line, n.Column, n.Tag.Length + 1);
                if (s.Left.IsSet && s.Right.IsSet && s.Width.IsSet && !s.Width.IsAuto)
                    _d.Warning(AuiCodes.EdgeConflict, "<" + n.Tag + "> sets left, right and width; width wins and right is ignored.", n.Line, n.Column, n.Tag.Length + 1);
            }
            else if (hasEdge && s.Anchor == "none")
            {
                _d.Warning(AuiCodes.EdgeIgnored, "<" + n.Tag + "> has top/right/bottom/left but is not position: absolute; the edges are ignored.", n.Line, n.Column, n.Tag.Length + 1);
            }
        }

        // ---------------------------------------------------------------- measure

        /// <param name="availW">Width the element can expect from its ancestors (0 = unknown); lets wrapping containers measure their real height.</param>
        private void Measure(AuiNode n, float availW)
        {
            AuiLayoutNode ln = _out[n.Index];
            AuiResolvedStyle s = S(n);
            float w = 0, h = 0;

            CheckPlacement(n, s);
            ln.OutOfFlow = n.Parent != null && (s.IsOutOfFlow || n.Tag == "modal" || n.Tag == "template");
            if (n.Parent != null && IsHidden(n))
            {
                // display: none / hidden: the subtree is neither measured nor laid out. A template
                // IS measured (its parked instances need their intrinsic sizes at runtime) but takes no space.
                if (n.Tag == "template")
                {
                    float availT = s.Width.IsSet && !s.Width.IsAuto ? s.Width.Resolve(availW, availW) : availW;
                    foreach (AuiNode c in n.Children) Measure(c, availT);
                }
                ln.Hidden = true;
                ln.OutOfFlow = true;
                ln.IntrinsicW = 0;
                ln.IntrinsicH = 0;
                return;
            }

            float own = s.Width.IsSet && !s.Width.IsAuto ? s.Width.Resolve(availW, availW) : availW;
            float inner = own > 0 ? System.Math.Max(0, own - s.Padding.Horizontal) : 0;
            foreach (AuiNode c in n.Children) Measure(c, inner);

            switch (n.Tag)
            {
                case "text":
                    MeasureText(n, s, ln, availW, out w, out h, ref ln.ContentSized);
                    break;
                case "icon":
                {
                    float size;
                    if (!AuiNum.TryParse(n.Get("size") ?? "", out size)) size = s.Width.Resolve(0, 18);
                    w = h = size;
                    break;
                }
                case "image":
                {
                    float iw, ih;
                    if (_ctx.ImageSize != null && n.Get("src") != null && _ctx.ImageSize(n.Get("src"), out iw, out ih)) { w = iw; h = ih; }
                    else { w = 64; h = 64; }
                    break;
                }
                case "rawimage":
                    // No texture exists at bake time; the stylesheet's width/height override this.
                    w = 64; h = 64;
                    break;
                case "spacer":
                {
                    float size;
                    if (AuiNum.TryParse(n.Get("size") ?? "", out size))
                    {
                        bool parentRow = n.Parent != null && S(n.Parent).IsRow;
                        w = parentRow ? size : 0;
                        h = parentRow ? 0 : size;
                    }
                    break;
                }
                case "slider":
                case "input":
                case "colorpicker":
                    w = 0; h = 0; // theme gives width/height/min-*
                    if (n.Tag == "input") ln.ContentSized = false;
                    break;
                case "repeat":
                    MeasureRepeat(n, s, ln, inner, out w, out h);
                    break;
                case "pages":
                {
                    foreach (AuiNode c in n.Children)
                    {
                        AuiLayoutNode cl = _out[c.Index];
                        w = System.Math.Max(w, cl.IntrinsicW);
                        h = System.Math.Max(h, cl.IntrinsicH);
                    }
                    AuiBox p = s.Padding;
                    w += p.Horizontal; h += p.Vertical;
                    break;
                }
                default:
                    if (n.IsComponentTag)
                    {
                        // A nested view's size is its own <view width height> when the host can
                        // provide it; otherwise a placeholder the author will see immediately.
                        AuiViewInfo child = _ctx.ChildInfo != null ? _ctx.ChildInfo(n.Tag) : null;
                        float cw = child != null && child.Width > 0 ? child.Width : 200;
                        float ch = child != null && child.Height > 0 ? child.Height : 120;
                        w = s.Width.Resolve(0, cw);
                        h = s.Height.Resolve(0, ch);
                    }
                    else MeasureContainer(n, s, ln, inner, out w, out h);
                    break;
            }

            ln.RawW = w;
            ln.RawH = h;
            // explicit sizes (non-percent) override; percent resolves in layout
            if (s.Width.IsSet && !s.Width.IsAuto && !s.Width.IsPercent) w = s.Width.Value;
            if (s.Height.IsSet && !s.Height.IsAuto && !s.Height.IsPercent) h = s.Height.Value;
            w = Clamp(w, s.MinWidth, s.MaxWidth);
            h = Clamp(h, s.MinHeight, s.MaxHeight);
            ln.IntrinsicW = w;
            ln.IntrinsicH = h;
        }

        private static float Clamp(float v, AuiLength min, AuiLength max)
        {
            if (min.IsSet && !min.IsAuto && !min.IsPercent) v = System.Math.Max(v, min.Value);
            if (max.IsSet && !max.IsAuto && !max.IsPercent) v = System.Math.Min(v, max.Value);
            return v;
        }

        private void MeasureText(AuiNode n, AuiResolvedStyle s, AuiLayoutNode ln, float availW, out float w, out float h, ref bool contentSized)
        {
            float size = s.FontSize;
            int weight = s.FontWeight;
            bool wrap = IsFlagTrue(n, "wrap", false);
            float maxW = s.MaxWidth.IsSet && !s.MaxWidth.IsPercent ? s.MaxWidth.Value : (s.Width.IsSet && !s.Width.IsAuto && !s.Width.IsPercent ? s.Width.Value : 0);
            // A wrapping text with no width of its own wraps at what its ancestors can give it,
            // the same rule the runtime solver uses (the width of the last solve). Measured as a
            // single line, the baked rect was one line tall and TMP wrapped into it and clipped.
            if (wrap && maxW <= 0 && availW > 0) maxW = System.Math.Max(0, availW - s.Padding.Horizontal);
            var candidates = new List<string>();
            string key = n.Get("key");
            // runtime="true": the author will SetText this label in-world, so it is measured like a
            // bound text (the sample string, content-sized, re-measured at runtime) whatever it
            // shows at bake time. It was accepted and ignored until BuilderVersion 18.
            bool runtime = IsFlagTrue(n, "runtime", false);
            if (runtime)
            {
                candidates.Add(_ctx.BoundTextSample);
                contentSized = true;
            }
            else if (key != null)
            {
                IEnumerable<string> all = _ctx.Translations != null ? _ctx.Translations(key) : null;
                if (all != null) foreach (string t in all) candidates.Add(StripPlaceholders(t, n));
                if (candidates.Count == 0) candidates.Add(key);
                if (n.Get("args") != null) contentSized = true;
            }
            else if (n.Get("text") != null)
            {
                candidates.Add(_ctx.BoundTextSample);
                contentSized = true;
            }
            else candidates.Add(n.Text ?? "");
            // A rich label draws its tags as formatting, not as glyphs; measured with them it baked a
            // box wider by every "<b>" and "</color=...>" in the string. The bound sample has none.
            bool rich = AuiAnalyzer.IsRichText(n);
            if (rich) for (int i = 0; i < candidates.Count; i++) candidates[i] = StripRichTags(candidates[i]);

            w = 0; h = 0;
            foreach (string t in candidates)
            {
                float tw, th;
                _ctx.Measurer.Measure(t, size, weight, wrap ? maxW : 0, wrap, out tw, out th);
                w = System.Math.Max(w, tw);
                h = System.Math.Max(h, th);
            }
            if (maxW > 0 && w > maxW) w = maxW;
            // Small slack so a glyph never clips at the exact measured edge.
            w += 2f;
            h += 1f;

            // Per-language rows for the runtime solver; the same text in every row when the
            // label is not keyed (bound sample or literal).
            int L = System.Math.Max(1, _ctx.LanguageCount);
            ln.LangW = new float[L];
            ln.LangH = new float[L];
            for (int l = 0; l < L; l++)
            {
                string t = key != null && _ctx.TranslationAt != null ? _ctx.TranslationAt(key, l) : null;
                if (key != null && t == null) { ln.LangW[l] = w; ln.LangH[l] = h; continue; }
                if (key != null) t = StripPlaceholders(t, n);
                else t = n.Get("text") != null ? _ctx.BoundTextSample : (n.Text ?? "");
                if (rich) t = StripRichTags(t);
                float tw, th;
                _ctx.Measurer.Measure(t, size, weight, wrap ? maxW : 0, wrap, out tw, out th);
                if (maxW > 0 && tw > maxW) tw = maxW;
                ln.LangW[l] = tw + 2f;
                ln.LangH[l] = th + 1f;
            }
        }

        /// <summary>Label sizes of a container's own text per language: width + 2, height + 1 (0 when it has none).</summary>
        private void MeasureLabel(AuiNode n, AuiResolvedStyle s, AuiLayoutNode ln, string key, string literal)
        {
            int L = System.Math.Max(1, _ctx.LanguageCount);
            ln.ContentW = new float[L];
            ln.ContentH = new float[L];
            for (int l = 0; l < L; l++)
            {
                string t = key != null ? (_ctx.TranslationAt != null ? _ctx.TranslationAt(key, l) : null) : literal;
                if (key != null && t == null)
                {
                    // Unknown language row: the widest translation, as the overall measure does.
                    float mw = 0, mh = 0;
                    IEnumerable<string> all = _ctx.Translations != null ? _ctx.Translations(key) : null;
                    var cands = new List<string>();
                    if (all != null) cands.AddRange(all);
                    if (cands.Count == 0) cands.Add(key);
                    foreach (string c in cands) { float a, b; _ctx.Measurer.Measure(c, s.FontSize, s.FontWeight, 0, false, out a, out b); mw = System.Math.Max(mw, a); mh = System.Math.Max(mh, b); }
                    ln.ContentW[l] = mw + 2f; ln.ContentH[l] = mh + 1f;
                    continue;
                }
                float tw, th;
                _ctx.Measurer.Measure(t ?? "", s.FontSize, s.FontWeight, 0, false, out tw, out th);
                ln.ContentW[l] = tw + 2f;
                ln.ContentH[l] = th + 1f;
            }
        }

        /// <summary>{placeholders} are replaced by the bound-text sample so the box fits a real value.</summary>
        private string StripPlaceholders(string t, AuiNode n)
        {
            if (t.IndexOf('{') < 0) return t;
            var sb = new System.Text.StringBuilder();
            int i = 0;
            while (i < t.Length)
            {
                int o = t.IndexOf('{', i);
                if (o < 0) { sb.Append(t, i, t.Length - i); break; }
                int c = t.IndexOf('}', o);
                if (c < 0) { sb.Append(t, i, t.Length - i); break; }
                sb.Append(t, i, o - i);
                sb.Append(_ctx.BoundTextSample.Length > 6 ? _ctx.BoundTextSample.Substring(0, 6) : _ctx.BoundTextSample);
                i = c + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// The text a &lt;text rich="true"&gt; shows, for measuring: every &lt;...&gt; run removed. An
        /// approximation on purpose: a size tag's effect is not modelled (the label is still
        /// re-measured by TMP in-world once code writes it), and a literal "a &lt; b &gt; c" in a rich
        /// label measures short, which it also renders as a (broken) tag.
        /// </summary>
        private static string StripRichTags(string t)
        {
            if (t == null || t.IndexOf('<') < 0) return t;
            var sb = new System.Text.StringBuilder(t.Length);
            int i = 0;
            while (i < t.Length)
            {
                int o = t.IndexOf('<', i);
                if (o < 0) { sb.Append(t, i, t.Length - i); break; }
                int c = t.IndexOf('>', o + 1);
                if (c < 0) { sb.Append(t, i, t.Length - i); break; }
                sb.Append(t, i, o - i);
                i = c + 1;
            }
            return sb.ToString();
        }

        private void MeasureContainer(AuiNode n, AuiResolvedStyle s, AuiLayoutNode ln, float availW, out float w, out float h)
        {
            bool row = s.IsRow;
            float gap = s.Gap;
            float main = 0, cross = 0;
            int flowCount = 0;
            // Own-label bookkeeping for the runtime solver (mirrors the branches below).
            if (n.Text != null && n.Children.Count == 0 && n.Get("key") == null) { ln.ContentText = true; MeasureLabel(n, s, ln, null, n.Text); }
            else if (n.Get("key") != null && (n.Tag == "button" || n.Tag == "toggle" || n.Tag == "tab" || n.Tag == "option")) { ln.ContentText = true; ln.ContentIcon = n.Get("icon") != null; MeasureLabel(n, s, ln, n.Get("key"), null); }
            else if (n.Get("icon") != null && (n.Tag == "button" || n.Tag == "tab")) ln.ContentIconOnly = true;
            if (n.Tag == "select") MeasureSelectLabel(n, s, ln);
            if (row && s.Wrap && availW > 0)
            {
                // Simulate the line breaks the layout pass will make, so the container's
                // intrinsic height is the wrapped height and not one line.
                float x = 0, y = 0, lineH = 0, widest = 0;
                foreach (AuiNode c in n.Children)
                {
                    if (!IsFlow(c)) continue;
                    AuiLayoutNode cl = _out[c.Index];
                    AuiBox m = S(c).Margin;
                    float cw = cl.IntrinsicW + m.Horizontal, ch = cl.IntrinsicH + m.Vertical;
                    if (x > 0 && x + cw > availW + 0.01f) { x = 0; y += lineH + gap; lineH = 0; }
                    x += cw + gap;
                    lineH = System.Math.Max(lineH, ch);
                    widest = System.Math.Max(widest, x - gap);
                    flowCount++;
                }
                AuiBox pp = s.Padding;
                w = System.Math.Min(availW, widest) + pp.Horizontal;
                h = y + lineH + pp.Vertical;
                return;
            }
            foreach (AuiNode c in n.Children)
            {
                if (!IsFlow(c)) continue;
                AuiLayoutNode cl = _out[c.Index];
                AuiBox m = S(c).Margin;
                float cw = cl.IntrinsicW + m.Horizontal, ch = cl.IntrinsicH + m.Vertical;
                if (row) { main += cw; cross = System.Math.Max(cross, ch); }
                else { main += ch; cross = System.Math.Max(cross, cw); }
                flowCount++;
            }
            if (flowCount > 1) main += gap * (flowCount - 1);
            // Literal text on a button/tab/option counts as content.
            if (n.Text != null && n.Children.Count == 0 && n.Get("key") == null)
            {
                float tw, th;
                _ctx.Measurer.Measure(n.Text, s.FontSize, s.FontWeight, 0, false, out tw, out th);
                if (row) { main += tw + 2; cross = System.Math.Max(cross, th + 1); }
                else { main += th + 1; cross = System.Math.Max(cross, tw + 2); }
            }
            else if (n.Get("key") != null && (n.Tag == "button" || n.Tag == "toggle" || n.Tag == "tab" || n.Tag == "option"))
            {
                float tw = 0, th = 0;
                IEnumerable<string> all = _ctx.Translations != null ? _ctx.Translations(n.Get("key")) : null;
                var cands = new List<string>();
                if (all != null) cands.AddRange(all);
                if (cands.Count == 0) cands.Add(n.Get("key"));
                foreach (string t in cands)
                {
                    float a, b;
                    _ctx.Measurer.Measure(t, s.FontSize, s.FontWeight, 0, false, out a, out b);
                    tw = System.Math.Max(tw, a); th = System.Math.Max(th, b);
                }
                bool hasIcon = n.Get("icon") != null;
                float iconW = hasIcon ? 18 + gap : 0;
                if (row) { main += tw + 2 + iconW; cross = System.Math.Max(cross, System.Math.Max(th + 1, hasIcon ? 18 : 0)); }
                else { main += th + 1; cross = System.Math.Max(cross, tw + 2); }
            }
            else if (n.Get("icon") != null && (n.Tag == "button" || n.Tag == "tab"))
            {
                if (row) { main += 18; cross = System.Math.Max(cross, 18); } else { main += 18; cross = System.Math.Max(cross, 18); }
            }
            if (n.Tag == "select")
            {
                // header label (widest option) + chevron
                float tw = 0, th = 0;
                foreach (AuiNode o in n.Children)
                {
                    var cands = new List<string>();
                    IEnumerable<string> all = o.Get("key") != null && _ctx.Translations != null ? _ctx.Translations(o.Get("key")) : null;
                    if (all != null) cands.AddRange(all);
                    if (cands.Count == 0) cands.Add(o.Text ?? o.Get("key") ?? "");
                    foreach (string t in cands)
                    {
                        float a, b;
                        _ctx.Measurer.Measure(t, s.FontSize, s.FontWeight, 0, false, out a, out b);
                        tw = System.Math.Max(tw, a); th = System.Math.Max(th, b);
                    }
                }
                main = tw + 2 + gap + 18;
                cross = System.Math.Max(th + 1, 18);
            }
            AuiBox p = s.Padding;
            if (row) { w = main + p.Horizontal; h = cross + p.Vertical; }
            else { w = cross + p.Horizontal; h = main + p.Vertical; }
        }

        /// <summary>Widest option label per language for the select header (the runtime adds gap + chevron).</summary>
        private void MeasureSelectLabel(AuiNode n, AuiResolvedStyle s, AuiLayoutNode ln)
        {
            int L = System.Math.Max(1, _ctx.LanguageCount);
            ln.ContentW = new float[L];
            ln.ContentH = new float[L];
            for (int l = 0; l < L; l++)
            {
                float tw = 0, th = 0;
                foreach (AuiNode o in n.Children)
                {
                    string key = o.Get("key");
                    var cands = new List<string>();
                    string at = key != null && _ctx.TranslationAt != null ? _ctx.TranslationAt(key, l) : null;
                    if (at != null) cands.Add(at);
                    else
                    {
                        IEnumerable<string> all = key != null && _ctx.Translations != null ? _ctx.Translations(key) : null;
                        if (all != null) cands.AddRange(all);
                        if (cands.Count == 0) cands.Add(o.Text ?? key ?? "");
                    }
                    foreach (string t in cands)
                    {
                        float a, b;
                        _ctx.Measurer.Measure(t, s.FontSize, s.FontWeight, 0, false, out a, out b);
                        tw = System.Math.Max(tw, a); th = System.Math.Max(th, b);
                    }
                }
                ln.ContentW[l] = tw + 2f;
                ln.ContentH[l] = th + 1f;
            }
        }

        private void MeasureRepeat(AuiNode n, AuiResolvedStyle s, AuiLayoutNode ln, float availW, out float w, out float h)
        {
            AuiRepeatInfo r = RepeatOf(n);
            ln.ContentSized = true;
            AuiNode template = r != null ? r.Template : (n.Children.Count > 0 ? n.Children[0] : null);
            int count = CountOf(r);
            float iw = 0, ih = 0;
            if (template != null)
            {
                AuiLayoutNode tl = _out[template.Index];
                AuiBox m = S(template).Margin;
                iw = tl.IntrinsicW + m.Horizontal;
                ih = tl.IntrinsicH + m.Vertical;
            }
            float gap = s.Gap;
            AuiBox p = s.Padding;
            if (count <= 0)
            {
                // No rows: the box is what <empty> needs (it fills the content box), so its message has room.
                float ew = 0, eh = 0;
                if (r != null && r.Empty != null) { ew = _out[r.Empty.Index].IntrinsicW; eh = _out[r.Empty.Index].IntrinsicH; }
                w = ew + p.Horizontal; h = eh + p.Vertical;
            }
            else if (s.IsRow && !s.Wrap) { w = iw * count + gap * (count - 1) + p.Horizontal; h = ih + p.Vertical; }
            else if (s.IsRow)
            {
                // Grid: as many per line as the available width allows, then stacked lines.
                float innerW = availW > 0 ? availW - p.Horizontal : 0;
                int perLine = innerW > 0 ? System.Math.Max(1, (int)System.Math.Floor((innerW + gap) / (iw + gap))) : count;
                int lines = (count + perLine - 1) / perLine;
                w = (innerW > 0 ? innerW : iw * count + gap * (count - 1)) + p.Horizontal;
                h = ih * lines + gap * (lines - 1) + p.Vertical;
            }
            else { w = iw + p.Horizontal; h = ih * count + gap * (count - 1) + p.Vertical; }
            // <empty> is measured but overlaps the first instance; it does not add size.
        }

        // ---------------------------------------------------------------- layout

        private void Layout(AuiNode n, AuiRect local, AuiRect absolute)
        {
            AuiLayoutNode ln = _out[n.Index];
            AuiResolvedStyle s = S(n);
            if (ln.Hidden)
            {
                ln.Local = new AuiRect(local.X, local.Y, 0, 0);
                ln.Absolute = new AuiRect(absolute.X, absolute.Y, 0, 0);
                ln.Content = new AuiRect();
                return;
            }
            ln.Local = local;
            ln.Absolute = absolute;
            AuiBox p = s.Padding;
            var content = new AuiRect(p.Left, p.Top, System.Math.Max(0, local.W - p.Horizontal), System.Math.Max(0, local.H - p.Vertical));
            ln.Content = content;

            if (n.Children.Count == 0 && n.Tag != "repeat") return;

            if (n.Tag == "pages")
            {
                foreach (AuiNode c in n.Children) LayoutFill(c, content, absolute);
                return;
            }
            if (n.Tag == "repeat")
            {
                LayoutRepeat(n, s, ln, content, absolute);
                return;
            }
            if (n.Tag == "select")
            {
                // Options are the popup, baked below the header at the header's width.
                float y = local.H;
                foreach (AuiNode o in n.Children)
                {
                    AuiLayoutNode ol = _out[o.Index];
                    float oh = System.Math.Max(ol.IntrinsicH, S(o).MinHeight.Resolve(0, 0));
                    var r = new AuiRect(0, y, local.W, oh);
                    Layout(o, r, new AuiRect(absolute.X + r.X, absolute.Y + r.Y, r.W, r.H));
                    y += oh;
                }
                return;
            }

            // ---- out-of-flow children (absolute, anchored, hidden)
            var flow = new List<AuiNode>();
            foreach (AuiNode c in n.Children)
            {
                if (IsFlow(c)) { flow.Add(c); continue; }
                if (_out[c.Index].Hidden) { Layout(c, new AuiRect(content.X, content.Y, 0, 0), new AuiRect(absolute.X + content.X, absolute.Y + content.Y, 0, 0)); continue; }
                LayoutAbsolute(c, content, absolute);
            }
            if (flow.Count == 0) return;

            bool row = s.IsRow;
            if (row && s.Wrap) LayoutWrap(n, s, flow, content, absolute);
            else LayoutFlow(n, s, flow, row, content, absolute);
        }

        private void LayoutFill(AuiNode c, AuiRect content, AuiRect parentAbs)
        {
            AuiLayoutNode cl = _out[c.Index];
            cl.AnchorFill = true;
            var r = new AuiRect(content.X, content.Y, content.W, content.H);
            Layout(c, r, new AuiRect(parentAbs.X + r.X, parentAbs.Y + r.Y, r.W, r.H));
        }

        /// <summary>
        /// The ONE placement routine for everything out of flow, mirrored by the Udon solver:
        /// anchor: fill is absolute with four zero edges; the other anchors centre on the
        /// axis they do not pin; top/right/bottom/left (negatives allowed) pin an edge to the
        /// parent's content box; an explicit size wins over an opposite edge; two opposite edges
        /// with no size stretch between them; otherwise the intrinsic size is used.
        /// </summary>
        private void LayoutAbsolute(AuiNode c, AuiRect content, AuiRect parentAbs)
        {
            AuiLayoutNode cl = _out[c.Index];
            AuiResolvedStyle cs = S(c);
            string anchor = cs.Anchor;
            bool fill = anchor == "fill";
            AuiLength top = cs.Top, right = cs.Right, bottom = cs.Bottom, left = cs.Left;
            if (fill)
            {
                if (!top.IsSet) top = AuiLength.Mm(0);
                if (!right.IsSet) right = AuiLength.Mm(0);
                if (!bottom.IsSet) bottom = AuiLength.Mm(0);
                if (!left.IsSet) left = AuiLength.Mm(0);
            }
            float t = top.IsSet && !top.IsAuto ? top.Resolve(content.H, 0) : 0;
            float rr = right.IsSet && !right.IsAuto ? right.Resolve(content.W, 0) : 0;
            float b = bottom.IsSet && !bottom.IsAuto ? bottom.Resolve(content.H, 0) : 0;
            float l = left.IsSet && !left.IsAuto ? left.Resolve(content.W, 0) : 0;
            bool hasT = top.IsSet && !top.IsAuto, hasR = right.IsSet && !right.IsAuto, hasB = bottom.IsSet && !bottom.IsAuto, hasL = left.IsSet && !left.IsAuto;

            float w, h;
            if (cs.Width.IsSet && !cs.Width.IsAuto) w = cs.Width.IsPercent ? content.W * cs.Width.Value / 100f : cs.Width.Value;
            else if (hasL && hasR) w = content.W - l - rr;
            else w = cl.IntrinsicW;
            if (cs.Height.IsSet && !cs.Height.IsAuto) h = cs.Height.IsPercent ? content.H * cs.Height.Value / 100f : cs.Height.Value;
            else if (hasT && hasB) h = content.H - t - b;
            else h = cl.IntrinsicH;
            w = Clamp(System.Math.Max(0, w), cs.MinWidth, cs.MaxWidth);
            h = Clamp(System.Math.Max(0, h), cs.MinHeight, cs.MaxHeight);

            float x, y;
            if (hasL) x = content.X + l;
            else if (hasR) x = content.X + content.W - rr - w;
            else if (anchor == "top" || anchor == "bottom" || anchor == "center") x = content.X + (content.W - w) / 2;
            else if (anchor == "right") x = content.X + content.W - w;
            else x = content.X;
            if (hasT) y = content.Y + t;
            else if (hasB) y = content.Y + content.H - b - h;
            else if (anchor == "left" || anchor == "right" || anchor == "center") y = content.Y + (content.H - h) / 2;
            else if (anchor == "bottom") y = content.Y + content.H - h;
            else y = content.Y;

            // A fill with no edge overrides and no explicit size still bakes as stretch anchors.
            if (fill && !cs.Width.IsSet && !cs.Height.IsSet && !cs.Top.IsSet && !cs.Right.IsSet && !cs.Bottom.IsSet && !cs.Left.IsSet) cl.AnchorFill = true;
            var r = new AuiRect(x, y, w, h);
            Layout(c, r, new AuiRect(parentAbs.X + r.X, parentAbs.Y + r.Y, r.W, r.H));
        }

        private void LayoutFlow(AuiNode n, AuiResolvedStyle s, List<AuiNode> flow, bool row, AuiRect content, AuiRect parentAbs)
        {
            float gap = s.Gap;
            float mainAvail = row ? content.W : content.H;
            float crossAvail = row ? content.H : content.W;

            // main sizes
            float[] main = new float[flow.Count];
            float[] cross = new float[flow.Count];
            float used = gap * (flow.Count - 1);
            float growSum = 0;
            for (int i = 0; i < flow.Count; i++)
            {
                AuiNode c = flow[i];
                AuiResolvedStyle cs = S(c);
                AuiLayoutNode cl = _out[c.Index];
                AuiBox m = cs.Margin;
                AuiLength mainLen = row ? cs.Width : cs.Height;
                AuiLength crossLen = row ? cs.Height : cs.Width;
                float mainIntrinsic = row ? cl.IntrinsicW : cl.IntrinsicH;
                float crossIntrinsic = row ? cl.IntrinsicH : cl.IntrinsicW;
                main[i] = mainLen.IsPercent ? mainAvail * mainLen.Value / 100f : mainIntrinsic;
                cross[i] = crossLen.IsPercent ? crossAvail * crossLen.Value / 100f : crossIntrinsic;
                main[i] += row ? m.Horizontal : m.Vertical;
                cross[i] += row ? m.Vertical : m.Horizontal;
                used += main[i];
                growSum += System.Math.Max(0, cs.Grow);
            }
            float leftover = mainAvail - used;
            if (leftover < -0.5f)
            {
                // Overflow: shrink the children that can lose WIDTH gracefully (text ellipsises,
                // content-sized boxes were only a guess anyway) before giving up, like flex-shrink.
                // Never on the y axis: a text box shorter than its line renders nothing under
                // Ellipsis, so a column that overflows warns and clips instead.
                const float MinShrunk = 24f;
                float shrinkable = 0f;
                if (row)
                    for (int i = 0; i < flow.Count; i++)
                        if (CanShrink(flow[i]) && main[i] > MinShrunk) shrinkable += main[i] - MinShrunk;
                float need = -leftover;
                if (shrinkable > 0f)
                {
                    float take = System.Math.Min(need, shrinkable);
                    for (int i = 0; i < flow.Count; i++)
                    {
                        if (!CanShrink(flow[i]) || main[i] <= MinShrunk) continue;
                        float share = (main[i] - MinShrunk) / shrinkable;
                        main[i] -= take * share;
                    }
                    leftover += take;
                }
                if (leftover < -0.5f)
                    _d.Warning(AuiCodes.LayoutOverflow, "<" + n.Tag + "> children need " + AuiNum.Fmt(used) + " mm but it has " + AuiNum.Fmt(mainAvail) + " mm on the " + (row ? "x" : "y") + " axis.", n.Line, n.Column, n.Tag.Length + 1);
                if (leftover < 0) leftover = 0;
            }
            if (growSum > 0 && leftover > 0)
            {
                for (int i = 0; i < flow.Count; i++)
                {
                    float g = System.Math.Max(0, S(flow[i]).Grow);
                    if (g > 0) main[i] += leftover * g / growSum;
                }
                leftover = 0;
            }

            // justify
            float cursor = 0, between = gap;
            switch (s.Justify)
            {
                case "center": cursor = leftover / 2; break;
                case "end": cursor = leftover; break;
                case "space-between": if (flow.Count > 1) between = gap + leftover / (flow.Count - 1); break;
            }

            for (int i = 0; i < flow.Count; i++)
            {
                AuiNode c = flow[i];
                AuiResolvedStyle cs = S(c);
                AuiBox m = cs.Margin;
                bool stretch = s.Align == "stretch" && !(row ? cs.Height : cs.Width).IsSet;
                float crossSize = stretch ? crossAvail - (row ? m.Vertical : m.Horizontal) : cross[i] - (row ? m.Vertical : m.Horizontal);
                float crossPos = 0;
                float crossFree = crossAvail - crossSize - (row ? m.Vertical : m.Horizontal);
                switch (s.Align)
                {
                    case "center": crossPos = crossFree / 2; break;
                    case "end": crossPos = crossFree; break;
                }
                float mainSize = main[i] - (row ? m.Horizontal : m.Vertical);
                AuiRect r = row
                    ? new AuiRect(content.X + cursor + m.Left, content.Y + crossPos + m.Top, mainSize, crossSize)
                    : new AuiRect(content.X + crossPos + m.Left, content.Y + cursor + m.Top, crossSize, mainSize);
                Layout(c, r, new AuiRect(parentAbs.X + r.X, parentAbs.Y + r.Y, r.W, r.H));
                cursor += main[i] + between;
            }
        }

        /// <summary>Text and content-sized elements may lose main-axis width when a row overflows; controls keep theirs.</summary>
        private bool CanShrink(AuiNode c)
        {
            if (S(c).Width.IsSet && !S(c).Width.IsAuto) return false;
            return c.Tag == "text" || _out[c.Index].ContentSized;
        }

        private void LayoutWrap(AuiNode n, AuiResolvedStyle s, List<AuiNode> flow, AuiRect content, AuiRect parentAbs)
        {
            float gap = s.Gap;
            float x = 0, y = 0, lineH = 0;
            foreach (AuiNode c in flow)
            {
                AuiLayoutNode cl = _out[c.Index];
                AuiBox m = S(c).Margin;
                float w = cl.IntrinsicW + m.Horizontal, h = cl.IntrinsicH + m.Vertical;
                if (x > 0 && x + w > content.W + 0.01f) { x = 0; y += lineH + gap; lineH = 0; }
                var r = new AuiRect(content.X + x + m.Left, content.Y + y + m.Top, cl.IntrinsicW, cl.IntrinsicH);
                Layout(c, r, new AuiRect(parentAbs.X + r.X, parentAbs.Y + r.Y, r.W, r.H));
                x += w + gap;
                lineH = System.Math.Max(lineH, h);
            }
            if (y + lineH > content.H + 0.5f)
                _d.Warning(AuiCodes.LayoutOverflow, "<" + n.Tag + "> wraps to " + AuiNum.Fmt(y + lineH) + " mm but has " + AuiNum.Fmt(content.H) + " mm.", n.Line, n.Column, n.Tag.Length + 1);
        }

        private void LayoutRepeat(AuiNode n, AuiResolvedStyle s, AuiLayoutNode ln, AuiRect content, AuiRect parentAbs)
        {
            AuiRepeatInfo r = RepeatOf(n);
            AuiNode template = r != null ? r.Template : (n.Children.Count > 0 ? n.Children[0] : null);
            int count = CountOf(r);
            ln.Instances = new List<AuiRect>();
            if (template == null) return;
            if (count <= 0)
            {
                // No rows: the template still gets a rect (row 0) so the builder has something to bake.
                var tl0 = _out[template.Index];
                var m0 = S(template).Margin;
                float iw0 = tl0.IntrinsicW, ih0 = tl0.IntrinsicH;
                if (!s.IsRow && !S(template).Width.IsSet) iw0 = System.Math.Max(iw0, content.W - m0.Horizontal);
                ln.Instances.Add(new AuiRect(m0.Left, m0.Top, iw0, ih0));
                var local0 = new AuiRect(content.X + m0.Left, content.Y + m0.Top, iw0, ih0);
                Layout(template, local0, new AuiRect(parentAbs.X + local0.X, parentAbs.Y + local0.Y, local0.W, local0.H));
                if (r != null && r.Empty != null) LayoutFill(r.Empty, content, parentAbs);
                return;
            }
            AuiLayoutNode tl = _out[template.Index];
            AuiResolvedStyle ts = S(template);
            AuiBox m = ts.Margin;
            float gap = s.Gap;
            bool row = s.IsRow;
            float iw = tl.IntrinsicW, ih = tl.IntrinsicH;
            // Column lists stretch items to the repeat's width, like align: stretch.
            if (!row && !ts.Width.IsSet) iw = System.Math.Max(iw, content.W - m.Horizontal);

            float x = 0, y = 0, lineH = 0;
            for (int i = 0; i < count; i++)
            {
                if (row && s.Wrap && x > 0 && x + iw + m.Horizontal > content.W + 0.01f) { x = 0; y += lineH + gap; lineH = 0; }
                var rect = new AuiRect(x + m.Left, y + m.Top, iw, ih);
                ln.Instances.Add(rect);
                if (row) { x += iw + m.Horizontal + gap; lineH = System.Math.Max(lineH, ih + m.Vertical); }
                else y += ih + m.Vertical + gap;
            }
            float needH = row ? y + lineH : y - gap;
            if (needH > content.H + 0.5f && r != null)
                _d.Info(AuiCodes.LayoutOverflow, "<repeat id=\"" + r.Id + "\"> needs " + AuiNum.Fmt(needH) + " mm for " + count + " rows but has " + AuiNum.Fmt(content.H) + " mm; rows beyond the box are hidden by paging.", n.Line, n.Column, 7);

            // The template is laid out at instance 0; the builder offsets clones by Instances[i].
            AuiRect first = ln.Instances[0];
            var local = new AuiRect(content.X + first.X, content.Y + first.Y, first.W, first.H);
            Layout(template, local, new AuiRect(parentAbs.X + local.X, parentAbs.Y + local.Y, local.W, local.H));
            if (r != null && r.Empty != null) LayoutFill(r.Empty, content, parentAbs);
        }
    }
}
