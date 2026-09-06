using System.Collections.Generic;
using System.IO;
using Armagan.AUI.Core;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// Imports a .aui file into an AuiViewAsset: the compile's verdict, not its artefacts.
    ///
    /// AN IMPORTER CANNOT WRITE A .cs FILE OR ADD A COMPONENT WHOSE CLASS DOES NOT EXIST YET,
    /// and UdonSharp's component setup needs a compiled program asset and an Undo context, so
    /// everything with side effects (Foo.g.cs, Foo.asset, Foo.prefab) happens later in
    /// AuiBuildQueue, kicked by AuiPostprocessor. This importer only parses, resolves, lays out
    /// and reports, and declares its dependencies so a change to a stylesheet, a language file
    /// or a nested view re-imports the views that use it.
    /// </summary>
    [ScriptedImporter(1, "aui")]
    public sealed class AuiImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = ScriptableObject.CreateInstance<AuiViewAsset>();
            string abs = Path.GetFullPath(ctx.assetPath);
            AuiSettings settings = AuiEditorEnv.FindSettings();
            AuiWorkspace ws = AuiEditorEnv.Scan(settings);
            var deps = new HashSet<string>();

            AuiCompileResult r = AuiEditorEnv.Compile(abs, settings, ws, p =>
            {
                string ap = AuiEditorEnv.AssetPath(p);
                if (ap != null && ap != ctx.assetPath) deps.Add(ap);
            });

            // Nested views: depend on their artifacts so a child change re-imports the parent.
            var children = new List<string>();
            if (r.Info != null)
                foreach (AuiNestedView nv in r.Info.NestedViews)
                {
                    children.Add(nv.ViewName);
                    string cp;
                    if (ws.Views.TryGetValue(nv.ViewName, out cp))
                    {
                        string ap = AuiEditorEnv.AssetPath(cp);
                        if (ap != null) deps.Add(ap);
                    }
                }
            // Every language file, so a translation edit re-measures text boxes.
            foreach (string p in ws.LangPaths) { string ap = AuiEditorEnv.AssetPath(p); if (ap != null) deps.Add(ap); }
            foreach (string p in ws.PackageLangPaths) { string ap = AuiEditorEnv.AssetPath(p); if (ap != null) deps.Add(ap); }
            if (settings != null) deps.Add(AssetDatabase.GetAssetPath(settings));
            foreach (string d in deps) ctx.DependsOnSourceAsset(d);

            asset.viewName = r.ViewName ?? Path.GetFileNameWithoutExtension(ctx.assetPath);
            asset.hash = r.Hash ?? "";
            asset.sourceText = r.SourceText;
            asset.childViews = children.ToArray();
            asset.dependencies = new List<string>(deps).ToArray();
            asset.hasErrors = r.HasErrors;
            asset.elementCount = r.Map != null ? r.Map.ElementCount : 0;
            asset.signalCount = r.Map != null ? r.Map.SignalCount : 0;
            asset.width = r.Info != null ? r.Info.Width : 0;
            asset.height = r.Info != null ? r.Info.Height : 0;
            var lines = new List<string>();
            foreach (AuiDiagnostic d in r.Diagnostics.Items)
            {
                lines.Add(d.ToString());
                string text = d.ToString();
                if (d.Severity == AuiSeverity.Error) ctx.LogImportError(text, asset);
                else if (d.Severity == AuiSeverity.Warning) ctx.LogImportWarning(text, asset);
            }
            asset.diagnostics = lines.ToArray();

            ctx.AddObjectToAsset("view", asset);
            ctx.SetMainObject(asset);
        }
    }

    [ScriptedImporter(1, "auis")]
    public sealed class AuiStyleImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = ScriptableObject.CreateInstance<AuiStyleSheetAsset>();
            string text = File.ReadAllText(ctx.assetPath);
            AuiStyleSheet sheet = AuiStyleParser.Parse(text, ctx.assetPath, AuiStyleTier.Linked);
            asset.sourceText = text;
            asset.classNames = new List<string>(sheet.ClassNames()).ToArray();
            asset.hasErrors = sheet.Diagnostics.HasErrors;
            var lines = new List<string>();
            foreach (AuiDiagnostic d in sheet.Diagnostics.Items)
            {
                lines.Add(d.ToString());
                if (d.Severity == AuiSeverity.Error) ctx.LogImportError(d.ToString(), asset);
                else if (d.Severity == AuiSeverity.Warning) ctx.LogImportWarning(d.ToString(), asset);
            }
            asset.diagnostics = lines.ToArray();
            ctx.AddObjectToAsset("sheet", asset);
            ctx.SetMainObject(asset);
        }
    }
}
