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
    }

    public sealed class AuiBindingUse
    {
        public AuiNode Node;
        public string Attribute;
        public AuiBindingType Type;
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
        public readonly List<AuiBinding> Bindings = new List<AuiBinding>();
        public readonly List<AuiHandler> Handlers = new List<AuiHandler>();
        public readonly List<AuiRepeatInfo> Repeats = new List<AuiRepeatInfo>();
        public readonly List<AuiPageGroup> PageGroups = new List<AuiPageGroup>();
        public readonly List<AuiSelectInfo> Selects = new List<AuiSelectInfo>();
        public readonly List<AuiKeyUse> Keys = new List<AuiKeyUse>();
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
