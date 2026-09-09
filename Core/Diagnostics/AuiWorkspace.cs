using System.Collections.Generic;
using System.IO;

namespace Armagan.AUI.Core
{
    /// <summary>
    /// Finds the files a view compile depends on: other views (by file stem), the theme,
    /// language files. The Unity host points it at Assets/ plus the package; the language
    /// server points it at the opened folder. Directories ending in '~' (Unity hides them),
    /// Library, obj and node_modules are skipped.
    /// </summary>
    public sealed class AuiWorkspace
    {
        public string Root;
        public string PackageRoot;
        public readonly Dictionary<string, string> Views = new Dictionary<string, string>();   // stem -> path
        /// <summary>
        /// "stem: pathA, pathB" for every stem seen more than once. Nested views resolve by stem,
        /// so two Card.aui in different folders would otherwise pick one by directory walk order
        /// and nobody would be told. Hosts report these (AUI107).
        /// </summary>
        public readonly List<string> DuplicateViews = new List<string>();
        public readonly List<string> StylePaths = new List<string>();
        public readonly List<string> LangPaths = new List<string>();
        public readonly List<string> PackageLangPaths = new List<string>();
        public readonly List<string> IconPaths = new List<string>();
        public string ThemePath;
        /// <summary>When set, only this folder's *.json files are the project's languages.</summary>
        public string LangFolder;

        public static AuiWorkspace Scan(string root, string packageRoot, string langFolder = null)
        {
            var w = new AuiWorkspace { Root = root, PackageRoot = packageRoot, LangFolder = langFolder };
            if (!string.IsNullOrEmpty(packageRoot) && Directory.Exists(packageRoot))
            {
                string theme = Path.Combine(packageRoot, "Theme", "Default.auis");
                if (File.Exists(theme)) w.ThemePath = theme;
                string plang = Path.Combine(packageRoot, "lang");
                if (Directory.Exists(plang)) foreach (string f in Directory.GetFiles(plang, "*.json")) w.PackageLangPaths.Add(f);
                string icons = Path.Combine(packageRoot, "Icons");
                if (Directory.Exists(icons)) foreach (string f in Directory.GetFiles(icons, "*.svg")) w.IconPaths.Add(f);
                // The package's own views (the language picker) are components too.
                string rv = Path.Combine(packageRoot, "Runtime", "Views");
                if (Directory.Exists(rv)) w.Walk(rv, true);
            }
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) w.Walk(root, false);
            if (!string.IsNullOrEmpty(langFolder) && Directory.Exists(langFolder))
            {
                w.LangPaths.Clear();
                foreach (string f in Directory.GetFiles(langFolder, "*.json")) w.LangPaths.Add(f);
            }
            return w;
        }

        private void Walk(string dir, bool packageViewsOnly)
        {
            string name = Path.GetFileName(dir);
            if (name.EndsWith("~") || name == "Library" || name == "obj" || name == "node_modules" || name == ".git") return;
            Include(dir, packageViewsOnly);
        }

        /// <summary>
        /// Adds a folder tree without the skip rule for the folder itself, so a host can pull in
        /// a tree the scan would otherwise ignore (the language server does this for a view opened
        /// under Samples~).
        /// </summary>
        public void Include(string dir, bool packageViewsOnly = false)
        {
            if (!Directory.Exists(dir)) return;
            bool isLang = string.Equals(Path.GetFileName(dir), "lang", System.StringComparison.OrdinalIgnoreCase);
            foreach (string f in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".aui")
                {
                    string stem = Path.GetFileNameWithoutExtension(f);
                    string existing;
                    if (Views.TryGetValue(stem, out existing) && !string.Equals(existing, f, System.StringComparison.OrdinalIgnoreCase))
                        DuplicateViews.Add(stem + ": " + existing + ", " + f);
                    Views[stem] = f;
                }
                else if (packageViewsOnly) continue;
                else if (ext == ".auis") StylePaths.Add(f);
                else if (ext == ".json" && isLang && LangFolder == null) LangPaths.Add(f);
                else if (ext == ".svg") IconPaths.Add(f);
            }
            foreach (string d in Directory.GetDirectories(dir)) Walk(d, packageViewsOnly);
        }

        public bool ViewExists(string name)
        {
            return Views.ContainsKey(name);
        }

        public bool IconExists(string name)
        {
            foreach (string p in IconPaths) if (Path.GetFileNameWithoutExtension(p) == name) return true;
            return false;
        }

        public IEnumerable<string> IconNames()
        {
            foreach (string p in IconPaths) yield return Path.GetFileNameWithoutExtension(p);
        }

        public AuiStyleSheet LoadTheme(System.Func<string, string> read = null)
        {
            if (ThemePath == null) return null;
            string text = read != null ? read(ThemePath) : File.ReadAllText(ThemePath);
            return text == null ? null : AuiStyleParser.Parse(text, ThemePath, AuiStyleTier.Theme);
        }

        /// <summary>Project languages plus the package's own keys merged in underneath (a project key wins).</summary>
        public AuiLanguageTable LoadLanguageTable(string defaultCode, AuiDiagnosticBag diagnostics, System.Func<string, string> read = null)
        {
            var files = new Dictionary<string, AuiLanguageFile>();
            // The project decides which languages exist; the package only contributes its own
            // keys (aui.lang.*) to those. With no project languages at all, the package's are used.
            var projectCodes = new HashSet<string>();
            foreach (string p in LangPaths) projectCodes.Add(Path.GetFileNameWithoutExtension(p));
            foreach (string p in PackageLangPaths)
                if (projectCodes.Count == 0 || projectCodes.Contains(Path.GetFileNameWithoutExtension(p))) Merge(files, p, read, diagnostics);
            foreach (string p in LangPaths) Merge(files, p, read, diagnostics);
            return AuiLanguageTable.Build(files.Values, defaultCode);
        }

        private static void Merge(Dictionary<string, AuiLanguageFile> files, string path, System.Func<string, string> read, AuiDiagnosticBag d)
        {
            string code = Path.GetFileNameWithoutExtension(path);
            string text = read != null ? read(path) : File.ReadAllText(path);
            if (text == null) return;
            AuiLanguageFile lf = AuiLanguageFile.Load(code, text, path);
            if (d != null) d.AddRange(lf.Data.Diagnostics);
            AuiLanguageFile existing;
            if (!files.TryGetValue(code, out existing)) { files[code] = lf; return; }
            // later file (the project's) overrides key by key
            foreach (string k in lf.Data.Order)
            {
                if (!existing.Data.Values.ContainsKey(k)) existing.Data.Order.Add(k);
                existing.Data.Values[k] = lf.Data.Values[k];
                existing.Data.Positions[k] = lf.Data.Positions[k];
            }
            if (lf.Data.Values.ContainsKey(AuiLanguageFile.NameKey)) existing.Name = lf.Name;
        }
    }
}
