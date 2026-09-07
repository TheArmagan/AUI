using System.Collections.Generic;
using System.Text;

namespace Armagan.AUI.Core
{
    /// <summary>
    /// A small JSON reader that flattens an object of nested objects and strings into
    /// "a.b.c" -> value, keeping the position of every leaf for go-to-definition.
    /// Numbers and booleans become their text; arrays and nulls are errors, because a
    /// language file is a string table and nothing else. Never throws.
    /// </summary>
    public static class AuiJson
    {
        public struct Pos
        {
            public int Line, Column;
            /// <summary>The file the value sits in; differs from the table's file after a merge (package keys under project keys).</summary>
            public string File;
        }

        public sealed class Result
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
            public readonly Dictionary<string, Pos> Positions = new Dictionary<string, Pos>();
            /// <summary>Order keys first appeared, for deterministic output.</summary>
            public readonly List<string> Order = new List<string>();
            public AuiDiagnosticBag Diagnostics;
        }

        public static Result Flatten(string text, string file)
        {
            var r = new Result { Diagnostics = new AuiDiagnosticBag(file) };
            var s = new State(text ?? "", r, file);
            s.Run();
            return r;
        }

        private sealed class State
        {
            private readonly string _s;
            private readonly Result _r;
            private readonly AuiDiagnosticBag _d;
            private readonly string _file;
            private int _i;
            private int _line = 1, _col = 1;

            public State(string s, Result r, string file)
            {
                _s = s;
                _r = r;
                _d = r.Diagnostics;
                _file = file;
            }

            private bool Eof { get { return _i >= _s.Length; } }
            private char Cur { get { return _i < _s.Length ? _s[_i] : '\0'; } }

            private void Advance()
            {
                if (_i >= _s.Length) return;
                char c = _s[_i++];
                if (c == '\n') { _line++; _col = 1; } else if (c != '\r') _col++;
            }

            private void Ws()
            {
                while (!Eof)
                {
                    if (char.IsWhiteSpace(Cur)) { Advance(); continue; }
                    // Tolerate // and /* */ comments: translators add notes.
                    if (Cur == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/') { while (!Eof && Cur != '\n') Advance(); continue; }
                    if (Cur == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*')
                    {
                        Advance(); Advance();
                        while (!Eof && !(Cur == '*' && _i + 1 < _s.Length && _s[_i + 1] == '/')) Advance();
                        if (!Eof) { Advance(); Advance(); }
                        continue;
                    }
                    break;
                }
            }

            public void Run()
            {
                Ws();
                if (Eof) { _d.Error(AuiCodes.LanguageFileSyntax, "Empty language file; expected a JSON object.", 0, 0, 0); return; }
                if (Cur != '{') { _d.Error(AuiCodes.LanguageFileSyntax, "Expected '{' at the top of a language file.", _line, _col, 1); return; }
                Object("");
                Ws();
                if (!Eof) _d.Error(AuiCodes.LanguageFileSyntax, "Unexpected content after the closing '}'.", _line, _col, 1);
            }

            private void Object(string prefix)
            {
                Advance(); // '{'
                bool first = true;
                while (true)
                {
                    Ws();
                    if (Eof) { _d.Error(AuiCodes.LanguageFileSyntax, "Unterminated object.", _line, _col, 1); return; }
                    if (Cur == '}') { Advance(); return; }
                    if (!first)
                    {
                        if (Cur != ',') { _d.Error(AuiCodes.LanguageFileSyntax, "Expected ',' or '}'.", _line, _col, 1); Skip(); continue; }
                        Advance();
                        Ws();
                        if (Cur == '}') { Advance(); return; } // trailing comma tolerated
                    }
                    first = false;
                    if (Cur != '"') { _d.Error(AuiCodes.LanguageFileSyntax, "Expected a quoted key.", _line, _col, 1); Skip(); continue; }
                    int kl = _line, kc = _col;
                    string key = String();
                    if (key == null) { Skip(); continue; }
                    Ws();
                    if (Cur != ':') { _d.Error(AuiCodes.LanguageFileSyntax, "Expected ':' after key '" + key + "'.", _line, _col, 1); Skip(); continue; }
                    Advance();
                    Ws();
                    string full = prefix.Length == 0 ? key : prefix + "." + key;
                    if (key.Length == 0 || key.IndexOf('.') >= 0)
                        _d.Error(AuiCodes.LanguageFileSyntax, "Key '" + key + "' must be non-empty and must not contain '.'; nest objects instead.", kl, kc, key.Length + 2);
                    if (Cur == '{') { Object(full); continue; }
                    int vl = _line, vc = _col;
                    string value = Value();
                    if (value == null) continue;
                    if (_r.Values.ContainsKey(full))
                    {
                        // The first value stands, as in every JSON reader people are used to.
                        _d.Error(AuiCodes.DuplicateKey, "Key '" + full + "' appears twice.", kl, kc, key.Length + 2);
                        continue;
                    }
                    _r.Order.Add(full);
                    _r.Values[full] = value;
                    _r.Positions[full] = new Pos { Line = vl, Column = vc, File = _file };
                }
            }

            /// <summary>Recover to the next ',' or '}' at this nesting level.</summary>
            private void Skip()
            {
                int depth = 0;
                while (!Eof)
                {
                    char c = Cur;
                    if (c == '"') { String(); continue; }
                    if (c == '{' || c == '[') depth++;
                    if (c == '}' || c == ']') { if (depth == 0) return; depth--; }
                    if (c == ',' && depth == 0) return;
                    Advance();
                }
            }

            private string Value()
            {
                if (Cur == '"') return String();
                int l = _line, c = _col;
                if (Cur == '[') { _d.Error(AuiCodes.LanguageFileSyntax, "Arrays are not allowed in a language file.", l, c, 1); Skip(); return null; }
                int start = _i;
                while (!Eof && Cur != ',' && Cur != '}' && !char.IsWhiteSpace(Cur)) Advance();
                string tok = _s.Substring(start, _i - start);
                if (tok == "null") { _d.Error(AuiCodes.LanguageFileSyntax, "null is not a translation; remove the key or give it text.", l, c, 4); return null; }
                if (tok == "true" || tok == "false") return tok;
                float f;
                if (AuiNum.TryParse(tok, out f)) return tok;
                _d.Error(AuiCodes.LanguageFileSyntax, "Unexpected '" + tok + "'; expected a string.", l, c, System.Math.Max(1, tok.Length));
                return null;
            }

            private string String()
            {
                int l = _line, c = _col;
                Advance(); // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (Eof) { _d.Error(AuiCodes.LanguageFileSyntax, "Unterminated string.", l, c, 1); return null; }
                    char ch = Cur;
                    if (ch == '"') { Advance(); return sb.ToString(); }
                    if (ch == '\n') { _d.Error(AuiCodes.LanguageFileSyntax, "Newline inside a string; use \\n.", _line, _col, 1); return null; }
                    if (ch == '\\')
                    {
                        Advance();
                        char e = Cur;
                        Advance();
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case 'r': sb.Append('\r'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'u':
                            {
                                if (_i + 4 > _s.Length) { _d.Error(AuiCodes.LanguageFileSyntax, "Bad \\u escape.", _line, _col, 1); return null; }
                                string hex = _s.Substring(_i, 4);
                                int code;
                                if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out code)) { _d.Error(AuiCodes.LanguageFileSyntax, "Bad \\u escape.", _line, _col, 4); return null; }
                                sb.Append((char)code);
                                for (int k = 0; k < 4; k++) Advance();
                                break;
                            }
                            default:
                                _d.Warning(AuiCodes.LanguageFileSyntax, "Unknown escape '\\" + e + "'.", _line, _col, 2);
                                sb.Append(e);
                                break;
                        }
                        continue;
                    }
                    sb.Append(ch);
                    Advance();
                }
            }
        }
    }
}
