using System.Collections.Generic;
using System.Text;

namespace Armagan.AUI.Core
{
    /// <summary>
    /// The built-in utility classes: a small, generated, tailwind-like set of star classes
    /// (`*p-8`, `*flex-row`, `*w-full`, ...). Star classes are style-only: they never reach
    /// GetElements, get no C_ constant and have their own 256-slot budget. The sheet sits in
    /// the Utility tier, between the theme and a view's own sheets, so a view can override any
    /// of them, and only the utilities a view references are baked into its runtime tables.
    ///
    /// The list is generated from this file, which is also what the docs table and the language
    /// server read, so the three cannot drift.
    /// </summary>
    public static class AuiUtilityClasses
    {
        public static readonly int[] Steps = { 0, 1, 2, 4, 6, 8, 12, 16, 24, 32, 48, 64 };
        public static readonly int[] Radii = { 4, 8, 12, 16, 24 };
        public static readonly int[] Opacities = { 0, 25, 50, 75, 100 };

        private static AuiStyleSheet _sheet;
        private static string _text;

        /// <summary>The utility sheet text (.auis syntax with .*name selectors).</summary>
        public static string Text()
        {
            if (_text != null) return _text;
            var sb = new StringBuilder();
            sb.AppendLine("/* generated: AuiUtilityClasses */");
            foreach (KeyValuePair<string, string> kv in All()) sb.Append(".*").Append(kv.Key).Append(" { ").Append(kv.Value).AppendLine(" }");
            _text = sb.ToString();
            return _text;
        }

        /// <summary>The parsed sheet, built once.</summary>
        public static AuiStyleSheet Sheet()
        {
            if (_sheet == null) _sheet = AuiStyleParser.Parse(Text(), "AuiUtilityClasses", AuiStyleTier.Utility);
            return _sheet;
        }

        /// <summary>Every utility as (name without the star, declarations), in a stable order.</summary>
        public static List<KeyValuePair<string, string>> All()
        {
            var list = new List<KeyValuePair<string, string>>();
            System.Action<string, string> add = (n, v) => list.Add(new KeyValuePair<string, string>(n, v));

            // ---- layout
            add("flex-row", "flex-direction: row;");
            add("flex-col", "flex-direction: column;");
            add("wrap", "wrap: true;");
            add("nowrap", "wrap: false;");
            add("grow", "grow: 1;");
            add("grow-0", "grow: 0;");
            foreach (string a in new[] { "start", "center", "end", "stretch" }) add("items-" + a, "align: " + a + ";");
            foreach (string j in new[] { "start", "center", "end" }) add("justify-" + j, "justify: " + j + ";");
            add("justify-between", "justify: space-between;");
            foreach (int n in Steps)
            {
                add("gap-" + n, "gap: " + n + ";");
                add("p-" + n, "padding: " + n + ";");
                add("px-" + n, "padding: 0 " + n + ";");
                add("py-" + n, "padding: " + n + " 0;");
                add("pt-" + n, "padding: " + n + " 0 0 0;");
                add("pr-" + n, "padding: 0 " + n + " 0 0;");
                add("pb-" + n, "padding: 0 0 " + n + " 0;");
                add("pl-" + n, "padding: 0 0 0 " + n + ";");
                add("m-" + n, "margin: " + n + ";");
                add("mx-" + n, "margin: 0 " + n + ";");
                add("my-" + n, "margin: " + n + " 0;");
                add("mt-" + n, "margin: " + n + " 0 0 0;");
                add("mr-" + n, "margin: 0 " + n + " 0 0;");
                add("mb-" + n, "margin: 0 0 " + n + " 0;");
                add("ml-" + n, "margin: 0 0 0 " + n + ";");
                add("w-" + n, "width: " + n + ";");
                add("h-" + n, "height: " + n + ";");
                add("top-" + n, "top: " + n + ";");
                add("right-" + n, "right: " + n + ";");
                add("bottom-" + n, "bottom: " + n + ";");
                add("left-" + n, "left: " + n + ";");
            }
            add("w-full", "width: 100%;");
            add("h-full", "height: 100%;");
            add("w-auto", "width: auto;");
            add("h-auto", "height: auto;");
            add("hidden", "display: none;");
            add("flex", "display: flex;");
            add("absolute", "position: absolute;");
            add("static", "position: static;");
            add("inset-0", "position: absolute; top: 0; right: 0; bottom: 0; left: 0;");
            add("center", "anchor: center;");

            // ---- visual
            foreach (int r in Radii) add("rounded-" + r, "radius: " + r + ";");
            foreach (int o in Opacities) add("opacity-" + o, "opacity: " + (o / 100f).ToString(System.Globalization.CultureInfo.InvariantCulture) + ";");
            foreach (string t in new[] { "left", "center", "right" }) add("text-" + t, "text-align: " + t + ";");
            add("text-sm", "font-size: 14;");
            add("text-base", "font-size: 18;");
            add("text-lg", "font-size: 22;");
            add("text-xl", "font-size: 26;");
            add("font-regular", "font-weight: 400;");
            add("font-medium", "font-weight: 500;");
            add("font-semibold", "font-weight: 600;");
            add("font-bold", "font-weight: 700;");
            add("bg-transparent", "background: transparent;");
            add("bg-none", "background: none;");
            // Fallbacks are the default theme's values, so the utilities still resolve in a view compiled without it.
            foreach (string v in new[] { "base", "nav", "surface", "panel", "card", "chip", "accent" }) add("bg-" + v, "background: sprite(Rounded8) var(--" + v + ", " + ThemeDefault(v) + ");");
            foreach (string v in new[] { "text", "muted", "accent", "base" }) add("fg-" + v, "color: var(--" + v + ", " + ThemeDefault(v) + "); icon-color: var(--" + v + ", " + ThemeDefault(v) + ");");
            add("scale-90", "scale: 0.9;");
            add("scale-100", "scale: 1;");
            add("scale-110", "scale: 1.1;");
            return list;
        }

        private static string ThemeDefault(string v)
        {
            switch (v)
            {
                case "base": return "#150F0B";
                case "nav": return "#17120D";
                case "surface": return "#1E1610";
                case "panel": return "#291E17";
                case "card": return "#241B14";
                case "chip": return "#3E3126";
                case "accent": return "#E0A86A";
                case "text": return "#EEE1D2";
                case "muted": return "#A38C76";
            }
            return "#FFFFFF";
        }

        public static bool IsStar(string className)
        {
            return !string.IsNullOrEmpty(className) && className[0] == '*';
        }
    }
}
