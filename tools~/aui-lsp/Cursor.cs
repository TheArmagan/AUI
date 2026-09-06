using System;
using System.Text;

namespace Armagan.AUI.Lsp
{
    /// <summary>
    /// Where the caret is inside a .aui or .auis file, decided from the text before it. The
    /// parser's tree is not enough for completion because the file is usually broken right at
    /// the caret; a backwards scan over raw text is what an editor needs.
    /// </summary>
    public sealed class Cursor
    {
        public enum Kind
        {
            None,
            TagName,          // "<pa|"
            AttributeName,    // "<panel cl|"
            AttributeValue,   // "<text key="set|"
            StyleProperty,    // ".card { pad|"
            StyleValue,       // ".card { padding: 1|"
            StyleSelector,    // "but|"  at rule level
        }

        public Kind Where;
        public string Tag;         // enclosing element name (markup)
        public string Attribute;   // attribute being completed (markup)
        public string Prefix;      // text typed so far for the current token
        public string Property;    // style property (stylesheet)
        public int TokenStart;     // offset of the token start within the line
        public bool InsideStyleBlock;

        public static int Offset(string text, int line, int character)
        {
            int l = 0, i = 0;
            while (i < text.Length && l < line)
            {
                if (text[i] == '\n') l++;
                i++;
            }
            return Math.Min(text.Length, i + character);
        }

        private static bool IsIdent(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ':';
        }

        public static Cursor Markup(string text, int offset)
        {
            var c = new Cursor { Where = Kind.None };
            if (offset > text.Length) offset = text.Length;
            // Inside a <style> block? Then the stylesheet rules apply.
            int styleOpen = text.LastIndexOf("<style", offset, StringComparison.OrdinalIgnoreCase);
            if (styleOpen >= 0)
            {
                int styleClose = text.LastIndexOf("</style", offset, StringComparison.OrdinalIgnoreCase);
                if (styleClose < styleOpen)
                {
                    int contentStart = text.IndexOf('>', styleOpen);
                    if (contentStart >= 0 && contentStart < offset)
                    {
                        Cursor s = Style(text, offset, contentStart + 1);
                        s.InsideStyleBlock = true;
                        return s;
                    }
                }
            }
            int lt = text.LastIndexOf('<', Math.Max(0, offset - 1));
            if (lt < 0) return c;
            int gt = text.LastIndexOf('>', Math.Max(0, offset - 1));
            if (gt > lt) return c; // caret is in content, not in a tag
            // Read the tag name
            int p = lt + 1;
            if (p < text.Length && text[p] == '/') p++;
            int nameStart = p;
            while (p < offset && p < text.Length && IsIdent(text[p])) p++;
            c.Tag = text.Substring(nameStart, p - nameStart);
            if (p >= offset)
            {
                c.Where = Kind.TagName;
                c.Prefix = c.Tag;
                c.TokenStart = nameStart;
                return c;
            }
            // Walk attributes
            while (p < offset)
            {
                while (p < offset && char.IsWhiteSpace(text[p])) p++;
                if (p >= offset) { c.Where = Kind.AttributeName; c.Prefix = ""; c.TokenStart = p; return c; }
                int aStart = p;
                while (p < offset && IsIdent(text[p])) p++;
                string attr = text.Substring(aStart, p - aStart);
                if (p >= offset) { c.Where = Kind.AttributeName; c.Prefix = attr; c.TokenStart = aStart; return c; }
                while (p < offset && char.IsWhiteSpace(text[p])) p++;
                if (p < offset && text[p] == '=')
                {
                    p++;
                    while (p < offset && char.IsWhiteSpace(text[p])) p++;
                    if (p < offset && (text[p] == '"' || text[p] == '\''))
                    {
                        char q = text[p];
                        p++;
                        int vStart = p;
                        while (p < offset && text[p] != q) p++;
                        if (p >= offset)
                        {
                            c.Where = Kind.AttributeValue;
                            c.Attribute = attr;
                            c.Prefix = text.Substring(vStart, offset - vStart);
                            c.TokenStart = vStart;
                            return c;
                        }
                        p++; // closing quote
                        continue;
                    }
                }
                if (p < offset && (text[p] == '/' || text[p] == '>')) return c;
            }
            c.Where = Kind.AttributeName;
            c.Prefix = "";
            c.TokenStart = offset;
            return c;
        }

        public static Cursor Style(string text, int offset, int from = 0)
        {
            var c = new Cursor { Where = Kind.StyleSelector };
            int open = text.LastIndexOf('{', Math.Max(from, offset - 1));
            int close = text.LastIndexOf('}', Math.Max(from, offset - 1));
            if (open < from || close > open)
            {
                // rule level: selector
                int s = offset;
                while (s > from && IsIdent(text[s - 1])) s--;
                c.Prefix = text.Substring(s, offset - s);
                c.TokenStart = s;
                return c;
            }
            // inside a declaration block: find the current declaration
            int declStart = Math.Max(open + 1, Math.Max(text.LastIndexOf(';', Math.Max(0, offset - 1)), text.LastIndexOf('\n', Math.Max(0, offset - 1)) ));
            if (declStart < open + 1) declStart = open + 1;
            string decl = text.Substring(declStart, offset - declStart);
            int colon = decl.IndexOf(':');
            if (colon < 0)
            {
                int s = offset;
                while (s > declStart && IsIdent(text[s - 1])) s--;
                c.Where = Kind.StyleProperty;
                c.Prefix = text.Substring(s, offset - s);
                c.TokenStart = s;
                return c;
            }
            c.Where = Kind.StyleValue;
            c.Property = decl.Substring(0, colon).Trim();
            int vs = offset;
            while (vs > declStart + colon + 1 && (IsIdent(text[vs - 1]) || text[vs - 1] == '(' || text[vs - 1] == '#')) vs--;
            c.Prefix = text.Substring(vs, offset - vs);
            c.TokenStart = vs;
            return c;
        }

        /// <summary>The word (identifier characters plus dots) under an offset, and its start.</summary>
        public static string WordAt(string text, int offset, out int start, out int end)
        {
            start = Math.Min(offset, text.Length);
            end = start;
            while (start > 0 && IsWordChar(text[start - 1])) start--;
            while (end < text.Length && IsWordChar(text[end])) end++;
            return text.Substring(start, end - start);
        }

        private static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.';
        }
    }
}
