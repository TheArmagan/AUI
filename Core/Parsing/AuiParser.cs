using System.Collections.Generic;
using System.Text;

namespace Armagan.AUI.Core
{
    /// <summary>
    /// Hand-written parser for the .aui markup: an XML-strict subset of HTML.
    ///
    /// WHY NOT System.Xml: the same code runs inside Unity and inside the language server, and
    /// an editor needs positions for every name and value plus recovery after a typo so that
    /// completion still works in a half-typed file. XmlReader gives up at the first error and
    /// reports positions inconsistently. This parser never throws; every problem is a
    /// diagnostic with a line and column, and the tree it returns is the best it could build.
    ///
    /// Grammar accepted:
    ///   document  := prolog? comment* element comment*
    ///   element   := '&lt;' name attr* ('/&gt;' | '&gt;' content '&lt;/' name '&gt;')
    ///   attr      := name '=' ('"' text '"' | "'" text "'")
    ///   content   := (text | element | comment)*
    ///   &lt;style&gt; content is raw until the matching '&lt;/style&gt;'.
    /// Entities: &amp;lt; &amp;gt; &amp;amp; &amp;quot; &amp;apos; &amp;#NN; &amp;#xHH;
    /// </summary>
    public static class AuiParser
    {
        public static AuiMarkupDocument Parse(string text, string file)
        {
            var p = new State(text ?? "", file);
            p.ParseDocument();
            return p.Doc;
        }

        private sealed class State
        {
            public readonly AuiMarkupDocument Doc;
            private readonly string _s;
            private int _i;
            private int _line = 1;
            private int _col = 1;
            private readonly AuiDiagnosticBag _d;

            public State(string text, string file)
            {
                _s = text;
                Doc = new AuiMarkupDocument(file);
                _d = Doc.Diagnostics;
            }

            // ---------------------------------------------------------------- cursor

            private bool Eof
            {
                get { return _i >= _s.Length; }
            }

            private char Cur
            {
                get { return _i < _s.Length ? _s[_i] : '\0'; }
            }

            private char Peek(int k)
            {
                int j = _i + k;
                return j < _s.Length ? _s[j] : '\0';
            }

            private bool StartsWith(string t)
            {
                return string.CompareOrdinal(_s, _i, t, 0, t.Length) == 0;
            }

            private void Advance()
            {
                if (_i >= _s.Length) return;
                char c = _s[_i++];
                if (c == '\n')
                {
                    _line++;
                    _col = 1;
                }
                else if (c != '\r')
                {
                    _col++;
                }
            }

            private void Advance(int n)
            {
                for (int k = 0; k < n; k++) Advance();
            }

            private void SkipWhitespace()
            {
                while (!Eof && char.IsWhiteSpace(Cur)) Advance();
            }

            private static bool IsNameStart(char c)
            {
                return char.IsLetter(c) || c == '_';
            }

            private static bool IsNameChar(char c)
            {
                return char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ':';
            }

            private string ReadName()
            {
                int start = _i;
                if (!IsNameStart(Cur)) return "";
                while (!Eof && IsNameChar(Cur)) Advance();
                return _s.Substring(start, _i - start);
            }

            // ---------------------------------------------------------------- document

            public void ParseDocument()
            {
                int index = 0;
                while (true)
                {
                    SkipWhitespace();
                    if (Eof) break;

                    if (StartsWith("<?"))
                    {
                        SkipProlog();
                        continue;
                    }
                    if (StartsWith("<!--"))
                    {
                        SkipComment();
                        continue;
                    }
                    if (StartsWith("<!"))
                    {
                        _d.Error(AuiCodes.UnexpectedCharacter, "Declarations are not supported.", _line, _col, 2);
                        SkipUntil('>');
                        if (!Eof) Advance();
                        continue;
                    }
                    if (Cur == '<' && IsNameStart(Peek(1)))
                    {
                        AuiNode el = ParseElement(null, ref index);
                        if (el == null) continue;
                        if (Doc.Root == null)
                        {
                            Doc.Root = el;
                        }
                        else
                        {
                            _d.Error(AuiCodes.MultipleRoots,
                                "A view file has exactly one root element; '<" + el.Tag + ">' is a second one.",
                                el.Line, el.Column, el.Tag.Length + 1);
                        }
                        continue;
                    }
                    if (Cur == '<' && Peek(1) == '/')
                    {
                        int l = _line, c = _col;
                        Advance(2);
                        string name = ReadName();
                        SkipUntil('>');
                        if (!Eof) Advance();
                        _d.Error(AuiCodes.MismatchedCloseTag, "Closing tag '</" + name + ">' has no open element.", l, c, name.Length + 3);
                        continue;
                    }

                    // Loose text outside the root.
                    int tl = _line, tc = _col;
                    string loose = ReadText();
                    if (loose.Length > 0)
                        _d.Error(AuiCodes.TextOutsideRoot, "Text outside the root element: '" + Shorten(loose) + "'.", tl, tc, loose.Length);
                    else if (!Eof)
                    {
                        _d.Error(AuiCodes.UnexpectedCharacter, "Unexpected '" + Cur + "'.", _line, _col, 1);
                        Advance();
                    }
                }

                if (Doc.Root == null)
                {
                    _d.Error(AuiCodes.EmptyDocument, "The file has no root element. Expected '<view name=\"...\">'.", 0, 0, 0);
                }

                // Lifting <link> and <style> out of Nodes leaves gaps in the numbering; every
                // consumer indexes arrays by node.Index, so it must equal the list position.
                for (int i = 0; i < Doc.Nodes.Count; i++) Doc.Nodes[i].Index = i;
            }

            private void SkipProlog()
            {
                int l = _line, c = _col;
                while (!Eof && !StartsWith("?>")) Advance();
                if (Eof)
                {
                    _d.Error(AuiCodes.UnterminatedComment, "Unterminated '<?' prolog.", l, c, 2);
                    return;
                }
                Advance(2);
            }

            private void SkipComment()
            {
                int l = _line, c = _col;
                Advance(4);
                while (!Eof && !StartsWith("-->")) Advance();
                if (Eof)
                {
                    _d.Error(AuiCodes.UnterminatedComment, "Unterminated comment.", l, c, 4);
                    return;
                }
                Advance(3);
            }

            private void SkipUntil(char ch)
            {
                while (!Eof && Cur != ch) Advance();
            }

            // ---------------------------------------------------------------- elements

            private AuiNode ParseElement(AuiNode parent, ref int index)
            {
                var node = new AuiNode();
                node.Line = _line;
                node.Column = _col;
                node.Parent = parent;
                node.Index = index++;
                Advance(); // '<'
                node.Tag = ReadName();
                Doc.Nodes.Add(node);
                if (parent != null) parent.Children.Add(node);

                // ---- attributes
                while (true)
                {
                    SkipWhitespace();
                    if (Eof)
                    {
                        _d.Error(AuiCodes.UnclosedTag, "Unclosed tag '<" + node.Tag + ">'.", node.Line, node.Column, node.Tag.Length + 1);
                        Finish(node);
                        return node;
                    }
                    if (Cur == '>')
                    {
                        Advance();
                        break;
                    }
                    if (Cur == '/' && Peek(1) == '>')
                    {
                        Advance(2);
                        node.SelfClosing = true;
                        Finish(node);
                        LiftSpecial(node);
                        return node;
                    }
                    if (!IsNameStart(Cur))
                    {
                        // Recover: skip the bad character. A '<' here means the author forgot
                        // to close the tag; stop the attribute list so the next element parses.
                        if (Cur == '<')
                        {
                            _d.Error(AuiCodes.UnclosedTag, "Unclosed tag '<" + node.Tag + ">'.", node.Line, node.Column, node.Tag.Length + 1);
                            Finish(node);
                            return node;
                        }
                        _d.Error(AuiCodes.UnexpectedCharacter, "Unexpected '" + Cur + "' in tag '<" + node.Tag + ">'.", _line, _col, 1);
                        Advance();
                        continue;
                    }
                    ParseAttribute(node);
                }

                // ---- content
                if (node.Tag == "style")
                {
                    ParseRawStyle(node);
                    Finish(node);
                    return node;
                }

                var textBuf = new StringBuilder();
                while (true)
                {
                    if (Eof)
                    {
                        _d.Error(AuiCodes.UnclosedTag, "Unclosed tag '<" + node.Tag + ">'.", node.Line, node.Column, node.Tag.Length + 1);
                        break;
                    }
                    if (StartsWith("<!--"))
                    {
                        SkipComment();
                        continue;
                    }
                    if (StartsWith("</"))
                    {
                        int cl = _line, cc = _col;
                        Advance(2);
                        string closeName = ReadName();
                        SkipWhitespace();
                        if (Cur == '>') Advance();
                        else _d.Error(AuiCodes.UnexpectedCharacter, "Expected '>' after '</" + closeName + "'.", _line, _col, 1);

                        if (closeName == node.Tag) break;

                        // Does it close an ancestor? Then this node was left unclosed.
                        bool closesAncestor = false;
                        for (AuiNode a = node.Parent; a != null; a = a.Parent)
                            if (a.Tag == closeName) { closesAncestor = true; break; }

                        if (closesAncestor)
                        {
                            _d.Error(AuiCodes.UnclosedTag, "Unclosed tag '<" + node.Tag + ">' (closed by '</" + closeName + ">').", node.Line, node.Column, node.Tag.Length + 1);
                            // Rewind so the parent sees this closing tag.
                            RewindTo(cl, cc);
                            break;
                        }
                        _d.Error(AuiCodes.MismatchedCloseTag, "Closing tag '</" + closeName + ">' does not match '<" + node.Tag + ">'.", cl, cc, closeName.Length + 3);
                        continue;
                    }
                    if (Cur == '<' && IsNameStart(Peek(1)))
                    {
                        ParseElement(node, ref index);
                        continue;
                    }
                    if (Cur == '<')
                    {
                        _d.Error(AuiCodes.UnexpectedCharacter, "Unexpected '<'.", _line, _col, 1);
                        Advance();
                        continue;
                    }
                    string t = ReadText();
                    if (t.Length > 0)
                    {
                        if (textBuf.Length > 0) textBuf.Append(' ');
                        textBuf.Append(t);
                    }
                }

                if (textBuf.Length > 0) node.Text = textBuf.ToString();
                Finish(node);
                LiftSpecial(node);
                return node;
            }

            /// <summary>Move the cursor back to a recorded position (only ever backwards, only a few chars).</summary>
            private void RewindTo(int line, int col)
            {
                // Positions are monotonic in _i; walk back until we match.
                while (_i > 0 && (_line > line || (_line == line && _col > col)))
                {
                    _i--;
                    char c = _s[_i];
                    if (c == '\n')
                    {
                        _line--;
                        // recompute column: distance to previous newline
                        int k = _i - 1;
                        int n = 1;
                        while (k >= 0 && _s[k] != '\n') { if (_s[k] != '\r') n++; k--; }
                        _col = n;
                    }
                    else if (c != '\r')
                    {
                        _col--;
                    }
                }
            }

            private void Finish(AuiNode node)
            {
                node.EndLine = _line;
                node.EndColumn = _col;
            }

            /// <summary>&lt;link&gt;, &lt;prop&gt;, &lt;event&gt; and &lt;component&gt; are lifted out of the tree into the document's declaration lists.</summary>
            private void LiftSpecial(AuiNode node)
            {
                switch (node.Tag)
                {
                    case "link":
                        Detach(node);
                        Doc.Links.Add(new AuiLink { Href = node.Get("href"), Line = node.Line, Column = node.Column });
                        if (string.IsNullOrEmpty(node.Get("href")))
                            _d.Error(AuiCodes.MissingRequiredAttribute, "<link> needs an href.", node.Line, node.Column, 5);
                        break;
                    case "prop":
                    {
                        Detach(node);
                        var p = new AuiPropDecl { Name = node.Get("name"), Type = node.Get("type") ?? "string", Default = node.Get("default"), Owner = node.Parent, Line = node.Line, Column = node.Column };
                        if (string.IsNullOrEmpty(p.Name)) { _d.Error(AuiCodes.MissingRequiredAttribute, "<prop> needs a name.", node.Line, node.Column, 5); break; }
                        if (p.Type != "string" && p.Type != "int" && p.Type != "float" && p.Type != "bool")
                        { _d.Error(AuiCodes.BadPropDeclaration, "<prop> type must be string, int, float or bool.", node.Line, node.Column, 5); p.Type = "string"; }
                        Doc.Props.Add(p);
                        break;
                    }
                    case "event":
                    {
                        Detach(node);
                        var ev = new AuiEventDecl { Name = node.Get("name"), Type = node.Get("type"), Owner = node.Parent, Line = node.Line, Column = node.Column };
                        if (string.IsNullOrEmpty(ev.Name)) { _d.Error(AuiCodes.MissingRequiredAttribute, "<event> needs a name.", node.Line, node.Column, 6); break; }
                        if (ev.Type != null && ev.Type != "string" && ev.Type != "int" && ev.Type != "float" && ev.Type != "bool")
                        { _d.Error(AuiCodes.BadPropDeclaration, "<event> type must be string, int, float or bool (or absent).", node.Line, node.Column, 6); ev.Type = null; }
                        Doc.Events.Add(ev);
                        break;
                    }
                    case "component":
                    {
                        // The template and its subtree leave the tree; the expander clones it per usage.
                        Detach(node);
                        RemoveSubtreeFromNodes(node);
                        var comp = new AuiComponentDecl { Name = node.Get("name"), Node = node };
                        if (string.IsNullOrEmpty(comp.Name) || !char.IsUpper(comp.Name[0]) || !AuiSchema.IsIdentifier(comp.Name))
                            _d.Error(AuiCodes.BadPropDeclaration, "<component> needs a name starting with a capital letter (used as <Name/>).", node.Line, node.Column, 10);
                        if (node.Children.Count != 1)
                            _d.Error(AuiCodes.BadPropDeclaration, "<component> takes exactly one template child (wrap several in a <panel>).", node.Line, node.Column, 10);
                        else comp.Root = node.Children[0];
                        // Its own <prop>/<event> declarations were lifted while inside it.
                        for (int i = Doc.Props.Count - 1; i >= 0; i--) if (Doc.Props[i].Owner == node) { comp.Props.Insert(0, Doc.Props[i]); Doc.Props.RemoveAt(i); }
                        for (int i = Doc.Events.Count - 1; i >= 0; i--) if (Doc.Events[i].Owner == node) { comp.Events.Insert(0, Doc.Events[i]); Doc.Events.RemoveAt(i); }
                        if (comp.Name != null && Doc.FindComponent(comp.Name) != null)
                            _d.Error(AuiCodes.DuplicateId, "Component '" + comp.Name + "' is declared twice.", node.Line, node.Column, 10);
                        else if (comp.Name != null) Doc.Components.Add(comp);
                        break;
                    }
                }
            }

            private void Detach(AuiNode node)
            {
                if (node.Parent != null) node.Parent.Children.Remove(node);
                Doc.Nodes.Remove(node);
            }

            private void RemoveSubtreeFromNodes(AuiNode node)
            {
                foreach (AuiNode c in node.Children) { Doc.Nodes.Remove(c); RemoveSubtreeFromNodes(c); }
            }

            private void ParseAttribute(AuiNode node)
            {
                var a = new AuiAttribute();
                a.Line = _line;
                a.Column = _col;
                a.Name = ReadName();
                SkipWhitespace();
                if (Cur != '=')
                {
                    // A bare attribute (hidden, wrap) is a flag with an empty value, as in HTML.
                    a.Value = "";
                    a.ValueLine = _line;
                    a.ValueColumn = _col;
                    AddAttribute(node, a);
                    return;
                }
                Advance(); // '='
                SkipWhitespace();
                char q = Cur;
                if (q != '"' && q != '\'')
                {
                    // Unquoted: read until whitespace or '>' so the rest of the tag survives.
                    _d.Error(AuiCodes.AttributeNotQuoted, "Attribute '" + a.Name + "' value must be quoted.", _line, _col, 1);
                    a.ValueLine = _line;
                    a.ValueColumn = _col;
                    int start = _i;
                    while (!Eof && !char.IsWhiteSpace(Cur) && Cur != '>' && !(Cur == '/' && Peek(1) == '>')) Advance();
                    a.Value = Decode(_s.Substring(start, _i - start), a.ValueLine, a.ValueColumn);
                    AddAttribute(node, a);
                    return;
                }
                Advance(); // opening quote
                a.ValueLine = _line;
                a.ValueColumn = _col;
                int vs = _i;
                while (!Eof && Cur != q && Cur != '<') Advance();
                if (Eof || Cur == '<')
                {
                    _d.Error(AuiCodes.UnterminatedAttribute, "Unterminated value for attribute '" + a.Name + "'.", a.Line, a.Column, a.Name.Length);
                    a.Value = Decode(_s.Substring(vs, _i - vs), a.ValueLine, a.ValueColumn);
                    AddAttribute(node, a);
                    return;
                }
                a.Value = Decode(_s.Substring(vs, _i - vs), a.ValueLine, a.ValueColumn);
                Advance(); // closing quote
                AddAttribute(node, a);
            }

            private void AddAttribute(AuiNode node, AuiAttribute a)
            {
                if (node.FindAttribute(a.Name) != null)
                    _d.Error(AuiCodes.DuplicateAttribute, "Attribute '" + a.Name + "' appears twice on '<" + node.Tag + ">'.", a.Line, a.Column, a.Name.Length);
                node.Attributes.Add(a);
            }

            private void ParseRawStyle(AuiNode node)
            {
                var block = new AuiStyleBlock { Line = _line, Column = _col };
                int start = _i;
                while (!Eof && !StartsWith("</style")) Advance();
                block.Text = _s.Substring(start, _i - start);
                if (Eof)
                {
                    _d.Error(AuiCodes.UnclosedTag, "Unclosed <style> block.", node.Line, node.Column, 6);
                }
                else
                {
                    Advance(7); // </style
                    SkipWhitespace();
                    if (Cur == '>') Advance();
                }
                Doc.Styles.Add(block);
                // The style element itself is not part of the layout tree.
                if (node.Parent != null) node.Parent.Children.Remove(node);
                Doc.Nodes.Remove(node);
            }

            // ---------------------------------------------------------------- text

            /// <summary>Read literal text up to the next '&lt;', decoding entities, trimming and collapsing whitespace.</summary>
            private string ReadText()
            {
                int l = _line, c = _col;
                int start = _i;
                while (!Eof && Cur != '<') Advance();
                string raw = _s.Substring(start, _i - start);
                return Collapse(Decode(raw, l, c));
            }

            private static string Collapse(string s)
            {
                var sb = new StringBuilder(s.Length);
                bool pendingSpace = false;
                for (int i = 0; i < s.Length; i++)
                {
                    char ch = s[i];
                    if (char.IsWhiteSpace(ch))
                    {
                        pendingSpace = sb.Length > 0;
                        continue;
                    }
                    if (pendingSpace) sb.Append(' ');
                    pendingSpace = false;
                    sb.Append(ch);
                }
                return sb.ToString();
            }

            private string Decode(string raw, int line, int col)
            {
                if (raw.IndexOf('&') < 0) return raw;
                var sb = new StringBuilder(raw.Length);
                int i = 0;
                int curLine = line, curCol = col;
                while (i < raw.Length)
                {
                    char ch = raw[i];
                    if (ch != '&')
                    {
                        sb.Append(ch);
                        if (ch == '\n') { curLine++; curCol = 1; } else curCol++;
                        i++;
                        continue;
                    }
                    int semi = raw.IndexOf(';', i + 1);
                    if (semi < 0 || semi - i > 10)
                    {
                        _d.Warning(AuiCodes.BareAmpersand, "Bare '&'; write '&amp;' for a literal ampersand.", curLine, curCol, 1);
                        sb.Append('&');
                        i++;
                        curCol++;
                        continue;
                    }
                    string ent = raw.Substring(i + 1, semi - i - 1);
                    string rep = null;
                    switch (ent)
                    {
                        case "lt": rep = "<"; break;
                        case "gt": rep = ">"; break;
                        case "amp": rep = "&"; break;
                        case "quot": rep = "\""; break;
                        case "apos": rep = "'"; break;
                        case "nbsp": rep = " "; break;
                        default:
                            if (ent.Length > 1 && ent[0] == '#')
                            {
                                int code;
                                bool ok = ent[1] == 'x' || ent[1] == 'X'
                                    ? int.TryParse(ent.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out code)
                                    : int.TryParse(ent.Substring(1), out code);
                                // ConvertFromUtf32 throws on the surrogate range; a lone surrogate is not a character.
                                if (ok && code >= 0 && code <= 0x10FFFF && !(code >= 0xD800 && code <= 0xDFFF)) rep = char.ConvertFromUtf32(code);
                            }
                            break;
                    }
                    if (rep == null)
                    {
                        _d.Warning(AuiCodes.UnknownEntity, "Unknown entity '&" + ent + ";'.", curLine, curCol, ent.Length + 2);
                        sb.Append('&');
                        i++;
                        curCol++;
                        continue;
                    }
                    sb.Append(rep);
                    curCol += semi - i + 1;
                    i = semi + 1;
                }
                return sb.ToString();
            }

            private static string Shorten(string s)
            {
                return s.Length > 24 ? s.Substring(0, 24) + "..." : s;
            }
        }
    }
}
