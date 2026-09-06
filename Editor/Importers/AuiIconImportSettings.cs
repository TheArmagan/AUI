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
                        if (!string.IsNullOrEmpty(f) && p.StartsWith(f.Replace('\\', '/').TrimEnd('/') + "/")) ours = true;
            }
            if (!ours) return;
            if (svg.SvgType == SVGType.TexturedSprite && svg.TextureSize == 128) return;
            svg.SvgType = SVGType.TexturedSprite;
            svg.TextureSize = 128;
            svg.KeepTextureAspectRatio = true;
            svg.SvgPixelsPerUnit = 100f;
            svg.TexturedSpriteMeshType = UnityEngine.SpriteMeshType.FullRect;
        }
    }
}
