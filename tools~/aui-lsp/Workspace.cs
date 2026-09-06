using System;
using System.Collections.Generic;
using System.IO;
using Armagan.AUI.Core;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Armagan.AUI.Lsp
{
    /// <summary>
    /// Everything the handlers share: where the files are, the text of open documents (which
    /// wins over the disk), the last compile of every view, and diagnostics publishing.
    ///
    /// The package root is found by walking up from the workspace root for a package.json named
    /// rest.armagan.aui, then by looking under Packages/. The language folder is Assets/AUI/lang
    /// when it exists, else every folder called lang.
    /// </summary>
    public sealed class Workspace
    {
        public string Root;
        public string PackageRoot;
        public string LangFolder;
        private ILanguageServerFacade _server;
        private readonly Dictionary<string, string> _open = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AuiCompileResult> _results = new Dictionary<string, AuiCompileResult>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        public void Initialize(string root)
        {
            Root = string.IsNullOrEmpty(root) ? Directory.GetCurrentDirectory() : Path.GetFullPath(root);
            PackageRoot = FindPackageRoot(Root);
            string assetsLang = Path.Combine(Root, "Assets", "AUI", "lang");
            LangFolder = Directory.Exists(assetsLang) ? assetsLang : null;
        }

        public void Attach(ILanguageServerFacade server)
        {
            _server = server;
        }

        private static string FindPackageRoot(string root)
        {
            foreach (string candidate in new[]
            {
                root,
                Path.Combine(root, "Packages", "rest.armagan.aui"),
            })
            {
                string pj = Path.Combine(candidate, "package.json");
                if (File.Exists(pj) && File.ReadAllText(pj).Contains("\"rest.armagan.aui\"")) return candidate;
            }
            // A world with the package embedded anywhere under Packages/
            string packages = Path.Combine(root, "Packages");
            if (Directory.Exists(packages))
                foreach (string dir in Directory.GetDirectories(packages))
                {
                    string pj = Path.Combine(dir, "package.json");
                    if (File.Exists(pj) && File.ReadAllText(pj).Contains("\"rest.armagan.aui\"")) return dir;
                }
            return null;
        }

        public AuiWorkspace Scan()
        {
            string scanRoot = Directory.Exists(Path.Combine(Root, "Assets")) ? Path.Combine(Root, "Assets") : Root;
            return AuiWorkspace.Scan(scanRoot, PackageRoot, LangFolder);
        }

        // ---------------------------------------------------------------- documents

        public void Open(string path, string text)
        {
            lock (_lock) _open[Path.GetFullPath(path)] = text;
        }

        public void Change(string path, string text)
        {
            lock (_lock) _open[Path.GetFullPath(path)] = text;
        }

        public void Close(string path)
        {
            lock (_lock) _open.Remove(Path.GetFullPath(path));
        }

        public string Read(string path)
        {
            string full = Path.GetFullPath(path);
            lock (_lock)
            {
                string t;
                if (_open.TryGetValue(full, out t)) return t;
            }
            return File.Exists(full) ? File.ReadAllText(full) : null;
        }

        public bool IsOpen(string path)
        {
            lock (_lock) return _open.ContainsKey(Path.GetFullPath(path));
        }

        // ---------------------------------------------------------------- compile

        public AuiCompileResult Compile(string viewPath)
        {
            AuiWorkspace ws = Scan();
            string text = Read(viewPath) ?? "";
            var langDiag = new AuiDiagnosticBag(viewPath);
            AuiLanguageTable lang = ws.LoadLanguageTable("en", langDiag, Read);
            var opt = new AuiCompileOptions
            {
                Theme = ws.LoadTheme(Read),
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

        public AuiCompileResult Result(string viewPath)
        {
            lock (_lock)
            {
                AuiCompileResult r;
                return _results.TryGetValue(Path.GetFullPath(viewPath), out r) ? r : null;
            }
        }

        /// <summary>Compile one view and publish its diagnostics, grouped by the file they belong to.</summary>
        public AuiCompileResult CompileAndPublish(string viewPath)
        {
            AuiCompileResult r = Compile(viewPath);
            Publish(r);
            return r;
        }

        /// <summary>Every view in the workspace: the cheap way to reflect a language-file or stylesheet edit.</summary>
        public void RecompileAll()
        {
            AuiWorkspace ws = Scan();
            foreach (string p in ws.Views.Values) CompileAndPublish(p);
        }

        /// <summary>Views that use a stylesheet or language file, so an edit there re-checks them.</summary>
        public void RecompileDependents(string changedPath)
        {
            string full = Path.GetFullPath(changedPath);
            AuiWorkspace ws = Scan();
            foreach (string p in ws.Views.Values)
            {
                AuiCompileResult r = Result(p);
                bool affected = r == null || changedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                if (r != null) foreach (string d in r.Dependencies) if (string.Equals(Path.GetFullPath(d), full, StringComparison.OrdinalIgnoreCase)) affected = true;
                if (affected) CompileAndPublish(p);
            }
        }

        private void Publish(AuiCompileResult r)
        {
            if (_server == null) return;
            var byFile = new Dictionary<string, List<Diagnostic>>(StringComparer.OrdinalIgnoreCase);
            byFile[Path.GetFullPath(r.FilePath)] = new List<Diagnostic>();
            foreach (string d in r.Dependencies) byFile[Path.GetFullPath(d)] = new List<Diagnostic>();
            foreach (AuiDiagnostic d in r.Diagnostics.Items)
            {
                string file = string.IsNullOrEmpty(d.File) ? r.FilePath : d.File;
                string full;
                try { full = Path.GetFullPath(file); } catch { continue; }
                List<Diagnostic> list;
                if (!byFile.TryGetValue(full, out list)) byFile[full] = list = new List<Diagnostic>();
                list.Add(ToLsp(d));
            }
            foreach (KeyValuePair<string, List<Diagnostic>> kv in byFile)
            {
                // A stylesheet or language file is shared by many views; only the view's own file
                // and its direct dependencies are (re)published from this compile.
                _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
                {
                    Uri = DocumentUri.FromFileSystemPath(kv.Key),
                    Diagnostics = new Container<Diagnostic>(kv.Value),
                });
            }
        }

        public static Diagnostic ToLsp(AuiDiagnostic d)
        {
            int line = Math.Max(0, d.Line - 1);
            int col = Math.Max(0, d.Column - 1);
            int len = d.Length > 0 ? d.Length : 1;
            return new Diagnostic
            {
                Range = new Range(new Position(line, col), new Position(line, col + len)),
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
