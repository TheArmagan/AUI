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
        public const string LangGlob = "**/{lang,Lang}/*.json";
        public static readonly TextDocumentSelector Both = TextDocumentSelector.ForPattern("**/*.aui", "**/*.auis");
        public static readonly TextDocumentSelector Views = TextDocumentSelector.ForPattern("**/*.aui");
        public static readonly TextDocumentSelector All = TextDocumentSelector.ForPattern("**/*.aui", "**/*.auis", LangGlob);

        public static bool IsView(string p) { return p.EndsWith(".aui", StringComparison.OrdinalIgnoreCase); }
        public static bool IsSheet(string p) { return p.EndsWith(".auis", StringComparison.OrdinalIgnoreCase); }
        public static bool IsLang(string p) { return p.EndsWith(".json", StringComparison.OrdinalIgnoreCase); }
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
            return new TextDocumentAttributes(uri, Sel.IsSheet(p) ? "auis" : Sel.IsLang(p) ? "json" : "aui");
        }

        protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
        {
            return new TextDocumentSyncRegistrationOptions
            {
                DocumentSelector = Sel.All,
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
            _ws.ScheduleRecheck(p, Recheck);
            return Unit.Task;
        }

        public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken token)
        {
            string p = Workspace.PathOf(request.TextDocument.Uri);
            _ws.CancelTimer(Path.GetFullPath(p));
            if (request.Text != null) _ws.Change(p, request.Text);
            Recheck(p);
            return Unit.Task;
        }

        public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken token)
        {
            string p = Workspace.PathOf(request.TextDocument.Uri);
            _ws.Close(p);
            if (File.Exists(p)) Recheck(p); else _ws.Forget(p);
            return Unit.Task;
        }

        private void Recheck(string path)
        {
            if (Sel.IsView(path)) _ws.CompileAndPublish(path);
            else _ws.RecompileDependents(path);
        }
    }

    // ================================================================ watched files (lang/*.json, sheets and views edited outside)

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
                    new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher { GlobPattern = Sel.LangGlob },
                    new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher { GlobPattern = "**/*.auis" },
                    new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher { GlobPattern = "**/*.aui" },
                    new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher { GlobPattern = "**/*.svg" }),
            };
        }

        public override Task<Unit> Handle(DidChangeWatchedFilesParams request, CancellationToken token)
        {
            bool rescanned = false;
            foreach (FileEvent e in request.Changes)
            {
                string p = Workspace.PathOf(e.Uri);
                bool appeared = e.Type == FileChangeType.Created, gone = e.Type == FileChangeType.Deleted;
                if ((appeared || gone) && !rescanned) { _ws.InvalidateScan(); rescanned = true; }
                if (Sel.IsView(p))
                {
                    if (gone) { _ws.Forget(p); _ws.RecompileUsersOf(Path.GetFileNameWithoutExtension(p)); continue; }
                    if (appeared) _ws.RecompileUsersOf(Path.GetFileNameWithoutExtension(p));
                    if (!_ws.IsOpen(p) && File.Exists(p)) _ws.CompileAndPublish(p);
                }
                else if (Sel.IsSheet(p))
                {
                    if (gone) _ws.Forget(p);
                    if (!_ws.IsOpen(p) || gone) _ws.RecompileDependents(p);
                }
                else if (Sel.IsLang(p))
                {
                    if (gone) _ws.Forget(p);
                    if (!_ws.IsOpen(p) || gone) _ws.RecompileDependents(p);
                }
                else if (p.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) && (appeared || gone))
                {
                    _ws.RecompileAll();
                }
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
                TriggerCharacters = new Container<string>("<", " ", "\"", "{", ":", "-", ".", "(", "/"),
                ResolveProvider = false,
            };
        }

        public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken token)
        {
            return Task.FromResult(request);
        }

        /// <summary>One completion request: the text, the caret and the replace range every item shares by default.</summary>
        private sealed class Ctx
        {
            public string Text;
            public int Offset;
            public int ReplaceStart;
            public readonly List<CompletionItem> Items = new List<CompletionItem>();

            public CompletionItem Add(string label, CompletionItemKind kind, string detail, string insert = null, bool snippet = false, int replaceStart = -1, string doc = null, string sort = null)
            {
                var item = new CompletionItem
                {
                    Label = label,
                    Kind = kind,
                    Detail = detail,
                    SortText = sort,
                    FilterText = label,
                    InsertTextFormat = snippet ? InsertTextFormat.Snippet : InsertTextFormat.PlainText,
                    TextEdit = new TextEdit { Range = Cursor.RangeOf(Text, replaceStart >= 0 ? replaceStart : ReplaceStart, Offset), NewText = insert ?? label },
                    Documentation = doc == null ? null : new StringOrMarkupContent(new MarkupContent { Kind = MarkupKind.Markdown, Value = doc }),
                };
                Items.Add(item);
                return item;
            }
        }

        public override Task<CompletionList> Handle(CompletionParams request, CancellationToken token)
        {
            string path = Workspace.PathOf(request.TextDocument.Uri);
            string text = _ws.Read(path) ?? "";
            int offset = Cursor.Offset(text, request.Position.Line, request.Position.Character);
            bool sheet = Sel.IsSheet(path);
            Cursor c = sheet ? Cursor.Style(text, offset) : Cursor.Markup(text, offset);
            var ctx = new Ctx { Text = text, Offset = offset, ReplaceStart = c.TokenStart };
            AuiWorkspace ws = _ws.Scan();
            AuiCompileResult r = sheet ? null : _ws.EnsureCompiled(path);
            char after = Cursor.AfterCaret(text, offset);

            switch (c.Where)
            {
                case Cursor.Kind.TagName:
                {
                    bool closing = c.TokenStart > 0 && text[c.TokenStart - 1] == '/';
                    if (closing)
                    {
                        string open = UnclosedTag(text, c.TokenStart - 2);
                        if (open != null) ctx.Add(open, CompletionItemKind.Class, "close <" + open + ">", open + (after == '>' ? "" : ">"), false, -1, null, "0");
                        break;
                    }
                    foreach (AuiElementDef e in AuiSchema.Elements) ctx.Add(e.Name, CompletionItemKind.Class, e.Doc);
                    ctx.Add("link", CompletionItemKind.Class, "link a stylesheet", "link href=\"$1\"/>", true);
                    ctx.Add("style", CompletionItemKind.Class, "inline stylesheet", "style>\n\t$0\n</style>", true);
                    foreach (string v in ws.Views.Keys) if (v != Path.GetFileNameWithoutExtension(path)) ctx.Add(v, CompletionItemKind.Module, "nested view " + v + ".aui");
                    break;
                }
                case Cursor.Kind.AttributeName:
                {
                    bool editing = after == '=';
                    AuiElementDef def = AuiSchema.FindElement(c.Tag);
                    var seen = new HashSet<string>(c.UsedAttributes);
                    if (def != null)
                    {
                        foreach (string a in def.Attributes) if (seen.Add(a)) ctx.Add(a, CompletionItemKind.Property, null, editing ? a : a + "=\"$0\"", !editing, -1, null, "0" + a);
                        foreach (string ev in def.Events) if (seen.Add(ev)) ctx.Add(ev, CompletionItemKind.Event, "handler method on the view", editing ? ev : ev + "=\"$0\"", !editing, -1, null, "1" + ev);
                    }
                    else if (c.Tag == "link")
                    {
                        if (seen.Add("href")) ctx.Add("href", CompletionItemKind.Property, "stylesheet path relative to this file", editing ? "href" : "href=\"$0\"", !editing);
                    }
                    else if (c.Tag.Length > 0 && char.IsUpper(c.Tag[0]))
                    {
                        AuiViewInfo child = ChildInfo(_ws, ws, c.Tag);
                        if (child != null)
                        {
                            foreach (AuiBinding b in child.Bindings) if (seen.Add(b.Name)) ctx.Add(b.Name, CompletionItemKind.Property, "child binding (" + b.CsType + ")", editing ? b.Name : b.Name + "=\"$0\"", !editing, -1, null, "0" + b.Name);
                            foreach (AuiHandler h in child.Handlers) { string ev = "on-" + h.Name; if (seen.Add(ev)) ctx.Add(ev, CompletionItemKind.Event, "child event", editing ? ev : ev + "=\"$0\"", !editing, -1, null, "1" + ev); }
                        }
                    }
                    if (c.Tag != "link")
                        foreach (string a in AuiSchema.CommonAttributes) if (seen.Add(a)) ctx.Add(a, CompletionItemKind.Property, null, editing ? a : a + "=\"$0\"", !editing, -1, null, "2" + a);
                    break;
                }
                case Cursor.Kind.AttributeValue:
                    AttributeValues(c, r, ws, ctx, path);
                    break;
                case Cursor.Kind.StyleSelector:
                    Selectors(c, ws, ctx);
                    break;
                case Cursor.Kind.StyleProperty:
                {
                    bool editing = after == ':';
                    foreach (AuiPropDef p in AuiSchema.Props) ctx.Add(p.Name, CompletionItemKind.Property, p.Doc ?? p.Kind.ToString(), editing ? p.Name : p.Name + ": $0;", !editing);
                    if (!c.InsideStyleAttribute)
                    {
                        AuiStyleSheet theme = _ws.Theme(ws);
                        if (theme != null) foreach (AuiVariable v in theme.Variables) ctx.Add(v.Name, CompletionItemKind.Variable, "redefine (" + v.Value + ")", editing ? v.Name : v.Name + ": $0;", !editing, -1, null, "z" + v.Name);
                    }
                    break;
                }
                case Cursor.Kind.StyleValue:
                {
                    AuiPropDef p = AuiSchema.FindProp(c.Property);
                    if (p == null) break;
                    if (p.EnumValues != null) foreach (string v in p.EnumValues) ctx.Add(v, CompletionItemKind.EnumMember, null);
                    if (p.Kind == AuiPropKind.Color || p.Kind == AuiPropKind.Background)
                    {
                        AuiStyleSheet theme = _ws.Theme(ws);
                        var seen = new HashSet<string>();
                        foreach (AuiStyleSheet s in Sheets(_ws, ws, path))
                            foreach (AuiVariable v in s.Variables)
                                if (seen.Add(v.Name)) ctx.Add("var(" + v.Name + ")", CompletionItemKind.Variable, v.Value + (s == theme ? "" : "  (" + Path.GetFileName(s.File) + ")"), null, false, -1, null, "0" + v.Name);
                        foreach (string k in new[] { "transparent", "white", "black" }) ctx.Add(k, CompletionItemKind.Color, null);
                        ctx.Add("rgba()", CompletionItemKind.Function, "rgba(r, g, b, a) with 0..255 channels and 0..1 alpha", "rgba(${1:255}, ${2:255}, ${3:255}, ${4:1})", true);
                        if (p.Kind == AuiPropKind.Background)
                        {
                            ctx.Add("none", CompletionItemKind.EnumMember, null);
                            foreach (string s in new[] { "Rounded4", "Rounded8", "Rounded12", "Rounded16", "Rounded24", "Square" }) ctx.Add("sprite(" + s + ")", CompletionItemKind.Value, "9-slice sprite", "sprite(" + s + ") $0", true);
                        }
                    }
                    if (p.Kind == AuiPropKind.Length) foreach (string k in new[] { "auto" }) ctx.Add(k, CompletionItemKind.EnumMember, null);
                    if (p.Kind == AuiPropKind.FontWeight || p.Kind == AuiPropKind.Font) foreach (string k in new[] { "regular", "medium", "semibold", "bold" }) ctx.Add(k, CompletionItemKind.EnumMember, null);
                    if (p.Kind == AuiPropKind.Transition)
                    {
                        ctx.Add("none", CompletionItemKind.EnumMember, null);
                        ctx.Add("all", CompletionItemKind.Property, "every animatable property");
                        foreach (AuiPropDef a in AuiSchema.Props) if (a.Animatable) ctx.Add(a.Name, CompletionItemKind.Property, "animatable", a.Name + " ${1:0.15s} ${2:out-quad}", true);
                        foreach (string e in AuiSchema.Eases) ctx.Add(e, CompletionItemKind.EnumMember, "ease");
                    }
                    break;
                }
            }
            return Task.FromResult(new CompletionList(ctx.Items));
        }

        /// <summary>The innermost element still open before the offset (for "&lt;/" completion).</summary>
        internal static string UnclosedTag(string text, int before)
        {
            var stack = new List<string>();
            var rx = new System.Text.RegularExpressions.Regex(@"<(/?)([A-Za-z_][A-Za-z0-9_.-]*)(?:""[^""]*""|'[^']*'|[^'"">])*?(/?)>|<!--[\s\S]*?-->", System.Text.RegularExpressions.RegexOptions.Compiled);
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(text.Substring(0, Math.Max(0, before + 1))))
            {
                if (m.Value.StartsWith("<!--")) continue;
                string name = m.Groups[2].Value;
                if (m.Groups[1].Value == "/") { int i = stack.LastIndexOf(name); if (i >= 0) stack.RemoveRange(i, stack.Count - i); }
                else if (m.Groups[3].Value != "/" && name != "link") stack.Add(name);
            }
            return stack.Count > 0 ? stack[stack.Count - 1] : null;
        }

        private void AttributeValues(Cursor c, AuiCompileResult r, AuiWorkspace ws, Ctx ctx, string path)
        {
            switch (c.Attribute)
            {
                case "key":
                case "placeholder-key":
                case "format-key":
                {
                    AuiLanguageTable t = _ws.Lang(ws);
                    foreach (string k in t.Keys) ctx.Add(k, CompletionItemKind.Text, t.GetWithFallback(t.DefaultLanguage, k));
                    break;
                }
                case "class":
                {
                    int s = c.Prefix.LastIndexOfAny(new[] { ' ', '\t' }) + 1;
                    ctx.ReplaceStart = c.ValueStart + s;
                    var used = new HashSet<string>(c.Prefix.Substring(0, s).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                    foreach (KeyValuePair<string, string> cls in AllClasses(_ws, ws, path)) if (!used.Contains(cls.Key)) ctx.Add(cls.Key, CompletionItemKind.Field, cls.Value);
                    break;
                }
                case "icon":
                case "name":
                    if (c.Attribute == "icon" || c.Tag == "icon") foreach (string n in ws.IconNames()) ctx.Add(n, CompletionItemKind.Value, "icon");
                    else if (c.Tag == "view") ctx.Add(Path.GetFileNameWithoutExtension(path), CompletionItemKind.Value, "the file stem; the generated class name");
                    break;
                case "page":
                case "for":
                case "default":
                    if (r != null)
                        foreach (AuiPageGroup g in r.Info.PageGroups)
                        {
                            if (c.Attribute == "for") ctx.Add(g.Id, CompletionItemKind.Reference, "pages group");
                            else foreach (AuiNode pg in g.Pages) if (pg.Id != null) ctx.Add(pg.Id, CompletionItemKind.Reference, "page");
                        }
                    break;
                case "mode":
                    foreach (string v in c.Tag == "image" ? new[] { "simple", "sliced", "fit" } : new[] { "crossfade", "sequential" }) ctx.Add(v, CompletionItemKind.EnumMember, null);
                    break;
                case "show":
                    foreach (string v in AuiSchema.FindProp("show").EnumValues) ctx.Add(v, CompletionItemKind.EnumMember, null);
                    break;
                case "hidden":
                case "wrap":
                case "ellipsis":
                case "runtime":
                case "start-hidden":
                    foreach (string v in new[] { "true", "false" }) ctx.Add(v, CompletionItemKind.EnumMember, null);
                    break;
                case "href":
                {
                    string dir = Path.GetDirectoryName(path) ?? "";
                    foreach (string s in ws.StylePaths)
                    {
                        string rel = Path.GetRelativePath(dir, s).Replace('\\', '/');
                        ctx.Add(rel, CompletionItemKind.File, "stylesheet");
                    }
                    break;
                }
                default:
                    if (c.Attribute == "text" || c.Attribute == "bind" || c.Attribute == "if" || c.Attribute == "disabled" || c.Attribute == "selected" || c.Attribute == "args" || (c.Tag.Length > 0 && char.IsUpper(c.Tag[0]) && !c.Attribute.StartsWith("on")))
                    {
                        int brace = c.Prefix.LastIndexOf('{');
                        ctx.ReplaceStart = brace >= 0 ? c.ValueStart + brace : ctx.Offset;
                        if (c.Attribute == "disabled" || c.Attribute == "selected")
                            foreach (string v in new[] { "true", "false" }) ctx.Add(v, CompletionItemKind.EnumMember, null, null, false, c.ValueStart);
                        if (r != null)
                        {
                            AuiRepeatInfo inside = RepeatAround(r, ctx.Text, ctx.Offset);
                            foreach (AuiBinding b in r.Info.Bindings) ctx.Add("{" + b.Name + "}", CompletionItemKind.Variable, b.CsType, null, false, -1, null, "0" + b.Name);
                            foreach (AuiRepeatInfo rep in r.Info.Repeats)
                                if (inside == null || inside == rep)
                                    foreach (AuiBinding b in rep.ItemBindings) ctx.Add("{item." + b.Name + "}", CompletionItemKind.Variable, b.CsType + " (row of #" + rep.Id + ")", null, false, -1, null, "1" + b.Name);
                        }
                    }
                    else if (c.Attribute != null && c.Attribute.StartsWith("on"))
                    {
                        var seen = new HashSet<string>();
                        if (r != null) foreach (AuiHandler h in r.Info.Handlers) if (seen.Add(h.Name)) ctx.Add(h.Name, CompletionItemKind.Method, h.Signature, null, false, -1, null, "0" + h.Name);
                        foreach (KeyValuePair<string, string> m in CSharp.PublicMethods(_ws, path)) if (seen.Add(m.Key)) ctx.Add(m.Key, CompletionItemKind.Method, "void " + m.Key + "(" + m.Value + ")  (" + Path.GetFileNameWithoutExtension(path) + ".cs)", null, false, -1, null, "1" + m.Key);
                    }
                    break;
            }
        }

        private static AuiRepeatInfo RepeatAround(AuiCompileResult r, string text, int offset)
        {
            Position p = Cursor.PositionOf(text, offset);
            AuiNode n = r.Doc.NodeAt(p.Line + 1, p.Character + 1);
            return n != null ? r.Info.RepeatContaining(n) : null;
        }

        private void Selectors(Cursor c, AuiWorkspace ws, Ctx ctx)
        {
            string prefix = c.Prefix ?? "";
            int dot = prefix.LastIndexOf('.'), colon = prefix.LastIndexOf(':');
            int seg = Math.Max(dot, colon);
            if (seg >= 0 && prefix[seg] == ':')
            {
                ctx.ReplaceStart = c.TokenStart + seg;
                foreach (string st in AuiSchema.States) ctx.Add(":" + st, CompletionItemKind.EnumMember, "pointer state");
                if (seg == 0) ctx.Add(":root", CompletionItemKind.Keyword, "variables block", ":root {\n\t$0\n}", true);
                return;
            }
            if (seg >= 0)
            {
                ctx.ReplaceStart = c.TokenStart + seg;
                foreach (KeyValuePair<string, string> cls in AllClasses(_ws, ws, null)) ctx.Add("." + cls.Key, CompletionItemKind.Field, cls.Value);
                return;
            }
            foreach (AuiElementDef e in AuiSchema.Elements) ctx.Add(e.Name, CompletionItemKind.Class, e.Doc);
            foreach (string v in ws.Views.Keys) ctx.Add(v, CompletionItemKind.Module, "nested view");
            foreach (KeyValuePair<string, string> cls in AllClasses(_ws, ws, null)) ctx.Add("." + cls.Key, CompletionItemKind.Field, cls.Value);
            foreach (string st in AuiSchema.States) ctx.Add(":" + st, CompletionItemKind.EnumMember, "pointer state");
            ctx.Add(":root", CompletionItemKind.Keyword, "variables block", ":root {\n\t$0\n}", true);
        }

        internal static AuiViewInfo ChildInfo(Workspace host, AuiWorkspace ws, string name)
        {
            string cp;
            if (!ws.Views.TryGetValue(name, out cp)) return null;
            string text = host.Read(cp);
            if (text == null) return null;
            return AuiAnalyzer.Analyze(AuiParser.Parse(text, cp), new AuiAnalysisContext(), new AuiDiagnosticBag(cp));
        }

        /// <summary>The sheets that apply to a file: theme, every linked sheet, and the view's own inline blocks.</summary>
        internal static IEnumerable<AuiStyleSheet> Sheets(Workspace host, AuiWorkspace ws, string forPath)
        {
            AuiStyleSheet theme = host.Theme(ws);
            if (theme != null) yield return theme;
            foreach (string p in ws.StylePaths)
            {
                string t = host.Read(p);
                if (t != null) yield return AuiStyleParser.Parse(t, p, AuiStyleTier.Linked);
            }
            if (forPath != null && Sel.IsView(forPath))
            {
                AuiCompileResult r = host.Result(forPath);
                if (r != null) foreach (AuiStyleSheet s in r.Sheets) if (s.Tier == AuiStyleTier.Inline) yield return s;
            }
        }

        /// <summary>Every class any sheet defines, with the sheet's name as detail.</summary>
        internal static IEnumerable<KeyValuePair<string, string>> AllClasses(Workspace host, AuiWorkspace ws, string forPath)
        {
            var seen = new HashSet<string>();
            foreach (AuiStyleSheet s in Sheets(host, ws, forPath))
                foreach (string c in s.ClassNames())
                    if (seen.Add(c)) yield return new KeyValuePair<string, string>(c, s.Tier == AuiStyleTier.Inline ? "<style> in this view" : Path.GetFileName(s.File));
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
            return new HoverRegistrationOptions { DocumentSelector = Sel.All };
        }

        public override Task<Hover> Handle(HoverParams request, CancellationToken token)
        {
            string path = Workspace.PathOf(request.TextDocument.Uri);
            string text = _ws.Read(path) ?? "";
            AuiWorkspace ws = _ws.Scan();
            string md = null;
            Range range = null;

            if (Sel.IsLang(path))
            {
                int off = Cursor.Offset(text, request.Position.Line, request.Position.Character);
                string key = LangJson.KeyAt(text, off);
                if (key != null) md = KeyHover(ws, key, path);
                return Task.FromResult(md == null ? null : new Hover { Contents = Md(md) });
            }

            Target t = Occurrences.At(path, text, request.Position);
            range = t.Range;
            switch (t.Kind)
            {
                case TargetKind.Key:
                    md = KeyHover(ws, t.Name, null);
                    break;
                case TargetKind.Variable:
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (AuiStyleSheet s in CompletionHandler.Sheets(_ws, ws, path))
                        foreach (AuiVariable v in s.Variables)
                            if (v.Name == t.Name) sb.Append("`").Append(v.Name).Append("`: ").Append(v.Value).Append("  (").Append(s.Tier == AuiStyleTier.Inline ? "inline" : Path.GetFileName(s.File)).Append(")\n\n");
                    if (sb.Length > 0) md = sb.ToString();
                    break;
                }
                case TargetKind.Class:
                    md = ClassHover(ws, t.Name, path);
                    break;
                case TargetKind.Binding:
                {
                    AuiCompileResult r = _ws.EnsureCompiled(path);
                    AuiBinding b = null;
                    if (t.IsItem) { foreach (AuiRepeatInfo rep in r.Info.Repeats) { b = r.Info.FindBinding(t.Name, rep); if (b != null) break; } }
                    else b = r.Info.FindBinding(t.Name, null);
                    if (b == null) break;
                    var sb = new System.Text.StringBuilder();
                    sb.Append("**{").Append(t.IsItem ? "item." : "").Append(b.Name).Append("}** `").Append(b.CsType).Append("`\n\n");
                    if (b.IsItem && b.Repeat != null) sb.Append("Row value of repeat `#").Append(b.Repeat.Id).Append("`: `Set").Append(b.Repeat.PascalId).Append(b.PropertyName).Append("(int row, ").Append(b.CsType).Append(" value)`\n\n");
                    else sb.Append("Generated property `").Append(b.PropertyName).Append("`\n\n");
                    foreach (AuiBindingUse u in b.Uses) sb.Append("- `<").Append(u.Node.Tag).Append(" ").Append(u.Attribute).Append("=...>` line ").Append(u.Node.Line).Append('\n');
                    md = sb.ToString();
                    break;
                }
                case TargetKind.Handler:
                {
                    AuiCompileResult r = _ws.EnsureCompiled(path);
                    AuiHandler h = r.Info.FindHandler(t.Name);
                    var sb = new System.Text.StringBuilder();
                    sb.Append("```csharp\npublic ").Append(h != null ? h.Signature : "void " + t.Name + "()").Append("\n```\n\n");
                    Occurrence m = CSharp.FindMethod(_ws, path, t.Name);
                    sb.Append(m != null ? "Defined in " + Path.GetFileName(m.File) + ":" + (m.Range.Start.Line + 1) : "Not found next to the view; the build will report AUI502 until it exists.");
                    md = sb.ToString();
                    break;
                }
                case TargetKind.Id:
                {
                    AuiCompileResult r = _ws.EnsureCompiled(path);
                    AuiNode n = r.Doc.FindById(t.Name);
                    if (n != null) md = "`" + n + "` line " + n.Line;
                    break;
                }
                case TargetKind.Icon:
                    foreach (string p in ws.IconPaths) if (Path.GetFileNameWithoutExtension(p) == t.Name) { md = "**" + t.Name + "**\n\n" + p; break; }
                    break;
                case TargetKind.Href:
                    md = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path) ?? "", t.Name));
                    break;
                case TargetKind.View:
                {
                    AuiViewInfo child = CompletionHandler.ChildInfo(_ws, ws, t.Name);
                    if (child == null) break;
                    var sb = new System.Text.StringBuilder("**" + t.Name + ".aui** " + AuiNum.Fmt(child.Width) + " x " + AuiNum.Fmt(child.Height) + " mm\n\n");
                    foreach (AuiBinding b in child.Bindings) sb.Append("- `").Append(b.Name).Append("` ").Append(b.CsType).Append('\n');
                    foreach (AuiHandler h in child.Handlers) sb.Append("- `on-").Append(h.Name).Append("` event\n");
                    md = sb.ToString();
                    break;
                }
                default:
                    md = WordHover(ws, text, request.Position, path, out range);
                    break;
            }
            if (md == null) return Task.FromResult<Hover>(null);
            return Task.FromResult(new Hover { Contents = Md(md), Range = range });
        }

        private static MarkedStringsOrMarkupContent Md(string md)
        {
            return new MarkedStringsOrMarkupContent(new MarkupContent { Kind = MarkupKind.Markdown, Value = md });
        }

        private string KeyHover(AuiWorkspace ws, string key, string exceptFile)
        {
            AuiLanguageTable table = _ws.Lang(ws);
            if (table.IndexOfKey(key) < 0) return null;
            var sb = new System.Text.StringBuilder("**" + key + "**\n\n");
            for (int i = 0; i < table.LanguageCount; i++)
            {
                string v = table.Get(i, key);
                sb.Append("- `").Append(table.Languages[i].Code).Append("` ").Append(v ?? "*(missing)*").Append('\n');
            }
            return sb.ToString();
        }

        private string ClassHover(AuiWorkspace ws, string cls, string path)
        {
            var sb = new System.Text.StringBuilder();
            foreach (AuiStyleSheet s in CompletionHandler.Sheets(_ws, ws, path))
                foreach (AuiStyleRule rule in s.Rules)
                    foreach (AuiSelector sel in rule.Selectors)
                        if (sel.Class == cls)
                        {
                            sb.Append("`").Append(sel).Append("` (").Append(s.Tier == AuiStyleTier.Inline ? "inline" : Path.GetFileName(s.File)).Append(")\n");
                            foreach (AuiDeclaration d in rule.Declarations) sb.Append("- ").Append(d.Property).Append(": ").Append(d.Value).Append('\n');
                            sb.Append('\n');
                        }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        /// <summary>Element and property docs by the word under the caret (tag names, selectors, declarations).</summary>
        private string WordHover(AuiWorkspace ws, string text, Position pos, string path, out Range range)
        {
            int offset = Cursor.Offset(text, pos.Line, pos.Character);
            int s, e;
            string word = Cursor.WordAt(text, offset, out s, out e);
            range = Cursor.RangeOf(text, s, e);
            if (word.Length == 0) return null;
            AuiElementDef el = AuiSchema.FindElement(word);
            if (el != null)
                return "**<" + el.Name + ">** " + (el.Doc ?? "") + "\n\nattributes: " + string.Join(", ", el.Attributes) + (el.Events.Length > 0 ? "\n\nevents: " + string.Join(", ", el.Events) : "");
            AuiPropDef p = AuiSchema.FindProp(word);
            if (p != null)
                return "**" + p.Name + "** (" + p.Kind + (p.Inherited ? ", inherited" : "") + (p.Animatable ? ", animatable" : "") + ")" + (p.Doc != null ? "\n\n" + p.Doc : "") + (p.EnumValues != null ? "\n\n" + string.Join(" | ", p.EnumValues) : "");
            if (AuiSchema.IsEase(word)) return "**" + word + "** easing curve";
            if (_ws.Lang(ws).IndexOfKey(word) >= 0) return KeyHover(ws, word, null);
            return null;
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
            return new DefinitionRegistrationOptions { DocumentSelector = Sel.All };
        }

        public override Task<LocationOrLocationLinks> Handle(DefinitionParams request, CancellationToken token)
        {
            string path = Workspace.PathOf(request.TextDocument.Uri);
            string text = _ws.Read(path) ?? "";
            var locations = new List<LocationOrLocationLink>();
            AuiWorkspace ws = _ws.Scan();

            if (Sel.IsLang(path))
            {
                string key = LangJson.KeyAt(text, Cursor.Offset(text, request.Position.Line, request.Position.Character));
                if (key != null)
                    foreach (Occurrence o in Occurrences.Find(_ws, new Target { Kind = TargetKind.Key, Name = key }, path))
                        if (Sel.IsView(o.File)) locations.Add(o.ToLocation());
                return Task.FromResult(new LocationOrLocationLinks(locations));
            }

            Target t = Occurrences.At(path, text, request.Position);
            switch (t.Kind)
            {
                case TargetKind.View:
                {
                    string vp;
                    if (ws.Views.TryGetValue(t.Name, out vp)) locations.Add(new Location { Uri = DocumentUri.FromFileSystemPath(vp), Range = new Range(new Position(0, 0), new Position(0, 0)) });
                    break;
                }
                case TargetKind.Key:
                {
                    AuiLanguageTable table = _ws.Lang(ws);
                    if (table.IndexOfKey(t.Name) < 0) break;
                    foreach (AuiLanguageFile lf in table.Languages)
                    {
                        AuiJson.Pos pos;
                        if (!lf.Data.Positions.TryGetValue(t.Name, out pos)) continue;
                        locations.Add(new Location { Uri = DocumentUri.FromFileSystemPath(pos.File ?? lf.File), Range = Cursor.RangeAt(pos.Line, pos.Column, 0) });
                    }
                    break;
                }
                case TargetKind.Class:
                    foreach (AuiStyleSheet sheet in CompletionHandler.Sheets(_ws, ws, path))
                        foreach (AuiStyleRule rule in sheet.Rules)
                            foreach (AuiSelector sel in rule.Selectors)
                                if (sel.Class == t.Name) { locations.Add(new Location { Uri = DocumentUri.FromFileSystemPath(sheet.File), Range = Cursor.RangeAt(rule.Line, rule.Column, 0) }); break; }
                    break;
                case TargetKind.Variable:
                    foreach (AuiStyleSheet sheet in CompletionHandler.Sheets(_ws, ws, path))
                        foreach (AuiVariable v in sheet.Variables)
                            if (v.Name == t.Name) locations.Add(new Location { Uri = DocumentUri.FromFileSystemPath(sheet.File), Range = Cursor.RangeAt(v.Line, v.Column, v.Name.Length) });
                    break;
                case TargetKind.Handler:
                {
                    Occurrence m = CSharp.FindMethod(_ws, path, t.Name);
                    if (m != null) locations.Add(m.ToLocation());
                    break;
                }
                case TargetKind.Id:
                {
                    AuiCompileResult r = _ws.EnsureCompiled(path);
                    AuiNode n = r.Doc.FindById(t.Name);
                    if (n != null) { AuiAttribute a = n.FindAttribute("id"); locations.Add(new Location { Uri = request.TextDocument.Uri, Range = Cursor.RangeAt(a.ValueLine, a.ValueColumn, a.Value.Length) }); }
                    break;
                }
                case TargetKind.Icon:
                    foreach (string p in ws.IconPaths) if (Path.GetFileNameWithoutExtension(p) == t.Name) { locations.Add(new Location { Uri = DocumentUri.FromFileSystemPath(p), Range = new Range(new Position(0, 0), new Position(0, 0)) }); break; }
                    break;
                case TargetKind.Href:
                {
                    string p = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path) ?? "", t.Name));
                    if (File.Exists(p)) locations.Add(new Location { Uri = DocumentUri.FromFileSystemPath(p), Range = new Range(new Position(0, 0), new Position(0, 0)) });
                    break;
                }
                case TargetKind.None:
                    // a tag selector in a sheet: every rule for that tag
                    if (Sel.IsSheet(path) || t.Tag == null)
                    {
                        string word = t.Name;
                        if (!string.IsNullOrEmpty(word) && AuiSchema.FindElement(word) != null)
                            foreach (AuiStyleSheet sheet in CompletionHandler.Sheets(_ws, ws, path))
                                foreach (AuiStyleRule rule in sheet.Rules)
                                    foreach (AuiSelector sel in rule.Selectors)
                                        if (sel.Tag == word && sel.Class == null && sel.State == null) locations.Add(new Location { Uri = DocumentUri.FromFileSystemPath(sheet.File), Range = Cursor.RangeAt(rule.Line, rule.Column, 0) });
                    }
                    break;
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
            return new DocumentSymbolRegistrationOptions { DocumentSelector = Sel.Both };
        }

        public override Task<SymbolInformationOrDocumentSymbolContainer> Handle(DocumentSymbolParams request, CancellationToken token)
        {
            string path = Workspace.PathOf(request.TextDocument.Uri);
            string text = _ws.Read(path) ?? "";
            var list = new List<SymbolInformationOrDocumentSymbol>();
            if (Sel.IsSheet(path))
            {
                AuiStyleSheet s = AuiStyleParser.Parse(text, path, AuiStyleTier.Linked);
                foreach (DocumentSymbol d in SheetSymbols(s, text)) list.Add(new SymbolInformationOrDocumentSymbol(d));
            }
            else
            {
                AuiMarkupDocument doc = AuiParser.Parse(text, path);
                if (doc.Root != null) list.Add(new SymbolInformationOrDocumentSymbol(Symbol(doc.Root)));
                foreach (AuiStyleBlock b in doc.Styles)
                {
                    AuiStyleSheet s = AuiStyleParser.Parse(b.Text, path, AuiStyleTier.Inline, b.Line, b.Column);
                    var children = new List<DocumentSymbol>(SheetSymbols(s, text));
                    int endLine = b.Line - 1 + Math.Max(0, b.Text.Split('\n').Length - 1);
                    var range = new Range(new Position(Math.Max(0, b.Line - 1), 0), new Position(endLine, Cursor.LineLength(text, endLine)));
                    list.Add(new SymbolInformationOrDocumentSymbol(new DocumentSymbol { Name = "<style>", Kind = SymbolKind.Namespace, Range = range, SelectionRange = new Range(range.Start, range.Start), Children = new Container<DocumentSymbol>(children) }));
                }
            }
            return Task.FromResult(new SymbolInformationOrDocumentSymbolContainer(list));
        }

        private static IEnumerable<DocumentSymbol> SheetSymbols(AuiStyleSheet s, string text)
        {
            if (s.Variables.Count > 0)
            {
                var vars = new List<DocumentSymbol>();
                foreach (AuiVariable v in s.Variables)
                {
                    Range r = Cursor.RangeAt(v.Line, v.Column, v.Name.Length);
                    vars.Add(new DocumentSymbol { Name = v.Name, Detail = v.Value, Kind = SymbolKind.Variable, Range = r, SelectionRange = r });
                }
                Range first = vars[0].Range;
                yield return new DocumentSymbol { Name = ":root", Kind = SymbolKind.Namespace, Range = new Range(first.Start, vars[vars.Count - 1].Range.End), SelectionRange = first, Children = new Container<DocumentSymbol>(vars) };
            }
            foreach (AuiStyleRule rule in s.Rules)
            {
                var names = new List<string>();
                bool anyClass = false;
                foreach (AuiSelector sel in rule.Selectors) { names.Add(sel.ToString()); if (sel.Class != null) anyClass = true; }
                int endLine = rule.Line;
                foreach (AuiDeclaration d in rule.Declarations) if (d.Line > endLine) endLine = d.Line;
                var range = new Range(new Position(Math.Max(0, rule.Line - 1), Math.Max(0, rule.Column - 1)), new Position(Math.Max(0, endLine - 1), Cursor.LineLength(text, Math.Max(0, endLine - 1))));
                string name = string.Join(", ", names);
                yield return new DocumentSymbol
                {
                    Name = name,
                    Detail = rule.Declarations.Count + " declaration" + (rule.Declarations.Count == 1 ? "" : "s"),
                    Kind = anyClass ? SymbolKind.Class : SymbolKind.Struct,
                    Range = range,
                    SelectionRange = new Range(range.Start, new Position(range.Start.Line, range.Start.Character + name.Length)),
                };
            }
        }

        private static DocumentSymbol Symbol(AuiNode n)
        {
            var range = new Range(new Position(Math.Max(0, n.Line - 1), Math.Max(0, n.Column - 1)), new Position(Math.Max(0, n.EndLine - 1), Math.Max(0, n.EndColumn - 1)));
            var children = new List<DocumentSymbol>();
            foreach (AuiNode c in n.Children) children.Add(Symbol(c));
            string name = n.Tag + (n.Id != null ? "#" + n.Id : "");
            string detail = n.Get("key") ?? n.Get("text") ?? n.Get("bind") ?? n.Get("icon") ?? (n.Text != null ? "\"" + n.Text + "\"" : null) ?? n.Get("class");
            return new DocumentSymbol
            {
                Name = name,
                Detail = detail,
                Kind = n.IsComponentTag ? SymbolKind.Module : n.Tag == "repeat" ? SymbolKind.Array : n.Tag == "pages" || n.Tag == "tabs" ? SymbolKind.Enum : n.Tag == "page" || n.Tag == "tab" ? SymbolKind.EnumMember : n.Tag == "text" ? SymbolKind.String : n.Tag == "button" || n.Tag == "toggle" || n.Tag == "slider" || n.Tag == "input" || n.Tag == "select" ? SymbolKind.Event : SymbolKind.Object,
                Range = range,
                SelectionRange = new Range(range.Start, new Position(range.Start.Line, range.Start.Character + n.Tag.Length + 1)),
                Children = new Container<DocumentSymbol>(children),
            };
        }
    }
}
