using System;
using System.Collections.Generic;
using System.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Armagan.AUI.Lsp
{
    /// <summary>
    /// Edits to a language file the quick fixes need: where a new key goes, and which key a
    /// caret is on. Works on the raw text so comments and formatting survive; the objects are
    /// found by the same nesting rule AuiJson flattens with.
    /// </summary>
    public static class LangJson
    {
        private sealed class Obj
        {
            public string Key;
            public int Open;
            public List<string> Path;
        }

        /// <summary>
        /// A TextEdit that adds "key": value at the end of the deepest existing object on the
        /// key's path, creating the missing nested objects. Null when the key's path collides
        /// with a string leaf or the file has no object.
        /// </summary>
        public static TextEdit Insert(string text, string key, string value, out Position caret)
        {
            caret = null;
            string[] segs = key.Split('.');
            int bestMatch = -1, bestOpen = -1, bestClose = -1;
            var stack = new List<Obj>();
            string currentKey = null;
            bool expectKey = false;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/') { while (i < text.Length && text[i] != '\n') i++; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { int e = text.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? text.Length : e + 2; continue; }
                if (c == '"')
                {
                    int close = StringEnd(text, i);
                    string s = Unescape(text.Substring(i + 1, close - i - 1));
                    if (expectKey) { currentKey = s; expectKey = false; }
                    else if (currentKey != null)
                    {
                        // a string leaf: a collision when it sits on the new key's path
                        var path = PathOf(stack, currentKey);
                        if (IsPrefix(path, segs) && path.Count <= segs.Length) return null;
                        currentKey = null;
                    }
                    i = close + 1;
                    continue;
                }
                if (c == '{')
                {
                    stack.Add(new Obj { Key = currentKey, Open = i, Path = PathOf(stack, currentKey) });
                    currentKey = null;
                    expectKey = true;
                    i++;
                    continue;
                }
                if (c == '}')
                {
                    if (stack.Count == 0) return null;
                    Obj o = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    if (IsPrefix(o.Path, segs) && o.Path.Count < segs.Length && o.Path.Count > bestMatch) { bestMatch = o.Path.Count; bestOpen = o.Open; bestClose = i; }
                    currentKey = null;
                    expectKey = false;
                    i++;
                    continue;
                }
                if (c == ':') { expectKey = false; i++; continue; }
                if (c == ',') { expectKey = true; currentKey = null; i++; continue; }
                // a bare value (number, true, false, null)
                if (currentKey != null)
                {
                    var path = PathOf(stack, currentKey);
                    if (IsPrefix(path, segs) && path.Count <= segs.Length) return null;
                    currentKey = null;
                }
                i++;
            }
            if (bestOpen < 0) return null;

            // ---- indentation: from the object's first child, else one unit deeper than its line
            string unit = "  ";
            string parentIndent = LineIndent(text, bestOpen);
            int first = bestOpen + 1;
            while (first < text.Length && char.IsWhiteSpace(text[first])) first++;
            bool empty = first >= text.Length || text[first] == '}';
            string childIndent;
            if (!empty)
            {
                childIndent = LineIndent(text, first);
                if (childIndent.Length <= parentIndent.Length) childIndent = parentIndent + unit;
                else unit = childIndent.Substring(parentIndent.Length);
            }
            else childIndent = parentIndent + unit;

            // ---- the lines to add
            var sb = new StringBuilder();
            int remaining = segs.Length - bestMatch;
            for (int j = 0; j < remaining; j++)
            {
                string seg = segs[bestMatch + j];
                sb.Append(childIndent);
                for (int k = 0; k < j; k++) sb.Append(unit);
                sb.Append('"').Append(Escape(seg)).Append("\": ");
                if (j == remaining - 1) sb.Append('"').Append(Escape(value)).Append('"');
                else sb.Append("{\n");
            }
            int caretInBlock = sb.Length - 1 - Escape(value).Length; // just after the opening quote of the value
            for (int j = remaining - 2; j >= 0; j--)
            {
                sb.Append('\n').Append(childIndent);
                for (int k = 0; k < j; k++) sb.Append(unit);
                sb.Append('}');
            }
            string block = sb.ToString();

            int start, end;
            string newText;
            int caretOffset;
            if (empty)
            {
                start = bestOpen + 1;
                end = bestClose;
                newText = "\n" + block + "\n" + parentIndent;
                caretOffset = start + 1 + caretInBlock;
            }
            else
            {
                int last = bestClose - 1;
                while (last > bestOpen && char.IsWhiteSpace(text[last])) last--;
                bool hasComma = text[last] == ',';
                start = end = last + 1;
                newText = (hasComma ? "" : ",") + "\n" + block;
                caretOffset = start + (hasComma ? 0 : 1) + 1 + caretInBlock;
            }
            string after = text.Substring(0, start) + newText + text.Substring(end);
            caret = Cursor.PositionOf(after, caretOffset);
            return new TextEdit { Range = Cursor.RangeOf(text, start, end), NewText = newText };
        }

        /// <summary>The flattened key whose name or value the offset is on, or null.</summary>
        public static string KeyAt(string text, int offset)
        {
            var stack = new List<Obj>();
            string currentKey = null;
            bool expectKey = false;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/') { while (i < text.Length && text[i] != '\n') i++; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { int e = text.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? text.Length : e + 2; continue; }
                if (c == '"')
                {
                    int end = StringEnd(text, i);
                    string s = Unescape(text.Substring(i + 1, end - i - 1));
                    if (expectKey) { currentKey = s; expectKey = false; if (offset >= i && offset <= end + 1) return Join(PathOf(stack, currentKey)); }
                    else if (currentKey != null)
                    {
                        if (offset >= i && offset <= end + 1) return Join(PathOf(stack, currentKey));
                        currentKey = null;
                    }
                    if (i > offset) return null;
                    i = end + 1;
                    continue;
                }
                if (c == '{') { stack.Add(new Obj { Key = currentKey, Open = i, Path = PathOf(stack, currentKey) }); currentKey = null; expectKey = true; i++; continue; }
                if (c == '}') { if (stack.Count == 0) return null; stack.RemoveAt(stack.Count - 1); currentKey = null; expectKey = false; i++; continue; }
                if (c == ':') { expectKey = false; i++; continue; }
                if (c == ',') { expectKey = true; currentKey = null; i++; continue; }
                if (i > offset) return null;
                i++;
            }
            return null;
        }

        private static List<string> PathOf(List<Obj> stack, string key)
        {
            var path = new List<string>();
            foreach (Obj o in stack) if (o.Key != null) path.Add(o.Key);
            if (key != null) path.Add(key);
            return path;
        }

        private static bool IsPrefix(List<string> path, string[] segs)
        {
            if (path.Count > segs.Length) return false;
            for (int i = 0; i < path.Count; i++) if (path[i] != segs[i]) return false;
            return true;
        }

        private static string Join(List<string> path)
        {
            return path.Count == 0 ? null : string.Join(".", path);
        }

        private static int StringEnd(string text, int openQuote)
        {
            for (int j = openQuote + 1; j < text.Length; j++)
            {
                if (text[j] == '\\') { j++; continue; }
                if (text[j] == '"') return j;
                if (text[j] == '\n') return j;
            }
            return text.Length;
        }

        private static string LineIndent(string text, int offset)
        {
            int ls = offset;
            while (ls > 0 && text[ls - 1] != '\n') ls--;
            int i = ls;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            return text.Substring(ls, i - ls);
        }

        public static string Escape(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char e = s[++i];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'u':
                        if (i + 4 < s.Length) { int code; if (int.TryParse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out code)) { sb.Append((char)code); i += 4; } }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }
    }
}
