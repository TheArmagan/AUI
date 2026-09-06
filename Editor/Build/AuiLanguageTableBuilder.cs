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
