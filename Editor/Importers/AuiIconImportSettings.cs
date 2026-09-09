using Unity.VectorGraphics;
using Unity.VectorGraphics.Editor;
using UnityEditor;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// Every SVG under the package's Icons/ folder (and a consumer's AUI icon folders) is
    /// imported as a Textured Sprite: the builder assigns the sprite to an Image and tints it
    /// through Image.color, which needs white source art. Set at preprocess time so a freshly
    /// copied SVG needs no hand configuration.
    /// </summary>
    internal sealed class AuiIconImportSettings : AssetPostprocessor
    {
        /// <summary>One-shot pass for icons imported before this class existed (or copied in while it was compiling).</summary>
        [MenuItem("AUI/Reimport Icons", false, 71)]
        public static void ReimportAll()
        {
            int n = 0;
            var folders = new System.Collections.Generic.List<string> { AuiEditorEnv.PackageAssetRoot + "/Icons" };
            // The consumer's own icon folders too: they were scanned for names but never reimported.
            AuiSettings settings = AuiEditorEnv.FindSettings();
            if (settings != null)
                foreach (string f in settings.iconFolders)
                {
                    string ap = IconFolderAssetPath(f);
                    if (ap != null && AssetDatabase.IsValidFolder(ap)) folders.Add(ap);
                }
            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", folders.ToArray()))
            {
                var imp = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as SVGImporter;
                if (imp == null || imp.SvgType == SVGType.TexturedSprite) continue;
                Apply(imp);
                imp.SaveAndReimport();
                n++;
            }
            AuiLog.Info("icons reimported as textured sprites: " + n);
        }

        /// <summary>An icon folder from the settings as "Assets/..." whether it was written relative, absolute or with "./".</summary>
        private static string IconFolderAssetPath(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            string ap = AuiEditorEnv.AssetPath(folder);
            return ap != null ? ap.TrimEnd('/') : null;
        }

        private static void Apply(SVGImporter svg)
        {
            svg.SvgType = SVGType.TexturedSprite;
            svg.TextureSize = 128;
            svg.KeepTextureAspectRatio = true;
            svg.SvgPixelsPerUnit = 100f;
            svg.TexturedSpriteMeshType = UnityEngine.SpriteMeshType.FullRect;
        }

        private void OnPreprocessAsset()
        {
            var svg = assetImporter as SVGImporter;
            if (svg == null) return;
            string p = assetPath.Replace('\\', '/');
            bool ours = p.StartsWith(AuiEditorEnv.PackageAssetRoot + "/Icons/");
            if (!ours)
            {
                AuiSettings settings = AuiEditorEnv.FindSettings();
                if (settings != null)
                    foreach (string f in settings.iconFolders)
                    {
                        string ap = IconFolderAssetPath(f);
                        if (ap != null && p.StartsWith(ap + "/")) ours = true;
                    }
            }
            if (!ours) return;
            if (svg.SvgType == SVGType.TexturedSprite && svg.TextureSize == 128) return;
            Apply(svg);
        }
    }
}
