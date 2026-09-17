using System.Collections.Generic;

namespace Armagan.AUI.Core
{
    public enum AuiBindingType
    {
        Unknown = 0, // set by a nested-view attribute whose child type is resolved later
        String,
        Bool,
        Float,
        Int,
        /// <summary>UnityEngine.Color, for style="color: {c}" and background colours.</summary>
        Color,
        /// <summary>UnityEngine.Texture, for &lt;rawimage texture="{t}"&gt;. Starts null.</summary>
        Texture,
        /// <summary>UnityEngine.Rect, for &lt;rawimage uv="{r}"&gt;. Starts as the whole texture (0, 0, 1, 1).</summary>
        Rect,
    }

    public sealed class AuiBindingUse
    {
        public AuiNode Node;
        public string Attribute;
        public AuiBindingType Type;
        /// <summary>For a class="a {b} {c}" attribute: which binding token this use is (0-based), so each keeps its own runtime mask.</summary>
        public int Slot;
        /// <summary>For a style="prop: {x}" use: the property, and which component of it (offset x/y, box edges) this binding fills.</summary>
        public string StyleProp;
        public int StyleIndex;
    }

    /// <summary>One {name} the markup mentions; becomes one generated property (or one SetRowsX method inside a repeat).</summary>
    public sealed class AuiBinding
    {
        public string Name;
        public AuiBindingType Type;
        /// <summary>{item.x} inside a repeat template.</summary>
        public bool IsItem;
        public AuiRepeatInfo Repeat;
        public readonly List<AuiBindingUse> Uses = new List<AuiBindingUse>();
        /// <summary>Initial value as written in a &lt;prop default="..."&gt;, or null for the type's zero.</summary>
        public string Default;
        /// <summary>Declared through &lt;prop&gt; (typed up front, visible to hosts even when unused in the markup).</summary>
        public bool IsProp;

        /// <summary>PascalCase name for the generated member.</summary>
        public string PropertyName
        {
            get { return AuiNaming.Pascal(Name); }
        }

        public string CsType
        {
            get { return AuiNaming.CsType(Type); }
        }
    }

    /// <summary>A method the author must write: named in an on* attribute.</summary>
    public sealed class AuiHandler
    {
        public string Name;
        public string Event;
        public AuiNode Node;
        /// <summary>"bool", "float", "int", "string" or null for none.</summary>
        public string ParamType;
        /// <summary>Inside a repeat: the handler takes the row index first.</summary>
        public bool HasRow;
        public AuiRepeatInfo Repeat;

        public string Signature
        {
            get
            {
                var ps = new List<string>();
                if (HasRow) ps.Add("int row");
                if (ParamType != null) ps.Add(ParamType + " value");
                return "void " + Name + "(" + string.Join(", ", ps) + ")";
            }
        }
    }

    public sealed class AuiRepeatInfo
    {
        public AuiNode Node;
        public string Id;
        public int Max;
        public AuiNode Template;
        public AuiNode Empty;
        /// <summary>A &lt;template&gt;: rows are parked at bake and linked under a parent by CreateElement, never laid out as a list.</summary>
        public bool IsTemplate;
        /// <summary>For a template synthesized from &lt;view pool="tag:N"&gt;: the tag it hands out.</summary>
        public string PoolTag;
        /// <summary>For a template synthesized from a &lt;slot&gt;: the slot id and the nested view it holds.</summary>
        public string SlotId;
        public string SlotView;
        public readonly List<AuiBinding> ItemBindings = new List<AuiBinding>();
        public readonly List<AuiHandler> Handlers = new List<AuiHandler>();

        public string PascalId
        {
            get { return AuiNaming.Pascal(Id); }
        }
    }

    public sealed class AuiPageGroup
    {
        public AuiNode Node;
        public string Id;
        public readonly List<AuiNode> Pages = new List<AuiNode>();
        public AuiNode Tabs;
        public readonly List<AuiNode> TabNodes = new List<AuiNode>();
        public int DefaultIndex;
        public string Mode; // crossfade | sequential
        public AuiBinding Binding;

        public string PascalId
        {
            get { return AuiNaming.Pascal(Id); }
        }

        public int IndexOfPage(string id)
        {
            for (int i = 0; i < Pages.Count; i++) if (Pages[i].Id == id) return i;
            return -1;
        }
    }

    public sealed class AuiKeyUse
    {
        public string Key;
        public AuiNode Node;
        public AuiAttribute Attribute;
        /// <summary>Binding names substituted into the translated string's {placeholders}.</summary>
        public readonly List<AuiBinding> Args = new List<AuiBinding>();
    }

    public sealed class AuiNestedView
    {
        public AuiNode Node;
        public string ViewName;
        public string FieldName;
    }

    public sealed class AuiSelectInfo
    {
        public AuiNode Node;
        public readonly List<AuiNode> Options = new List<AuiNode>();
        public int Max;
        public AuiBinding Binding;

        /// <summary>
        /// How many of <see cref="Options"/> the author actually wrote; the rest are rows
        /// reserved by <c>max</c> and expanded by AuiTemplateExpander. They are baked inactive
        /// and do not count toward the popup's resting height, so a select at rest is as tall as
        /// the options it really has.
        /// </summary>
        public int Authored
        {
            get { return Node != null && Node.ReservedFrom >= 0 ? Node.ReservedFrom : Options.Count; }
        }
    }

    public sealed class AuiModalInfo
    {
        public AuiNode Node;
        public string Id;
        /// <summary>The enabled="{flag}" binding, or null when enabled is literal / absent.</summary>
        public AuiBinding Binding;
        /// <summary>Open at start (enabled="true" literal).</summary>
        public bool StartOpen;
        public bool Dismissable = true;

        public string PascalId
        {
            get { return AuiNaming.Pascal(Id); }
        }
    }

    /// <summary>
    /// Everything the code generator and the prefab builder need to know about one view, as
    /// the analyzer derived it from the tree: bindings with their inferred types, handler
    /// signatures, repeats, page groups, selects, translation keys and nested views.
    /// </summary>
    public sealed class AuiViewInfo
    {
        public AuiMarkupDocument Doc;
        public string Name;
        public float Width;
        public float Height;
        /// <summary>&lt;view attach&gt;: index into AuiSchema.AttachModes, 0 for none. Read by a host that nests this view (AUI123) and by the builder.</summary>
        public int Attach;
        /// <summary>The attribute is present at all. attach="none" still gets the component, so code can call SetAttach later.</summary>
        public bool HasAttach;
        /// <summary>&lt;view attach-corner&gt;: index into AuiSchema.AttachCorners.</summary>
        public int AttachCorner;
        /// <summary>&lt;view attach-distance&gt; in metres.</summary>
        public float AttachDistance = 0.6f;
        public readonly List<AuiBinding> Bindings = new List<AuiBinding>();
        public readonly List<AuiHandler> Handlers = new List<AuiHandler>();
        public readonly List<AuiRepeatInfo> Repeats = new List<AuiRepeatInfo>();
        public readonly List<AuiPageGroup> PageGroups = new List<AuiPageGroup>();
        public readonly List<AuiSelectInfo> Selects = new List<AuiSelectInfo>();
        public readonly List<AuiModalInfo> Modals = new List<AuiModalInfo>();
        /// <summary>Events declared with &lt;event name type/&gt;; a host's on-name handler takes the payload type.</summary>
        public readonly List<AuiEventDecl> Events = new List<AuiEventDecl>();
        public readonly List<AuiKeyUse> Keys = new List<AuiKeyUse>();

        public AuiEventDecl FindEvent(string name)
        {
            foreach (AuiEventDecl e in Events) if (e.Name == name) return e;
            return null;
        }

        /// <summary>Bindings declared as props (for host completion and validation).</summary>
        public bool HasDeclaredProps
        {
            get { foreach (AuiBinding b in Bindings) if (b.IsProp) return true; return false; }
        }
        public readonly List<AuiNestedView> NestedViews = new List<AuiNestedView>();

        public AuiBinding FindBinding(string name, AuiRepeatInfo repeat)
        {
            if (repeat != null)
            {
                foreach (AuiBinding b in repeat.ItemBindings) if (b.Name == name) return b;
                return null;
            }
            foreach (AuiBinding b in Bindings) if (b.Name == name) return b;
            return null;
        }

        public AuiRepeatInfo RepeatContaining(AuiNode node)
        {
            foreach (AuiRepeatInfo r in Repeats)
                for (AuiNode p = node; p != null; p = p.Parent)
                    if (p == r.Template) return r;
            return null;
        }

        public AuiHandler FindHandler(string name)
        {
            foreach (AuiHandler h in Handlers) if (h.Name == name) return h;
            return null;
        }
    }

    public static class AuiNaming
    {
        /// <summary>playerName -> PlayerName, solid-furniture -> SolidFurniture, a.b -> AB.</summary>
        public static string Pascal(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            bool up = true;
            foreach (char c in s)
            {
                if (c == '-' || c == '_' || c == '.' || c == ' ') { up = true; continue; }
                sb.Append(up ? char.ToUpperInvariant(c) : c);
                up = false;
            }
            return sb.ToString();
        }

        /// <summary>PlayerName -> _playerName.</summary>
        public static string Field(string s)
        {
            string p = Pascal(s);
            return "_" + char.ToLowerInvariant(p[0]) + p.Substring(1);
        }

        public static string CsType(AuiBindingType t)
        {
            switch (t)
            {
                case AuiBindingType.Bool: return "bool";
                case AuiBindingType.Float: return "float";
                case AuiBindingType.Int: return "int";
                case AuiBindingType.Color: return "Color";
                case AuiBindingType.Texture: return "Texture";
                case AuiBindingType.Rect: return "Rect";
                default: return "string";
            }
        }

        /// <summary>A C# identifier from any id: strips illegal characters, prefixes a digit.</summary>
        public static string Identifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            if (sb.Length == 0) return "_";
            if (char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }
    }
}
