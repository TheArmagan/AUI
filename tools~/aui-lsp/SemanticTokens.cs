using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Armagan.AUI.Core;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Armagan.AUI.Lsp
{
    /// <summary>
    /// Semantic tokens for stylesheet text: a whole .auis file, every &lt;style&gt; body and every
    /// style="" value of a .aui. The TextMate grammar colours the same text in VS Code; these
    /// tokens come from the server, so any client (and any theme that ranks semantic tokens
    /// first) renders the embedded stylesheet the same way, whatever grammar it has.
    ///
    /// The lexer is deliberately independent of the stylesheet parser: it never fails, colours
    /// half-typed text, and keeps comments where the parser would drop them.
    /// </summary>
    public sealed class SemanticTokensHandler : SemanticTokensHandlerBase
    {
        private readonly Workspace _ws;

        public SemanticTokensHandler(Workspace ws)
        {
            _ws = ws;
        }

        private static readonly SemanticTokensLegend Legend = new SemanticTokensLegend
        {
            TokenTypes = new Container<SemanticTokenType>(
                SemanticTokenType.Comment, SemanticTokenType.Keyword, SemanticTokenType.Class, SemanticTokenType.Type,
                SemanticTokenType.Property, SemanticTokenType.Variable, SemanticTokenType.Number, SemanticTokenType.Function,
                SemanticTokenType.EnumMember, SemanticTokenType.Operator, SemanticTokenType.Decorator, SemanticTokenType.String),
            TokenModifiers = new Container<SemanticTokenModifier>(SemanticTokenModifier.Declaration, SemanticTokenModifier.DefaultLibrary),
        };

        protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(SemanticTokensCapability capability, ClientCapabilities clientCapabilities)
        {
            return new SemanticTokensRegistrationOptions
            {
                DocumentSelector = Sel.Both,
                Legend = Legend,
                Full = new SemanticTokensCapabilityRequestFull { Delta = false },
                Range = true,
            };
        }

        protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
        {
            return Task.FromResult(new SemanticTokensDocument(Legend));
        }

        protected override Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
        {
            string path = Workspace.PathOf(identifier.TextDocument.Uri);
            string text = _ws.Read(path) ?? "";
            var tokens = new List<Token>();
            if (Sel.IsSheet(path))
            {
                SheetLexer.Lex(text, 0, false, tokens);
            }
            else
            {
                AuiMarkupDocument doc = AuiParser.Parse(text, path);
                foreach (AuiStyleBlock b in doc.Styles)
                    SheetLexer.Lex(b.Text, Cursor.Offset(text, b.Line - 1, b.Column - 1), false, tokens);
                foreach (AuiNode n in doc.Nodes)
                {
                    AuiAttribute a = n.FindAttribute("style");
                    if (a == null || a.Value == null) continue;
                    SheetLexer.Lex(a.Value, Cursor.Offset(text, a.ValueLine - 1, a.ValueColumn - 1), true, tokens);
                }
            }
            // The builder wants document order; blocks and attributes were visited in tree order,
            // but an attribute can sit above a style block.
            tokens.Sort((x, y) => x.Start.CompareTo(y.Start));
            foreach (Token t in tokens)
            {
                // A token never crosses a line: split a multi-line comment into one token per line.
                int start = t.Start, end = t.Start + t.Length;
                while (start < end)
                {
                    int nl = text.IndexOf('\n', start);
                    int stop = nl < 0 || nl >= end ? end : nl;
                    int len = stop - start;
                    if (len > 0 && text[stop - 1] == '\r') len--;
                    if (len > 0)
                    {
                        Position p = Cursor.PositionOf(text, start);
                        builder.Push(p.Line, p.Character, len, t.Type, t.Modifiers);
                    }
                    start = stop + 1;
                }
            }
            return Task.CompletedTask;
        }

        internal struct Token
        {
            public int Start, Length;
            public SemanticTokenType Type;
            public SemanticTokenModifier[] Modifiers;
        }

        /// <summary>A forgiving lexer for .auis text. Offsets are absolute (base + index into the fragment).</summary>
        internal static class SheetLexer
        {
            private static readonly SemanticTokenModifier[] None = new SemanticTokenModifier[0];
            private static readonly SemanticTokenModifier[] Declaration = { SemanticTokenModifier.Declaration };
            private static readonly SemanticTokenModifier[] Library = { SemanticTokenModifier.DefaultLibrary };

            private static readonly HashSet<string> Tags = new HashSet<string>
            {
                "view", "panel", "div", "text", "image", "rawimage", "icon", "spacer", "button", "toggle", "slider", "input", "select", "option",
                "tabs", "tab", "pages", "page", "repeat", "empty", "modal", "template", "slot", "component",
            };
            private static readonly HashSet<string> States = new HashSet<string> { "hover", "pressed", "selected", "disabled" };
            private static readonly HashSet<string> Functions = new HashSet<string> { "var", "sprite", "material", "shader", "rgb", "rgba" };
            private static readonly HashSet<string> Values = new HashSet<string>
            {
                "auto", "none", "all", "flex", "static", "absolute", "row", "column", "start", "center", "end", "space-between", "stretch",
                "true", "false", "fill", "top", "bottom", "left", "right", "regular", "normal", "medium", "semibold", "semi-bold", "bold",
                "transparent", "white", "black", "fade", "fade-scale", "fade-slide", "slide-left", "slide-right", "slide-up", "slide-down", "scale",
            };

            /// <param name="declarationsOnly">A style="" value: declarations without a selector or braces.</param>
            public static void Lex(string s, int baseOffset, bool declarationsOnly, List<Token> outTokens)
            {
                int i = 0, n = s.Length;
                bool inBlock = declarationsOnly;
                bool expectValue = false;   // after "name:" until ';' or '}'
                while (i < n)
                {
                    char c = s[i];
                    if (char.IsWhiteSpace(c)) { i++; continue; }
                    // comments
                    if (c == '/' && i + 1 < n && s[i + 1] == '*')
                    {
                        int close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                        int end = close < 0 ? n : close + 2;
                        Add(outTokens, baseOffset + i, end - i, SemanticTokenType.Comment, None);
                        i = end;
                        continue;
                    }
                    if (c == '/' && i + 1 < n && s[i + 1] == '/')
                    {
                        int end = i;
                        while (end < n && s[end] != '\n' && s[end] != '\r') end++;
                        Add(outTokens, baseOffset + i, end - i, SemanticTokenType.Comment, None);
                        i = end;
                        continue;
                    }
                    if (c == '{') { inBlock = true; expectValue = false; i++; continue; }
                    if (c == '}') { inBlock = declarationsOnly; expectValue = false; i++; continue; }
                    if (c == ';') { expectValue = false; i++; continue; }
                    if (c == ',' || c == '(' || c == ')') { i++; continue; }

                    if (!inBlock)
                    {
                        // ---- selectors
                        if (c == ':')
                        {
                            int start = i;
                            i++;
                            bool pseudoElement = i < n && s[i] == ':';
                            if (pseudoElement) i++;
                            int nameStart = i;
                            while (i < n && IsWord(s[i])) i++;
                            string name = s.Substring(nameStart, i - nameStart);
                            if (name == "root") Add(outTokens, baseOffset + start, i - start, SemanticTokenType.Keyword, None);
                            else if (pseudoElement || States.Contains(name)) Add(outTokens, baseOffset + start, i - start, SemanticTokenType.Decorator, None);
                            else Add(outTokens, baseOffset + start, i - start, SemanticTokenType.Decorator, None);
                            continue;
                        }
                        if (c == '.')
                        {
                            int start = i;
                            i++;
                            if (i < n && s[i] == '*') i++;
                            while (i < n && IsWord(s[i])) i++;
                            Add(outTokens, baseOffset + start, i - start, SemanticTokenType.Class, Declaration);
                            continue;
                        }
                        if (c == '*') { Add(outTokens, baseOffset + i, 1, SemanticTokenType.Operator, None); i++; continue; }
                        if (IsWord(c))
                        {
                            int start = i;
                            while (i < n && IsWord(s[i])) i++;
                            string word = s.Substring(start, i - start);
                            if (Tags.Contains(word)) Add(outTokens, baseOffset + start, i - start, SemanticTokenType.Type, Library);
                            else if (char.IsUpper(word[0])) Add(outTokens, baseOffset + start, i - start, SemanticTokenType.Type, None);
                            continue;
                        }
                        i++;
                        continue;
                    }

                    // ---- declarations
                    if (!expectValue)
                    {
                        // property name or variable name, up to ':'
                        int start = i;
                        if (c == '-' && i + 1 < n && s[i + 1] == '-')
                        {
                            i += 2;
                            while (i < n && IsWord(s[i])) i++;
                            Add(outTokens, baseOffset + start, i - start, SemanticTokenType.Variable, Declaration);
                        }
                        else if (IsWord(c))
                        {
                            while (i < n && IsWord(s[i])) i++;
                            string word = s.Substring(start, i - start);
                            Add(outTokens, baseOffset + start, i - start, SemanticTokenType.Property, AuiSchema.FindProp(word) != null ? Library : None);
                        }
                        else { i++; continue; }
                        // skip to ':' (a missing colon: treat the rest as a value anyway)
                        int j = i;
                        while (j < n && char.IsWhiteSpace(s[j])) j++;
                        if (j < n && s[j] == ':') { i = j + 1; expectValue = true; }
                        else expectValue = true;
                        continue;
                    }

                    // value tokens
                    if (c == '#')
                    {
                        int start = i;
                        i++;
                        while (i < n && Uri.IsHexDigit(s[i])) i++;
                        Add(outTokens, baseOffset + start, i - start, SemanticTokenType.Number, None);
                        continue;
                    }
                    if (c == '{')
                    {
                        // a {binding} in a style="" value
                        int close = s.IndexOf('}', i);
                        int end = close < 0 ? n : close + 1;
                        Add(outTokens, baseOffset + i, end - i, SemanticTokenType.Variable, None);
                        i = end;
                        continue;
                    }
                    if (c == '-' && i + 1 < n && s[i + 1] == '-')
                    {
                        int start = i;
                        i += 2;
                        while (i < n && IsWord(s[i])) i++;
                        Add(outTokens, baseOffset + start, i - start, SemanticTokenType.Variable, None);
                        continue;
                    }
                    if (char.IsDigit(c) || ((c == '-' || c == '+' || c == '.') && i + 1 < n && (char.IsDigit(s[i + 1]) || s[i + 1] == '.')))
                    {
                        int start = i;
                        i++;
                        while (i < n && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                        // unit: mm, %, s, ms
                        if (i < n && s[i] == '%') i++;
                        else while (i < n && char.IsLetter(s[i])) i++;
                        Add(outTokens, baseOffset + start, i - start, SemanticTokenType.Number, None);
                        continue;
                    }
                    if (IsWord(c))
                    {
                        int start = i;
                        while (i < n && IsWord(s[i])) i++;
                        string word = s.Substring(start, i - start);
                        int j = i;
                        while (j < n && char.IsWhiteSpace(s[j])) j++;
                        if (j < n && s[j] == '(' && Functions.Contains(word)) Add(outTokens, baseOffset + start, i - start, SemanticTokenType.Function, Library);
                        else if (Values.Contains(word) || AuiSchema.IsEase(word)) Add(outTokens, baseOffset + start, i - start, SemanticTokenType.EnumMember, Library);
                        else if (word.StartsWith("Rounded") || word == "Square" || word == "Circle" || word.StartsWith("Border")) Add(outTokens, baseOffset + start, i - start, SemanticTokenType.String, None);
                        else if (AuiSchema.FindProp(word) != null) Add(outTokens, baseOffset + start, i - start, SemanticTokenType.Property, Library);   // transition: <prop> ...
                        else Add(outTokens, baseOffset + start, i - start, SemanticTokenType.String, None);   // a sprite, material or shader name
                        continue;
                    }
                    i++;
                }
            }

            private static bool IsWord(char c)
            {
                return char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '/';
            }

            private static void Add(List<Token> list, int start, int length, SemanticTokenType type, SemanticTokenModifier[] mods)
            {
                if (length <= 0) return;
                list.Add(new Token { Start = start, Length = length, Type = type, Modifiers = mods });
            }
        }
    }
}
