using System.Collections.Generic;
using System.IO;
using Armagan.AUI.Core;
using UnityEditor;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// The Unity host's view of the world: where the package is, which AuiSettings applies,
    /// the workspace scan, and a compile of one view with everything wired the Unity way
    /// (TMP metrics, nested-view lookup through the workspace, language table from the
    /// settings' folder). The importer, the build queue, the menu and the validator all go
    /// through here so they agree on every input.
    /// </summary>
    public static class AuiEditorEnv
    {
        public const string PackageName = "rest.armagan.aui";
        public const string DefaultSettingsPath = "Assets/AUI/AuiSettings.asset";

        public static string PackageRoot
        {
            get
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + PackageName + "/package.json");
                if (info != null) return info.resolvedPath;
                return Path.GetFullPath("Packages/" + PackageName);
            }
        }

        public static string PackageAssetRoot
        {
            get { return "Packages/" + PackageName; }
        }

        public static AuiSettings FindSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:AuiSettings");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<AuiSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        public static AuiSettings EnsureSettings()
        {
            AuiSettings s = FindSettings();
            if (s != null) return s;
            Directory.CreateDirectory("Assets/AUI");
            Directory.CreateDirectory("Assets/AUI/lang");
            s = ScriptableObject.CreateInstance<AuiSettings>();
            AssetDatabase.CreateAsset(s, DefaultSettingsPath);
            AssetDatabase.SaveAssets();
            AuiLog.Info("created " + DefaultSettingsPath);
            return s;
        }

        // THE WORKSPACE SCAN IS CACHED, AND DROPPED PRECISELY. Walking a world's Assets tree
        // costs seconds, and every .aui import and every queue drain used to do it again, plus
        // re-read every language file and re-parse the theme, because the postprocessor threw
        // everything away on any .aui/.auis/.json/.svg import. Now: the scan only changes when a
        // .aui or .svg appears, disappears or moves (or the settings change); the language table
        // only when a language file's content changes; the theme only when the theme file does.
        // A re-saved view or a stylesheet edit invalidates nothing here (sheets are read fresh
        // by every compile anyway).
        private static AuiWorkspace _ws;
        private static AuiSettings _wsSettings;
        private static AuiLanguageTable _lang;
        private static AuiStyleSheet _theme;
        private static AuiDiagnosticBag _langDiag;

        private static string _scanReason = "first scan";

        /// <summary>Drop everything: the scan, the language table, the theme and the child cache. <paramref name="reason"/> is logged by the next scan.</summary>
        public static void InvalidateWorkspace(string reason = "invalidated")
        {
            _ws = null;
            _lang = null;
            _theme = null;
            _langDiag = null;
            _scanReason = reason;
            ChildCache.Clear();
        }

        /// <summary>A language file's content changed: the merged table must be rebuilt, the scan is still right.</summary>
        public static void InvalidateLanguages()
        {
            _lang = null;
            _langDiag = null;
        }

        /// <summary>The package theme changed.</summary>
        public static void InvalidateTheme()
        {
            _theme = null;
        }

        /// <summary>The currently cached scan, or null. Lets the postprocessor tell a new file from a re-saved one.</summary>
        public static AuiWorkspace CachedWorkspace
        {
            get { return _ws; }
        }

        /// <summary>True when the cached scan already lists this file (a view, an icon or a language file), so its re-import needs no rescan.</summary>
        public static bool WorkspaceKnows(string absolutePath)
        {
            if (_ws == null) return false;
            string full = Norm(absolutePath);
            string ext = Path.GetExtension(full).ToLowerInvariant();
            if (ext == ".aui")
            {
                // The stem from the ORIGINAL path: view names are case-sensitive keys, the normalized path is lower-case.
                string p;
                return _ws.Views.TryGetValue(Path.GetFileNameWithoutExtension(absolutePath), out p) && Norm(p) == full;
            }
            if (ext == ".svg") { foreach (string p in _ws.IconPaths) if (Norm(p) == full) return true; return false; }
            if (ext == ".json")
            {
                foreach (string p in _ws.LangPaths) if (Norm(p) == full) return true;
                foreach (string p in _ws.PackageLangPaths) if (Norm(p) == full) return true;
                return false;
            }
            return true;
        }

        public static bool IsThemePath(string absolutePath)
        {
            if (_ws == null || _ws.ThemePath == null) return false;
            string a = Norm(absolutePath), t = Norm(_ws.ThemePath);
            // A junction under Packages/ resolves to two spellings of the same file; the tail is enough.
            return a == t || (a.EndsWith("/theme/default.auis") && t.EndsWith("/theme/default.auis"));
        }

        private static string Norm(string p)
        {
            return Path.GetFullPath(p).Replace('\\', '/').ToLowerInvariant();
        }

        public static AuiWorkspace Scan(AuiSettings settings)
        {
            if (_ws != null && _wsSettings == settings) return _ws;
            // Scans should be rare now; when they are not, the reason is the first thing to know.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            string why = _ws != null ? "settings object changed" : _scanReason;
            _scanReason = "invalidated";
            string lang = settings != null && !string.IsNullOrEmpty(settings.langFolder) ? Path.GetFullPath(settings.langFolder) : null;
            AuiWorkspace w = AuiWorkspace.Scan(Path.GetFullPath("Assets"), PackageRoot, lang);
            if (settings != null)
            {
                foreach (string f in settings.componentFolders)
                    if (!string.IsNullOrEmpty(f) && Directory.Exists(f))
                        foreach (string p in Directory.GetFiles(f, "*.aui")) w.Views[Path.GetFileNameWithoutExtension(p)] = Path.GetFullPath(p);
                foreach (string f in settings.iconFolders)
                    if (!string.IsNullOrEmpty(f) && Directory.Exists(f))
                        foreach (string p in Directory.GetFiles(f, "*.svg")) w.IconPaths.Insert(0, Path.GetFullPath(p));
            }
            _ws = w;
            _wsSettings = settings;
            _lang = null;
            _theme = null;
            _langDiag = null;
            ChildCache.Clear();
            AuiLog.Info("workspace scan (" + why + "): " + w.Views.Count + " view(s), " + clock.ElapsedMilliseconds + " ms");
            return w;
        }

        /// <summary>
        /// The scan, made right for one view: the importer runs before the postprocessor in an
        /// import batch, so a view created a moment ago is not in the cached scan yet. When the
        /// view's own stem is missing or points elsewhere, rescan first.
        /// </summary>
        public static AuiWorkspace ScanFor(string absoluteViewPath, AuiSettings settings)
        {
            AuiWorkspace ws = Scan(settings);
            if (!WorkspaceKnows(absoluteViewPath))
            {
                string p;
                _ws.Views.TryGetValue(Path.GetFileNameWithoutExtension(absoluteViewPath), out p);
                InvalidateWorkspace("view not in scan: " + absoluteViewPath + (p != null ? " (scan has " + p + ")" : ""));
                ws = Scan(settings);
            }
            return ws;
        }

        /// <summary>The merged language table for the cached workspace; built once per invalidation.</summary>
        public static AuiLanguageTable LanguageTable(AuiSettings settings, AuiWorkspace ws, System.Func<string, string> read)
        {
            if (_lang == null || ws != _ws)
            {
                _langDiag = new AuiDiagnosticBag("lang");
                _lang = ws.LoadLanguageTable(settings != null ? settings.defaultLanguage : "en", _langDiag, read);
            }
            else if (read != null)
            {
                // The importer records dependencies through `read`; touch the files so they are declared.
                foreach (string p in ws.PackageLangPaths) read(p);
                foreach (string p in ws.LangPaths) read(p);
            }
            return _lang;
        }

        public static AuiStyleSheet Theme(AuiWorkspace ws, System.Func<string, string> read)
        {
            if (_theme == null || ws != _ws) _theme = ws.LoadTheme(read);
            else if (read != null && ws.ThemePath != null) read(ws.ThemePath);
            return _theme;
        }

        /// <summary>Unity asset path ("Assets/..." or "Packages/...") from an absolute path, or null when outside the project.</summary>
        public static string AssetPath(string absolute)
        {
            if (string.IsNullOrEmpty(absolute)) return null;
            string full = Path.GetFullPath(absolute).Replace('\\', '/');
            string project = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/') + "/";
            if (full.StartsWith(project)) return full.Substring(project.Length);
            string pkg = PackageRoot.Replace('\\', '/').TrimEnd('/') + "/";
            if (full.StartsWith(pkg)) return PackageAssetRoot + "/" + full.Substring(pkg.Length);
            return null;
        }

        // COMPILE RESULTS ARE REUSED. The importer compiles a view, the queue compiled it again
        // in the drain, and a third time when it had to retry: three compiles for one save. A
        // result is kept per view together with a hash of every file that compile read (the
        // source, its sheets, the theme, the language files, nested views) and the identity of
        // the cached scan, table and theme it used. A lookup re-hashes those few small files,
        // which is far cheaper than a compile, and hands the whole result back; the builder
        // compares node references, so a result is never assembled from two compiles.
        private sealed class CachedCompile
        {
            public AuiCompileResult Result;
            public AuiWorkspace Ws;
            public AuiLanguageTable Lang;
            public AuiStyleSheet Theme;
            public string Stamp;
            public Dictionary<string, string> Files;   // absolute path -> sha1 of text, "" when missing
        }

        private static readonly Dictionary<string, CachedCompile> CompileCache = new Dictionary<string, CachedCompile>();

        /// <summary>True when the last <see cref="Compile"/> call was served from the cache.</summary>
        public static bool LastCompileCached { get; private set; }

        /// <summary>Why the last <see cref="Compile"/> call missed the cache ("" on a hit).</summary>
        public static string LastCompileMiss { get; private set; }

        private static string Stamp(AuiSettings settings)
        {
            return settings == null ? "" :
                (settings.fontSet != null ? settings.fontSet.GetInstanceID() + ":" + settings.fontSet.bakedHash : "nofont") + "|" +
                settings.boundTextSample + "|" + settings.defaultLanguage;
        }

        private static string HashText(string text)
        {
            return text == null ? "" : AuiCompiler.Sha1(text);
        }

        /// <summary>Compile one view the Unity way. <paramref name="onRead"/> lets the importer record dependencies.</summary>
        public static AuiCompileResult Compile(string absolutePath, AuiSettings settings, AuiWorkspace ws, System.Action<string> onRead)
        {
            string key = Norm(absolutePath);
            string stamp = Stamp(settings);
            AuiLanguageTable langNow = LanguageTable(settings, ws, null);
            AuiStyleSheet themeNow = Theme(ws, null);

            CachedCompile hit;
            string miss;
            if (!CompileCache.TryGetValue(key, out hit)) miss = "no entry";
            else if (hit.Ws != ws) miss = "workspace rescanned";
            else if (hit.Lang != langNow) miss = "language table rebuilt";
            else if (hit.Theme != themeNow) miss = "theme reparsed";
            else if (hit.Stamp != stamp) miss = "settings/fonts changed";
            else
            {
                miss = null;
                foreach (KeyValuePair<string, string> f in hit.Files)
                {
                    string text = File.Exists(f.Key) ? SafeRead(f.Key) : null;
                    if (HashText(text) != f.Value) { miss = "changed: " + f.Key; break; }
                }
                if (miss == null)
                {
                    if (onRead != null) foreach (string p in hit.Files.Keys) onRead(p);
                    LastCompileCached = true;
                    LastCompileMiss = "";
                    return hit.Result;
                }
            }
            LastCompileCached = false;
            LastCompileMiss = miss;

            var seen = new Dictionary<string, string>();
            string source = File.ReadAllText(absolutePath);
            seen[absolutePath] = HashText(source);
            if (onRead != null) onRead(absolutePath);
            System.Func<string, string> read = p =>
            {
                string text = File.Exists(p) ? SafeRead(p) : null;
                seen[p] = HashText(text);
                if (text != null && onRead != null) onRead(p);
                return text;
            };
            AuiLanguageTable lang = LanguageTable(settings, ws, read);
            AuiDiagnosticBag langDiag = _langDiag ?? new AuiDiagnosticBag(absolutePath);
            var opt = new AuiCompileOptions
            {
                Theme = Theme(ws, read),
                ReadFile = read,
                Lang = lang,
                Measurer = new AuiTextMeasurer(settings),
                ViewExists = ws.ViewExists,
                IconExists = ws.IconExists,
                ImageSize = (string src, out float w, out float h) => ImageSize(absolutePath, src, out w, out h),
                ChildInfo = name => ChildInfo(name, ws, settings, read),
                BoundTextSample = settings != null && !string.IsNullOrEmpty(settings.boundTextSample) ? settings.boundTextSample : "Mmmmmmmmmmm日",
            };
            AuiCompileResult r = AuiCompiler.Compile(source, absolutePath, opt);
            // Language-file syntax problems belong to those files but are worth seeing here once.
            foreach (AuiDiagnostic d in langDiag.Items) if (d.Severity == AuiSeverity.Error) r.Diagnostics.Items.Add(d);

            CompileCache[key] = new CachedCompile { Result = r, Ws = ws, Lang = lang, Theme = opt.Theme, Stamp = stamp, Files = seen };
            return r;
        }

        private static string SafeRead(string path)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) { return null; }
        }

        /// <summary>Forget a cached compile (a file was deleted or renamed).</summary>
        public static void ForgetCompile(string absolutePath)
        {
            CompileCache.Remove(Norm(absolutePath));
        }

        // A nested view's model was re-parsed and re-analyzed on every reference, in every
        // compile of every parent. The text is still read (so the dependency is recorded), but
        // the model is reused while the text is unchanged.
        private sealed class CachedChild
        {
            public string Text;
            public AuiViewInfo Info;
        }

        private static readonly Dictionary<string, CachedChild> ChildCache = new Dictionary<string, CachedChild>();

        private static AuiViewInfo ChildInfo(string name, AuiWorkspace ws, AuiSettings settings, System.Func<string, string> read)
        {
            string path;
            if (!ws.Views.TryGetValue(name, out path)) return null;
            string text = read(path);
            if (text == null) return null;
            CachedChild c;
            if (ChildCache.TryGetValue(path, out c) && c.Text == text) return c.Info;
            AuiMarkupDocument doc = AuiParser.Parse(text, path);
            AuiViewInfo info = AuiAnalyzer.Analyze(doc, new AuiAnalysisContext { FileStem = name, ViewExists = ws.ViewExists }, new AuiDiagnosticBag(path));
            ChildCache[path] = new CachedChild { Text = text, Info = info };
            return info;
        }

        private static bool ImageSize(string viewPath, string src, out float w, out float h)
        {
            w = h = 0;
            string p = Path.Combine(Path.GetDirectoryName(viewPath) ?? "", src);
            string ap = AssetPath(p);
            if (ap == null) return false;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ap);
            if (sprite == null) return false;
            // A sprite's pixel size in mm at 1 px = 1 mm keeps authoring intuitive.
            w = sprite.rect.width;
            h = sprite.rect.height;
            return true;
        }

        /// <summary>Lines the console can click: "[AUI] path:line:col code: message".</summary>
        public static void Report(AuiCompileResult r)
        {
            AuiLog.Report(r.Diagnostics.Items);
        }
    }
}
