using System.Collections.Generic;
using System.Text;

namespace Armagan.AUI.Core
{
    /// <summary>
    /// The one formatter for .aui markup and .auis stylesheets (the VS Code "Format Document"
    /// goes through the language server to here; it is pure C# so tests can pin the shape).
    ///
    /// The house style prefers NEWLINES over inline packing:
    ///   - every child element on its own line, the closing tag on its own line; an element
    ///     whose only content is text stays on one line (`&lt;text&gt;Debug build&lt;/text&gt;`);
    ///   - attributes stay on one line while the tag fits in <see cref="MaxWidth"/> columns,
    ///     otherwise one per line at one extra indent, the closer on its own line; nothing is
    ///     ever aligned with padding spaces;
    ///   - a stylesheet rule is always expanded: the selectors on one line, one declaration per
    ///     line, the brace on its own line; `:root` too; one blank line between rules, whether
    ///     the source had it or not, and none inside a block. A `&lt;style&gt;` block is formatted
    ///     the same way at its own indent;
    ///   - comments are kept verbatim and re-indented; one blank line between things is kept,
    ///     more than one collapses to one.
    ///
    /// It is a TOKEN formatter, not an AST printer: the parser drops comments and the order of
    /// a &lt;style&gt; block against its siblings, and a formatter that loses either is worse than
    /// none. A document with syntax errors is returned untouched (null): re-indenting a file
    /// whose tags do not balance would only move the mistake.
    /// </summary>
    public static class AuiFormatter
    {
        public const int MaxWidth = 100;
        private const string Indent = "  ";

        // ================================================================ markup

        /// <summary>Formatted markup, or null when the source has syntax errors (0xx) and must be left alone.</summary>
        public static string FormatMarkup(string text)
        {
            if (text == null) return null;
            AuiMarkupDocument doc = AuiParser.Parse(text, "format.aui");
            foreach (AuiDiagnostic d in doc.Diagnostics.Items)
                if (d.Severity == AuiSeverity.Error && d.Code.StartsWith("AUI0")) return null;
            var tokens = new List<Token>();
            if (!Tokenize(text.Replace("\r\n", "\n"), tokens)) return null;
            return Emit(tokens);
        }

        private enum Kind { Open, Close, SelfClose, Text, Comment, Prolog, Style }

        private sealed class Token
        {
            public Kind Kind;
            public string Tag;
            public List<KeyValuePair<string, string>> Attrs;   // value null = flag
            public string Text;          // text run, comment body (with delimiters), prolog, style content
            public string StyleOpen;     // the <style ...> open tag as written, normalised
            public int BlankBefore;      // blank lines between the previous token and this one
            public int SourceIndent;     // column of the token's first character (comments: to shift continuation lines)
            public bool OnlyTextChild;   // Open: the element holds text and nothing else
        }

        private static bool Tokenize(string s, List<Token> tokens)
        {
            int i = 0, n = s.Length;
            int lineStart = 0;
            while (i < n)
            {
                // whitespace between tokens: count blank lines
                int ws = i;
                int newlines = 0;
                while (i < n && char.IsWhiteSpace(s[i])) { if (s[i] == '\n') { newlines++; lineStart = i + 1; } i++; }
                if (i >= n) break;
                int blank = tokens.Count == 0 ? 0 : System.Math.Max(0, newlines - 1);
                int col = i - lineStart;

                if (s[i] == '<')
                {
                    if (Starts(s, i, "<!--"))
                    {
                        int end = s.IndexOf("-->", i + 4, System.StringComparison.Ordinal);
                        if (end < 0) return false;
                        tokens.Add(new Token { Kind = Kind.Comment, Text = s.Substring(i, end + 3 - i), BlankBefore = blank, SourceIndent = col });
                        i = end + 3;
                        continue;
                    }
                    if (Starts(s, i, "<?"))
                    {
                        int end = s.IndexOf("?>", i + 2, System.StringComparison.Ordinal);
                        if (end < 0) return false;
                        tokens.Add(new Token { Kind = Kind.Prolog, Text = s.Substring(i, end + 2 - i), BlankBefore = blank });
                        i = end + 2;
                        continue;
                    }
                    if (Starts(s, i, "</"))
                    {
                        int end = s.IndexOf('>', i);
                        if (end < 0) return false;
                        tokens.Add(new Token { Kind = Kind.Close, Tag = s.Substring(i + 2, end - i - 2).Trim(), BlankBefore = blank });
                        i = end + 1;
                        continue;
                    }
                    // an open or self-closing tag; attribute values may hold '>'
                    int j = i + 1;
                    int nameStart = j;
                    while (j < n && !char.IsWhiteSpace(s[j]) && s[j] != '>' && s[j] != '/') j++;
                    string tag = s.Substring(nameStart, j - nameStart);
                    var attrs = new List<KeyValuePair<string, string>>();
                    bool selfClose = false;
                    while (true)
                    {
                        while (j < n && char.IsWhiteSpace(s[j])) j++;
                        if (j >= n) return false;
                        if (s[j] == '>') { j++; break; }
                        if (s[j] == '/') { if (j + 1 < n && s[j + 1] == '>') { selfClose = true; j += 2; break; } return false; }
                        int an = j;
                        while (j < n && !char.IsWhiteSpace(s[j]) && s[j] != '=' && s[j] != '>' && s[j] != '/') j++;
                        string name = s.Substring(an, j - an);
                        if (name.Length == 0) return false;
                        while (j < n && char.IsWhiteSpace(s[j])) j++;
                        if (j < n && s[j] == '=')
                        {
                            j++;
                            while (j < n && char.IsWhiteSpace(s[j])) j++;
                            if (j >= n) return false;
                            char q = s[j];
                            if (q != '"' && q != '\'') return false;
                            int vEnd = s.IndexOf(q, j + 1);
                            if (vEnd < 0) return false;
                            attrs.Add(new KeyValuePair<string, string>(name, s.Substring(j + 1, vEnd - j - 1)));
                            j = vEnd + 1;
                        }
                        else attrs.Add(new KeyValuePair<string, string>(name, null));
                    }
                    if (tag == "style" && !selfClose)
                    {
                        int end = s.IndexOf("</style>", j, System.StringComparison.Ordinal);
                        if (end < 0) return false;
                        tokens.Add(new Token { Kind = Kind.Style, Tag = tag, Attrs = attrs, Text = s.Substring(j, end - j), BlankBefore = blank });
                        i = end + "</style>".Length;
                        continue;
                    }
                    tokens.Add(new Token { Kind = selfClose ? Kind.SelfClose : Kind.Open, Tag = tag, Attrs = attrs, BlankBefore = blank });
                    i = j;
                    continue;
                }

                // a text run up to the next '<'
                int t = i;
                while (i < n && s[i] != '<') i++;
                string run = Collapse(s.Substring(t, i - t));
                if (run.Length > 0) tokens.Add(new Token { Kind = Kind.Text, Text = run, BlankBefore = blank });
            }

            // Mark opens whose only content is a single text token.
            for (int k = 0; k < tokens.Count; k++)
            {
                if (tokens[k].Kind != Kind.Open) continue;
                if (k + 2 < tokens.Count && tokens[k + 1].Kind == Kind.Text && tokens[k + 2].Kind == Kind.Close) tokens[k].OnlyTextChild = true;
            }
            return true;
        }

        private static bool Starts(string s, int i, string what)
        {
            return string.CompareOrdinal(s, i, what, 0, what.Length) == 0;
        }

        private static string Collapse(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool space = false;
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c)) { space = true; continue; }
                if (space && sb.Length > 0) sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Emit(List<Token> tokens)
        {
            var sb = new StringBuilder();
            int depth = 0;
            for (int k = 0; k < tokens.Count; k++)
            {
                Token t = tokens[k];
                // the text of a text-only element is emitted with its open tag; its close tag too
                if (t.Kind == Kind.Text && k > 0 && tokens[k - 1].Kind == Kind.Open && tokens[k - 1].OnlyTextChild) continue;
                if (t.Kind == Kind.Close && k > 1 && tokens[k - 2].Kind == Kind.Open && tokens[k - 2].OnlyTextChild) continue;
                if (t.Kind == Kind.Close) depth = System.Math.Max(0, depth - 1);

                for (int b = 0; b < System.Math.Min(1, t.BlankBefore); b++) sb.Append('\n');
                string ind = Repeat(Indent, depth);
                switch (t.Kind)
                {
                    case Kind.Prolog:
                        sb.Append(ind).Append(t.Text).Append('\n');
                        break;
                    case Kind.Comment:
                        AppendShifted(sb, t.Text, ind, t.SourceIndent);
                        sb.Append('\n');
                        break;
                    case Kind.Text:
                        sb.Append(ind).Append(t.Text).Append('\n');
                        break;
                    case Kind.Close:
                        sb.Append(ind).Append("</").Append(t.Tag).Append(">\n");
                        break;
                    case Kind.SelfClose:
                        sb.Append(ind).Append(TagText(t, ind, true)).Append('\n');
                        break;
                    case Kind.Open:
                        sb.Append(ind).Append(TagText(t, ind, false));
                        if (t.OnlyTextChild) sb.Append(tokens[k + 1].Text).Append("</").Append(t.Tag).Append('>');
                        else depth++;
                        sb.Append('\n');
                        break;
                    case Kind.Style:
                        sb.Append(ind).Append(TagText(t, ind, false)).Append('\n');
                        string body = FormatSheetCore(t.Text, depth + 1);
                        if (body != null && body.Length > 0) sb.Append(body).Append('\n');
                        else if (body == null) sb.Append(t.Text.Trim('\n')).Append('\n');   // a sheet with errors is left as it was
                        sb.Append(ind).Append("</style>\n");
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Attributes on one line while the tag fits MaxWidth; otherwise one per line, indented
        /// one level (never aligned under the first: alignment padding is what a diff churns on),
        /// with the closer on its own line.
        /// </summary>
        private static string TagText(Token t, string indent, bool selfClose)
        {
            var one = new StringBuilder();
            one.Append('<').Append(t.Tag);
            foreach (KeyValuePair<string, string> a in t.Attrs) one.Append(' ').Append(AttrText(a));
            one.Append(selfClose ? "/>" : ">");
            if (indent.Length + one.Length <= MaxWidth || t.Attrs.Count <= 1) return one.ToString();

            var sb = new StringBuilder();
            sb.Append('<').Append(t.Tag);
            foreach (KeyValuePair<string, string> a in t.Attrs) sb.Append('\n').Append(indent).Append(Indent).Append(AttrText(a));
            sb.Append('\n').Append(indent).Append(selfClose ? "/>" : ">");
            return sb.ToString();
        }

        private static string AttrText(KeyValuePair<string, string> a)
        {
            if (a.Value == null) return a.Key;
            // Double quotes unless the value itself holds one.
            return a.Value.IndexOf('"') >= 0 && a.Value.IndexOf('\'') < 0 ? a.Key + "='" + a.Value + "'" : a.Key + "=\"" + a.Value + "\"";
        }

        /// <summary>A multi-line comment keeps its shape: the first line goes at the new indent and every other line shifts by the same amount.</summary>
        private static void AppendShifted(StringBuilder sb, string text, string indent, int sourceIndent)
        {
            string[] lines = text.Split('\n');
            sb.Append(indent).Append(lines[0].TrimEnd());
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();
                int lead = 0;
                while (lead < line.Length && line[lead] == ' ') lead++;
                int shifted = System.Math.Max(0, lead - sourceIndent + indent.Length);
                sb.Append('\n');
                if (line.Trim().Length > 0) sb.Append(Repeat(" ", shifted)).Append(line.Substring(lead));
            }
        }

        private static string Repeat(string s, int n)
        {
            var sb = new StringBuilder(s.Length * System.Math.Max(0, n));
            for (int i = 0; i < n; i++) sb.Append(s);
            return sb.ToString();
        }

        // ================================================================ stylesheet

        /// <summary>Formatted sheet, or null when it has syntax errors and must be left alone.</summary>
        public static string FormatSheet(string text)
        {
            if (text == null) return null;
            string r = FormatSheetCore(text.Replace("\r\n", "\n"), 0);
            return r == null ? null : r + "\n";
        }

        private enum SKind { Comment, Selector, Decl, Close }

        private sealed class SToken
        {
            public SKind Kind;
            public string Text;        // comment (with delimiters), selector list, or declaration text without ';'
            public int BlankBefore;
            public bool Trailing;      // comment on the same line as the previous token
            public int SourceIndent;   // comment: its column, to shift continuation lines with it
        }

        /// <summary>Base-indented sheet text with no trailing newline; null on a syntax error.</summary>
        private static string FormatSheetCore(string s, int baseDepth)
        {
            AuiStyleSheet sheet = AuiStyleParser.Parse(s, "format.auis", AuiStyleTier.Linked);
            foreach (AuiDiagnostic d in sheet.Diagnostics.Items)
                if (d.Severity == AuiSeverity.Error && d.Code == AuiCodes.StyleSyntax) return null;

            var tokens = new List<SToken>();
            int i = 0, n = s.Length, newlines = 0, lineStart = 0;
            bool first = true;
            while (i < n)
            {
                while (i < n && char.IsWhiteSpace(s[i])) { if (s[i] == '\n') { newlines++; lineStart = i + 1; } i++; }
                if (i >= n) break;
                int blank = first ? 0 : System.Math.Max(0, newlines - 1);
                bool trailing = !first && newlines == 0;
                int col = i - lineStart;
                newlines = 0;
                first = false;
                if (Starts(s, i, "/*"))
                {
                    int end = s.IndexOf("*/", i + 2, System.StringComparison.Ordinal);
                    if (end < 0) return null;
                    string body = s.Substring(i, end + 2 - i);
                    tokens.Add(new SToken { Kind = SKind.Comment, Text = body, BlankBefore = blank, Trailing = trailing, SourceIndent = col });
                    int lastNl = body.LastIndexOf('\n');
                    if (lastNl >= 0) lineStart = i + lastNl + 1;
                    i = end + 2;
                    continue;
                }
                if (Starts(s, i, "//"))
                {
                    int end = s.IndexOf('\n', i);
                    if (end < 0) end = n;
                    tokens.Add(new SToken { Kind = SKind.Comment, Text = s.Substring(i, end - i).TrimEnd(), BlankBefore = blank, Trailing = trailing, SourceIndent = col });
                    i = end;
                    continue;
                }
                if (s[i] == '}') { tokens.Add(new SToken { Kind = SKind.Close, BlankBefore = blank }); i++; continue; }
                if (s[i] == ';') { i++; continue; }

                // a selector list up to '{', or a declaration up to ';' / '}' / newline-before-name (parser tolerance)
                int start = i;
                int paren = 0, brace = 0;
                bool isDecl = false;
                while (i < n)
                {
                    char c = s[i];
                    if (c == '(') paren++;
                    else if (c == ')' && paren > 0) paren--;
                    else if (c == '{' && (paren > 0 || brace > 0 || isDecl)) brace++;
                    else if (c == '}' && brace > 0) brace--;
                    else if (c == '{') break;                                  // selector done
                    else if (c == '}' && paren == 0) break;                    // block done (declaration without ';')
                    else if (c == ';' && paren == 0 && brace == 0) break;
                    else if (c == ':' && !isDecl && paren == 0 && LooksLikeDeclaration(s, start, i)) isDecl = true;
                    else if (c == '\n' && isDecl && paren == 0 && brace == 0 && NextLineIsDeclaration(s, i)) break;
                    else if (c == '/' && i + 1 < n && (s[i + 1] == '*' || s[i + 1] == '/') && paren == 0) break;
                    if (c == '\n') lineStart = i + 1;
                    i++;
                }
                string raw = s.Substring(start, i - start).Trim();
                if (i < n && s[i] == '{' && !isDecl)
                {
                    tokens.Add(new SToken { Kind = SKind.Selector, Text = NormalizeSelectors(raw), BlankBefore = blank });
                    i++;
                    continue;
                }
                if (raw.Length > 0) tokens.Add(new SToken { Kind = SKind.Decl, Text = NormalizeDeclaration(raw), BlankBefore = blank });
                if (i < n && s[i] == ';') i++;
            }

            var sb = new StringBuilder();
            int depth = baseDepth;
            bool atLineStart = true;
            SKind prev = SKind.Close;
            foreach (SToken t in tokens)
            {
                if (t.Kind == SKind.Close) depth = System.Math.Max(baseDepth, depth - 1);
                if (t.Kind == SKind.Comment && t.Trailing && !atLineStart)
                {
                    sb.Append("   ").Append(t.Text);
                    continue;
                }
                if (!atLineStart) sb.Append('\n');
                // ONE BLANK LINE BETWEEN RULES, always: after a '}' the next rule or comment gets
                // one whether the source had it or not. A comment directly above a rule stays
                // attached to it (no blank unless the source had one). Inside a block, none: the
                // braces frame the declarations.
                bool topLevel = depth == baseDepth && t.Kind != SKind.Close;
                bool blank = !atLineStart && topLevel && (prev == SKind.Close || t.BlankBefore > 0);
                if (blank) sb.Append('\n');
                string ind = Repeat(Indent, depth);
                switch (t.Kind)
                {
                    case SKind.Comment: AppendShifted(sb, t.Text, ind, t.SourceIndent); break;
                    case SKind.Selector: sb.Append(ind).Append(t.Text).Append(" {"); depth++; break;
                    case SKind.Decl: sb.Append(ind).Append(t.Text).Append(';'); break;
                    case SKind.Close: sb.Append(ind).Append('}'); break;
                }
                atLineStart = false;
                prev = t.Kind;
            }
            return sb.ToString();
        }

        /// <summary>"name:" at the start of a segment marks a declaration; a selector's ':' follows a tag or class (`button:hover`).</summary>
        private static bool LooksLikeDeclaration(string s, int start, int colon)
        {
            string head = s.Substring(start, colon - start).Trim();
            if (head.Length == 0) return false;
            if (head == ":root" || head.StartsWith(":")) return false;
            if (head.IndexOf('.') >= 0 || head.IndexOf(',') >= 0 || head.IndexOf('*') >= 0 || head.IndexOf(' ') >= 0) return false;
            // a bare word could be a tag before ':hover'; a declaration's ':' is followed by a space or a value that is not a state
            int j = colon + 1;
            while (j < s.Length && s[j] == ' ') j++;
            if (j == colon + 1 && j < s.Length && char.IsLetter(s[j]))
            {
                int k = j;
                while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] == '-')) k++;
                string word = s.Substring(j, k - j);
                if (AuiSchema.IsState(word) || word == "root") return false;
            }
            return AuiSchema.FindProp(head) != null || head.StartsWith("--");
        }

        private static bool NextLineIsDeclaration(string s, int nl)
        {
            int j = nl + 1;
            while (j < s.Length && (s[j] == ' ' || s[j] == '\t')) j++;
            int k = j;
            while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] == '-' || s[k] == '_')) k++;
            int m = k;
            while (m < s.Length && (s[m] == ' ' || s[m] == '\t')) m++;
            return k > j && m < s.Length && s[m] == ':';
        }

        private static string NormalizeSelectors(string raw)
        {
            string[] parts = raw.Split(',');
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = Collapse(parts[i]);
                if (p.Length == 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(p);
            }
            return sb.ToString();
        }

        private static string NormalizeDeclaration(string raw)
        {
            int colon = raw.IndexOf(':');
            if (colon < 0) return Collapse(raw);
            string name = raw.Substring(0, colon).Trim();
            string value = Collapse(raw.Substring(colon + 1));
            return name + ": " + value;
        }
    }
}
