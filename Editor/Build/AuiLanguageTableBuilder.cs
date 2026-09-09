using System.Collections.Generic;
using Armagan.AUI.Core;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// Bakes the merged language table (project lang/*.json over the package's own) onto the
    /// scene's AuiLanguageService, creating the service object when the scene has none.
    /// Views carry key INDICES into this table, so the table and the views must be baked from
    /// the same files: the build queue calls this after every view build.
    /// </summary>
    public static class AuiLanguageTableBuilder
    {
        public const string ObjectName = "AUI Language Service";

        public static AuiLanguageService FindOrCreate()
        {
            AuiLanguageService svc = Object.FindObjectOfType<AuiLanguageService>(true);
            if (svc != null) return svc;
            AuiProgramAssets.EnsurePackageRuntime();
            var go = new GameObject(ObjectName);
            Undo.RegisterCreatedObjectUndo(go, "Create " + ObjectName);
            svc = (AuiLanguageService)UdonSharpUndo.AddComponent(go, typeof(AuiLanguageService));
            AuiLog.Info("created '" + ObjectName + "' in the scene.");
            return svc;
        }

        public static AuiLanguageTable LoadTable(AuiSettings settings, AuiWorkspace ws)
        {
            var diag = new AuiDiagnosticBag("lang");
            AuiLanguageTable table = ws.LoadLanguageTable(settings != null ? settings.defaultLanguage : "en", diag, null);
            AuiLog.Report(diag.Items);
            return table;
        }

        public static void Rebuild()
        {
            AuiSettings settings = AuiEditorEnv.FindSettings();
            AuiWorkspace ws = AuiEditorEnv.Scan(settings);
            AuiLanguageTable table = LoadTable(settings, ws);
            AuiLanguageService svc = FindOrCreate();
            Apply(svc, table);
            AuiLog.Info("language table: " + table.LanguageCount + " language(s) [" + string.Join(", ", table.Codes()) + "], " + table.KeyCount + " key(s).");
        }

        /// <summary>
        /// Called by the build queue after every drain that built a prefab. Views bake key INDICES,
        /// so a table the views were compiled against but the scene's service does not carry
        /// renders every new key as "??". Cheap when nothing changed: one string compare per cell.
        /// Never creates the service: an unrelated scene may be open.
        /// </summary>
        public static void ApplyIfChanged(AuiSettings settings, AuiWorkspace ws)
        {
            AuiLanguageService svc = Object.FindObjectOfType<AuiLanguageService>(true);
            if (svc == null) return;
            AuiLanguageTable table = AuiEditorEnv.LanguageTable(settings, ws, null);
            if (Same(svc.langCodes, table.Codes()) && Same(svc.langNames, table.Names()) && svc.defaultLang == table.DefaultLanguage &&
                svc.keyCount == table.KeyCount && Same(svc.keys, table.Keys.ToArray()) && Same(svc.values, table.Flat()))
                return;
            Apply(svc, table);
            AuiLog.Info("language table rebaked onto '" + svc.name + "': " + table.KeyCount + " key(s).");
            WarnIfFontsStale(settings, ws);
        }

        private static string _staleWarnedFor = "";

        /// <summary>
        /// AUI600: the characters the language files and views use no longer match what the font
        /// set was baked from. Run when the table changed (the only time the charset can move);
        /// once per distinct charset, so a session does not repeat itself.
        /// </summary>
        public static void WarnIfFontsStale(AuiSettings settings, AuiWorkspace ws)
        {
            if (settings == null || settings.fontSet == null) return;
            string now = AuiCharsetCollector.Hash(AuiCharsetCollector.Collect(settings, ws));
            if (now == settings.fontSet.bakedHash || now == _staleWarnedFor) return;
            _staleWarnedFor = now;
            AuiLog.Warn(AuiCodes.FontSetStale + " the character set changed since the last font bake; characters added since render as boxes in-world. Run AUI/Bake Fonts.");
        }

        private static bool Same(string[] a, string[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public static void Apply(AuiLanguageService svc, AuiLanguageTable table)
        {
            if (svc == null) return;
            Undo.RecordObject(svc, "Bake language table");
            svc.langCodes = table.Codes();
            svc.langNames = table.Names();
            svc.defaultLang = table.DefaultLanguage;
            svc.keyCount = table.KeyCount;
            svc.keys = table.Keys.ToArray();
            svc.values = table.Flat();
            AuiProgramAssets.ApplySyncMode(svc);
            EditorUtility.SetDirty(svc);
            UdonSharpEditorUtility.CopyProxyToUdon(svc);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(svc.gameObject.scene);
        }
    }
}
