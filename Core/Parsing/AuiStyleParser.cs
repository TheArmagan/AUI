using System.Text;

namespace Armagan.AUI.Core
{
    /// <summary>
    /// Parser for the .auis stylesheet: a deliberately small CSS.
    ///
    ///   sheet := (comment | rule)*
    ///   rule  := selector (',' selector)* '{' decl* '}'
    ///   decl  := name ':' value ';'?
    ///   selector := ':root' | tag? ('.' class)? (':' state)?
    ///
    /// Variables are declared only inside ':root { --name: value; }' and referenced as var(--name)
    /// or var(--name, fallback) anywhere in a value. No nesting, no descendant selectors, no
    /// !important, no @rules. Like the markup parser it never throws and always returns the
    /// rules it could read.
    /// </summary>
    public static class AuiStyleParser
    {
        public static AuiStyleSheet Parse(string text, string file, AuiStyleTier tier)
        {
            return Parse(text, file, tier, 1, 1);
        }

        /// <summary>Parse a block embedded in another file; positions are offset to that file.</summary>
        public static AuiStyleSheet Parse(string text, string file, AuiStyleTier tier, int startLine, int startColumn)
        {
            var sheet = new AuiStyleSheet(file, tier);
            var p = new State(text ?? "", sheet, startLine, startColumn);
            p.Run();
            return sheet;
        }

        /// <summary>Parse a style="" attribute: declarations only, into one rule with no selector.</summary>
        public static AuiStyleRule ParseDeclarations(string text, string file, int line, int column, AuiDiagnosticBag diagnostics)
        {
            var sheet = new AuiStyleSheet(file, AuiStyleTier.Attribute);
            var rule = new AuiStyleRule { Line = line, Column = column, Sheet = sheet };
            var p = new State(text ?? "", sheet, line, column);
            p.ParseDeclarationsInto(rule, false);
            if (diagnostics != null) diagnostics.AddRange(sheet.Diagnostics);
            return rule;
        }

        private sealed class State
        {
            private readonly string _s;
            private readonly AuiStyleSheet _sheet;
            private readonly AuiDiagnosticBag _d;
            private int _i;
            private int _line;
            private int _col;

            public State(string s, AuiStyleSheet sheet, int line, int col)
            {
                _s = s;
                _sheet = sheet;
                _d = sheet.Diagnostics;
                _line = line;
                _col = col;
            }

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
                return _i + k < _s.Length ? _s[_i + k] : '\0';
            }

            private void Advance()
            {
                if (_i >= _s.Length) return;
                char c = _s[_i++];
                if (c == '\n') { _line++; _col = 1; }
                else if (c != '\r') _col++;
            }

            private void SkipWsAndComments()
            {
                while (!Eof)
                {
                    if (char.IsWhiteSpace(Cur)) { Advance(); continue; }
                    if (Cur == '/' && Peek(1) == '*')
                    {
                        int l = _line, c = _col;
                        Advance(); Advance();
                        while (!Eof && !(Cur == '*' && Peek(1) == '/')) Advance();
                        if (Eof) { _d.Error(AuiCodes.StyleSyntax, "Unterminated comment.", l, c, 2); return; }
                        Advance(); Advance();
                        continue;
                    }
                    if (Cur == '/' && Peek(1) == '/')
                    {
                        while (!Eof && Cur != '\n') Advance();
                        continue;
                    }
                    break;
                }
            }

            private static bool IsIdent(char c)
            {
                return char.IsLetterOrDigit(c) || c == '-' || c == '_';
            }

            private string ReadIdent()
            {
                int start = _i;
                while (!Eof && IsIdent(Cur)) Advance();
                return _s.Substring(start, _i - start);
            }

            public void Run()
            {
                while (true)
                {
                    SkipWsAndComments();
                    if (Eof) return;
                    ParseRule();
                }
            }

            private void ParseRule()
            {
                var rule = new AuiStyleRule { Line = _line, Column = _col, Sheet = _sheet };
                bool isRoot = false;

                // ---- selectors
                while (true)
                {
                    SkipWsAndComments();
                    if (Eof) { _d.Error(AuiCodes.StyleSyntax, "Expected '{' after selector.", rule.Line, rule.Column, 1); return; }
                    if (Cur == '{') break;
                    int sl = _line, sc = _col;
                    AuiSelector sel = ParseSelector();
                    if (sel == null)
                    {
                        // Recover: skip to the next '{' or '}'.
                        while (!Eof && Cur != '{' && Cur != '}') Advance();
                        if (Cur == '}') { Advance(); return; }
                        if (Eof) return;
                        break;
                    }
                    if (sel.IsRoot) isRoot = true;
                    rule.Selectors.Add(sel);
                    SkipWsAndComments();
                    if (Cur == ',') { Advance(); continue; }
                    if (Cur == '{') break;
                    _d.Error(AuiCodes.BadSelector, "Unexpected '" + Cur + "' in selector.", _line, _col, 1);
                    while (!Eof && Cur != '{' && Cur != '}') Advance();
                    if (Cur == '}') { Advance(); return; }
                    if (Eof) return;
                    break;
                }
                Advance(); // '{'

                if (isRoot)
                {
                    if (rule.Selectors.Count > 1)
                        _d.Error(AuiCodes.BadSelector, ":root cannot be combined with other selectors.", rule.Line, rule.Column, 5);
                    ParseDeclarationsInto(rule, true);
                    // Variables are lifted; a :root rule keeps no ordinary declarations.
                    foreach (AuiDeclaration decl in rule.Declarations)
                        _d.Error(AuiCodes.StyleSyntax, "Only --variables belong in :root; '" + decl.Property + "' is not one.", decl.Line, decl.Column, decl.Property.Length);
                    return;
                }

                ParseDeclarationsInto(rule, true);
                if (rule.Selectors.Count > 0) _sheet.Rules.Add(rule);
            }

            private AuiSelector ParseSelector()
            {
                var sel = new AuiSelector();
                int l = _line, c = _col;
                if (Cur == '*') { Advance(); }
                else if (Cur == ':' && string.CompareOrdinal(_s, _i, ":root", 0, 5) == 0)
                {
                    for (int k = 0; k < 5; k++) Advance();
                    sel.IsRoot = true;
                    return sel;
                }
                else if (char.IsLetter(Cur))
                {
                    sel.Tag = ReadIdent();
                    if (AuiSchema.FindElement(sel.Tag) == null && !char.IsUpper(sel.Tag[0]))
                        _d.Warning(AuiCodes.UnknownTag, "Selector names unknown element '" + sel.Tag + "'.", l, c, sel.Tag.Length);
                }
                if (Cur == '.')
                {
                    Advance();
                    int cl = _line, cc = _col;
                    // .*name: a style-only (star) class, outside the 64-class budget and invisible to GetElements.
                    bool star = Cur == '*';
                    if (star) Advance();
                    sel.Class = ReadIdent();
                    if (sel.Class.Length == 0) { _d.Error(AuiCodes.BadSelector, "Expected a class name after '.'.", cl, cc, 1); return null; }
                    if (star) sel.Class = "*" + sel.Class;
                }
                if (Cur == ':' && Peek(1) == ':')
                {
                    // Pseudo-element: only modal::backdrop exists (the dimming scrim behind a modal).
                    Advance(); Advance();
                    int pl = _line, pc = _col;
                    sel.Pseudo = ReadIdent();
                    if (sel.Pseudo != "backdrop") { _d.Error(AuiCodes.BadSelector, "Unknown pseudo-element '::" + sel.Pseudo + "'; only ::backdrop exists.", pl, pc, sel.Pseudo.Length + 2); return null; }
                    if (sel.Tag != "modal") { _d.Error(AuiCodes.BadSelector, "::backdrop only applies to modal (modal::backdrop, modal.dim::backdrop).", pl, pc, 10); return null; }
                    if (Cur == ':') { _d.Error(AuiCodes.BadSelector, "::backdrop takes no state; style the modal itself for hover and friends.", _line, _col, 1); return null; }
                    return sel;
                }
                if (Cur == ':')
                {
                    Advance();
                    int stl = _line, stc = _col;
                    sel.State = ReadIdent();
                    if (!AuiSchema.IsState(sel.State))
                    {
                        string hint = AuiSchema.Suggest(sel.State, AuiSchema.States);
                        _d.Error(AuiCodes.UnknownState, "Unknown state ':" + sel.State + "'." + (hint != null ? " Did you mean ':" + hint + "'?" : " States: hover, pressed, selected, disabled."), stl, stc, sel.State.Length);
                        return null;
                    }
                }
                if (sel.Tag == null && sel.Class == null && sel.State == null && !sel.IsRoot)
                {
                    // '*' alone is allowed; anything else here is junk.
                    if (_s.Length > 0 && _i > 0 && _s[_i - 1] == '*') return sel;
                    _d.Error(AuiCodes.BadSelector, "Expected a selector (tag, .class, tag:state, :root).", l, c, 1);
                    return null;
                }
                if (Cur == ' ' || Cur == '>' || Cur == '\t')
                {
                    // Descendant / child combinators are not supported. Detect "a b {" or "a > b {".
                    int save = _i, sl = _line, sc = _col;
                    while (!Eof && (Cur == ' ' || Cur == '\t')) Advance();
                    if (Cur == '>' || char.IsLetter(Cur) || Cur == '.')
                    {
                        _d.Error(AuiCodes.BadSelector, "Descendant selectors are not supported; AUI styles are baked per element. Use a class instead.", l, c, _i - save + 1);
                        return null;
                    }
                    _i = save; _line = sl; _col = sc;
                }
                return sel;
            }

            public void ParseDeclarationsInto(AuiStyleRule rule, bool expectClosingBrace)
            {
                while (true)
                {
                    SkipWsAndComments();
                    if (Eof)
                    {
                        if (expectClosingBrace) _d.Error(AuiCodes.StyleSyntax, "Missing '}' for rule at " + rule.Line + ":" + rule.Column + ".", rule.Line, rule.Column, 1);
                        return;
                    }
                    if (Cur == '}')
                    {
                        Advance();
                        return;
                    }
                    if (Cur == ';') { Advance(); continue; }
                    if (!IsIdent(Cur))
                    {
                        _d.Error(AuiCodes.StyleSyntax, "Unexpected '" + Cur + "'; expected a property name.", _line, _col, 1);
                        Advance();
                        continue;
                    }
                    int pl = _line, pc = _col;
                    string name = ReadIdent();
                    SkipWsAndComments();
                    if (Cur != ':')
                    {
                        _d.Error(AuiCodes.StyleSyntax, "Expected ':' after '" + name + "'.", _line, _col, 1);
                        while (!Eof && Cur != ';' && Cur != '}') Advance();
                        continue;
                    }
                    Advance();
                    SkipWsAndComments();
                    int vl = _line, vc = _col;
                    var value = new StringBuilder();
                    int depth = 0;
                    while (!Eof)
                    {
                        char ch = Cur;
                        if (ch == '/' && Peek(1) == '*')
                        {
                            // A comment inside a value is dropped, not kept as text.
                            Advance(); Advance();
                            while (!Eof && !(Cur == '*' && Peek(1) == '/')) Advance();
                            if (!Eof) { Advance(); Advance(); }
                            continue;
                        }
                        // Parentheses and {binding} braces nest; a bare '}' closes the rule.
                        if (ch == '(' || ch == '{') depth++;
                        if (ch == ')' && depth > 0) depth--;
                        if (ch == '}' && depth > 0) { depth--; value.Append(ch); Advance(); continue; }
                        if (depth <= 0 && (ch == ';' || ch == '}')) break;
                        if (ch == '\n' && depth <= 0)
                        {
                            // A newline ends a declaration that forgot its ';' only if the next
                            // token looks like "name:"; otherwise values may wrap.
                            int save = _i;
                            int j = _i + 1;
                            while (j < _s.Length && (_s[j] == ' ' || _s[j] == '\t')) j++;
                            int k = j;
                            while (k < _s.Length && IsIdent(_s[k])) k++;
                            int m = k;
                            while (m < _s.Length && (_s[m] == ' ' || _s[m] == '\t')) m++;
                            if (k > j && m < _s.Length && _s[m] == ':')
                            {
                                _d.Warning(AuiCodes.StyleSyntax, "Missing ';' after '" + name + "'.", pl, pc, name.Length);
                                break;
                            }
                            _i = save;
                        }
                        value.Append(ch);
                        Advance();
                    }
                    string v = value.ToString().Trim();
                    if (Cur == ';') Advance();

                    if (name.StartsWith("--"))
                    {
                        _sheet.Variables.Add(new AuiVariable { Name = name, Value = v, Line = pl, Column = pc });
                        continue;
                    }

                    var decl = new AuiDeclaration { Property = name, Value = v, Line = pl, Column = pc, ValueColumn = vc };
                    if (AuiSchema.FindProp(name) == null)
                    {
                        var names = new System.Collections.Generic.List<string>();
                        foreach (AuiPropDef p in AuiSchema.Props) names.Add(p.Name);
                        string hint = AuiSchema.Suggest(name, names);
                        _d.Error(AuiCodes.UnknownStyleProperty, "Unknown property '" + name + "'." + (hint != null ? " Did you mean '" + hint + "'?" : ""), pl, pc, name.Length);
                        continue;
                    }
                    if (v.Length == 0)
                    {
                        _d.Error(AuiCodes.BadStyleValue, "Property '" + name + "' has no value.", pl, pc, name.Length);
                        continue;
                    }
                    AuiDeclaration existing = rule.Find(name);
                    if (existing != null) rule.Declarations.Remove(existing);
                    rule.Declarations.Add(decl);
                }
            }
        }
    }
}
