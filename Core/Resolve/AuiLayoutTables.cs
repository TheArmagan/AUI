using System.Collections.Generic;

namespace Armagan.AUI.Core
{
    /// <summary>
    /// The layout inputs of a compiled view as flat arrays indexed by RUNTIME ELEMENT INDEX,
    /// exactly what the prefab builder copies onto AuiView so the Udon solver (Runtime/
    /// AuiView.Layout.cs) can re-run the flex layout in-world. Pure C#, built from the compile
    /// result alone, so the parity tests can drive both solvers without Unity.
    ///
    /// Every constant here is mirrored in AuiView (LAY_*, LK_*, LF_*); a change to one is a
    /// change to both.
    /// </summary>
    public sealed class AuiLayoutTables
    {
        // ---- kinds
        public const int LK_CONTAINER = 0, LK_TEXT = 1, LK_LEAF = 2, LK_REPEAT = 3, LK_PAGES = 4, LK_SELECT = 5, LK_NESTED = 6, LK_IGNORE = 7, LK_EMPTY = 8;
        // ---- flags
        public const int LF_ROW = 1, LF_WRAP = 2, LF_ABSOLUTE = 4, LF_DISPLAY_NONE = 8, LF_HIDDEN_START = 16, LF_RUNTIME_TEXT = 32, LF_SHRINKABLE = 64,
            LF_TEXT_WRAP = 128, LF_SPACER = 256, LF_CONTENT_TEXT = 512, LF_CONTENT_ICON = 1024, LF_CONTENT_ICON_ONLY = 2048;
        // ---- enums
        public const int ANCHOR_NONE = 0, ANCHOR_FILL = 1, ANCHOR_TOP = 2, ANCHOR_BOTTOM = 3, ANCHOR_LEFT = 4, ANCHOR_RIGHT = 5, ANCHOR_CENTER = 6;
        public const int JUSTIFY_START = 0, JUSTIFY_CENTER = 1, JUSTIFY_END = 2, JUSTIFY_BETWEEN = 3;
        public const int ALIGN_START = 0, ALIGN_CENTER = 1, ALIGN_END = 2, ALIGN_STRETCH = 3;
        public const int SIZE_UNSET = 0, SIZE_MM = 1, SIZE_PCT = 2, SIZE_AUTO = 3;
        // ---- size slots (6 per element) and edge slots (4 per element)
        public const int SZ_W = 0, SZ_H = 1, SZ_MINW = 2, SZ_MINH = 3, SZ_MAXW = 4, SZ_MAXH = 5;
        public const int EDGE_TOP = 0, EDGE_RIGHT = 1, EDGE_BOTTOM = 2, EDGE_LEFT = 3;

        public int N;
        public int LangCount;
        public float RootW, RootH;
        public int[] Parent, FirstChild, NextSibling, Kind, Flags, Justify, Align, Anchor, Repeat, SizeKind, EdgeKind;
        public float[] Grow, Gap, Pad, Margin, SizeVal, EdgeVal, IntrinsicW, IntrinsicH, LangW, LangH, ContentW, ContentH;

        public static AuiLayoutTables Build(AuiCompileResult r)
        {
            var t = new AuiLayoutTables();
            AuiIndexMap map = r.Map;
            int n = map.ElementCount;
            t.N = n;
            t.RootW = r.Info.Width;
            t.RootH = r.Info.Height;
            int L = 1;
            foreach (AuiLayoutNode ln in r.Layout) if (ln.LangW != null && ln.LangW.Length > L) L = ln.LangW.Length;
            t.LangCount = L;

            t.Parent = new int[n]; t.FirstChild = new int[n]; t.NextSibling = new int[n]; t.Kind = new int[n]; t.Flags = new int[n];
            t.Justify = new int[n]; t.Align = new int[n]; t.Anchor = new int[n]; t.Repeat = new int[n];
            t.SizeKind = new int[6 * n]; t.EdgeKind = new int[4 * n];
            t.Grow = new float[n]; t.Gap = new float[n]; t.Pad = new float[4 * n]; t.Margin = new float[4 * n];
            t.SizeVal = new float[6 * n]; t.EdgeVal = new float[4 * n];
            t.IntrinsicW = new float[n]; t.IntrinsicH = new float[n];
            t.LangW = new float[n * L]; t.LangH = new float[n * L]; t.ContentW = new float[n * L]; t.ContentH = new float[n * L];
            var last = new int[n];
            for (int e = 0; e < n; e++) { t.Parent[e] = -1; t.FirstChild[e] = -1; t.NextSibling[e] = -1; t.Repeat[e] = -1; last[e] = -1; }

            for (int e = 0; e < n; e++)
            {
                AuiNode node = map.NodeOf[e];
                if (node == null) { t.Kind[e] = LK_IGNORE; continue; }
                int row = map.RowOf[e] < 0 ? 0 : map.RowOf[e];
                AuiLayoutNode ln = r.Layout[node.Index];
                AuiResolvedStyle s = r.Styles[node.Index];

                t.Parent[e] = node.Parent != null ? map.Elem(node.Parent, row) : -1;
                t.Kind[e] = KindOf(node);
                if (t.Kind[e] == LK_REPEAT)
                    for (int i = 0; i < r.Info.Repeats.Count; i++) if (r.Info.Repeats[i].Node == node) t.Repeat[e] = i;

                int f = 0;
                if (s.IsRow) f |= LF_ROW;
                if (s.Wrap) f |= LF_WRAP;
                if (s.IsAbsolute || s.Anchor != "none" || node.Tag == "modal") f |= LF_ABSOLUTE;
                if (s.IsDisplayNone) f |= LF_DISPLAY_NONE;
                string hidden = node.Get("hidden");
                if (s.IsDisplayNone || (hidden != null && hidden != "false")) f |= LF_HIDDEN_START;
                // A template and its parked instance roots start hidden; CreateElement shows an instance when it links it.
                if (node.Tag == "template" || (node.Parent != null && node.Parent.Tag == "template")) f |= LF_HIDDEN_START;
                if (node.Tag == "text" && (node.Get("text") != null || node.Get("args") != null)) f |= LF_RUNTIME_TEXT;
                if (node.Tag == "text" || ln.ContentSized) f |= LF_SHRINKABLE;
                if (node.Tag == "text") { string w = node.Get("wrap"); if (w != null && (w == "" || w == "true")) f |= LF_TEXT_WRAP; }
                if (node.Tag == "spacer") f |= LF_SPACER;
                if (ln.ContentText) f |= LF_CONTENT_TEXT;
                if (ln.ContentIcon) f |= LF_CONTENT_ICON;
                if (ln.ContentIconOnly) f |= LF_CONTENT_ICON_ONLY;
                t.Flags[e] = f;

                t.Justify[e] = s.Justify == "center" ? JUSTIFY_CENTER : s.Justify == "end" ? JUSTIFY_END : s.Justify == "space-between" ? JUSTIFY_BETWEEN : JUSTIFY_START;
                t.Align[e] = s.Align == "start" ? ALIGN_START : s.Align == "center" ? ALIGN_CENTER : s.Align == "end" ? ALIGN_END : ALIGN_STRETCH;
                t.Anchor[e] = AnchorOf(s.Anchor);
                t.Grow[e] = System.Math.Max(0, s.Grow);
                t.Gap[e] = s.Gap;
                AuiBox p = s.Padding, m = s.Margin;
                t.Pad[4 * e] = p.Top; t.Pad[4 * e + 1] = p.Right; t.Pad[4 * e + 2] = p.Bottom; t.Pad[4 * e + 3] = p.Left;
                t.Margin[4 * e] = m.Top; t.Margin[4 * e + 1] = m.Right; t.Margin[4 * e + 2] = m.Bottom; t.Margin[4 * e + 3] = m.Left;
                PutSize(t, 6 * e + SZ_W, s.Width); PutSize(t, 6 * e + SZ_H, s.Height);
                PutSize(t, 6 * e + SZ_MINW, s.MinWidth); PutSize(t, 6 * e + SZ_MINH, s.MinHeight);
                PutSize(t, 6 * e + SZ_MAXW, s.MaxWidth); PutSize(t, 6 * e + SZ_MAXH, s.MaxHeight);
                PutEdge(t, 4 * e + EDGE_TOP, s.Top); PutEdge(t, 4 * e + EDGE_RIGHT, s.Right); PutEdge(t, 4 * e + EDGE_BOTTOM, s.Bottom); PutEdge(t, 4 * e + EDGE_LEFT, s.Left);

                // Spacer: the solver picks the axis from the parent's direction; Core baked one axis already.
                float rawW = ln.RawW, rawH = ln.RawH;
                if (node.Tag == "spacer")
                {
                    float size;
                    if (!AuiNum.TryParse(node.Get("size") ?? "", out size)) size = 0;
                    rawW = size; rawH = size;
                }
                t.IntrinsicW[e] = rawW;
                t.IntrinsicH[e] = rawH;
                for (int l = 0; l < L; l++)
                {
                    t.LangW[e * L + l] = ln.LangW != null && l < ln.LangW.Length ? ln.LangW[l] : rawW;
                    t.LangH[e * L + l] = ln.LangH != null && l < ln.LangH.Length ? ln.LangH[l] : rawH;
                    t.ContentW[e * L + l] = ln.ContentW != null && l < ln.ContentW.Length ? ln.ContentW[l] : 0;
                    t.ContentH[e * L + l] = ln.ContentH != null && l < ln.ContentH.Length ? ln.ContentH[l] : 0;
                }
            }

            // Sibling chains in element (document) order: children of a repeat come out row by row.
            for (int e = 0; e < n; e++)
            {
                int p = t.Parent[e];
                if (p < 0) continue;
                if (t.FirstChild[p] < 0) t.FirstChild[p] = e; else t.NextSibling[last[p]] = e;
                last[p] = e;
            }
            return t;
        }

        public static int KindOf(AuiNode node)
        {
            if (node.IsComponentTag) return LK_NESTED;
            switch (node.Tag)
            {
                case "text": return LK_TEXT;
                case "icon": case "image": case "spacer": case "slider": case "input": return LK_LEAF;
                case "repeat": return LK_REPEAT;
                case "template": return LK_CONTAINER;   // hidden; instances are laid out under whatever parent links them
                case "pages": return LK_PAGES;
                case "select": return LK_SELECT;
                case "option": return LK_IGNORE;
                case "empty": return LK_EMPTY;
            }
            return LK_CONTAINER;
        }

        public static int AnchorOf(string a)
        {
            switch (a)
            {
                case "fill": return ANCHOR_FILL;
                case "top": return ANCHOR_TOP;
                case "bottom": return ANCHOR_BOTTOM;
                case "left": return ANCHOR_LEFT;
                case "right": return ANCHOR_RIGHT;
                case "center": return ANCHOR_CENTER;
            }
            return ANCHOR_NONE;
        }

        private static void PutSize(AuiLayoutTables t, int slot, AuiLength l)
        {
            t.SizeKind[slot] = !l.IsSet ? SIZE_UNSET : l.IsAuto ? SIZE_AUTO : l.IsPercent ? SIZE_PCT : SIZE_MM;
            t.SizeVal[slot] = l.IsSet && !l.IsAuto ? l.Value : 0;
        }

        private static void PutEdge(AuiLayoutTables t, int slot, AuiLength l)
        {
            t.EdgeKind[slot] = !l.IsSet || l.IsAuto ? SIZE_UNSET : l.IsPercent ? SIZE_PCT : SIZE_MM;
            t.EdgeVal[slot] = l.IsSet && !l.IsAuto ? l.Value : 0;
        }

        /// <summary>Elements that take part in their parent's flow at bake time, in sibling order (for tests and tools).</summary>
        public List<int> FlowChildren(int e)
        {
            var list = new List<int>();
            for (int c = FirstChild[e]; c >= 0; c = NextSibling[c])
            {
                if (Kind[c] == LK_IGNORE || Kind[c] == LK_EMPTY) continue;
                if ((Flags[c] & (LF_ABSOLUTE | LF_HIDDEN_START)) != 0) continue;
                list.Add(c);
            }
            return list;
        }
    }
}
