using System.IO;
using Armagan.AUI.Core;
using UnityEditor;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>The AUI menu. Everything here is idempotent and safe to run twice.</summary>
    public static class AuiMenu
    {
        [MenuItem("AUI/Rebuild All Views", false, 1)]
        public static void RebuildAll()
        {
            AuiProgramAssets.EnsurePackageRuntime();
            AuiBuildQueue.EnqueueAll();
            AuiBuildQueue.DrainNow();
        }

        private const string AutoBuildItem = "AUI/Auto-build Views";

        [MenuItem(AutoBuildItem, false, 5)]
        public static void ToggleAutoBuild()
        {
            AuiBuildQueue.AutoBuild = !AuiBuildQueue.AutoBuild;
            AuiLog.Info(AuiBuildQueue.AutoBuild ? "auto-build on: saved views build as before." : "auto-build off: saved views are queued; use Rebuild All Views, the inspector's Rebuild view or the preview window's Build prefab.");
        }

        [MenuItem(AutoBuildItem, true)]
        private static bool ToggleAutoBuildValidate()
        {
            Menu.SetChecked(AutoBuildItem, AuiBuildQueue.AutoBuild);
            return true;
        }

        [MenuItem("AUI/Rebuild Language Table", false, 2)]
        public static void RebuildLanguageTable()
        {
            AuiLanguageTableBuilder.Rebuild();
        }

        [MenuItem("AUI/Rebuild Input Panel", false, 3)]
        public static void RebuildInputPanel()
        {
            AuiInputPanelBuilder.Rebuild();
        }

        [MenuItem("AUI/Validate", false, 20)]
        public static void Validate()
        {
            AuiSettings settings = AuiEditorEnv.FindSettings();
            AuiWorkspace ws = AuiEditorEnv.Scan(settings);
            int errors = 0, warnings = 0, views = 0;
            foreach (string p in ws.Views.Values)
            {
                AuiCompileResult r = AuiEditorEnv.Compile(p, settings, ws, null);
                views++;
                foreach (AuiDiagnostic d in r.Diagnostics.Items)
                {
                    if (d.Severity == AuiSeverity.Error) errors++;
                    else if (d.Severity == AuiSeverity.Warning) warnings++;
                }
                AuiEditorEnv.Report(r);
            }
            AuiLog.Info("validated " + views + " view(s): " + errors + " error(s), " + warnings + " warning(s). Languages: " + ws.LangPaths.Count + " project + " + ws.PackageLangPaths.Count + " package.");
        }

        [MenuItem("AUI/Create Settings", false, 40)]
        public static void CreateSettings()
        {
            AuiSettings s = AuiEditorEnv.EnsureSettings();
            Selection.activeObject = s;
            EditorGUIUtility.PingObject(s);
        }

        [MenuItem("AUI/Create missing program assets", false, 41)]
        public static void CreateProgramAssets()
        {
            AuiProgramAssets.EnsurePackageRuntime();
            AuiWorkspace ws = AuiEditorEnv.Scan(AuiEditorEnv.FindSettings());
            int made = 0;
            foreach (string p in ws.Views.Values)
            {
                string cs = AuiEditorEnv.AssetPath(Path.ChangeExtension(p, ".cs"));
                if (cs != null && File.Exists(cs) && AuiProgramAssets.Ensure(cs)) made++;
            }
            AuiLog.Info("program assets checked; created " + made + ".");
        }

        [MenuItem("AUI/Open Package Folder", false, 60)]
        public static void OpenPackage()
        {
            EditorUtility.RevealInFinder(AuiEditorEnv.PackageRoot);
        }
    }
}
