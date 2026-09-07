using System.Collections.Generic;

namespace Armagan.AUI.Core
{
    /// <summary>
    /// Assigns every baked GameObject its runtime element index, and every interactive
    /// element its signal id. Codegen and the prefab builder both walk this map, which is the
    /// only reason a generated `E_TITLE` matches what the builder put in `elemText[...]`.
    ///
    /// Rules: document order; a repeat template subtree gets Max consecutive blocks of
    /// Stride elements (instance i, template node with offset o -> Base + i * Stride + o);
    /// &lt;empty&gt; and &lt;option&gt; subtrees are baked once; a select additionally gets a popup
    /// root and a scrim element after its options; lifted nodes (link, style) get nothing.
    /// Mirror of the constants in Runtime/AuiView.cs: KIND_*, STATE_COUNT, PROP_*.
    /// </summary>
    public sealed class AuiIndexMap
    {
        public const int KIND_NONE = 0, KIND_BUTTON = 1, KIND_TOGGLE = 2, KIND_TAB = 3, KIND_OPTION = 4, KIND_INPUT = 5, KIND_SELECT = 6, KIND_SLIDER = 7, KIND_SCRIM = 8, KIND_MODAL_SCRIM = 9;
        public const int STATE_COUNT = 5;

        public int ElementCount;
        public int SignalCount;
        /// <summary>Per doc node index: runtime element index of instance 0, or -1.</summary>
        public int[] Base;
        /// <summary>Per doc node index: elements per instance when inside a repeat template, else 0.</summary>
        public int[] Stride;
        /// <summary>Per doc node index: number of baked copies (repeat Max inside a template, else 1).</summary>
        public int[] Copies;
        /// <summary>Per runtime element: signal id or -1.</summary>
        public int[] SignalOf;
        /// <summary>Per runtime element: KIND_*.</summary>
        public int[] KindOf;
        /// <summary>Per runtime element: the doc node it was baked from.</summary>
        public AuiNode[] NodeOf;
        /// <summary>Per runtime element: repeat instance (row) it belongs to, or -1.</summary>
        public int[] RowOf;
        /// <summary>Per select node index: element index of the popup root and the scrim.</summary>
        public readonly Dictionary<int, int> PopupElem = new Dictionary<int, int>();
        public readonly Dictionary<int, int> ScrimElem = new Dictionary<int, int>();
        /// <summary>Per modal node index: element index of its backdrop scrim (baked just before the modal body).</summary>
        public readonly Dictionary<int, int> ModalScrimElem = new Dictionary<int, int>();
        /// <summary>Per repeat node index: first signal id of instance 0 and signals per instance.</summary>
        public readonly Dictionary<int, int> RepeatSignalBase = new Dictionary<int, int>();
        public readonly Dictionary<int, int> RepeatSignalStride = new Dictionary<int, int>();

        private readonly AuiMarkupDocument _doc;
        private readonly AuiViewInfo _info;
        private readonly AuiResolvedStyle[] _styles;
        private readonly List<int> _signalOf = new List<int>();
        private readonly List<int> _kindOf = new List<int>();
        private readonly List<AuiNode> _nodeOf = new List<AuiNode>();
        private readonly List<int> _rowOf = new List<int>();
        // Templates are walked after everything else: a parked instance may be linked under ANY
        // element at runtime, and the solvers rely on a parent's index being lower than its child's.
        private readonly List<AuiNode> _deferred = new List<AuiNode>();

        private AuiIndexMap(AuiMarkupDocument doc, AuiViewInfo info, AuiResolvedStyle[] styles)
        {
            _doc = doc;
            _info = info;
            _styles = styles;
        }

        public static AuiIndexMap Build(AuiMarkupDocument doc, AuiViewInfo info, AuiResolvedStyle[] styles)
        {
            var m = new AuiIndexMap(doc, info, styles);
            int n = doc.Nodes.Count;
            m.Base = new int[n];
            m.Stride = new int[n];
            m.Copies = new int[n];
            for (int i = 0; i < n; i++) { m.Base[i] = -1; m.Copies[i] = 1; }
            if (doc.Root != null) m.Walk(doc.Root, -1);
            foreach (AuiNode t in m._deferred) m.WalkRepeat(t);
            m.ElementCount = m._nodeOf.Count;
            m.SignalOf = m._signalOf.ToArray();
            m.KindOf = m._kindOf.ToArray();
            m.NodeOf = m._nodeOf.ToArray();
            m.RowOf = m._rowOf.ToArray();
            return m;
        }

        public int Elem(AuiNode node, int instance)
        {
            int b = Base[node.Index];
            if (b < 0) return -1;
            return b + instance * Stride[node.Index];
        }

        public int Signal(AuiNode node, int instance)
        {
            int e = Elem(node, instance);
            return e < 0 ? -1 : SignalOf[e];
        }

        public static int KindOfTag(string tag)
        {
            switch (tag)
            {
                case "button": return KIND_BUTTON;
                case "toggle": return KIND_TOGGLE;
                case "tab": return KIND_TAB;
                case "option": return KIND_OPTION;
                case "input": return KIND_INPUT;
                case "select": return KIND_SELECT;
                case "slider": return KIND_SLIDER;
            }
            return KIND_NONE;
        }

        /// <summary>Interactive by tag, styled by a state rule, or carrying a pointer handler.</summary>
        public bool NeedsSignal(AuiNode node)
        {
            if (KindOfTag(node.Tag) != KIND_NONE) return true;
            if (_styles != null && _styles[node.Index].HasStateRules) return true;
            // A repeat's onselect is a click on each row root.
            if (node.Parent != null && node.Parent.Tag == "repeat" && node.Tag != "empty" && node.Parent.HasAttribute("onselect")) return true;
            foreach (AuiAttribute a in node.Attributes)
                if (a.Name == "onclick" || a.Name == "onhover" || a.Name == "onleave" || a.Name == "onpress" || a.Name == "onrelease") return true;
            return false;
        }

        private int AddElement(AuiNode node, int kind, int row)
        {
            int e = _nodeOf.Count;
            _nodeOf.Add(node);
            _kindOf.Add(kind);
            _rowOf.Add(row);
            bool sig = node != null && NeedsSignal(node) || kind == KIND_SCRIM || kind == KIND_MODAL_SCRIM;
            _signalOf.Add(sig ? SignalCount++ : -1);
            return e;
        }

        private void Walk(AuiNode node, int row)
        {
            if (node.Tag == "repeat")
            {
                WalkRepeat(node);
                return;
            }
            if (node.Tag == "template")
            {
                _deferred.Add(node);
                return;
            }
            // A modal's backdrop scrim is baked right before its body, so the body draws over it.
            if (node.Tag == "modal") ModalScrimElem[node.Index] = AddElement(null, KIND_MODAL_SCRIM, row);
            int e = AddElement(node, KindOfTag(node.Tag), row);
            if (row < 0) Base[node.Index] = e;
            foreach (AuiNode c in node.Children) Walk(c, row);
            if (node.Tag == "select")
            {
                // popup root (after the options, so it draws on top) and a click-away scrim
                PopupElem[node.Index] = AddElement(null, KIND_NONE, row);
                ScrimElem[node.Index] = AddElement(null, KIND_SCRIM, row);
            }
        }

        private void WalkRepeat(AuiNode repeat)
        {
            AuiRepeatInfo r = null;
            if (_info != null) foreach (AuiRepeatInfo ri in _info.Repeats) if (ri.Node == repeat) r = ri;
            AuiNode template = r != null ? r.Template : (repeat.Children.Count > 0 ? repeat.Children[0] : null);
            int max = r != null ? r.Max : 1;

            int e = AddElement(repeat, KIND_NONE, -1);
            Base[repeat.Index] = e;

            if (template != null)
            {
                // Instance 0 assigns Base/Stride; later instances only add elements.
                var templateNodes = new List<AuiNode>();
                Collect(template, templateNodes);
                int stride = templateNodes.Count;
                int firstSignal = SignalCount;
                for (int i = 0; i < max; i++)
                {
                    foreach (AuiNode t in templateNodes)
                    {
                        int te = AddElement(t, KindOfTag(t.Tag), i);
                        if (i == 0)
                        {
                            Base[t.Index] = te;
                            Stride[t.Index] = stride;
                            Copies[t.Index] = max;
                        }
                    }
                    if (i == 0)
                    {
                        RepeatSignalBase[repeat.Index] = firstSignal;
                        RepeatSignalStride[repeat.Index] = SignalCount - firstSignal;
                    }
                }
            }
            if (r != null && r.Empty != null) Walk(r.Empty, -1);
        }

        private static void Collect(AuiNode n, List<AuiNode> list)
        {
            list.Add(n);
            foreach (AuiNode c in n.Children) Collect(c, list);
        }

        /// <summary>Row of a signal that belongs to a repeat, or -1.</summary>
        public int RowOfSignal(int signal)
        {
            for (int e = 0; e < SignalOf.Length; e++) if (SignalOf[e] == signal) return RowOf[e];
            return -1;
        }
    }
}
