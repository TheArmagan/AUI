using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Armagan.AUI.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Armagan.AUI.Lsp
{
    // ================================================================ references

    public sealed class ReferencesHandler : ReferencesHandlerBase
    {
        private readonly Workspace _ws;

        public ReferencesHandler(Workspace ws)
        {
            _ws = ws;
        }

        protected override ReferenceRegistrationOptions CreateRegistrationOptions(ReferenceCapability capability, ClientCapabilities clientCapabilities)
        {
            return new ReferenceRegistrationOptions { DocumentSelector = Sel.All };
        }

        public override Task<LocationContainer> Handle(ReferenceParams request, CancellationToken token)
        {
            string path = Workspace.PathOf(request.TextDocument.Uri);
            string text = _ws.Read(path) ?? "";
            Target t = TargetAt(_ws, path, text, request.Position);
            var list = new List<Location>();
            foreach (Occurrence o in Occurrences.Find(_ws, t, path)) list.Add(o.ToLocation());
            return Task.FromResult(new LocationContainer(list));
        }

        internal static Target TargetAt(Workspace ws, string path, string text, Position pos)
        {
            if (!Sel.IsLang(path)) return Occurrences.At(path, text, pos);
            int off = Cursor.Offset(text, pos.Line, pos.Character);
            string key = LangJson.KeyAt(text, off);
            if (key == null) return new Target { Kind = TargetKind.None };
            return new Target { Kind = TargetKind.Key, Name = key };
        }
    }

    // ================================================================ rename

    public sealed class PrepareRenameHandler : PrepareRenameHandlerBase
    {
        private readonly Workspace _ws;

        public PrepareRenameHandler(Workspace ws)
        {
            _ws = ws;
        }

        protected override RenameRegistrationOptions CreateRegistrationOptions(RenameCapability capability, ClientCapabilities clientCapabilities)
        {
            return new RenameRegistrationOptions { DocumentSelector = Sel.All, PrepareProvider = true };
        }

        public override Task<RangeOrPlaceholderRange> Handle(PrepareRenameParams request, CancellationToken token)
        {
            string path = Workspace.PathOf(request.TextDocument.Uri);
            string text = _ws.Read(path) ?? "";
            Target t = ReferencesHandler.TargetAt(_ws, path, text, request.Position);
            if (!RenameHandler.CanRename(t)) return Task.FromResult<RangeOrPlaceholderRange>(null);
            Range range = t.Range;
            if (range == null)
                foreach (Occurrence o in Occurrences.Find(_ws, t, path))
                    if (string.Equals(Path.GetFullPath(o.File), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) && Cursor.Contains(o.Range, request.Position)) { range = o.Range; break; }
            if (range == null) return Task.FromResult<RangeOrPlaceholderRange>(null);
            return Task.FromResult(new RangeOrPlaceholderRange(new PlaceholderRange { Range = range, Placeholder = t.Name }));
        }
    }

    public sealed class RenameHandler : RenameHandlerBase
    {
        private readonly Workspace _ws;

        public RenameHandler(Workspace ws)
        {
            _ws = ws;
        }

        protected override RenameRegistrationOptions CreateRegistrationOptions(RenameCapability capability, ClientCapabilities clientCapabilities)
        {
            return new RenameRegistrationOptions { DocumentSelector = Sel.All, PrepareProvider = true };
        }

        internal static bool CanRename(Target t)
        {
            switch (t.Kind)
            {
                case TargetKind.Class:
                case TargetKind.Key:
                case TargetKind.Binding:
                case TargetKind.Handler:
                case TargetKind.Id:
                case TargetKind.Variable:
                    return !string.IsNullOrEmpty(t.Name);
            }
            return false;
        }

        public override Task<WorkspaceEdit> Handle(RenameParams request, CancellationToken token)
        {
            string path = Workspace.PathOf(request.TextDocument.Uri);
            string text = _ws.Read(path) ?? "";
            Target t = ReferencesHandler.TargetAt(_ws, path, text, request.Position);
            string problem = null;
            string newName = request.NewName ?? "";
            string replacement = newName;
            if (!CanRename(t)) problem = "Nothing renameable here: classes, keys, bindings, handlers, ids and variables can be renamed.";
            else switch (t.Kind)
            {
                case TargetKind.Class:
                    if (!Regex.IsMatch(newName, @"^[A-Za-z_][A-Za-z0-9_-]*$")) problem = "A class name is letters, digits, '_' and '-'.";
                    break;
                case TargetKind.Variable:
                    if (!Regex.IsMatch(newName, @"^--[A-Za-z_][A-Za-z0-9_-]*$")) problem = "A variable name starts with '--'.";
                    break;
                case TargetKind.Binding:
                case TargetKind.Handler:
                    if (!AuiSchema.IsIdentifier(newName)) problem = "'" + newName + "' is not an identifier (letters, digits, underscore).";
                    break;
                case TargetKind.Id:
                    if (!AuiSchema.IsIdentifier(AuiNaming.Identifier(newName)) || newName.Length == 0) problem = "An id is a plain identifier.";
                    break;
                case TargetKind.Key:
                {
                    int dot = t.Name.LastIndexOf('.');
                    string oldPrefix = dot < 0 ? "" : t.Name.Substring(0, dot + 1);
                    if (!newName.StartsWith(oldPrefix) || newName.Length <= oldPrefix.Length || newName.Substring(oldPrefix.Length).IndexOf('.') >= 0)
                        problem = "Rename changes the last segment only: '" + oldPrefix + "<name>'. Move the key by hand to change its nesting.";
                    else replacement = newName.Substring(oldPrefix.Length); // the JSON token is the last segment; the view attribute is patched below
                    break;
                }
            }
            if (problem != null)
            {
                _ws.ShowError("AUI rename: " + problem);
                return Task.FromResult(new WorkspaceEdit { Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>() });
            }
            var changes = new Dictionary<DocumentUri, List<TextEdit>>();
            foreach (Occurrence o in Occurrences.Find(_ws, t, path))
            {
                DocumentUri uri = DocumentUri.FromFileSystemPath(o.File);
                List<TextEdit> list;
                if (!changes.TryGetValue(uri, out list)) changes[uri] = list = new List<TextEdit>();
                string newText = replacement;
                if (t.Kind == TargetKind.Key && !Sel.IsLang(o.File)) newText = newName;
                list.Add(new TextEdit { Range = o.Range, NewText = newText });
            }
            var result = new Dictionary<DocumentUri, IEnumerable<TextEdit>>();
            foreach (KeyValuePair<DocumentUri, List<TextEdit>> kv in changes) result[kv.Key] = kv.Value;
            return Task.FromResult(new WorkspaceEdit { Changes = result });
        }
    }

    // ================================================================ code actions

    public sealed class CodeActionHandler : CodeActionHandlerBase
    {
        private readonly Workspace _ws;
        private static readonly Regex Quoted = new Regex("'([^']+)'|<([A-Za-z][A-Za-z0-9_-]*)>");
        private static readonly Regex DidYouMean = new Regex(@"Did you mean (?:'([^']+)'|<([^>]+)>)\?");

        public CodeActionHandler(Workspace ws)
        {
            _ws = ws;
        }

        protected override CodeActionRegistrationOptions CreateRegistrationOptions(CodeActionCapability capability, ClientCapabilities clientCapabilities)
        {
            return new CodeActionRegistrationOptions
            {
                DocumentSelector = Sel.Both,
                CodeActionKinds = new Container<CodeActionKind>(CodeActionKind.QuickFix, CodeActionKind.Refactor),
                ResolveProvider = false,
            };
        }

        public override Task<CodeAction> Handle(CodeAction request, CancellationToken token)
        {
            return Task.FromResult(request);
        }

        public override Task<CommandOrCodeActionContainer> Handle(CodeActionParams request, CancellationToken token)
        {
            string path = Workspace.PathOf(request.TextDocument.Uri);
            string text = _ws.Read(path) ?? "";
            var actions = new List<CommandOrCodeAction>();
            AuiWorkspace ws = _ws.Scan();

            foreach (Diagnostic d in request.Context.Diagnostics)
            {
                if (d.Source != "aui") continue;
                string code = d.Code.HasValue && d.Code.Value.IsString ? d.Code.Value.String : null;
                if (code == null) continue;

                // ---- "did you mean": replace the wrong token with the hint
                Match hint = DidYouMean.Match(d.Message ?? "");
                if (hint.Success)
                {
                    string good = hint.Groups[1].Success ? hint.Groups[1].Value : hint.Groups[2].Value;
                    Match wrongM = Quoted.Match(d.Message);
                    string wrong = wrongM.Success ? (wrongM.Groups[1].Success ? wrongM.Groups[1].Value : wrongM.Groups[2].Value) : null;
                    if (good.StartsWith(":") && wrong != null && wrong.StartsWith(":")) { good = good.Substring(1); wrong = wrong.Substring(1); }
                    if (wrong != null)
                    {
                        Range where = FindToken(text, d.Range.Start, wrong);
                        if (where != null)
                            actions.Add(Fix("Change to '" + good + "'", d, request.TextDocument.Uri, new TextEdit { Range = where, NewText = good }, true));
                    }
                }

                switch (code)
                {
                    case AuiCodes.StyleSyntax:
                        if (d.Message.StartsWith("Missing ';'"))
                        {
                            int line = d.Range.Start.Line;
                            string lt = Cursor.LineText(text, line).TrimEnd();
                            actions.Add(Fix("Insert ';'", d, request.TextDocument.Uri, new TextEdit { Range = new Range(new Position(line, lt.Length), new Position(line, lt.Length)), NewText = ";" }, true));
                        }
                        break;
                    case AuiCodes.UnknownKey:
                    case AuiCodes.MissingTranslation:
                    case AuiCodes.MissingDefaultTranslation:
                        KeyFixes(code, d, ws, actions);
                        break;
                    case AuiCodes.NestedViewNotFound:
                    {
                        Match m = Quoted.Match(d.Message);
                        if (!m.Success) break;
                        string name = m.Groups[1].Value;
                        string file = Path.Combine(Path.GetDirectoryName(path) ?? "", name + ".aui");
                        if (File.Exists(file)) break;
                        string body = "<view name=\"" + name + "\" width=\"200\" height=\"100\">\n  \n</view>\n";
                        actions.Add(CreateFileAction("Create " + name + ".aui next to this view", d, file, body));
                        break;
                    }
                    case AuiCodes.StylesheetNotFound:
                    {
                        Match m = Quoted.Match(d.Message);
                        if (!m.Success) break;
                        string file = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path) ?? "", m.Groups[1].Value));
                        if (File.Exists(file)) break;
                        actions.Add(CreateFileAction("Create " + Path.GetFileName(file), d, file, "/* styles for " + Path.GetFileNameWithoutExtension(path) + " */\n"));
                        break;
                    }
                    case AuiCodes.UnusedClass:
                    {
                        // remove the whole rule when it has one selector
                        if (!Sel.IsSheet(path) && !Sel.IsView(path)) break;
                        RemoveRuleAction(text, d, request.TextDocument.Uri, actions);
                        break;
                    }
                    case AuiCodes.LiteralTextInTranslatedView:
                        break;
                }
            }

            // ---- create the handler method the caret's on* attribute names
            if (Sel.IsView(path))
            {
                Target t = Occurrences.At(path, text, request.Range.Start);
                if (t.Kind == TargetKind.Handler && CSharp.FindMethod(_ws, path, t.Name) == null)
                {
                    string file, ctext, indent;
                    int at;
                    if (CSharp.ClassEnd(_ws, path, out file, out ctext, out at, out indent))
                    {
                        AuiCompileResult r = _ws.EnsureCompiled(path);
                        AuiHandler h = r.Info.FindHandler(t.Name);
                        string sig = h != null ? h.Signature : "void " + t.Name + "()";
                        string method = indent + "public " + sig + "\n" + indent + "{\n" + indent + "    \n" + indent + "}\n\n";
                        Position pos = Cursor.PositionOf(ctext, at);
                        var edit = new WorkspaceEdit
                        {
                            Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>> { { DocumentUri.FromFileSystemPath(file), new[] { new TextEdit { Range = new Range(pos, pos), NewText = method } } } },
                        };
                        actions.Add(new CodeAction
                        {
                            Title = "Create '" + t.Name + "' in " + Path.GetFileName(file),
                            Kind = CodeActionKind.QuickFix,
                            Edit = edit,
                            Command = Reveal(file, pos.Line + 2, indent.Length + 4),
                        });
                    }
                }
            }
            return Task.FromResult(new CommandOrCodeActionContainer(actions));
        }

        private static CodeAction Fix(string title, Diagnostic d, DocumentUri uri, TextEdit edit, bool preferred)
        {
            return new CodeAction
            {
                Title = title,
                Kind = CodeActionKind.QuickFix,
                Diagnostics = new Container<Diagnostic>(d),
                IsPreferred = preferred,
                Edit = new WorkspaceEdit { Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>> { { uri, new[] { edit } } } },
            };
        }

        private static Command Reveal(string file, int line, int character)
        {
            return new Command { Name = "aui.reveal", Title = "Reveal", Arguments = new JArray(DocumentUri.FromFileSystemPath(file).ToString(), line, character) };
        }

        private static CodeAction CreateFileAction(string title, Diagnostic d, string file, string body)
        {
            DocumentUri uri = DocumentUri.FromFileSystemPath(file);
            var changes = new List<WorkspaceEditDocumentChange>
            {
                new WorkspaceEditDocumentChange(new CreateFile { Uri = uri, Options = new CreateFileOptions { IgnoreIfExists = true } }),
                new WorkspaceEditDocumentChange(new TextDocumentEdit
                {
                    TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = uri },
                    Edits = new TextEditContainer(new TextEdit { Range = new Range(new Position(0, 0), new Position(0, 0)), NewText = body }),
                }),
            };
            return new CodeAction
            {
                Title = title,
                Kind = CodeActionKind.QuickFix,
                Diagnostics = new Container<Diagnostic>(d),
                Edit = new WorkspaceEdit { DocumentChanges = new Container<WorkspaceEditDocumentChange>(changes) },
                Command = Reveal(file, 1, 2),
            };
        }

        /// <summary>The first occurrence of a token at or after a position (same line first, then the next few).</summary>
        private static Range FindToken(string text, Position from, string token)
        {
            int start = Cursor.Offset(text, from.Line, from.Character);
            int limit = Math.Min(text.Length, Cursor.Offset(text, from.Line + 3, 0));
            int i = start;
            while (i >= 0 && i < limit)
            {
                i = text.IndexOf(token, i, StringComparison.Ordinal);
                if (i < 0 || i >= limit) return null;
                bool leftOk = i == 0 || !IsTokenChar(text[i - 1]) || token.StartsWith("-") || token.StartsWith("<");
                bool rightOk = i + token.Length >= text.Length || !IsTokenChar(text[i + token.Length]);
                if (leftOk && rightOk) return Cursor.RangeOf(text, i, i + token.Length);
                i += token.Length;
            }
            return null;
        }

        private static bool IsTokenChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '-';
        }

        /// <summary>Create a missing key in the default language, in every language, or in the languages that lack it.</summary>
        private void KeyFixes(string code, Diagnostic d, AuiWorkspace ws, List<CommandOrCodeAction> actions)
        {
            Match m = Quoted.Match(d.Message ?? "");
            if (!m.Success) return;
            string key = m.Groups[1].Value;
            AuiLanguageTable table = _ws.Lang(ws);
            if (table.LanguageCount == 0) return;
            AuiLanguageFile def = table.Languages[table.DefaultLanguage];
            string defText = table.Get(table.DefaultLanguage, key) ?? "";
            var missing = new List<AuiLanguageFile>();
            foreach (AuiLanguageFile lf in table.Languages) if (string.IsNullOrEmpty(lf.Get(key))) missing.Add(lf);
            if (missing.Count == 0) return;

            if (code == AuiCodes.UnknownKey || code == AuiCodes.MissingDefaultTranslation)
            {
                if (missing.Contains(def))
                {
                    CodeAction one = KeyAction("Create '" + key + "' in " + Path.GetFileName(def.File), d, key, "", new[] { def }, def, true);
                    if (one != null) actions.Add(one);
                }
                if (missing.Count > 1)
                {
                    CodeAction all = KeyAction("Create '" + key + "' in every language (" + missing.Count + " files)", d, key, "", missing, def, false);
                    if (all != null) actions.Add(all);
                }
            }
            else
            {
                var names = new List<string>();
                foreach (AuiLanguageFile lf in missing) names.Add(Path.GetFileName(lf.File));
                CodeAction all = KeyAction("Add '" + key + "' to " + string.Join(", ", names) + " (empty, to translate)", d, key, "", missing, missing[0], true);
                if (all != null) actions.Add(all);
                if (defText.Length > 0)
                {
                    CodeAction copy = KeyAction("Add '" + key + "' to " + string.Join(", ", names) + " with the " + def.Code + " text", d, key, defText, missing, missing[0], false);
                    if (copy != null) actions.Add(copy);
                }
            }
        }

        private CodeAction KeyAction(string title, Diagnostic d, string key, string value, IEnumerable<AuiLanguageFile> files, AuiLanguageFile reveal, bool preferred)
        {
            var changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>();
            Command cmd = null;
            foreach (AuiLanguageFile lf in files)
            {
                string text = _ws.Read(lf.File);
                if (text == null) return null;
                Position caret;
                TextEdit edit = LangJson.Insert(text, key, value, out caret);
                if (edit == null) return null;
                changes[DocumentUri.FromFileSystemPath(lf.File)] = new[] { edit };
                if (lf == reveal) cmd = Reveal(lf.File, caret.Line, caret.Character);
            }
            return new CodeAction
            {
                Title = title,
                Kind = CodeActionKind.QuickFix,
                Diagnostics = new Container<Diagnostic>(d),
                IsPreferred = preferred,
                Edit = new WorkspaceEdit { Changes = changes },
                Command = cmd,
            };
        }

        private static void RemoveRuleAction(string text, Diagnostic d, DocumentUri uri, List<CommandOrCodeAction> actions)
        {
            int start = Cursor.Offset(text, d.Range.Start.Line, d.Range.Start.Character);
            int open = text.IndexOf('{', start);
            if (open < 0) return;
            string selectors = text.Substring(start, open - start);
            if (selectors.Contains(",")) return; // shared rule: the class is one of several selectors
            int depth = 0, end = -1;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) { end = i + 1; break;
                }
            }
            if (end < 0) return;
            // take the whole lines
            int ls = start; while (ls > 0 && text[ls - 1] != '\n') ls--;
            int le = end; while (le < text.Length && text[le] != '\n') le++;
            if (le < text.Length) le++;
            actions.Add(Fix("Remove unused rule", d, uri, new TextEdit { Range = Cursor.RangeOf(text, ls, le), NewText = "" }, false));
        }
    }

    // ================================================================ colors

    public sealed class DocumentColorHandler : DocumentColorHandlerBase
    {
        private readonly Workspace _ws;
        internal static readonly Regex ColorToken = new Regex(@"#[0-9A-Fa-f]{3,8}(?![0-9A-Fa-f])|rgba?\([^)]*\)|\b(?:white|black|transparent)\b");

        public DocumentColorHandler(Workspace ws)
        {
            _ws = ws;
        }

        protected override DocumentColorRegistrationOptions CreateRegistrationOptions(ColorProviderCapability capability, ClientCapabilities clientCapabilities)
        {
            return new DocumentColorRegistrationOptions { DocumentSelector = Sel.Both };
        }

        public override Task<Container<ColorInformation>> Handle(DocumentColorParams request, CancellationToken token)
        {
            string path = Workspace.PathOf(request.TextDocument.Uri);
            string text = _ws.Read(path) ?? "";
            var list = new List<ColorInformation>();
            foreach (Range r in ColorRanges(text, path))
            {
                string literal = text.Substring(Cursor.Offset(text, r.Start.Line, r.Start.Character), r.End.Character - r.Start.Character);
                AuiColor c;
                if (!AuiColor.TryParse(literal, out c)) continue;
                list.Add(new ColorInformation { Range = r, Color = new DocumentColor { Red = c.R, Green = c.G, Blue = c.B, Alpha = c.A } });
            }
            return Task.FromResult(new Container<ColorInformation>(list));
        }

        /// <summary>Colour literals in stylesheet text: the whole file for .auis, the style blocks and style="" values for .aui.</summary>
        internal static IEnumerable<Range> ColorRanges(string text, string path)
        {
            if (Sel.IsSheet(path))
            {
                foreach (Match m in ColorToken.Matches(text)) yield return Cursor.RangeOf(text, m.Index, m.Index + m.Length);
                yield break;
            }
            AuiMarkupDocument doc = AuiParser.Parse(text, path);
            foreach (AuiStyleBlock b in doc.Styles)
            {
                int baseOffset = Cursor.Offset(text, b.Line - 1, b.Column - 1);
                foreach (Match m in ColorToken.Matches(b.Text)) yield return Cursor.RangeOf(text, baseOffset + m.Index, baseOffset + m.Index + m.Length);
            }
            foreach (AuiNode n in doc.Nodes)
            {
                AuiAttribute a = n.FindAttribute("style");
                if (a == null) continue;
                foreach (Match m in ColorToken.Matches(a.Value)) yield return Cursor.RangeAt(a.ValueLine, a.ValueColumn + m.Index, m.Length);
            }
        }
    }

    public sealed class ColorPresentationHandler : ColorPresentationHandlerBase
    {
        public override Task<Container<ColorPresentation>> Handle(ColorPresentationParams request, CancellationToken token)
        {
            var c = new AuiColor((float)request.Color.Red, (float)request.Color.Green, (float)request.Color.Blue, (float)request.Color.Alpha);
            string hex = c.ToHex();
            string rgba = "rgba(" + Math.Round(c.R * 255) + ", " + Math.Round(c.G * 255) + ", " + Math.Round(c.B * 255) + ", " + AuiNum.Fmt(c.A) + ")";
            var list = new List<ColorPresentation>
            {
                new ColorPresentation { Label = hex, TextEdit = new TextEdit { Range = request.Range, NewText = hex } },
                new ColorPresentation { Label = rgba, TextEdit = new TextEdit { Range = request.Range, NewText = rgba } },
            };
            return Task.FromResult(new Container<ColorPresentation>(list));
        }
    }

    // ================================================================ inlay hints: the default-language text after key="..."

    public sealed class InlayHintsHandler : InlayHintsHandlerBase
    {
        private readonly Workspace _ws;

        public InlayHintsHandler(Workspace ws)
        {
            _ws = ws;
        }

        protected override InlayHintRegistrationOptions CreateRegistrationOptions(InlayHintClientCapabilities capability, ClientCapabilities clientCapabilities)
        {
            return new InlayHintRegistrationOptions { DocumentSelector = Sel.Views, ResolveProvider = false };
        }

        public override Task<InlayHint> Handle(InlayHint request, CancellationToken token)
        {
            return Task.FromResult(request);
        }

        public override Task<InlayHintContainer> Handle(InlayHintParams request, CancellationToken token)
        {
            string path = Workspace.PathOf(request.TextDocument.Uri);
            AuiCompileResult r = _ws.EnsureCompiled(path);
            AuiLanguageTable table = _ws.Lang(_ws.Scan());
            var list = new List<InlayHint>();
            if (r.Info == null) return Task.FromResult(new InlayHintContainer(list));
            foreach (AuiKeyUse use in r.Info.Keys)
            {
                AuiAttribute a = use.Attribute;
                var pos = new Position(a.ValueLine - 1, a.ValueColumn - 1 + a.Value.Length + 1);
                if (!Cursor.Contains(request.Range, pos)) continue;
                string v = table.GetWithFallback(table.DefaultLanguage, use.Key);
                if (string.IsNullOrEmpty(v)) continue;
                string label = v.Replace("\n", " ");
                if (label.Length > 40) label = label.Substring(0, 39) + "…";
                var tip = new System.Text.StringBuilder();
                for (int i = 0; i < table.LanguageCount; i++) tip.Append("- `").Append(table.Languages[i].Code).Append("` ").Append(table.Get(i, use.Key) ?? "*(missing)*").Append('\n');
                list.Add(new InlayHint
                {
                    Position = pos,
                    Label = new StringOrInlayHintLabelParts(label),
                    Kind = InlayHintKind.Parameter,
                    PaddingLeft = true,
                    Tooltip = new StringOrMarkupContent(new MarkupContent { Kind = MarkupKind.Markdown, Value = tip.ToString() }),
                });
            }
            return Task.FromResult(new InlayHintContainer(list));
        }
    }

    // ================================================================ document links: <link href> and <image src>

    public sealed class DocumentLinkHandler : DocumentLinkHandlerBase
    {
        private readonly Workspace _ws;

        public DocumentLinkHandler(Workspace ws)
        {
            _ws = ws;
        }

        protected override DocumentLinkRegistrationOptions CreateRegistrationOptions(DocumentLinkCapability capability, ClientCapabilities clientCapabilities)
        {
            return new DocumentLinkRegistrationOptions { DocumentSelector = Sel.Views, ResolveProvider = false };
        }

        public override Task<DocumentLink> Handle(DocumentLink request, CancellationToken token)
        {
            return Task.FromResult(request);
        }

        public override Task<DocumentLinkContainer> Handle(DocumentLinkParams request, CancellationToken token)
        {
            string path = Workspace.PathOf(request.TextDocument.Uri);
            string text = _ws.Read(path) ?? "";
            string dir = Path.GetDirectoryName(path) ?? "";
            var list = new List<DocumentLink>();
            var rx = new Regex(@"<(link|image)\b[^>]*?\b(href|src)\s*=\s*""([^""]+)""");
            foreach (Match m in rx.Matches(text))
            {
                Group g = m.Groups[3];
                string target = Path.GetFullPath(Path.Combine(dir, g.Value));
                if (!File.Exists(target)) continue;
                list.Add(new DocumentLink { Range = Cursor.RangeOf(text, g.Index, g.Index + g.Length), Target = DocumentUri.FromFileSystemPath(target), Tooltip = "Open " + Path.GetFileName(target) });
            }
            return Task.FromResult(new DocumentLinkContainer(list));
        }
    }

    // ================================================================ workspace symbols: views, classes, keys

    public sealed class WorkspaceSymbolsHandler : WorkspaceSymbolsHandlerBase
    {
        private readonly Workspace _ws;

        public WorkspaceSymbolsHandler(Workspace ws)
        {
            _ws = ws;
        }

        protected override WorkspaceSymbolRegistrationOptions CreateRegistrationOptions(WorkspaceSymbolCapability capability, ClientCapabilities clientCapabilities)
        {
            return new WorkspaceSymbolRegistrationOptions();
        }

        public override Task<Container<WorkspaceSymbol>> Handle(WorkspaceSymbolParams request, CancellationToken token)
        {
            string q = (request.Query ?? "").Trim();
            AuiWorkspace ws = _ws.Scan();
            var list = new List<WorkspaceSymbol>();
            foreach (KeyValuePair<string, string> v in ws.Views)
                if (Matches(v.Key, q)) list.Add(new WorkspaceSymbol { Name = v.Key, Kind = SymbolKind.Module, ContainerName = "view", Location = new Location { Uri = DocumentUri.FromFileSystemPath(v.Value), Range = new Range(new Position(0, 0), new Position(0, 0)) } });
            var seen = new HashSet<string>();
            foreach (AuiStyleSheet s in CompletionHandler.Sheets(_ws, ws, null))
                foreach (AuiStyleRule rule in s.Rules)
                    foreach (AuiSelector sel in rule.Selectors)
                        if (sel.Class != null && seen.Add(sel.Class) && Matches(sel.Class, q))
                            list.Add(new WorkspaceSymbol { Name = "." + sel.Class, Kind = SymbolKind.Class, ContainerName = Path.GetFileName(s.File), Location = new Location { Uri = DocumentUri.FromFileSystemPath(s.File), Range = Cursor.RangeAt(rule.Line, rule.Column, sel.Class.Length + 1) } });
            AuiLanguageTable table = _ws.Lang(ws);
            if (table.LanguageCount > 0)
            {
                AuiLanguageFile def = table.Languages[table.DefaultLanguage];
                foreach (string k in table.Keys)
                {
                    if (!Matches(k, q)) continue;
                    AuiJson.Pos pos;
                    AuiLanguageFile lf = def.Data.Positions.ContainsKey(k) ? def : null;
                    if (lf == null) foreach (AuiLanguageFile other in table.Languages) if (other.Data.Positions.ContainsKey(k)) { lf = other; break; }
                    if (lf == null || !lf.Data.Positions.TryGetValue(k, out pos)) continue;
                    string file = pos.File ?? lf.File;
                    list.Add(new WorkspaceSymbol { Name = k, Kind = SymbolKind.String, ContainerName = Path.GetFileName(file), Location = new Location { Uri = DocumentUri.FromFileSystemPath(file), Range = Cursor.RangeAt(pos.Line, pos.Column, 0) } });
                }
            }
            return Task.FromResult(new Container<WorkspaceSymbol>(list));
        }

        private static bool Matches(string name, string q)
        {
            return q.Length == 0 || name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    // ================================================================ custom: the generated C# for a view

    public sealed class GeneratedParams
    {
        [JsonProperty("uri")] public string Uri;
    }

    public sealed class GeneratedResult
    {
        [JsonProperty("text")] public string Text;
        [JsonProperty("hasErrors")] public bool HasErrors;
    }

    public static class Generated
    {
        public static Task<GeneratedResult> Handle(Workspace ws, GeneratedParams p)
        {
            string path = DocumentUri.Parse(p.Uri).GetFileSystemPath();
            AuiCompileResult r = ws.EnsureCompiled(path);
            string text = r.GeneratedSource;
            if (text == null)
            {
                var sb = new System.Text.StringBuilder("// No code: the view does not compile.\n");
                foreach (AuiDiagnostic d in r.Diagnostics.Items) if (d.Severity == AuiSeverity.Error) sb.Append("// ").Append(d).Append('\n');
                text = sb.ToString();
            }
            return Task.FromResult(new GeneratedResult { Text = text, HasErrors = r.HasErrors });
        }
    }
}
