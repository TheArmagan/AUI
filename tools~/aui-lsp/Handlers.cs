using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Armagan.AUI.Core;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Armagan.AUI.Lsp
{
    internal static class Sel
    {
        public static readonly TextDocumentSelector Both = TextDocumentSelector.ForPattern("**/*.aui", "**/*.auis");
    }

    // ================================================================ sync

    public sealed class TextDocumentHandler : TextDocumentSyncHandlerBase
    {
        private readonly Workspace _ws;

        public TextDocumentHandler(Workspace ws)
        {
            _ws = ws;
        }

        public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
        {
            string p = uri.GetFileSystemPath();
            return new TextDocumentAttributes(uri, p.EndsWith(".auis", StringComparison.OrdinalIgnoreCase) ? "auis" : "aui");
        }

        protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
        {
            return new TextDocumentSyncRegistrationOptions
            {
                DocumentSelector = Sel.Both,
                Change = TextDocumentSyncKind.Full,
                Save = new SaveOptions { IncludeText = true },
            };
        }

        public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken token)
        {
            string p = Workspace.PathOf(request.TextDocument.Uri);
            _ws.Open(p, request.TextDocument.Text);
            Recheck(p);
            return Unit.Task;
        }

        public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken token)
        {
            string p = Workspace.PathOf(request.TextDocument.Uri);
            foreach (TextDocumentContentChangeEvent ch in request.ContentChanges) _ws.Change(p, ch.Text);
            Recheck(p);
            return Unit.Task;
        }

        public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken token)
        {
            string p = Workspace.PathOf(request.TextDocument.Uri);
            if (request.Text != null) _ws.Change(p, request.Text);
            Recheck(p);
            return Unit.Task;
        }

        public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken token)
        {
            _ws.Close(Workspace.PathOf(request.TextDocument.Uri));
            return Unit.Task;
        }

        private void Recheck(string path)
        {
            if (path.EndsWith(".aui", StringComparison.OrdinalIgnoreCase)) _ws.CompileAndPublish(path);
            else _ws.RecompileDependents(path);
        }
    }

    // ================================================================ watched files (lang/*.json, sheets edited outside)

    public sealed class WatchedFilesHandler : DidChangeWatchedFilesHandlerBase
    {
        private readonly Workspace _ws;

        public WatchedFilesHandler(Workspace ws)
        {
            _ws = ws;
        }

        protected override DidChangeWatchedFilesRegistrationOptions CreateRegistrationOptions(DidChangeWatchedFilesCapability capability, ClientCapabilities clientCapabilities)
        {
            return new DidChangeWatchedFilesRegistrationOptions
            {
                Watchers = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher>(
                    new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher { GlobPattern = "**/lang/*.json" },
                    new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher { GlobPattern = "**/*.auis" },
                    new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher { GlobPattern = "**/*.aui" }),
            };
        }

        public override Task<Unit> Handle(DidChangeWatchedFilesParams request, CancellationToken token)
        {
            foreach (FileEvent e in request.Changes)
            {
                string p = Workspace.PathOf(e.Uri);
                if (_ws.IsOpen(p)) continue; // the open document already drives its own rechecks
                if (p.EndsWith(".aui", StringComparison.OrdinalIgnoreCase)) { if (File.Exists(p)) _ws.CompileAndPublish(p); }
                else _ws.RecompileDependents(p);
            }
            return Unit.Task;
        }
    }

    // ================================================================ completion

    public sealed class CompletionHandler : CompletionHandlerBase
    {
        private readonly Workspace _ws;

        public CompletionHandler(Workspace ws)
        {
            _ws = ws;
        }

        protected override CompletionRegistrationOptions CreateRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities)
        {
            return new CompletionRegistrationOptions
            {
                DocumentSelector = Sel.Both,
                TriggerCharacters = new Container<string>("<", " ", "\"", "{", ":", "-", ".", "("),
                ResolveProvider = false,
            };
        }

        public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken token)
        {
            return Task.FromResult(request);
        }

        public override Task<CompletionList> Handle(CompletionParams request, CancellationToken token)
        {
            string path = Workspace.PathOf(request.TextDocument.Uri);
            string text = _ws.Read(path) ?? "";
            int offset = Cursor.Offset(text, request.Position.Line, request.Position.Character);
            bool sheet = path.EndsWith(".auis", StringComparison.OrdinalIgnoreCase);
            Cursor c = sheet ? Cursor.Style(text, offset) : Cursor.Markup(text, offset);
            var items = new List<CompletionItem>();
            AuiWorkspace ws = _ws.Scan();
            AuiCompileResult r = sheet ? null : (_ws.Result(path) ?? _ws.Compile(path));

            switch (c.Where)
            {
                case Cursor.Kind.TagName:
                    foreach (AuiElementDef e in AuiSchema.Elements) items.Add(Item(e.Name, CompletionItemKind.Class, e.Doc));
                    foreach (string v in ws.Views.Keys) if (v != Path.GetFileNameWithoutExtension(path)) items.Add(Item(v, CompletionItemKind.Module, "nested view " + v + ".aui"));
                    break;
                case Cursor.Kind.AttributeName:
                {
                    AuiElementDef def = AuiSchema.FindElement(c.Tag);
                    if (def != null)
                    {
                        foreach (string a in def.Attributes) items.Add(Item(a, CompletionItemKind.Property, null, a + "=\"$0\""));
                        foreach (string ev in def.Events) items.Add(Item(ev, CompletionItemKind.Event, "handler method on the view", ev + "=\"$0\""));
                    }
                    else if (c.Tag.Length > 0 && char.IsUpper(c.Tag[0]))
                    {
                        AuiViewInfo child = ChildInfo(ws, c.Tag);
                        if (child != null) foreach (AuiBinding b in child.Bindings) items.Add(Item(b.Name, CompletionItemKind.Property, "child binding (" + b.CsType + ")", b.Name + "=\"$0\""));
                    }
                    foreach (string a in AuiSchema.CommonAttributes) items.Add(Item(a, CompletionItemKind.Property, null, a + "=\"$0\""));
                    break;
                }
                case Cursor.Kind.AttributeValue:
                    AttributeValues(c, r, ws, items);
                    break;
                case Cursor.Kind.StyleSelector:
                    foreach (AuiElementDef e in AuiSchema.Elements) items.Add(Item(e.Name, CompletionItemKind.Class, e.Doc));
                    foreach (string cls in AllClasses(ws)) items.Add(Item("." + cls, CompletionItemKind.Field, null));
                    foreach (string st in AuiSchema.States) items.Add(Item(":" + st, CompletionItemKind.EnumMember, "pointer state"));
                    items.Add(Item(":root", CompletionItemKind.Keyword, "variables block"));
                    break;
                case Cursor.Kind.StyleProperty:
                    foreach (AuiPropDef p in AuiSchema.Props) items.Add(Item(p.Name, CompletionItemKind.Property, p.Doc ?? p.Kind.ToString(), p.Name + ": $0;"));
                    break;
                case Cursor.Kind.StyleValue:
                {
                    AuiPropDef p = AuiSchema.FindProp(c.Property);
                    if (p != null && p.EnumValues != null) foreach (string v in p.EnumValues) items.Add(Item(v, CompletionItemKind.EnumMember, null));
                    if (p != null && (p.Kind == AuiPropKind.Color || p.Kind == AuiPropKind.Background))
                    {
                        AuiStyleSheet theme = ws.LoadTheme(_ws.Read);
                        if (theme != null) foreach (AuiVariable v in theme.Variables) items.Add(Item("var(" + v.Name + ")", CompletionItemKind.Variable, v.Value));
                        if (p.Kind == AuiPropKind.Background)
                        {
                            items.Add(Item("none", CompletionItemKind.EnumMember, null));
                            foreach (string s in new[] { "Rounded4", "Rounded8", "Rounded12", "Rounded16", "Rounded24", "Square" }) items.Add(Item("sprite(" + s + ")", CompletionItemKind.Value, "9-slice sprite"));
                        }
                    }
                    if (p != null && p.Kind == AuiPropKind.Transition)
                    {
                        foreach (AuiPropDef a in AuiSchema.Props) if (a.Animatable) items.Add(Item(a.Name, CompletionItemKind.Property, "animatable"));
                        foreach (string e in AuiSchema.Eases) items.Add(Item(e, CompletionItemKind.EnumMember, "ease"));
                    }
                    break;
                }
            }
            return Task.FromResult(new CompletionList(items));
        }

        private void AttributeValues(Cursor c, AuiCompileResult r, AuiWorkspace ws, List<CompletionItem> items)
        {
            switch (c.Attribute)
            {
                case "key":
                case "placeholder-key":
                case "format-key":
                {
                    AuiLanguageTable t = ws.LoadLanguageTable("en", null, _ws.Read);
                    foreach (string k in t.Keys) items.Add(Item(k, CompletionItemKind.Text, t.GetWithFallback(t.DefaultLanguage, k)));
                    break;
                }
                case "class":
                    foreach (string cls in AllClasses(ws)) items.Add(Item(cls, CompletionItemKind.Field, null));
                    break;
                case "icon":
                case "name":
                    if (c.Attribute == "icon" || c.Tag == "icon") foreach (string n in ws.IconNames()) items.Add(Item(n, CompletionItemKind.Value, "icon"));
                    break;
                case "page":
                case "for":
                case "default":
                    if (r != null)
                        foreach (AuiPageGroup g in r.Info.PageGroups)
                        {
                            if (c.Attribute == "for") items.Add(Item(g.Id, CompletionItemKind.Reference, "pages group"));
                            else foreach (AuiNode pg in g.Pages) if (pg.Id != null) items.Add(Item(pg.Id, CompletionItemKind.Reference, "page"));
                        }
                    break;
                case "mode":
                    foreach (string v in c.Tag == "image" ? new[] { "simple", "sliced", "fit" } : new[] { "crossfade", "sequential" }) items.Add(Item(v, CompletionItemKind.EnumMember, null));
                    break;
                case "show":
                    foreach (string v in AuiSchema.FindProp("show").EnumValues) items.Add(Item(v, CompletionItemKind.EnumMember, null));
                    break;
                default:
                    if (c.Attribute == "text" || c.Attribute == "bind" || c.Attribute == "if" || c.Attribute == "disabled" || c.Attribute == "selected" || c.Attribute == "args")
                    {
                        if (r != null)
                        {
                            foreach (AuiBinding b in r.Info.Bindings) items.Add(Item("{" + b.Name + "}", CompletionItemKind.Variable, b.CsType));
                            foreach (AuiRepeatInfo rep in r.Info.Repeats) foreach (AuiBinding b in rep.ItemBindings) items.Add(Item("{item." + b.Name + "}", CompletionItemKind.Variable, b.CsType + " (row)"));
                        }
                    }
                    else if (c.Attribute != null && c.Attribute.StartsWith("on") && r != null)
                    {
                        foreach (AuiHandler h in r.Info.Handlers) items.Add(Item(h.Name, CompletionItemKind.Method, h.Signature));
                    }
                    break;
            }
        }

        internal static AuiViewInfo ChildInfo(AuiWorkspace ws, string name)
        {
            string cp;
            if (!ws.Views.TryGetValue(name, out cp) || !File.Exists(cp)) return null;
            return AuiAnalyzer.Analyze(AuiParser.Parse(File.ReadAllText(cp), cp), new AuiAnalysisContext(), new AuiDiagnosticBag(cp));
        }

        internal static IEnumerable<string> AllClasses(AuiWorkspace ws)
        {
            var seen = new HashSet<string>();
            AuiStyleSheet theme = ws.LoadTheme();
            if (theme != null) foreach (string c in theme.ClassNames()) if (seen.Add(c)) yield return c;
            foreach (string p in ws.StylePaths)
            {
                AuiStyleSheet s = AuiStyleParser.Parse(File.ReadAllText(p), p, AuiStyleTier.Linked);
                foreach (string c in s.ClassNames()) if (seen.Add(c)) yield return c;
            }
        }

        private static CompletionItem Item(string label, CompletionItemKind kind, string detail, string snippet = null)
        {
            if (snippet == null) return new CompletionItem { Label = label, Kind = kind, Detail = detail };
            return new CompletionItem { Label = label, Kind = kind, Detail = detail, InsertText = snippet, InsertTextFormat = InsertTextFormat.Snippet };
        }
    }

    // ================================================================ hover

    public sealed class HoverHandler : HoverHandlerBase
    {
        private readonly Workspace _ws;

        public HoverHandler(Workspace ws)
        {
            _ws = ws;
        }

        protected override HoverRegistrationOptions CreateRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities)
        {
            return new HoverRegistrationOptions { DocumentSelector = Sel.Both };
        }

        public override Task<Hover> Handle(HoverParams request, CancellationToken token)
        {
            string path = Workspace.PathOf(request.TextDocument.Uri);
            string text = _ws.Read(path) ?? "";
            int offset = Cursor.Offset(text, request.Position.Line, request.Position.Character);
            int ws0, we;
            string word = Cursor.WordAt(text, offset, out ws0, out we);
            if (word.Length == 0) return Task.FromResult<Hover>(null);
            AuiWorkspace ws = _ws.Scan();
            string md = null;

            // translation key
            AuiLanguageTable table = ws.LoadLanguageTable("en", null, _ws.Read);
            if (table.IndexOfKey(word) >= 0)
            {
                var sb = new System.Text.StringBuilder("**" + word + "**\n\n");
                for (int i = 0; i < table.LanguageCount; i++)
                {
                    string v = table.Get(i, word);
                    sb.Append("- `").Append(table.Languages[i].Code).Append("` ").Append(v ?? "*(missing)*").Append('\n');
                }
                md = sb.ToString();
            }
            else if (word.StartsWith("--"))
            {
                AuiStyleSheet theme = ws.LoadTheme(_ws.Read);
                AuiVariable v = theme != null ? theme.Variables.Find(x => x.Name == word) : null;
                if (v != null) md = "`" + word + "`: " + v.Value;
            }
            else if (AuiSchema.FindElement(word) != null)
            {
                AuiElementDef e = AuiSchema.FindElement(word);
                md = "**<" + e.Name + ">** " + (e.Doc ?? "") + "\n\nattributes: " + string.Join(", ", e.Attributes) + (e.Events.Length > 0 ? "\n\nevents: " + string.Join(", ", e.Events) : "");
            }
            else if (AuiSchema.FindProp(word) != null)
            {
                AuiPropDef p = AuiSchema.FindProp(word);
                md = "**" + p.Name + "** (" + p.Kind + (p.Inherited ? ", inherited" : "") + (p.Animatable ? ", animatable" : "") + ")" + (p.Doc != null ? "\n\n" + p.Doc : "") + (p.EnumValues != null ? "\n\n" + string.Join(" | ", p.EnumValues) : "");
            }
            else
            {
                // class: show its declarations from every sheet
                var sb = new System.Text.StringBuilder();
                foreach (AuiStyleSheet s in Sheets(ws))
                    foreach (AuiStyleRule rule in s.Rules)
                        foreach (AuiSelector sel in rule.Selectors)
                            if (sel.Class == word)
                            {
                                sb.Append("`").Append(sel).Append("` (").Append(Path.GetFileName(s.File)).Append(")\n");
                                foreach (AuiDeclaration d in rule.Declarations) sb.Append("- ").Append(d.Property).Append(": ").Append(d.Value).Append('\n');
                                sb.Append('\n');
                            }
                if (sb.Length > 0) md = sb.ToString();
            }
            if (md == null) return Task.FromResult<Hover>(null);
            return Task.FromResult(new Hover { Contents = new MarkedStringsOrMarkupContent(new MarkupContent { Kind = MarkupKind.Markdown, Value = md }) });
        }

        internal IEnumerable<AuiStyleSheet> Sheets(AuiWorkspace ws)
        {
            AuiStyleSheet theme = ws.LoadTheme(_ws.Read);
            if (theme != null) yield return theme;
            foreach (string p in ws.StylePaths)
            {
                string t = _ws.Read(p);
                if (t != null) yield return AuiStyleParser.Parse(t, p, AuiStyleTier.Linked);
            }
        }
    }

    // ================================================================ definition

    public sealed class DefinitionHandler : DefinitionHandlerBase
    {
        private readonly Workspace _ws;

        public DefinitionHandler(Workspace ws)
        {
            _ws = ws;
        }

        protected override DefinitionRegistrationOptions CreateRegistrationOptions(DefinitionCapability capability, ClientCapabilities clientCapabilities)
        {
            return new DefinitionRegistrationOptions { DocumentSelector = Sel.Both };
        }

        public override Task<LocationOrLocationLinks> Handle(DefinitionParams request, CancellationToken token)
        {
            string path = Workspace.PathOf(request.TextDocument.Uri);
            string text = _ws.Read(path) ?? "";
            int offset = Cursor.Offset(text, request.Position.Line, request.Position.Character);
            int s, e;
            string word = Cursor.WordAt(text, offset, out s, out e);
            var locations = new List<LocationOrLocationLink>();
            if (word.Length == 0) return Task.FromResult(new LocationOrLocationLinks(locations));
            AuiWorkspace ws = _ws.Scan();

            // nested view
            string vp;
            if (ws.Views.TryGetValue(word, out vp))
                locations.Add(new Location { Uri = DocumentUri.FromFileSystemPath(vp), Range = new Range(new Position(0, 0), new Position(0, 0)) });

            // translation key: every language file that defines it
            AuiLanguageTable table = ws.LoadLanguageTable("en", null, _ws.Read);
            if (table.IndexOfKey(word) >= 0)
                foreach (AuiLanguageFile lf in table.Languages)
                {
                    AuiJson.Pos pos;
                    if (!lf.Data.Positions.TryGetValue(word, out pos)) continue;
                    locations.Add(new Location { Uri = DocumentUri.FromFileSystemPath(lf.File), Range = new Range(new Position(Math.Max(0, pos.Line - 1), Math.Max(0, pos.Column - 1)), new Position(Math.Max(0, pos.Line - 1), Math.Max(0, pos.Column - 1))) });
                }

            // class or variable: rule positions in sheets
            AuiStyleSheet theme = ws.LoadTheme(_ws.Read);
            var sheets = new List<AuiStyleSheet>();
            if (theme != null) sheets.Add(theme);
            foreach (string p in ws.StylePaths) { string t = _ws.Read(p); if (t != null) sheets.Add(AuiStyleParser.Parse(t, p, AuiStyleTier.Linked)); }
            foreach (AuiStyleSheet sheet in sheets)
            {
                foreach (AuiStyleRule rule in sheet.Rules)
                    foreach (AuiSelector sel in rule.Selectors)
                        if (sel.Class == word || (sel.Tag == word && sel.Class == null))
                            locations.Add(new Location { Uri = DocumentUri.FromFileSystemPath(sheet.File), Range = new Range(new Position(rule.Line - 1, rule.Column - 1), new Position(rule.Line - 1, rule.Column - 1)) });
                foreach (AuiVariable v in sheet.Variables)
                    if (v.Name == word)
                        locations.Add(new Location { Uri = DocumentUri.FromFileSystemPath(sheet.File), Range = new Range(new Position(v.Line - 1, v.Column - 1), new Position(v.Line - 1, v.Column - 1)) });
            }
            return Task.FromResult(new LocationOrLocationLinks(locations));
        }
    }

    // ================================================================ symbols

    public sealed class SymbolHandler : DocumentSymbolHandlerBase
    {
        private readonly Workspace _ws;

        public SymbolHandler(Workspace ws)
        {
            _ws = ws;
        }

        protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(DocumentSymbolCapability capability, ClientCapabilities clientCapabilities)
        {
            return new DocumentSymbolRegistrationOptions { DocumentSelector = TextDocumentSelector.ForPattern("**/*.aui") };
        }

        public override Task<SymbolInformationOrDocumentSymbolContainer> Handle(DocumentSymbolParams request, CancellationToken token)
        {
            string path = Workspace.PathOf(request.TextDocument.Uri);
            AuiMarkupDocument doc = AuiParser.Parse(_ws.Read(path) ?? "", path);
            var list = new List<SymbolInformationOrDocumentSymbol>();
            if (doc.Root != null) list.Add(new SymbolInformationOrDocumentSymbol(Symbol(doc.Root)));
            return Task.FromResult(new SymbolInformationOrDocumentSymbolContainer(list));
        }

        private static DocumentSymbol Symbol(AuiNode n)
        {
            var range = new Range(new Position(Math.Max(0, n.Line - 1), Math.Max(0, n.Column - 1)), new Position(Math.Max(0, n.EndLine - 1), Math.Max(0, n.EndColumn - 1)));
            var children = new List<DocumentSymbol>();
            foreach (AuiNode c in n.Children) children.Add(Symbol(c));
            string name = n.Tag + (n.Id != null ? "#" + n.Id : "") + (n.Get("key") != null ? " " + n.Get("key") : "");
            return new DocumentSymbol
            {
                Name = name,
                Kind = n.IsComponentTag ? SymbolKind.Module : n.Tag == "repeat" ? SymbolKind.Array : SymbolKind.Object,
                Range = range,
                SelectionRange = new Range(range.Start, new Position(range.Start.Line, range.Start.Character + n.Tag.Length + 1)),
                Children = new Container<DocumentSymbol>(children),
            };
        }
    }
}
