using System.Collections.Generic;

namespace Armagan.AUI.Core
{
    /// <summary>One attribute on one element, with the positions the language server needs.</summary>
    public sealed class AuiAttribute
    {
        public string Name;
        public string Value;
        /// <summary>1-based position of the first character of the name.</summary>
        public int Line;
        public int Column;
        /// <summary>1-based position of the first character INSIDE the quotes.</summary>
        public int ValueLine;
        public int ValueColumn;

        public override string ToString()
        {
            return Name + "=\"" + Value + "\"";
        }
    }

    /// <summary>
    /// One element of the markup tree. Literal text between tags is kept on the element as
    /// <see cref="Text"/> (trimmed, inner whitespace collapsed) rather than as child nodes,
    /// because in AUI text is content of an element, never a sibling of one.
    /// </summary>
    public sealed class AuiNode
    {
        public string Tag;
        public readonly List<AuiAttribute> Attributes = new List<AuiAttribute>();
        public readonly List<AuiNode> Children = new List<AuiNode>();
        public AuiNode Parent;
        public string Text;
        public bool SelfClosing;

        /// <summary>1-based position of the opening '&lt;'.</summary>
        public int Line;
        public int Column;
        /// <summary>1-based position just after the last character of the element.</summary>
        public int EndLine;
        public int EndColumn;

        /// <summary>Document-order index, assigned by the parser. Stable for a given source.</summary>
        public int Index;

        /// <summary>Depth from the root (root = 0).</summary>
        public int Depth
        {
            get
            {
                int d = 0;
                for (AuiNode p = Parent; p != null; p = p.Parent) d++;
                return d;
            }
        }

        public AuiAttribute FindAttribute(string name)
        {
            for (int i = 0; i < Attributes.Count; i++)
                if (Attributes[i].Name == name) return Attributes[i];
            return null;
        }

        public bool HasAttribute(string name)
        {
            return FindAttribute(name) != null;
        }

        /// <summary>Attribute value, or null when absent.</summary>
        public string Get(string name)
        {
            AuiAttribute a = FindAttribute(name);
            return a == null ? null : a.Value;
        }

        public string Get(string name, string fallback)
        {
            AuiAttribute a = FindAttribute(name);
            return a == null ? fallback : a.Value;
        }

        public string Id
        {
            get { return Get("id"); }
        }

        /// <summary>Class names in source order; empty array when none.</summary>
        public string[] Classes
        {
            get
            {
                string c = Get("class");
                if (string.IsNullOrEmpty(c)) return new string[0];
                return c.Split(new[] { ' ', '\t', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
            }
        }

        /// <summary>True for a nested-view tag: first letter upper-case.</summary>
        public bool IsComponentTag
        {
            get { return !string.IsNullOrEmpty(Tag) && char.IsUpper(Tag[0]); }
        }

        public AuiNode FindAncestor(string tag)
        {
            for (AuiNode p = Parent; p != null; p = p.Parent)
                if (p.Tag == tag) return p;
            return null;
        }

        public override string ToString()
        {
            return "<" + Tag + (Attributes.Count > 0 ? " " + string.Join(" ", Attributes) : "") +
                   (SelfClosing ? "/>" : ">");
        }
    }
}
