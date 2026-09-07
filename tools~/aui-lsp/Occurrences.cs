using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Armagan.AUI.Core;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Armagan.AUI.Lsp
{
    public enum TargetKind
    {
        None,
        Class,     // .card in a sheet, class="card" in a view
        Key,       // key="demo.title", the JSON leaf in every language
        Binding,   // {name} / {item.name}, file-local
        Handler,   // onclick="Name", plus the C# method next to the view
        Id,        // id="x", page="x", for="x", default="x", file-local
        Variable,  // --accent, declaration and every var(--accent)
        View,      // <Name/> and Name.aui
        Icon,      // icon="x" / <icon name="x">
        Href,      // <link href="x.auis">
    }

    /// <summary>What a position names, so hover, definition, references and rename agree.</summary>
    public sealed class Target
    {
        public TargetKind Kind;
        public string Name;
        public bool IsItem;
        /// <summary>The token under the caret in the requesting file.</summary>
        public Range Range;
        /// <summary>For an attribute target: the element's tag and the attribute's name.</summary>
        public string Tag;
        public string Attribute;

        public override string ToString()
        {
            return Kind + " " + Name;
        }
    }

    public sealed class Occurrence
    {
        public string File;
        public Range Range;

        public Location ToLocation()
        {
            return new Location { Uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.FromFileSystemPath(File), Range = Range };
        }
    }

    public static class Occurrences
    {
        private static readonly string[] KeyAttributes = { "key", "placeholder-key", "format-key" };
        private static readonly string[] IdReferenceAttributes = { "page", "for", "default" };

        public static bool IsKeyAttribute(string name)
        {
            return Array.IndexOf(KeyAttributes, name) >= 0;
        }

        // ---------------------------------------------------------------- what is here

        public static Target At(string path, string text, Position pos)
        {
            int offset = Cursor.Offset(text, pos.Line, pos.Character);
            if (path.EndsWith(".auis", StringComparison.OrdinalIgnoreCase)) return StyleTarget(text, offset);
            Cursor c = Cursor.Markup(text, offset);
            if (c.InsideStyleBlock || c.InsideStyleAttribute) return StyleTarget(text, offset);
            if (c.Where == Cursor.Kind.TagName)
            {
                int end = c.TokenStart;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_' || text[end] == '-' || text[end] == '.')) end++;
                string tag = text.Substring(c.TokenStart, end - c.TokenStart);
                if (tag.Length > 0 && char.IsUpper(tag[0]))
                    return new Target { Kind = TargetKind.View, Name = tag, Range = Cursor.RangeOf(text, c.TokenStart, end), Tag = tag };
                return new Target { Kind = TargetKind.None, Name = tag, Range = Cursor.RangeOf(text, c.TokenStart, end), Tag = tag };
            }
            if (c.Where != Cursor.Kind.AttributeValue) return new Target { Kind = TargetKind.None };
            char quote = text[c.ValueStart - 1];
            int close = text.IndexOf(quote, c.ValueStart);
            if (close < 0) close = text.Length;
            string value = text.Substring(c.ValueStart, close - c.ValueStart);
            int idx = offset - c.ValueStart;
            var t = new Target { Tag = c.Tag, Attribute = c.Attribute, Name = value, Range = Cursor.RangeOf(text, c.ValueStart, close) };
            if (IsKeyAttribute(c.Attribute)) { t.Kind = TargetKind.Key; return t; }
            if (c.Attribute == "class")
            {
                int s, e;
                string token = TokenAt(value, idx, out s, out e);
                if (token == null) { t.Kind = TargetKind.None; return t; }
                t.Kind = TargetKind.Class; t.Name = token; t.Range = Cursor.RangeOf(text, c.ValueStart + s, c.ValueStart + e);
                return t;
            }
            if (c.Attribute == "id" || Array.IndexOf(IdReferenceAttributes, c.Attribute) >= 0) { t.Kind = TargetKind.Id; return t; }
            if (c.Attribute == "icon" || (c.Attribute == "name" && c.Tag == "icon")) { t.Kind = TargetKind.Icon; return t; }
            if (c.Attribute == "href") { t.Kind = TargetKind.Href; return t; }
            if (c.Attribute.StartsWith("on")) { t.Kind = TargetKind.Handler; return t; }
            int bs, be;
            string binding = Cursor.BindingAt(value, idx, out bs, out be);
            if (binding != null)
            {
                string inner = binding.Substring(1, binding.Length - 2).Trim();
                bool item = inner.StartsWith("item.");
                string name = item ? inner.Substring(5) : inner;
                int nameStart = binding.IndexOf(name, StringComparison.Ordinal);
                t.Kind = TargetKind.Binding; t.Name = name; t.IsItem = item;
                t.Range = Cursor.RangeOf(text, c.ValueStart + bs + nameStart, c.ValueStart + bs + nameStart + name.Length);
                return t;
            }
            t.Kind = TargetKind.None;
            return t;
        }

        private static Target StyleTarget(string text, int offset)
        {
            int s, e;
            string word = Cursor.WordAt(text, offset, out s, out e);
            var t = new Target { Kind = TargetKind.None, Name = word, Range = Cursor.RangeOf(text, s, e) };
            if (word.Length == 0) return t;
            if (word.StartsWith("--")) { t.Kind = TargetKind.Variable; return t; }
            if (s > 0 && text[s - 1] == '.') { t.Kind = TargetKind.Class; return t; }
            return t;
        }

        /// <summary>The whitespace-separated token of a value that contains index.</summary>
        public static string TokenAt(string value, int index, out int start, out int end)
        {
            start = end = -1;
            if (value == null) return null;
            int i = 0;
            while (i < value.Length)
            {
                while (i < value.Length && char.IsWhiteSpace(value[i])) i++;
                int s = i;
                while (i < value.Length && !char.IsWhiteSpace(value[i])) i++;
                if (i > s && index >= s && index <= i) { start = s; end = i; return value.Substring(s, i - s); }
            }
            return null;
        }

        // ---------------------------------------------------------------- every place a target appears

        public static List<Occurrence> Find(Workspace ws, Target t, string currentPath)
        {
            var list = new List<Occurrence>();
            if (t == null || t.Kind == TargetKind.None || string.IsNullOrEmpty(t.Name)) return list;
            AuiWorkspace scan = ws.Scan();
            switch (t.Kind)
            {
                case TargetKind.Class:
                    foreach (string v in AllViews(ws, scan))
                    {
                        string text = ws.Read(v);
                        if (text == null) continue;
                        AuiMarkupDocument doc = AuiParser.Parse(text, v);
                        foreach (AuiNode n in doc.Nodes)
                        {
                            AuiAttribute a = n.FindAttribute("class");
                            if (a == null) continue;
                            foreach (Range r in TokenRanges(a, t.Name)) list.Add(new Occurrence { File = v, Range = r });
                        }
                        foreach (AuiStyleBlock b in doc.Styles) SelectorOccurrences(b.Text, t.Name, b.Line, b.Column, v, list);
                    }
                    foreach (string s in AllSheets(scan))
                    {
                        string text = ws.Read(s);
                        if (text != null) SelectorOccurrences(text, t.Name, 1, 1, s, list);
                    }
                    break;

                case TargetKind.Variable:
                {
                    var rx = new Regex(Regex.Escape(t.Name) + @"(?![A-Za-z0-9_-])");
                    foreach (string f in AllSheetsAndViews(ws, scan))
                    {
                        string text = ws.Read(f);
                        if (text == null) continue;
                        foreach (Match m in rx.Matches(text)) list.Add(new Occurrence { File = f, Range = Cursor.RangeOf(text, m.Index, m.Index + m.Length) });
                    }
                    break;
                }

                case TargetKind.Key:
                {
                    foreach (string v in AllViews(ws, scan))
                    {
                        string text = ws.Read(v);
                        if (text == null) continue;
                        AuiMarkupDocument doc = AuiParser.Parse(text, v);
                        foreach (AuiNode n in doc.Nodes)
                            foreach (AuiAttribute a in n.Attributes)
                                if (IsKeyAttribute(a.Name) && a.Value == t.Name) list.Add(new Occurrence { File = v, Range = Cursor.RangeAt(a.ValueLine, a.ValueColumn, a.Value.Length) });
                    }
                    AuiLanguageTable table = ws.Lang(scan);
                    string last = t.Name.Substring(t.Name.LastIndexOf('.') + 1);
                    foreach (AuiLanguageFile lf in table.Languages)
                    {
                        AuiJson.Pos pos;
                        if (!lf.Data.Positions.TryGetValue(t.Name, out pos)) continue;
                        string file = pos.File ?? lf.File;
                        string text = ws.Read(file);
                        if (text == null) continue;
                        Range r = KeyTokenRange(text, pos, last);
                        if (r != null) list.Add(new Occurrence { File = file, Range = r });
                    }
                    break;
                }

                case TargetKind.Binding:
                {
                    string text = ws.Read(currentPath);
                    if (text == null) break;
                    AuiMarkupDocument doc = AuiParser.Parse(text, currentPath);
                    string want = (t.IsItem ? "item." : "") + t.Name;
                    foreach (AuiNode n in doc.Nodes)
                        foreach (AuiAttribute a in n.Attributes)
                        {
                            int i = 0;
                            while (i < a.Value.Length)
                            {
                                int o = a.Value.IndexOf('{', i);
                                if (o < 0) break;
                                int cl = a.Value.IndexOf('}', o);
                                if (cl < 0) break;
                                string inner = a.Value.Substring(o + 1, cl - o - 1);
                                if (inner.Trim() == want)
                                {
                                    int nameStart = o + 1 + inner.IndexOf(t.Name, t.IsItem ? inner.IndexOf("item.", StringComparison.Ordinal) + 5 : 0, StringComparison.Ordinal);
                                    list.Add(new Occurrence { File = currentPath, Range = Cursor.RangeAt(a.ValueLine, a.ValueColumn + nameStart, t.Name.Length) });
                                }
                                i = cl + 1;
                            }
                        }
                    break;
                }

                case TargetKind.Handler:
                {
                    string text = ws.Read(currentPath);
                    if (text == null) break;
                    AuiMarkupDocument doc = AuiParser.Parse(text, currentPath);
                    foreach (AuiNode n in doc.Nodes)
                        foreach (AuiAttribute a in n.Attributes)
                            if (a.Name.StartsWith("on") && a.Value == t.Name) list.Add(new Occurrence { File = currentPath, Range = Cursor.RangeAt(a.ValueLine, a.ValueColumn, a.Value.Length) });
                    Occurrence method = CSharp.FindMethod(ws, currentPath, t.Name);
                    if (method != null) list.Add(method);
                    break;
                }

                case TargetKind.Id:
                {
                    string text = ws.Read(currentPath);
                    if (text == null) break;
                    AuiMarkupDocument doc = AuiParser.Parse(text, currentPath);
                    foreach (AuiNode n in doc.Nodes)
                        foreach (AuiAttribute a in n.Attributes)
                            if ((a.Name == "id" || Array.IndexOf(IdReferenceAttributes, a.Name) >= 0) && a.Value == t.Name)
                                list.Add(new Occurrence { File = currentPath, Range = Cursor.RangeAt(a.ValueLine, a.ValueColumn, a.Value.Length) });
                    break;
                }

                case TargetKind.View:
                {
                    var rx = new Regex(@"</?(" + Regex.Escape(t.Name) + @")(?![A-Za-z0-9_.-])");
                    foreach (string v in AllViews(ws, scan))
                    {
                        string text = ws.Read(v);
                        if (text == null) continue;
                        foreach (Match m in rx.Matches(text))
                        {
                            Group g = m.Groups[1];
                            list.Add(new Occurrence { File = v, Range = Cursor.RangeOf(text, g.Index, g.Index + g.Length) });
                        }
                    }
                    string vp;
                    if (scan.Views.TryGetValue(t.Name, out vp)) list.Insert(0, new Occurrence { File = vp, Range = new Range(new Position(0, 0), new Position(0, 0)) });
                    break;
                }
            }
            return list;
        }

        public static IEnumerable<string> AllViews(Workspace ws, AuiWorkspace scan)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in scan.Views.Values) if (seen.Add(Path.GetFullPath(p))) yield return p;
            foreach (string p in ws.OpenPaths()) if (p.EndsWith(".aui", StringComparison.OrdinalIgnoreCase) && seen.Add(p)) yield return p;
        }

        public static IEnumerable<string> AllSheets(AuiWorkspace scan)
        {
            if (scan.ThemePath != null) yield return scan.ThemePath;
            foreach (string p in scan.StylePaths) yield return p;
        }

        private static IEnumerable<string> AllSheetsAndViews(Workspace ws, AuiWorkspace scan)
        {
            foreach (string s in AllSheets(scan)) yield return s;
            foreach (string v in AllViews(ws, scan)) yield return v;
        }

        /// <summary>Ranges of every whitespace-separated token equal to name inside an attribute value.</summary>
        public static IEnumerable<Range> TokenRanges(AuiAttribute a, string name)
        {
            int i = 0;
            string v = a.Value;
            while (i < v.Length)
            {
                while (i < v.Length && char.IsWhiteSpace(v[i])) i++;
                int s = i;
                while (i < v.Length && !char.IsWhiteSpace(v[i])) i++;
                if (i > s && v.Substring(s, i - s) == name) yield return Cursor.RangeAt(a.ValueLine, a.ValueColumn + s, name.Length);
            }
        }

        /// <summary>".name" at rule level (outside braces and comments) in stylesheet text, positioned into the owning file.</summary>
        private static void SelectorOccurrences(string text, string cls, int baseLine, int baseColumn, string file, List<Occurrence> into)
        {
            int line = baseLine, col = baseColumn, depth = 0, i = 0;
            while (i < text.Length)
            {
                char ch = text[i];
                if (ch == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0) end = text.Length; else end += 2;
                    while (i < end) Step(text, ref i, ref line, ref col);
                    continue;
                }
                if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n') Step(text, ref i, ref line, ref col);
                    continue;
                }
                if (ch == '{') depth++;
                else if (ch == '}') depth--;
                else if (depth == 0 && ch == '.' && string.CompareOrdinal(text, i + 1, cls, 0, cls.Length) == 0)
                {
                    int after = i + 1 + cls.Length;
                    bool boundary = after >= text.Length || !(char.IsLetterOrDigit(text[after]) || text[after] == '_' || text[after] == '-');
                    if (boundary)
                    {
                        into.Add(new Occurrence { File = file, Range = new Range(new Position(line - 1, col), new Position(line - 1, col + cls.Length)) });
                    }
                }
                Step(text, ref i, ref line, ref col);
            }
        }

        private static void Step(string text, ref int i, ref int line, ref int col)
        {
            char c = text[i++];
            if (c == '\n') { line++; col = 1; }
            else if (c != '\r') col++;
        }

        /// <summary>The last "seg" before a value's position on its line: the key token of a JSON leaf.</summary>
        public static Range KeyTokenRange(string text, AuiJson.Pos valuePos, string lastSegment)
        {
            int line = valuePos.Line - 1;
            string lt = Cursor.LineText(text, line);
            int limit = Math.Min(lt.Length, Math.Max(0, valuePos.Column - 1));
            int at = lt.LastIndexOf("\"" + lastSegment + "\"", limit, StringComparison.Ordinal);
            if (at < 0) return null;
            return new Range(new Position(line, at + 1), new Position(line, at + 1 + lastSegment.Length));
        }
    }

    /// <summary>The C# next to a view: where a handler lives, and which public methods exist.</summary>
    public static class CSharp
    {
        public static IEnumerable<string> FilesNextTo(string viewPath)
        {
            string dir = Path.GetDirectoryName(viewPath);
            string stem = Path.GetFileNameWithoutExtension(viewPath);
            if (dir == null || !Directory.Exists(dir)) yield break;
            string own = Path.Combine(dir, stem + ".cs");
            if (File.Exists(own)) yield return own;
            foreach (string f in Directory.GetFiles(dir, "*.cs"))
                if (!string.Equals(f, own, StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)) yield return f;
        }

        public static Occurrence FindMethod(Workspace ws, string viewPath, string name)
        {
            var rx = new Regex(@"\bvoid\s+(" + Regex.Escape(name) + @")\s*\(");
            foreach (string f in FilesNextTo(viewPath))
            {
                string text = ws.Read(f);
                if (text == null) continue;
                Match m = rx.Match(text);
                if (!m.Success) continue;
                Group g = m.Groups[1];
                return new Occurrence { File = f, Range = Cursor.RangeOf(text, g.Index, g.Index + g.Length) };
            }
            return null;
        }

        private static readonly Regex PublicVoid = new Regex(@"public\s+void\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(([^)]*)\)");

        /// <summary>Public void methods of the view's own partial class, for handler completion: name -> parameter list.</summary>
        public static List<KeyValuePair<string, string>> PublicMethods(Workspace ws, string viewPath)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (string f in FilesNextTo(viewPath))
            {
                string text = ws.Read(f);
                if (text == null) continue;
                foreach (Match m in PublicVoid.Matches(text)) list.Add(new KeyValuePair<string, string>(m.Groups[1].Value, m.Groups[2].Value.Trim()));
            }
            return list;
        }

        /// <summary>The file and offset just before the closing brace of the view's partial class, or null.</summary>
        public static bool ClassEnd(Workspace ws, string viewPath, out string file, out string text, out int insertAt, out string indent)
        {
            file = null; text = null; insertAt = -1; indent = "    ";
            string stem = Path.GetFileNameWithoutExtension(viewPath);
            var decl = new Regex(@"partial\s+class\s+" + Regex.Escape(stem) + @"\b[^{]*\{");
            foreach (string f in FilesNextTo(viewPath))
            {
                string t = ws.Read(f);
                if (t == null) continue;
                Match m = decl.Match(t);
                if (!m.Success) continue;
                int open = m.Index + m.Length - 1;
                int depth = 0;
                for (int i = open; i < t.Length; i++)
                {
                    char c = t[i];
                    if (c == '"') { i = SkipString(t, i); continue; }
                    if (c == '/' && i + 1 < t.Length && t[i + 1] == '/') { while (i < t.Length && t[i] != '\n') i++; continue; }
                    if (c == '/' && i + 1 < t.Length && t[i + 1] == '*') { int e = t.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? t.Length : e + 1; continue; }
                    if (c == '{') depth++;
                    else if (c == '}' && --depth == 0)
                    {
                        file = f; text = t; insertAt = i;
                        // the class body's indentation: that of the closing brace plus one level
                        int ls = i; while (ls > 0 && t[ls - 1] != '\n') ls--;
                        string braceIndent = t.Substring(ls, i - ls);
                        indent = braceIndent + "    ";
                        insertAt = ls;
                        return true;
                    }
                }
            }
            return false;
        }

        private static int SkipString(string t, int i)
        {
            bool verbatim = i > 0 && t[i - 1] == '@';
            for (int j = i + 1; j < t.Length; j++)
            {
                if (!verbatim && t[j] == '\\') { j++; continue; }
                if (t[j] == '"') { if (verbatim && j + 1 < t.Length && t[j + 1] == '"') { j++; continue; } return j; }
            }
            return t.Length;
        }
    }
}
