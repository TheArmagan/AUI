using System;
using System.Collections.Generic;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

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
        public int TokenStart;     // offset of the token start
        public int ValueStart;     // offset of the first character inside the quotes (attribute values)
        public bool InsideStyleBlock;
        public bool InsideStyleAttribute;
        /// <summary>Attributes already written on the tag before the caret.</summary>
        public readonly List<string> UsedAttributes = new List<string>();

        // ---------------------------------------------------------------- positions

        public static int Offset(string text, int line, int character)
        {
            int l = 0, i = 0;
            while (i < text.Length && l < line)
            {
                if (text[i] == '\n') l++;
                i++;
            }
            int lineEnd = i;
            while (lineEnd < text.Length && text[lineEnd] != '\n' && text[lineEnd] != '\r') lineEnd++;
            return Math.Min(lineEnd, i + character);
        }

        public static Position PositionOf(string text, int offset)
        {
            int line = 0, col = 0;
            for (int i = 0; i < offset && i < text.Length; i++)
            {
                if (text[i] == '\n') { line++; col = 0; }
                else if (text[i] != '\r') col++;
            }
            return new Position(line, col);
        }

        public static Range RangeOf(string text, int start, int end)
        {
            return new Range(PositionOf(text, start), PositionOf(text, end));
        }

        /// <summary>A range from 1-based parser coordinates and a length.</summary>
        public static Range RangeAt(int line, int column, int length)
        {
            int l = Math.Max(0, line - 1), c = Math.Max(0, column - 1);
            return new Range(new Position(l, c), new Position(l, c + Math.Max(0, length)));
        }

        public static bool Contains(Range r, Position p)
        {
            if (p.Line < r.Start.Line || p.Line > r.End.Line) return false;
            if (p.Line == r.Start.Line && p.Character < r.Start.Character) return false;
            if (p.Line == r.End.Line && p.Character > r.End.Character) return false;
            return true;
        }

        public static int LineLength(string text, int line)
        {
            int start = Offset(text, line, 0);
            int end = start;
            while (end < text.Length && text[end] != '\n' && text[end] != '\r') end++;
            return end - start;
        }

        public static int FirstNonSpace(string text, int line)
        {
            int start = Offset(text, line, 0);
            int i = start;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            return i - start;
        }

        public static string LineText(string text, int line)
        {
            int start = Offset(text, line, 0);
            return text.Substring(start, LineLength(text, line));
        }

        /// <summary>Next non-blank character at or after the offset on the same line, or '\0'.</summary>
        public static char AfterCaret(string text, int offset)
        {
            for (int i = offset; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ' ' || c == '\t') continue;
                if (c == '\n' || c == '\r') return '\0';
                return c;
            }
            return '\0';
        }

        // ---------------------------------------------------------------- markup

        private static bool IsIdent(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ':';
        }

        public static Cursor Markup(string text, int offset)
        {
            var c = new Cursor { Where = Kind.None };
            if (offset > text.Length) offset = text.Length;
            // Inside a comment? Nothing to complete.
            int commentOpen = text.LastIndexOf("<!--", offset, StringComparison.Ordinal);
            if (commentOpen >= 0 && text.LastIndexOf("-->", offset, StringComparison.Ordinal) < commentOpen) return c;
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
            int gt = LastCloseOutsideQuotes(text, lt, offset);
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
                            if (attr == "style")
                            {
                                Cursor s = Style(text, offset, vStart, true);
                                s.InsideStyleAttribute = true;
                                s.Tag = c.Tag;
                                s.ValueStart = vStart;
                                return s;
                            }
                            c.Where = Kind.AttributeValue;
                            c.Attribute = attr;
                            c.Prefix = text.Substring(vStart, offset - vStart);
                            c.TokenStart = vStart;
                            c.ValueStart = vStart;
                            return c;
                        }
                        p++; // closing quote
                        c.UsedAttributes.Add(attr);
                        continue;
                    }
                }
                c.UsedAttributes.Add(attr);
                if (p < offset && (text[p] == '/' || text[p] == '>')) return c;
            }
            c.Where = Kind.AttributeName;
            c.Prefix = "";
            c.TokenStart = offset;
            return c;
        }

        /// <summary>The last '>' between from and offset that is not inside an attribute value.</summary>
        private static int LastCloseOutsideQuotes(string text, int from, int offset)
        {
            int last = -1;
            char quote = '\0';
            for (int i = from; i < offset && i < text.Length; i++)
            {
                char ch = text[i];
                if (quote != '\0') { if (ch == quote) quote = '\0'; continue; }
                if (ch == '"' || ch == '\'') { quote = ch; continue; }
                if (ch == '>') last = i;
            }
            return last;
        }

        // ---------------------------------------------------------------- stylesheet

        /// <param name="inBlock">True for a style="" attribute: declarations only, no selector level.</param>
        public static Cursor Style(string text, int offset, int from = 0, bool inBlock = false)
        {
            var c = new Cursor { Where = Kind.StyleSelector };
            int open = inBlock ? from - 1 : text.LastIndexOf('{', Math.Max(from, offset - 1));
            int close = inBlock ? -1 : text.LastIndexOf('}', Math.Max(from, offset - 1));
            if (!inBlock && (open < from || close > open))
            {
                // rule level: selector
                int s = offset;
                while (s > from && IsIdent(text[s - 1])) s--;
                c.Prefix = text.Substring(s, offset - s);
                c.TokenStart = s;
                return c;
            }
            // inside a declaration block: find the current declaration
            int declStart = Math.Max(open + 1, Math.Max(text.LastIndexOf(';', Math.Max(0, offset - 1)), text.LastIndexOf('\n', Math.Max(0, offset - 1))));
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

        // ---------------------------------------------------------------- words

        /// <summary>The word (identifier characters plus dots and dashes) under an offset, and its start.</summary>
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

        /// <summary>The {binding} token around an offset inside an attribute value, or null.</summary>
        public static string BindingAt(string value, int index, out int start, out int end)
        {
            start = end = -1;
            if (value == null) return null;
            int open = value.LastIndexOf('{', Math.Min(index, value.Length - 1));
            if (open < 0) return null;
            int close = value.IndexOf('}', open);
            if (close < 0 || close < index) return null;
            start = open;
            end = close + 1;
            return value.Substring(open, close - open + 1);
        }
    }
}
