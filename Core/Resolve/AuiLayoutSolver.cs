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

            Measure(_doc.Root);
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

        // ---------------------------------------------------------------- measure

        private void Measure(AuiNode n)
        {
            AuiLayoutNode ln = _out[n.Index];
            AuiResolvedStyle s = S(n);
            float w = 0, h = 0;

            foreach (AuiNode c in n.Children) Measure(c);

            switch (n.Tag)
            {
                case "text":
                    MeasureText(n, s, out w, out h, ref ln.ContentSized);
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
                    w = 0; h = 0; // theme gives width/height/min-*
                    if (n.Tag == "input") ln.ContentSized = false;
                    break;
                case "repeat":
                    MeasureRepeat(n, s, ln, out w, out h);
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
                        // A nested view's size comes from its own compiled info when the host
                        // provides it; otherwise a placeholder the author will see immediately.
                        w = s.Width.Resolve(0, 200);
                        h = s.Height.Resolve(0, 120);
                    }
                    else MeasureContainer(n, s, out w, out h);
                    break;
            }

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

        private void MeasureText(AuiNode n, AuiResolvedStyle s, out float w, out float h, ref bool contentSized)
        {
            float size = s.FontSize;
            int weight = s.FontWeight;
            bool wrap = IsFlagTrue(n, "wrap", false);
            float maxW = s.MaxWidth.IsSet && !s.MaxWidth.IsPercent ? s.MaxWidth.Value : (s.Width.IsSet && !s.Width.IsAuto && !s.Width.IsPercent ? s.Width.Value : 0);
            var candidates = new List<string>();
            string key = n.Get("key");
            if (key != null)
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

        private void MeasureContainer(AuiNode n, AuiResolvedStyle s, out float w, out float h)
        {
            bool row = s.IsRow;
            float gap = s.Gap;
            float main = 0, cross = 0;
            int flowCount = 0;
            foreach (AuiNode c in n.Children)
            {
                if (S(c).Anchor != "none") continue;
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

        private void MeasureRepeat(AuiNode n, AuiResolvedStyle s, AuiLayoutNode ln, out float w, out float h)
        {
            AuiRepeatInfo r = RepeatOf(n);
            ln.ContentSized = true;
            AuiNode template = r != null ? r.Template : (n.Children.Count > 0 ? n.Children[0] : null);
            int count = r != null ? r.Max : 1;
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
            if (s.IsRow && !s.Wrap) { w = iw * count + gap * (count - 1) + p.Horizontal; h = ih + p.Vertical; }
            else if (s.IsRow)
            {
                // Grid: width comes from the parent in the layout pass; intrinsic is one row.
                w = iw * count + gap * (count - 1) + p.Horizontal;
                h = ih + p.Vertical;
            }
            else { w = iw + p.Horizontal; h = ih * count + gap * (count - 1) + p.Vertical; }
            // <empty> is measured but overlaps the first instance; it does not add size.
        }

        // ---------------------------------------------------------------- layout

        private void Layout(AuiNode n, AuiRect local, AuiRect absolute)
        {
            AuiLayoutNode ln = _out[n.Index];
            AuiResolvedStyle s = S(n);
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

            // ---- anchored children
            var flow = new List<AuiNode>();
            foreach (AuiNode c in n.Children)
            {
                string a = S(c).Anchor;
                if (a == "none") { flow.Add(c); continue; }
                LayoutAnchored(c, a, content, absolute);
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

        private void LayoutAnchored(AuiNode c, string anchor, AuiRect content, AuiRect parentAbs)
        {
            if (anchor == "fill") { LayoutFill(c, content, parentAbs); return; }
            AuiLayoutNode cl = _out[c.Index];
            AuiResolvedStyle cs = S(c);
            float w = cs.Width.IsPercent ? content.W * cs.Width.Value / 100f : cl.IntrinsicW;
            float h = cs.Height.IsPercent ? content.H * cs.Height.Value / 100f : cl.IntrinsicH;
            float x = content.X, y = content.Y;
            switch (anchor)
            {
                case "top": x = content.X + (content.W - w) / 2; y = content.Y; break;
                case "bottom": x = content.X + (content.W - w) / 2; y = content.Y + content.H - h; break;
                case "left": x = content.X; y = content.Y + (content.H - h) / 2; break;
                case "right": x = content.X + content.W - w; y = content.Y + (content.H - h) / 2; break;
                case "center": x = content.X + (content.W - w) / 2; y = content.Y + (content.H - h) / 2; break;
            }
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
                _d.Warning(AuiCodes.LayoutOverflow, "<" + n.Tag + "> children need " + AuiNum.Fmt(used) + " mm but it has " + AuiNum.Fmt(mainAvail) + " mm on the " + (row ? "x" : "y") + " axis.", n.Line, n.Column, n.Tag.Length + 1);
                leftover = 0;
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
            int count = r != null ? r.Max : 1;
            ln.Instances = new List<AuiRect>();
            if (template == null) return;
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
