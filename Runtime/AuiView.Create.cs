using UnityEngine;

namespace Armagan.AUI
{
    /// <summary>
    /// Runtime creation without allocation: a &lt;template&gt; is a repeat whose rows are baked
    /// parked (inactive, under the template's own object, at the END of the element order so
    /// any parent index stays lower than theirs). CreateElement takes a parked instance, moves
    /// its GameObject under the chosen parent, links it into the layout tree (parent / first
    /// child / next sibling arrays are mutable at runtime) and shows it; DestroyElement parks
    /// it again. Tag pools (&lt;view pool="text:16"&gt;) and view slots (&lt;slot&gt;) are templates
    /// the compiler synthesized, so the same path serves all three.
    /// </summary>
    public partial class AuiView
    {
        [Header("Templates and pools (baked)")]
        [HideInInspector] public bool[] repeatIsTemplate;
        [HideInInspector] public int[] repeatElem;        // the repeat / template element itself
        [HideInInspector] public string[] poolTag;        // per pool: the tag, and its template (repeat index)
        [HideInInspector] public int[] poolTemplate;
        [HideInInspector] public string[] slotId;         // per slot template: slot id, view name, template (repeat index)
        [HideInInspector] public string[] slotView;
        [HideInInspector] public int[] slotTemplate;

        private bool[] _instanceLive;   // [repeat row] flattened: repeatFirst offsets differ, so index by (repeat index * 64 + row)
        private const int MaxRows = 64;

        private void _AuiCreateInit()
        {
            int reps = repeatFirst == null ? 0 : repeatFirst.Length;
            _instanceLive = new bool[Mathf.Max(1, reps * MaxRows)];
        }

        private bool IsTemplate(int repeat)
        {
            return repeatIsTemplate != null && repeat >= 0 && repeat < repeatIsTemplate.Length && repeatIsTemplate[repeat];
        }

        /// <summary>Row (instance index) of an element that is a template's instance root, or -1.</summary>
        public int InstanceRow(int template, int elem)
        {
            if (!IsTemplate(template) || elem < 0) return -1;
            int first = repeatFirst[template], stride = repeatStride[template];
            if (stride <= 0 || elem < first) return -1;
            int row = (elem - first) / stride;
            if (row >= repeatMax[template] || first + row * stride != elem) return -1;
            return row;
        }

        /// <summary>Instance root element of a template row.</summary>
        public int InstanceElem(int template, int row)
        {
            if (!IsTemplate(template) || row < 0 || row >= repeatMax[template]) return -1;
            return repeatFirst[template] + row * repeatStride[template];
        }

        public bool IsInstanceLive(int template, int row)
        {
            return _instanceLive != null && row >= 0 && row < MaxRows && template >= 0 && _instanceLive[template * MaxRows + row];
        }

        /// <summary>Which template an instance root belongs to, or -1.</summary>
        public int TemplateOf(int elem)
        {
            int reps = repeatFirst == null ? 0 : repeatFirst.Length;
            for (int r = 0; r < reps; r++) if (IsTemplate(r) && InstanceRow(r, elem) >= 0) return r;
            return -1;
        }

        // ================================================================ create / destroy / move

        /// <summary>Take a parked instance of the template, link it under parent at index (-1 = last) and show it. Returns its root element, or -1 when the pool is empty.</summary>
        public int CreateElement(int template, int parent, int index)
        {
            if (!_started) { _auiStashI = template; _auiStashI2 = parent; _AuiStart(); template = _auiStashI; parent = _auiStashI2; }
            if (!IsTemplate(template) || !_layoutReady) return -1;
            if (parent < 0 || parent >= layParent.Length || layKind[parent] == LK_IGNORE || layKind[parent] == LK_TEXT || layKind[parent] == LK_LEAF)
            {
                Debug.LogWarning("[AUI] " + name + ": CreateElement needs a container parent; element " + parent + " is not one.");
                return -1;
            }
            int max = repeatMax[template];
            int row = -1;
            for (int i = 0; i < max; i++) if (!_instanceLive[template * MaxRows + i]) { row = i; break; }
            if (row < 0)
            {
                Debug.LogWarning("[AUI] " + name + ": template " + template + " has no free instance (max " + max + "); raise max=\"...\".");
                return -1;
            }
            int elem = InstanceElem(template, row);
            _instanceLive[template * MaxRows + row] = true;
            Link(elem, parent, index);
            GameObject go = elemObjects[elem];
            if (go != null)
            {
                go.transform.SetParent(elemObjects[parent].transform, false);
                go.transform.SetSiblingIndex(SiblingIndexFor(elem, parent));
                go.SetActive(true);
            }
            _shown[elem] = true;
            if (_stylesReady) ReResolveSubtree(elem);
            MarkLayoutDirty();
            return elem;
        }

        /// <summary>CreateElement from a tag pool (&lt;view pool="text:16 panel:8"&gt;), with classes applied.</summary>
        public int CreateElementByTag(string tag, int parent, string classes, int index)
        {
            if (!_started) { _auiStashS = tag; _auiStashI = parent; _auiStashI2 = index; _AuiStart(); tag = _auiStashS; parent = _auiStashI; index = _auiStashI2; }
            int t = PoolTemplate(tag);
            if (t < 0)
            {
                Debug.LogWarning("[AUI] " + name + ": no pool for '" + tag + "'; add pool=\"" + tag + ":N\" to <view>.");
                return -1;
            }
            int elem = CreateElement(t, parent, index);
            if (elem >= 0 && !string.IsNullOrEmpty(classes)) SetClasses(elem, classes);
            return elem;
        }

        public int PoolTemplate(string tag)
        {
            if (poolTag == null) return -1;
            if (tag == "div") tag = "panel";
            for (int i = 0; i < poolTag.Length; i++) if (poolTag[i] == tag) return poolTemplate[i];
            return -1;
        }

        /// <summary>Park a created element again (its subtree keeps its texts and classes for the next CreateElement).</summary>
        public void DestroyElement(int elem)
        {
            if (!_started) { _auiStashI = elem; _AuiStart(); elem = _auiStashI; }
            int t = TemplateOf(elem);
            if (t < 0) { Debug.LogWarning("[AUI] " + name + ": element " + elem + " was not created from a template."); return; }
            int row = InstanceRow(t, elem);
            if (!_instanceLive[t * MaxRows + row]) return;
            _instanceLive[t * MaxRows + row] = false;
            ResetPointerStatesUnder(elem);
            Unlink(elem);
            _shown[elem] = false;
            GameObject go = elemObjects[elem];
            if (go != null)
            {
                go.SetActive(false);
                int home = repeatElem != null && t < repeatElem.Length ? repeatElem[t] : -1;
                if (home >= 0 && elemObjects[home] != null) go.transform.SetParent(elemObjects[home].transform, false);
            }
            MarkLayoutDirty();
        }

        /// <summary>Move a created element under another parent (or reorder it within the same one).</summary>
        public void MoveElement(int elem, int newParent, int index)
        {
            if (!_started) { _auiStashI = elem; _auiStashI2 = newParent; _AuiStart(); elem = _auiStashI; newParent = _auiStashI2; }
            int t = TemplateOf(elem);
            if (t < 0 || !IsInstanceLive(t, InstanceRow(t, elem))) return;
            if (newParent < 0 || newParent >= layParent.Length) return;
            Unlink(elem);
            Link(elem, newParent, index);
            GameObject go = elemObjects[elem];
            if (go != null)
            {
                go.transform.SetParent(elemObjects[newParent].transform, false);
                go.transform.SetSiblingIndex(SiblingIndexFor(elem, newParent));
            }
            if (_stylesReady) ReResolveSubtree(elem);
            MarkLayoutDirty();
        }

        /// <summary>Take a parked nested view from a slot template and link it under parent. Cast the result to the view's class.</summary>
        public AuiView CreateView(int template, int parent, int index)
        {
            int elem = CreateElement(template, parent, index);
            if (elem < 0 || elemChildView == null || elem >= elemChildView.Length) return null;
            AuiView child = elemChildView[elem];
            if (child != null)
            {
                child.EnsureStarted();
                child.Show();
                child._AuiSetLayoutSize(child.layRootW, child.layRootH);
            }
            return child;
        }

        /// <summary>CreateView by slot id and view name (&lt;slot id="s" views="Foo Bar"/&gt;).</summary>
        public AuiView CreateViewNamed(string slot, string viewName, int parent)
        {
            if (slotId == null) return null;
            for (int i = 0; i < slotId.Length; i++)
                if (slotId[i] == slot && slotView[i] == viewName) return CreateView(slotTemplate[i], parent, -1);
            Debug.LogWarning("[AUI] " + name + ": no slot '" + slot + "' holding '" + viewName + "'.");
            return null;
        }

        public void DestroyView(AuiView child)
        {
            if (child == null || elemChildView == null) return;
            for (int e = 0; e < elemChildView.Length; e++) if (elemChildView[e] == child) { DestroyElement(e); return; }
        }

        /// <summary>Row of a created nested view inside a slot template, or -1.</summary>
        public int InstanceRowOfView(int template, AuiView child)
        {
            if (child == null || elemChildView == null || !IsTemplate(template)) return -1;
            for (int i = 0; i < repeatMax[template]; i++)
            {
                int e = InstanceElem(template, i);
                if (e >= 0 && e < elemChildView.Length && elemChildView[e] == child) return i;
            }
            return -1;
        }

        public int InstanceElemOfView(int template, AuiView child)
        {
            int row = InstanceRowOfView(template, child);
            return row < 0 ? -1 : InstanceElem(template, row);
        }

        // ================================================================ tree queries

        public int ElementParent(int elem)
        {
            return layParent != null && elem >= 0 && elem < layParent.Length ? layParent[elem] : -1;
        }

        /// <summary>Number of live children in the layout tree.</summary>
        public int ChildCount(int parent)
        {
            if (layFirstChild == null || parent < 0 || parent >= layFirstChild.Length) return 0;
            int n = 0;
            for (int c = layFirstChild[parent]; c >= 0; c = layNextSibling[c]) if (layKind[c] != LK_IGNORE) n++;
            return n;
        }

        public int ChildAt(int parent, int index)
        {
            if (layFirstChild == null || parent < 0 || parent >= layFirstChild.Length) return -1;
            int n = 0;
            for (int c = layFirstChild[parent]; c >= 0; c = layNextSibling[c])
            {
                if (layKind[c] == LK_IGNORE) continue;
                if (n == index) return c;
                n++;
            }
            return -1;
        }

        // ================================================================ linking

        private void Link(int elem, int parent, int index)
        {
            layParent[elem] = parent;
            layNextSibling[elem] = -1;
            int first = layFirstChild[parent];
            if (first < 0 || index == 0) { layNextSibling[elem] = first; layFirstChild[parent] = elem; return; }
            int prev = first, i = 1;
            while (layNextSibling[prev] >= 0 && (index < 0 || i < index)) { prev = layNextSibling[prev]; i++; }
            layNextSibling[elem] = layNextSibling[prev];
            layNextSibling[prev] = elem;
        }

        private void Unlink(int elem)
        {
            int parent = layParent[elem];
            if (parent < 0) return;
            if (layFirstChild[parent] == elem) layFirstChild[parent] = layNextSibling[elem];
            else
            {
                for (int c = layFirstChild[parent]; c >= 0; c = layNextSibling[c])
                    if (layNextSibling[c] == elem) { layNextSibling[c] = layNextSibling[elem]; break; }
            }
            layParent[elem] = -1;
            layNextSibling[elem] = -1;
        }

        /// <summary>Position among the parent's GameObject children that matches the layout order (baked children come first in element order).</summary>
        private int SiblingIndexFor(int elem, int parent)
        {
            int i = 0;
            for (int c = layFirstChild[parent]; c >= 0; c = layNextSibling[c])
            {
                if (c == elem) return i;
                if (elemObjects[c] != null && elemObjects[c].transform.parent == elemObjects[parent].transform) i++;
            }
            return i;
        }
    }
}
