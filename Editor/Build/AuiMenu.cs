using System.Collections.Generic;
using System.IO;
using Armagan.AUI.Core;
using UnityEditor;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>The AUI menu. Everything here is idempotent and safe to run twice.</summary>
    public static class AuiMenu
    {
        /// <summary>
        /// Every view, but only the ones whose prefab is out of date (bake hash, BuilderVersion) and
        /// the views that nest one of those. Unchanged prefabs are left alone, which also keeps any
        /// wiring other tools put into them.
        /// </summary>
        [MenuItem("AUI/Rebuild All Views", false, 1)]
        public static void RebuildAll()
        {
            AuiProgramAssets.EnsurePackageRuntime();
            AuiBuildQueue.EnqueueAll(false);
            AuiBuildQueue.DrainNow();
        }

        /// <summary>Every view, rebuilt even when its bake hash matches (what Rebuild All did before 2026-11).</summary>
        [MenuItem("AUI/Force Rebuild All Views", false, 2)]
        public static void ForceRebuildAll()
        {
            AuiProgramAssets.EnsurePackageRuntime();
            AuiBuildQueue.EnqueueAll(true);
            AuiBuildQueue.DrainNow();
        }

        [MenuItem("AUI/Rebuild Selected Views", false, 3)]
        public static void RebuildSelected()
        {
            AuiBuildQueue.RebuildViews(SelectedViews());
        }

        [MenuItem("AUI/Rebuild Selected Views", true)]
        private static bool RebuildSelectedValidate()
        {
            return SelectedViews().Count > 0;
        }

        // The Project window's context menu shows Assets/ items.
        [MenuItem("Assets/AUI/Rebuild View", false, 1000)]
        private static void RebuildSelectedAssets()
        {
            RebuildSelected();
        }

        [MenuItem("Assets/AUI/Rebuild View", true)]
        private static bool RebuildSelectedAssetsValidate()
        {
            return SelectedViews().Count > 0;
        }

        // The Hierarchy's context menu shows GameObject/ items with priority 0..49 near the top; 49 keeps it out of the way.
        [MenuItem("GameObject/AUI/Rebuild View", false, 49)]
        private static void RebuildSelectedObjects()
        {
            RebuildSelected();
        }

        [MenuItem("GameObject/AUI/Rebuild View", true)]
        private static bool RebuildSelectedObjectsValidate()
        {
            return SelectedViews().Count > 0;
        }

        [MenuItem("AUI/Build Status", false, 6)]
        public static void OpenBuildStatus()
        {
            AuiBuildStatusWindow.Open();
        }

        /// <summary>
        /// The .aui asset paths the selection stands for: a .aui itself, any of its generated siblings
        /// (Foo.prefab, Foo.cs, Foo.g.cs, Foo.asset), a folder (every view under it), or a scene object
        /// with an AuiView on it or above it.
        /// </summary>
        public static List<string> SelectedViews()
        {
            var result = new List<string>();
            foreach (Object o in Selection.objects)
            {
                if (o == null) continue;
                var go = o as GameObject;
                if (go != null && !EditorUtility.IsPersistent(go))
                {
                    AuiView v = go.GetComponentInParent<AuiView>(true);
                    if (v != null)
                    {
                        var script = MonoScript.FromMonoBehaviour(v);
                        string cs = script != null ? AssetDatabase.GetAssetPath(script) : null;
                        if (!string.IsNullOrEmpty(cs)) AddView(result, Path.ChangeExtension(cs, ".aui"));
                    }
                    continue;
                }
                string path = AssetDatabase.GetAssetPath(o);
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (string guid in AssetDatabase.FindAssets("", new[] { path }))
                    {
                        string p = AssetDatabase.GUIDToAssetPath(guid);
                        if (p.EndsWith(".aui")) AddView(result, p);
                    }
                    continue;
                }
                if (path.EndsWith(".g.cs")) path = path.Substring(0, path.Length - 5) + ".aui";
                else path = Path.ChangeExtension(path, ".aui");
                AddView(result, path);
            }
            return result;
        }

        private static void AddView(List<string> into, string auiPath)
        {
            auiPath = auiPath.Replace('\\', '/');
            if (!auiPath.EndsWith(".aui") || into.Contains(auiPath) || !File.Exists(auiPath)) return;
            into.Add(auiPath);
        }

        private const string AutoBuildItem = "AUI/Auto-build Views";

        [MenuItem(AutoBuildItem, false, 5)]
        public static void ToggleAutoBuild()
        {
            AuiBuildQueue.AutoBuild = !AuiBuildQueue.AutoBuild;
            AuiLog.Info(AuiBuildQueue.AutoBuild ? "auto-build on: saved views build as before." : "auto-build off: saved views are queued; use Rebuild All Views, Rebuild Selected Views, the inspector's Rebuild view or the preview window's Build prefab.");
        }

        [MenuItem(AutoBuildItem, true)]
        private static bool ToggleAutoBuildValidate()
        {
            Menu.SetChecked(AutoBuildItem, AuiBuildQueue.AutoBuild);
            return true;
        }

        [MenuItem("AUI/Rebuild Language Table", false, 20)]
        public static void RebuildLanguageTable()
        {
            AuiLanguageTableBuilder.Rebuild();
        }

        [MenuItem("AUI/Rebuild Input Panel", false, 21)]
        public static void RebuildInputPanel()
        {
            AuiInputPanelBuilder.Rebuild();
        }

        [MenuItem("AUI/Validate", false, 30)]
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
