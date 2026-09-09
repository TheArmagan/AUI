using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Armagan.AUI.Core;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Armagan.AUI.Lsp
{
    /// <summary>
    /// Everything the handlers share: where the files are, the text of open documents (which
    /// wins over the disk), the last compile of every view, and diagnostics publishing.
    ///
    /// The package root is found by walking up from the workspace root for a package.json named
    /// rest.armagan.aui, then by looking under Packages/. Project settings come from the same
    /// AuiSettings.asset the Unity editor reads (language folder, default language, component
    /// and icon folders), so both hosts see one workspace. Without the asset the language folder
    /// is Assets/AUI/Lang in any letter case, else every folder called lang.
    ///
    /// Scans, the parsed theme and the language table are cached and dropped only when a
    /// watched file appears or disappears or their text changes; a keystroke never walks the
    /// Assets tree. Diagnostics are aggregated per file across every view that touched it, so a
    /// shared stylesheet or language file shows the union of what all views found, and a
    /// problem fixed in one place disappears everywhere.
    /// </summary>
    public sealed class Workspace
    {
        public string Root;
        public string PackageRoot;
        public string LangFolder;
        public string DefaultLanguage = "en";
        public readonly List<string> ComponentFolders = new List<string>();
        public readonly List<string> IconFolders = new List<string>();

        private ILanguageServerFacade _server;
        private readonly Dictionary<string, string> _open = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AuiCompileResult> _results = new Dictionary<string, AuiCompileResult>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();
        private readonly object _compileLock = new object();

        // ---- caches
        private AuiWorkspace _scan;
        private AuiStyleSheet _theme;
        private string _themeText;
        private AuiLanguageTable _lang;
        private AuiDiagnosticBag _langDiag;
        private List<KeyValuePair<string, string>> _langTexts;

        // ---- debounce
        private readonly Dictionary<string, Timer> _timers = new Dictionary<string, Timer>(StringComparer.OrdinalIgnoreCase);
        public int DebounceMs = 150;

        // ---- published diagnostics: producer (a view compile, or a sheet parsed on its own) -> file -> items
        private readonly Dictionary<string, Dictionary<string, List<AuiDiagnostic>>> _byProducer = new Dictionary<string, Dictionary<string, List<AuiDiagnostic>>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _depsOf = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        // ================================================================ setup

        public void Initialize(string root)
        {
            Root = string.IsNullOrEmpty(root) ? Directory.GetCurrentDirectory() : Path.GetFullPath(root);
            PackageRoot = FindPackageRoot(Root);
            ReadSettings();
            if (LangFolder == null)
            {
                string aui = FindDirectoryIgnoreCase(Path.Combine(Root, "Assets"), "AUI");
                string lang = aui != null ? FindDirectoryIgnoreCase(aui, "Lang") : null;
                LangFolder = lang;
            }
        }

        public void Attach(ILanguageServerFacade server)
        {
            _server = server;
        }

        /// <summary>A message box in the editor, for a request that cannot do what was asked.</summary>
        public void ShowError(string message)
        {
            if (_server != null) _server.Window.ShowError(message);
        }

        private static bool IsAuiPackage(string dir)
        {
            string pj = Path.Combine(dir, "package.json");
            try { return File.Exists(pj) && File.ReadAllText(pj).Contains("\"rest.armagan.aui\""); }
            catch { return false; }
        }

        /// <summary>The folder itself, Packages/rest.armagan.aui, any package under Packages/, then the same from each parent folder.</summary>
        private static string FindPackageRoot(string root)
        {
            string dir = root;
            for (int up = 0; dir != null && up < 6; up++)
            {
                if (IsAuiPackage(dir)) return dir;
                string packages = Path.Combine(dir, "Packages");
                if (Directory.Exists(packages))
                {
                    string direct = Path.Combine(packages, "rest.armagan.aui");
                    if (IsAuiPackage(direct)) return direct;
                    foreach (string sub in Directory.GetDirectories(packages)) if (IsAuiPackage(sub)) return sub;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        private static string FindDirectoryIgnoreCase(string parent, string name)
        {
            if (parent == null || !Directory.Exists(parent)) return null;
            foreach (string d in Directory.GetDirectories(parent))
                if (string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        /// <summary>Reads AuiSettings.asset (Unity YAML) for the fields the compile depends on. Missing asset = defaults.</summary>
        private void ReadSettings()
        {
            string asset = FindSettingsAsset();
            if (asset == null) return;
            string[] lines;
            try { lines = File.ReadAllLines(asset); } catch { return; }
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();
                int colon = line.IndexOf(':');
                if (colon < 0 || line.Length == 0 || !char.IsWhiteSpace(line[0])) continue;
                string key = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                switch (key)
                {
                    case "langFolder":
                        if (value.Length > 0) { string p = Path.GetFullPath(Path.Combine(Root, value)); if (Directory.Exists(p)) LangFolder = p; }
                        break;
                    case "defaultLanguage":
                        if (value.Length > 0) DefaultLanguage = value;
                        break;
                    case "componentFolders":
                        ReadList(lines, ref i, value, ComponentFolders);
                        break;
                    case "iconFolders":
                        ReadList(lines, ref i, value, IconFolders);
                        break;
                }
            }
        }

        private void ReadList(string[] lines, ref int i, string inline, List<string> into)
        {
            if (inline.StartsWith("["))
            {
                string inner = inline.Trim('[', ']');
                foreach (string s in inner.Split(',')) AddFolder(into, s.Trim());
                return;
            }
            while (i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith("- "))
            {
                i++;
                AddFolder(into, lines[i].TrimStart().Substring(2).Trim());
            }
        }

        private void AddFolder(List<string> into, string rel)
        {
            if (rel.Length == 0) return;
            string p = Path.GetFullPath(Path.Combine(Root, rel));
            if (Directory.Exists(p)) into.Add(p);
        }

        private string FindSettingsAsset()
        {
            string assets = Path.Combine(Root, "Assets");
            string direct = Path.Combine(assets, "AUI", "AuiSettings.asset");
            if (File.Exists(direct)) return direct;
            if (!Directory.Exists(assets)) return null;
            try
            {
                foreach (string f in Directory.EnumerateFiles(assets, "AuiSettings.asset", SearchOption.AllDirectories)) return f;
            }
            catch { }
            return null;
        }

        // ================================================================ scan

        public AuiWorkspace Scan()
        {
            lock (_lock) if (_scan != null) return _scan;
            string scanRoot = Directory.Exists(Path.Combine(Root, "Assets")) ? Path.Combine(Root, "Assets") : Root;
            AuiWorkspace w = AuiWorkspace.Scan(scanRoot, PackageRoot, LangFolder);
            foreach (string f in ComponentFolders)
                foreach (string p in Directory.GetFiles(f, "*.aui")) w.Views[Path.GetFileNameWithoutExtension(p)] = Path.GetFullPath(p);
            foreach (string f in IconFolders)
                foreach (string p in Directory.GetFiles(f, "*.svg")) w.IconPaths.Insert(0, Path.GetFullPath(p));
            // A view opened outside the scanned tree (a sample under Samples~, a folder above
            // Assets) brings its own neighbourhood in, so its nested views and sheets resolve.
            List<string> open;
            lock (_lock) open = new List<string>(_open.Keys);
            var extra = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in open)
            {
                if (!p.EndsWith(".aui", StringComparison.OrdinalIgnoreCase)) continue;
                string known;
                if (w.Views.TryGetValue(Path.GetFileNameWithoutExtension(p), out known) && string.Equals(Path.GetFullPath(known), p, StringComparison.OrdinalIgnoreCase)) continue;
                string extraRoot = ExtraRootFor(p);
                if (extraRoot != null && extra.Add(extraRoot)) w.Include(extraRoot);
            }
            lock (_lock) _scan = w;
            return w;
        }

        /// <summary>The nearest ancestor ending in '~' (Unity's hidden folders), else the file's own folder.</summary>
        private string ExtraRootFor(string file)
        {
            string dir = Path.GetDirectoryName(file);
            for (string d = dir; d != null; d = Path.GetDirectoryName(d))
            {
                string name = Path.GetFileName(d);
                if (name.EndsWith("~")) return d;
                if (string.Equals(d, Root, StringComparison.OrdinalIgnoreCase)) break;
            }
            return dir;
        }

        public void InvalidateScan()
        {
            lock (_lock) _scan = null;
        }

        public AuiStyleSheet Theme(AuiWorkspace ws)
        {
            if (ws.ThemePath == null) return null;
            string text = Read(ws.ThemePath);
            if (text == null) return null;
            lock (_lock)
            {
                if (_theme != null && string.Equals(_themeText, text, StringComparison.Ordinal)) return _theme;
            }
            AuiStyleSheet sheet = AuiStyleParser.Parse(text, ws.ThemePath, AuiStyleTier.Theme);
            lock (_lock) { _theme = sheet; _themeText = text; }
            return sheet;
        }

        /// <summary>The merged language table; rebuilt only when a language file's text changed. Diagnostics for the files come out too.</summary>
        public AuiLanguageTable Lang(AuiWorkspace ws, out AuiDiagnosticBag diagnostics)
        {
            var texts = new List<KeyValuePair<string, string>>();
            foreach (string p in ws.PackageLangPaths) texts.Add(new KeyValuePair<string, string>(p, Read(p)));
            foreach (string p in ws.LangPaths) texts.Add(new KeyValuePair<string, string>(p, Read(p)));
            lock (_lock)
            {
                if (_lang != null && Same(_langTexts, texts)) { diagnostics = _langDiag; return _lang; }
            }
            var d = new AuiDiagnosticBag("lang");
            AuiLanguageTable table = ws.LoadLanguageTable(DefaultLanguage, d, Read);
            lock (_lock) { _lang = table; _langDiag = d; _langTexts = texts; }
            diagnostics = d;
            return table;
        }

        private static bool Same(List<KeyValuePair<string, string>> a, List<KeyValuePair<string, string>> b)
        {
            if (a == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i].Key, b[i].Key, StringComparison.OrdinalIgnoreCase) || !string.Equals(a[i].Value, b[i].Value, StringComparison.Ordinal)) return false;
            return true;
        }

        public AuiLanguageTable Lang(AuiWorkspace ws)
        {
            AuiDiagnosticBag ignored;
            return Lang(ws, out ignored);
        }

        // ================================================================ documents

        public void Open(string path, string text)
        {
            string full = Path.GetFullPath(path);
            bool unknown;
            lock (_lock)
            {
                _open[full] = text;
                string known;
                unknown = _scan != null && path.EndsWith(".aui", StringComparison.OrdinalIgnoreCase)
                    && !(_scan.Views.TryGetValue(Path.GetFileNameWithoutExtension(full), out known) && string.Equals(Path.GetFullPath(known), full, StringComparison.OrdinalIgnoreCase));
            }
            if (unknown) InvalidateScan();
        }

        public void Change(string path, string text)
        {
            lock (_lock) _open[Path.GetFullPath(path)] = text;
        }

        public void Close(string path)
        {
            string full = Path.GetFullPath(path);
            lock (_lock) _open.Remove(full);
            CancelTimer(full);
        }

        public string Read(string path)
        {
            string full;
            try { full = Path.GetFullPath(path); } catch { return null; }
            lock (_lock)
            {
                string t;
                if (_open.TryGetValue(full, out t)) return t;
            }
            try { return File.Exists(full) ? File.ReadAllText(full) : null; }
            catch { return null; }
        }

        public bool IsOpen(string path)
        {
            lock (_lock) return _open.ContainsKey(Path.GetFullPath(path));
        }

        public List<string> OpenPaths()
        {
            lock (_lock) return new List<string>(_open.Keys);
        }

        // ================================================================ compile

        public AuiCompileResult Compile(string viewPath)
        {
            lock (_compileLock)
            {
                AuiWorkspace ws = Scan();
                string text = Read(viewPath) ?? "";
                AuiDiagnosticBag langDiag;
                AuiLanguageTable lang = Lang(ws, out langDiag);
                var opt = new AuiCompileOptions
                {
                    Theme = Theme(ws),
                    ReadFile = Read,
                    Lang = lang,
                    Measurer = new AuiApproxTextMeasurer(),
                    ViewExists = ws.ViewExists,
                    IconExists = ws.IconExists,
                    ChildInfo = name =>
                    {
                        string cp;
                        if (!ws.Views.TryGetValue(name, out cp)) return null;
                        string ct = Read(cp);
                        if (ct == null) return null;
                        return AuiAnalyzer.Analyze(AuiParser.Parse(ct, cp), new AuiAnalysisContext { FileStem = name, ViewExists = ws.ViewExists }, new AuiDiagnosticBag(cp));
                    },
                };
                AuiCompileResult r = AuiCompiler.Compile(text, viewPath, opt);
                foreach (AuiDiagnostic d in langDiag.Items) r.Diagnostics.Items.Add(d);
                lock (_lock) _results[Path.GetFullPath(viewPath)] = r;
                return r;
            }
        }

        public AuiCompileResult Result(string viewPath)
        {
            lock (_lock)
            {
                AuiCompileResult r;
                return _results.TryGetValue(Path.GetFullPath(viewPath), out r) ? r : null;
            }
        }

        /// <summary>The last compile if it saw the current text, else a fresh one. What every request that reads bindings or keys wants.</summary>
        public AuiCompileResult EnsureCompiled(string viewPath)
        {
            AuiCompileResult r = Result(viewPath);
            string now = Read(viewPath) ?? "";
            if (r != null && string.Equals(r.SourceText, now, StringComparison.Ordinal)) return r;
            return CompileAndPublish(viewPath);
        }

        /// <summary>Compile one view and publish its diagnostics, grouped by the file they belong to.</summary>
        public AuiCompileResult CompileAndPublish(string viewPath)
        {
            AuiCompileResult r = Compile(viewPath);
            Publish(Path.GetFullPath(viewPath), r.Diagnostics.Items, r.Dependencies);
            return r;
        }

        /// <summary>Every view in the workspace: the cheap way to reflect a language-file or stylesheet edit.</summary>
        public void RecompileAll()
        {
            AuiWorkspace ws = Scan();
            foreach (string p in new List<string>(ws.Views.Values)) CompileAndPublish(p);
        }

        /// <summary>Views that use a stylesheet or language file, so an edit there re-checks them.</summary>
        public void RecompileDependents(string changedPath)
        {
            string full = Path.GetFullPath(changedPath);
            AuiWorkspace ws = Scan();
            bool isLang = changedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            bool isTheme = ws.ThemePath != null && string.Equals(Path.GetFullPath(ws.ThemePath), full, StringComparison.OrdinalIgnoreCase);
            foreach (string p in new List<string>(ws.Views.Values))
            {
                AuiCompileResult r = Result(p);
                bool affected = r == null || isLang || isTheme;
                if (r != null && !affected) foreach (string d in r.Dependencies) if (string.Equals(Path.GetFullPath(d), full, StringComparison.OrdinalIgnoreCase)) { affected = true; break; }
                if (affected) CompileAndPublish(p);
            }
            if (changedPath.EndsWith(".auis", StringComparison.OrdinalIgnoreCase)) CheckSheet(full);
        }

        /// <summary>A stylesheet on its own: syntax and property names, so a sheet no view links yet still gets squiggles.</summary>
        public void CheckSheet(string sheetPath)
        {
            string text = Read(sheetPath);
            if (text == null) { Forget(sheetPath); return; }
            AuiStyleSheet s = AuiStyleParser.Parse(text, sheetPath, AuiStyleTier.Linked);
            Publish("sheet:" + sheetPath, s.Diagnostics.Items, new string[0], sheetPath);
        }

        /// <summary>The view whose stem matches a stylesheet (Name.auis next to Name.aui), if any.</summary>
        public void RecompileSibling(string sheetPath)
        {
            string view = Path.ChangeExtension(sheetPath, ".aui");
            if (File.Exists(view)) CompileAndPublish(view);
        }

        /// <summary>Re-read AuiSettings.asset (it changed on disk) and drop the scan that was built from the old one.</summary>
        public void ReloadSettings()
        {
            LangFolder = null;
            DefaultLanguage = "en";
            ComponentFolders.Clear();
            IconFolders.Clear();
            ReadSettings();
            if (LangFolder == null)
            {
                string aui = FindDirectoryIgnoreCase(Path.Combine(Root, "Assets"), "AUI");
                LangFolder = aui != null ? FindDirectoryIgnoreCase(aui, "Lang") : null;
            }
            InvalidateScan();
        }

        /// <summary>Views that nest a view of this name; a created or deleted .aui changes their AUI107.</summary>
        public void RecompileUsersOf(string viewStem)
        {
            AuiWorkspace ws = Scan();
            foreach (string p in new List<string>(ws.Views.Values))
            {
                AuiCompileResult r = Result(p);
                bool affected = r == null;
                if (r != null && r.Info != null) foreach (AuiNestedView nv in r.Info.NestedViews) if (nv.ViewName == viewStem) { affected = true; break; }
                if (affected) CompileAndPublish(p);
            }
        }

        /// <summary>A file is gone: its diagnostics and what it produced are withdrawn.</summary>
        public void Forget(string path)
        {
            string full = Path.GetFullPath(path);
            lock (_lock) _results.Remove(full);
            var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (_lock)
            {
                foreach (string producer in new[] { full, "sheet:" + full })
                {
                    Dictionary<string, List<AuiDiagnostic>> map;
                    if (_byProducer.TryGetValue(producer, out map)) { foreach (string f in map.Keys) affected.Add(f); _byProducer.Remove(producer); }
                    _depsOf.Remove(producer);
                }
                affected.Add(full);
            }
            foreach (string f in affected) PublishFile(f);
        }

        // ================================================================ debounce

        /// <summary>Re-check after a pause in typing; a save or a request that needs the result runs at once.</summary>
        public void ScheduleRecheck(string path, Action<string> recheck)
        {
            string full = Path.GetFullPath(path);
            lock (_timers)
            {
                Timer t;
                if (_timers.TryGetValue(full, out t)) t.Dispose();
                _timers[full] = new Timer(_ =>
                {
                    lock (_timers) { Timer cur; if (_timers.TryGetValue(full, out cur)) { cur.Dispose(); _timers.Remove(full); } }
                    try { recheck(full); } catch (Exception e) { Console.Error.WriteLine("[aui-lsp] recheck failed: " + e); }
                }, null, DebounceMs, Timeout.Infinite);
            }
        }

        public void CancelTimer(string path)
        {
            lock (_timers)
            {
                Timer t;
                if (_timers.TryGetValue(path, out t)) { t.Dispose(); _timers.Remove(path); }
            }
        }

        // ================================================================ diagnostics

        private void Publish(string producer, List<AuiDiagnostic> items, IEnumerable<string> dependencies, string defaultFile = null)
        {
            if (defaultFile == null) defaultFile = producer;
            var files = new Dictionary<string, List<AuiDiagnostic>>(StringComparer.OrdinalIgnoreCase);
            var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            files[Path.GetFullPath(defaultFile)] = new List<AuiDiagnostic>();
            foreach (string d in dependencies)
            {
                string full;
                try { full = Path.GetFullPath(d); } catch { continue; }
                deps.Add(full);
                if (!files.ContainsKey(full)) files[full] = new List<AuiDiagnostic>();
            }
            foreach (AuiDiagnostic d in items)
            {
                string file = string.IsNullOrEmpty(d.File) || d.File == "lang" ? defaultFile : d.File;
                string full;
                try { full = Path.GetFullPath(file); } catch { continue; }
                List<AuiDiagnostic> list;
                if (!files.TryGetValue(full, out list)) files[full] = list = new List<AuiDiagnostic>();
                list.Add(d);
            }
            var affected = new HashSet<string>(files.Keys, StringComparer.OrdinalIgnoreCase);
            lock (_lock)
            {
                Dictionary<string, List<AuiDiagnostic>> previous;
                if (_byProducer.TryGetValue(producer, out previous)) foreach (string f in previous.Keys) affected.Add(f);
                _byProducer[producer] = files;
                _depsOf[producer] = deps;
            }
            foreach (string f in affected) PublishFile(f);
        }

        /// <summary>
        /// The union of what every producer found in one file, deduplicated. "Class not used by
        /// this view" is the exception: a shared sheet's class is only unused when no view that
        /// links the sheet uses it.
        /// </summary>
        private void PublishFile(string file)
        {
            if (_server == null) return;
            var items = new List<AuiDiagnostic>();
            var seen = new HashSet<string>();
            var unusedVotes = new Dictionary<string, int>();
            var unusedItems = new Dictionary<string, AuiDiagnostic>();
            int linkers = 0;
            lock (_lock)
            {
                foreach (KeyValuePair<string, Dictionary<string, List<AuiDiagnostic>>> kv in _byProducer)
                {
                    HashSet<string> deps;
                    bool links = _depsOf.TryGetValue(kv.Key, out deps) && deps.Contains(file);
                    if (links) linkers++;
                    List<AuiDiagnostic> list;
                    if (!kv.Value.TryGetValue(file, out list)) continue;
                    foreach (AuiDiagnostic d in list)
                    {
                        string key = d.Code + "|" + d.Line + "|" + d.Column + "|" + d.Message;
                        if (d.Code == AuiCodes.UnusedClass && links)
                        {
                            int n;
                            unusedVotes.TryGetValue(key, out n);
                            unusedVotes[key] = n + 1;
                            unusedItems[key] = d;
                            continue;
                        }
                        if (seen.Add(key)) items.Add(d);
                    }
                }
            }
            foreach (KeyValuePair<string, int> kv in unusedVotes)
                if (kv.Value >= linkers && seen.Add(kv.Key)) items.Add(unusedItems[kv.Key]);

            string text = Read(file);
            var diags = new List<Diagnostic>(items.Count);
            foreach (AuiDiagnostic d in items) diags.Add(ToLsp(d, text));
            _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
            {
                Uri = DocumentUri.FromFileSystemPath(file),
                Diagnostics = new Container<Diagnostic>(diags),
            });
        }

        public static Diagnostic ToLsp(AuiDiagnostic d, string text)
        {
            int line = Math.Max(0, d.Line - 1);
            int col = Math.Max(0, d.Column - 1);
            int end = col + Math.Max(1, d.Length);
            if (d.Length <= 0 && text != null)
            {
                // Length 0 means the whole line.
                int lineLen = Cursor.LineLength(text, line);
                if (lineLen > col) { end = lineLen; if (d.Column <= 0) col = Cursor.FirstNonSpace(text, line); }
            }
            return new Diagnostic
            {
                Range = new Range(new Position(line, col), new Position(line, Math.Max(col + 1, end))),
                Severity = d.Severity == AuiSeverity.Error ? DiagnosticSeverity.Error : d.Severity == AuiSeverity.Warning ? DiagnosticSeverity.Warning : DiagnosticSeverity.Information,
                Code = d.Code,
                Source = "aui",
                Message = d.Message,
            };
        }

        public static string PathOf(DocumentUri uri)
        {
            return uri.GetFileSystemPath();
        }
    }
}
