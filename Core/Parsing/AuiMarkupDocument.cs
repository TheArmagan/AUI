using System.Collections.Generic;

namespace Armagan.AUI.Core
{
    /// <summary>A &lt;style&gt; block found in the markup, kept as raw text for the stylesheet parser.</summary>
    public sealed class AuiStyleBlock
    {
        public string Text;
        /// <summary>1-based position of the first character of the block's content.</summary>
        public int Line;
        public int Column;
    }

    /// <summary>A &lt;link href="..."/&gt; found under the root.</summary>
    public sealed class AuiLink
    {
        public string Href;
        public int Line;
        public int Column;
    }

    /// <summary>A &lt;prop name type default/&gt; declaration on a view or an inline component.</summary>
    public sealed class AuiPropDecl
    {
        public string Name;
        /// <summary>string | int | float | bool (as written).</summary>
        public string Type;
        public string Default;
        /// <summary>The view root or the component the prop belongs to.</summary>
        public AuiNode Owner;
        public int Line;
        public int Column;
    }

    /// <summary>An &lt;event name type/&gt; declaration: what a view's Emit carries to its host.</summary>
    public sealed class AuiEventDecl
    {
        public string Name;
        /// <summary>string | int | float | bool, or null for no payload.</summary>
        public string Type;
        public AuiNode Owner;
        public int Line;
        public int Column;
    }

    /// <summary>An inline &lt;component name="X"&gt; ... &lt;/component&gt;: a template expanded wherever &lt;X/&gt; appears in the same file.</summary>
    public sealed class AuiComponentDecl
    {
        public string Name;
        public AuiNode Node;
        /// <summary>The single template child.</summary>
        public AuiNode Root;
        public readonly List<AuiPropDecl> Props = new List<AuiPropDecl>();
        public readonly List<AuiEventDecl> Events = new List<AuiEventDecl>();

        public AuiPropDecl FindProp(string name)
        {
            foreach (AuiPropDecl p in Props) if (p.Name == name) return p;
            return null;
        }
    }

    /// <summary>
    /// The parsed shape of one .aui file: a tree, the style blocks and links lifted out of it,
    /// and every diagnostic the parser raised. Purely syntactic; the validator and the
    /// resolvers read it, they do not mutate it (the component expander runs before them).
    /// </summary>
    public sealed class AuiMarkupDocument
    {
        public string File;
        public AuiNode Root;
        /// <summary>Every element in document order (Root first). Index == node.Index.</summary>
        public readonly List<AuiNode> Nodes = new List<AuiNode>();
        public readonly List<AuiStyleBlock> Styles = new List<AuiStyleBlock>();
        public readonly List<AuiLink> Links = new List<AuiLink>();
        /// <summary>&lt;prop&gt; declarations of the view itself (component props are on their component).</summary>
        public readonly List<AuiPropDecl> Props = new List<AuiPropDecl>();
        public readonly List<AuiEventDecl> Events = new List<AuiEventDecl>();
        public readonly List<AuiComponentDecl> Components = new List<AuiComponentDecl>();
        public readonly AuiDiagnosticBag Diagnostics;

        public AuiComponentDecl FindComponent(string name)
        {
            foreach (AuiComponentDecl c in Components) if (c.Name == name) return c;
            return null;
        }

        public AuiMarkupDocument(string file)
        {
            File = file;
            Diagnostics = new AuiDiagnosticBag(file);
        }

        /// <summary>The root's name attribute, or null.</summary>
        public string ViewName
        {
            get { return Root == null ? null : Root.Get("name"); }
        }

        public AuiNode FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Nodes.Count; i++)
                if (Nodes[i].Id == id) return Nodes[i];
            return null;
        }

        /// <summary>The innermost element whose span contains the position (LSP hover/completion).</summary>
        public AuiNode NodeAt(int line, int column)
        {
            AuiNode best = null;
            for (int i = 0; i < Nodes.Count; i++)
            {
                AuiNode n = Nodes[i];
                if (!Contains(n, line, column)) continue;
                if (best == null || n.Depth > best.Depth) best = n;
            }
            return best;
        }

        private static bool Contains(AuiNode n, int line, int column)
        {
            if (line < n.Line || line > n.EndLine) return false;
            if (line == n.Line && column < n.Column) return false;
            if (line == n.EndLine && column > n.EndColumn) return false;
            return true;
        }
    }
}
