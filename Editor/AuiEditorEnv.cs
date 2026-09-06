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

        public static AuiWorkspace Scan(AuiSettings settings)
        {
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
            return w;
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

        /// <summary>Compile one view the Unity way. <paramref name="onRead"/> lets the importer record dependencies.</summary>
        public static AuiCompileResult Compile(string absolutePath, AuiSettings settings, AuiWorkspace ws, System.Action<string> onRead)
        {
            string text = File.ReadAllText(absolutePath);
            if (onRead != null) onRead(absolutePath);
            System.Func<string, string> read = p =>
            {
                if (!File.Exists(p)) return null;
                if (onRead != null) onRead(p);
                return File.ReadAllText(p);
            };
            var langDiag = new AuiDiagnosticBag(absolutePath);
            AuiLanguageTable lang = ws.LoadLanguageTable(settings != null ? settings.defaultLanguage : "en", langDiag, read);
            var opt = new AuiCompileOptions
            {
                Theme = ws.LoadTheme(read),
                ReadFile = read,
                Lang = lang,
                Measurer = new AuiTextMeasurer(settings),
                ViewExists = ws.ViewExists,
                IconExists = ws.IconExists,
                ImageSize = (string src, out float w, out float h) => ImageSize(absolutePath, src, out w, out h),
                ChildInfo = name => ChildInfo(name, ws, settings, read),
                BoundTextSample = settings != null && !string.IsNullOrEmpty(settings.boundTextSample) ? settings.boundTextSample : "Mmmmmmmmmmmm",
            };
            AuiCompileResult r = AuiCompiler.Compile(text, absolutePath, opt);
            // Language-file syntax problems belong to those files but are worth seeing here once.
            foreach (AuiDiagnostic d in langDiag.Items) if (d.Severity == AuiSeverity.Error) r.Diagnostics.Items.Add(d);
            return r;
        }

        private static readonly Dictionary<string, AuiViewInfo> ChildCache = new Dictionary<string, AuiViewInfo>();

        private static AuiViewInfo ChildInfo(string name, AuiWorkspace ws, AuiSettings settings, System.Func<string, string> read)
        {
            string path;
            if (!ws.Views.TryGetValue(name, out path)) return null;
            string text = read(path);
            if (text == null) return null;
            AuiMarkupDocument doc = AuiParser.Parse(text, path);
            return AuiAnalyzer.Analyze(doc, new AuiAnalysisContext { FileStem = name, ViewExists = ws.ViewExists }, new AuiDiagnosticBag(path));
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
