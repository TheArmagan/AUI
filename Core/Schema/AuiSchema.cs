using System.Collections.Generic;

namespace Armagan.AUI.Core
{
    /// <summary>What kind of value a style property takes. Drives parsing, validation and completion.</summary>
    public enum AuiPropKind
    {
        Length,      // 12 | 12mm | auto | 50%
        Box,         // 1..4 lengths (padding, margin)
        Number,      // 0.5
        Color,       // #RRGGBB(AA) | rgba() | transparent | var()
        Background,  // none | color | sprite(name) color
        Font,        // weight size   (600 18)
        FontWeight,  // 400 | regular | medium | semibold | bold
        Enum,        // one of a fixed list
        Transition,  // prop dur ease, ...
        Offset,      // x y
    }

    public sealed class AuiPropDef
    {
        public string Name;
        public AuiPropKind Kind;
        public string[] EnumValues;
        public bool Inherited;
        /// <summary>Can appear in a :state rule and be tweened at runtime.</summary>
        public bool Animatable;
        /// <summary>Applied by the prefab builder only; a class that gains it at runtime has no effect (AUI212).</summary>
        public bool BakeOnly;
        /// <summary>Human text for hover in the editor.</summary>
        public string Doc;
    }

    public sealed class AuiElementDef
    {
        public string Name;
        /// <summary>Element-specific attributes (the common ones are implied).</summary>
        public string[] Attributes;
        /// <summary>Event attributes this element accepts.</summary>
        public string[] Events;
        public bool AllowsChildren;
        public bool AllowsText;
        /// <summary>Tags this element must sit directly under, or null for anywhere.</summary>
        public string[] AllowedParents;
        public string Doc;

        public bool HasAttribute(string name)
        {
            for (int i = 0; i < Attributes.Length; i++) if (Attributes[i] == name) return true;
            return false;
        }

        public bool HasEvent(string name)
        {
            for (int i = 0; i < Events.Length; i++) if (Events[i] == name) return true;
            return false;
        }
    }

    /// <summary>
    /// The single source of truth for what the markup and the stylesheet may contain. Both
    /// the validator (diagnostics) and the language server (completion, hover) read these
    /// tables, so a new element or property is added exactly once, here.
    /// </summary>
    public static class AuiSchema
    {
        public static readonly string[] CommonAttributes = { "id", "class", "style", "if", "hidden" };
        public static readonly string[] States = { "hover", "pressed", "selected", "disabled" };
        public static readonly string[] PointerEvents = { "onhover", "onleave", "onpress", "onrelease" };
        public static readonly string[] Eases =
        {
            "linear",
            "in-quad", "out-quad", "in-out-quad",
            "in-cubic", "out-cubic", "in-out-cubic",
            "in-quart", "out-quart", "in-out-quart",
            "in-sine", "out-sine", "in-out-sine",
            "in-expo", "out-expo", "in-out-expo",
            "in-back", "out-back", "in-out-back",
            "in-bounce", "out-bounce", "in-out-bounce",
            "in-elastic", "out-elastic", "in-out-elastic",
        };

        private static readonly string[] NoAttrs = new string[0];
        private static readonly string[] NoEvents = new string[0];
        private static readonly string[] ClickEvents = { "onclick", "onhover", "onleave", "onpress", "onrelease" };

        public static readonly AuiElementDef[] Elements =
        {
            new AuiElementDef { Name = "view", Attributes = new[] { "name", "width", "height", "show", "start-hidden", "icons", "sprites", "materials", "pool", "utilities" }, Events = NoEvents, AllowsChildren = true, AllowsText = false, Doc = "Root of a view file. name must equal the file stem; width/height in mm. icons / sprites / materials / utilities preload names for SetIcon / SetBackground / SetMaterial / AddClass; pool=\"text:16 panel:8\" bakes elements CreateElement(tag, ...) can hand out." },
            new AuiElementDef { Name = "panel", Attributes = NoAttrs, Events = NoEvents, AllowsChildren = true, AllowsText = false, Doc = "A container. Direction, gap and padding come from the stylesheet." },
            new AuiElementDef { Name = "div", Attributes = NoAttrs, Events = NoEvents, AllowsChildren = true, AllowsText = false, Doc = "Alias of panel." },
            new AuiElementDef { Name = "text", Attributes = new[] { "key", "text", "args", "wrap", "max-lines", "ellipsis", "runtime" }, Events = NoEvents, AllowsChildren = false, AllowsText = true, Doc = "A label. key= for a translated string, text=\"{binding}\" for a runtime value, or literal content." },
            new AuiElementDef { Name = "image", Attributes = new[] { "src", "mode" }, Events = NoEvents, AllowsChildren = false, AllowsText = false, Doc = "A sprite. mode: simple | sliced | fit." },
            new AuiElementDef { Name = "icon", Attributes = new[] { "name", "size" }, Events = NoEvents, AllowsChildren = false, AllowsText = false, Doc = "A Lucide icon by name, tinted by icon-color." },
            new AuiElementDef { Name = "spacer", Attributes = new[] { "size" }, Events = NoEvents, AllowsChildren = false, AllowsText = false, Doc = "Flexible space (grows), or a fixed gap when size is given." },
            new AuiElementDef { Name = "button", Attributes = new[] { "key", "icon", "disabled", "selected" }, Events = ClickEvents, AllowsChildren = true, AllowsText = true, Doc = "A clickable. onclick=\"Handler\" calls Handler() on the view." },
            new AuiElementDef { Name = "toggle", Attributes = new[] { "bind", "key", "icon", "disabled" }, Events = new[] { "onchange", "onhover", "onleave", "onpress", "onrelease" }, AllowsChildren = true, AllowsText = true, Doc = "A two-state button. bind=\"{flag}\"; onchange=\"Handler\" gets the new bool." },
            new AuiElementDef { Name = "slider", Attributes = new[] { "bind", "min", "max", "step", "format-key", "decimals", "disabled" }, Events = new[] { "onchange" }, AllowsChildren = false, AllowsText = false, Doc = "A horizontal slider. bind=\"{value}\"; onchange gets the float." },
            new AuiElementDef { Name = "input", Attributes = new[] { "bind", "placeholder-key", "maxlength", "disabled" }, Events = new[] { "onsubmit" }, AllowsChildren = false, AllowsText = false, Doc = "A text box. Click arms the keyboard panel in front of the player; onsubmit gets the string." },
            new AuiElementDef { Name = "select", Attributes = new[] { "bind", "max", "disabled" }, Events = new[] { "onchange" }, AllowsChildren = true, AllowsText = false, Doc = "A dropdown of <option>s. bind=\"{index}\"; onchange gets the int." },
            new AuiElementDef { Name = "option", Attributes = new[] { "value", "key" }, Events = NoEvents, AllowsChildren = false, AllowsText = true, AllowedParents = new[] { "select" }, Doc = "One entry of a select." },
            new AuiElementDef { Name = "tabs", Attributes = new[] { "bind", "for" }, Events = new[] { "onchange" }, AllowsChildren = true, AllowsText = false, Doc = "A tab strip bound to a <pages>. for= names the pages id when there is more than one set." },
            new AuiElementDef { Name = "tab", Attributes = new[] { "page", "key", "icon" }, Events = NoEvents, AllowsChildren = true, AllowsText = true, AllowedParents = new[] { "tabs" }, Doc = "One tab; page= is the id of the page it shows." },
            new AuiElementDef { Name = "pages", Attributes = new[] { "mode", "default" }, Events = new[] { "onchange" }, AllowsChildren = true, AllowsText = false, Doc = "A page group; exactly one child <page> is visible. mode: crossfade | sequential." },
            new AuiElementDef { Name = "page", Attributes = NoAttrs, Events = NoEvents, AllowsChildren = true, AllowsText = false, AllowedParents = new[] { "pages" }, Doc = "One page of a pages group. Needs an id." },
            new AuiElementDef { Name = "repeat", Attributes = new[] { "max" }, Events = new[] { "onselect" }, AllowsChildren = true, AllowsText = false, Doc = "Bakes max copies of its single template child. {item.x} bindings become SetIdX(row, value)." },
            new AuiElementDef { Name = "empty", Attributes = NoAttrs, Events = NoEvents, AllowsChildren = true, AllowsText = true, AllowedParents = new[] { "repeat" }, Doc = "Shown when the repeat count is 0." },
            new AuiElementDef { Name = "template", Attributes = new[] { "max", "pool", "slot", "view" }, Events = NoEvents, AllowsChildren = true, AllowsText = false, Doc = "Bakes max parked copies of its single child; CreateElement(T_ID, parent) links one into the tree at runtime. {item.x} bindings become SetIdX(elem, value)." },
            new AuiElementDef { Name = "slot", Attributes = new[] { "views", "max" }, Events = NoEvents, AllowsChildren = false, AllowsText = false, Doc = "Bakes max parked instances of each listed view; CreateView(\"slotId\", \"View\", parent) links one in at runtime." },
            new AuiElementDef { Name = "prop", Attributes = new[] { "name", "type", "default" }, Events = NoEvents, AllowsChildren = false, AllowsText = false, AllowedParents = new[] { "view", "component" }, Doc = "Declares a property of this view or component: name, type (string | int | float | bool), default." },
            new AuiElementDef { Name = "event", Attributes = new[] { "name", "type" }, Events = NoEvents, AllowsChildren = false, AllowsText = false, AllowedParents = new[] { "view" }, Doc = "Declares an event this view raises with EmitX(name, value); type is the payload (string | int | float | bool) or absent." },
            new AuiElementDef { Name = "component", Attributes = new[] { "name" }, Events = NoEvents, AllowsChildren = true, AllowsText = false, AllowedParents = new[] { "view" }, Doc = "An inline template: <component name=\"Card\"> one child </component>, used as <Card prop=\"...\"/> in the same file. {prop} inside it is substituted at compile time." },
            new AuiElementDef { Name = "modal", Attributes = new[] { "enabled", "dismissable", "show" }, Events = new[] { "onclose", "onopen" }, AllowsChildren = true, AllowsText = false, AllowedParents = new[] { "view" }, Doc = "A dialog over a dimmed backdrop. enabled=\"{flag}\" opens and closes it; a click on the backdrop closes it unless dismissable=\"false\"." },
        };

        public static readonly AuiPropDef[] Props =
        {
            new AuiPropDef { Name = "width", Kind = AuiPropKind.Length, Doc = "Width in mm, auto, or a percentage of the parent." },
            new AuiPropDef { Name = "height", Kind = AuiPropKind.Length, Doc = "Height in mm, auto, or a percentage of the parent." },
            new AuiPropDef { Name = "min-width", Kind = AuiPropKind.Length },
            new AuiPropDef { Name = "min-height", Kind = AuiPropKind.Length },
            new AuiPropDef { Name = "max-width", Kind = AuiPropKind.Length },
            new AuiPropDef { Name = "max-height", Kind = AuiPropKind.Length },
            new AuiPropDef { Name = "anchor", Kind = AuiPropKind.Enum, EnumValues = new[] { "none", "fill", "top", "bottom", "left", "right", "center" }, Doc = "Absolute placement inside the parent instead of flowing with siblings." },
            new AuiPropDef { Name = "display", Kind = AuiPropKind.Enum, EnumValues = new[] { "flex", "none" }, Doc = "flex takes part in the parent's flow; none removes the element and its space." },
            new AuiPropDef { Name = "position", Kind = AuiPropKind.Enum, EnumValues = new[] { "static", "absolute" }, Doc = "absolute takes the element out of the flow; place it with top/right/bottom/left." },
            new AuiPropDef { Name = "top", Kind = AuiPropKind.Length, Doc = "Distance from the parent's content top for position: absolute. May be negative." },
            new AuiPropDef { Name = "right", Kind = AuiPropKind.Length, Doc = "Distance from the parent's content right edge for position: absolute. May be negative." },
            new AuiPropDef { Name = "bottom", Kind = AuiPropKind.Length, Doc = "Distance from the parent's content bottom for position: absolute. May be negative." },
            new AuiPropDef { Name = "left", Kind = AuiPropKind.Length, Doc = "Distance from the parent's content left edge for position: absolute. May be negative." },
            new AuiPropDef { Name = "padding", Kind = AuiPropKind.Box, Doc = "1 to 4 values: all | vertical horizontal | top horizontal bottom | top right bottom left." },
            new AuiPropDef { Name = "margin", Kind = AuiPropKind.Box },
            new AuiPropDef { Name = "gap", Kind = AuiPropKind.Length, Doc = "Space between children." },
            new AuiPropDef { Name = "flex-direction", Kind = AuiPropKind.Enum, EnumValues = new[] { "row", "column" } },
            new AuiPropDef { Name = "justify", Kind = AuiPropKind.Enum, EnumValues = new[] { "start", "center", "end", "space-between" }, Doc = "Main-axis distribution of children." },
            new AuiPropDef { Name = "align", Kind = AuiPropKind.Enum, EnumValues = new[] { "start", "center", "end", "stretch" }, Doc = "Cross-axis placement of children." },
            new AuiPropDef { Name = "grow", Kind = AuiPropKind.Number, Doc = "Share of leftover main-axis space this element takes." },
            new AuiPropDef { Name = "wrap", Kind = AuiPropKind.Enum, EnumValues = new[] { "true", "false" }, Doc = "Row containers wrap fixed-size children into lines." },
            new AuiPropDef { Name = "color", Kind = AuiPropKind.Color, Inherited = true, Animatable = true, Doc = "Text color." },
            new AuiPropDef { Name = "background", Kind = AuiPropKind.Background, Animatable = true, Doc = "none | color | sprite(Name) color | material(Name) color | shader(Name) color. sprite() combines with material()/shader()." },
            new AuiPropDef { Name = "border", Kind = AuiPropKind.Color, Animatable = true, Doc = "Border color (none for no border)." },
            new AuiPropDef { Name = "radius", Kind = AuiPropKind.Length, BakeOnly = true, Doc = "Corner radius; picks the matching 9-slice sprite. Bake only." },
            new AuiPropDef { Name = "opacity", Kind = AuiPropKind.Number, Animatable = true },
            new AuiPropDef { Name = "scale", Kind = AuiPropKind.Number, Animatable = true },
            new AuiPropDef { Name = "offset", Kind = AuiPropKind.Offset, Animatable = true, Doc = "x y displacement in mm, for state styles." },
            new AuiPropDef { Name = "icon-color", Kind = AuiPropKind.Color, Inherited = true, Animatable = true },
            new AuiPropDef { Name = "font", Kind = AuiPropKind.Font, Inherited = true, Doc = "weight size, e.g. 600 18. The weight is bake only; the size can change at runtime." },
            new AuiPropDef { Name = "font-weight", Kind = AuiPropKind.FontWeight, Inherited = true, BakeOnly = true, Doc = "Bake only: Udon cannot swap a text's font asset." },
            new AuiPropDef { Name = "font-size", Kind = AuiPropKind.Length, Inherited = true },
            new AuiPropDef { Name = "text-align", Kind = AuiPropKind.Enum, EnumValues = new[] { "left", "center", "right" }, Inherited = true },
            new AuiPropDef { Name = "line-height", Kind = AuiPropKind.Number, Inherited = true },
            new AuiPropDef { Name = "transition", Kind = AuiPropKind.Transition, Doc = "prop duration ease, ... e.g. background 0.12s out-quad" },
            new AuiPropDef { Name = "page-transition", Kind = AuiPropKind.Enum, EnumValues = new[] { "none", "fade", "slide-left", "slide-right", "slide-up", "slide-down", "scale" }, BakeOnly = true },
            new AuiPropDef { Name = "show", Kind = AuiPropKind.Enum, EnumValues = new[] { "none", "fade", "fade-scale", "fade-slide" }, BakeOnly = true, Doc = "How the view itself enters and leaves." },
        };

        private static Dictionary<string, AuiElementDef> _elements;
        private static Dictionary<string, AuiPropDef> _props;

        public static AuiElementDef FindElement(string tag)
        {
            if (_elements == null)
            {
                _elements = new Dictionary<string, AuiElementDef>();
                foreach (AuiElementDef e in Elements) _elements[e.Name] = e;
            }
            AuiElementDef d;
            return tag != null && _elements.TryGetValue(tag, out d) ? d : null;
        }

        public static AuiPropDef FindProp(string name)
        {
            if (_props == null)
            {
                _props = new Dictionary<string, AuiPropDef>();
                foreach (AuiPropDef p in Props) _props[p.Name] = p;
            }
            AuiPropDef d;
            return name != null && _props.TryGetValue(name, out d) ? d : null;
        }

        public static bool IsCommonAttribute(string name)
        {
            for (int i = 0; i < CommonAttributes.Length; i++) if (CommonAttributes[i] == name) return true;
            return false;
        }

        public static bool IsState(string name)
        {
            for (int i = 0; i < States.Length; i++) if (States[i] == name) return true;
            return false;
        }

        public static bool IsEase(string name)
        {
            for (int i = 0; i < Eases.Length; i++) if (Eases[i] == name) return true;
            return false;
        }

        public static bool IsIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (!(char.IsLetter(s[0]) || s[0] == '_')) return false;
            for (int i = 1; i < s.Length; i++)
                if (!(char.IsLetterOrDigit(s[i]) || s[i] == '_')) return false;
            return true;
        }

        /// <summary>Closest name from a candidate list for "did you mean" hints; null when nothing is close.</summary>
        public static string Suggest(string input, IEnumerable<string> candidates)
        {
            if (string.IsNullOrEmpty(input)) return null;
            string best = null;
            int bestDist = int.MaxValue;
            foreach (string c in candidates)
            {
                int d = Levenshtein(input, c);
                if (d < bestDist) { bestDist = d; best = c; }
            }
            int limit = input.Length <= 4 ? 1 : 2;
            return bestDist <= limit ? best : null;
        }

        /// <summary>Optimal string alignment distance: a swapped pair ("kye" for "key") costs 1, as a typo should.</summary>
        private static int Levenshtein(string a, string b)
        {
            int[,] m = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) m[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) m[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    m[i, j] = System.Math.Min(System.Math.Min(m[i - 1, j] + 1, m[i, j - 1] + 1), m[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                        m[i, j] = System.Math.Min(m[i, j], m[i - 2, j - 2] + 1);
                }
            return m[a.Length, b.Length];
        }
    }
}
