using System.IO;
using UnityEditor;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// Generates the package's 9-slice sprites: RoundedN (a filled rounded rectangle with
    /// corner radius N mm) and BorderN (the same outline, 1.5 mm stroke), white so Image.color
    /// tints them. Imported at 1 pixel per unit with a slice border of N + 2, so the corners
    /// stay exactly N mm at any element size. Run once from the menu; the PNGs are committed.
    /// </summary>
    public static class AuiSpriteGenerator
    {
        public static readonly int[] Radii = { 4, 8, 12, 16, 24 };

        [MenuItem("AUI/Generate Sprites", false, 70)]
        public static void Generate()
        {
            string dir = AuiEditorEnv.PackageAssetRoot + "/Sprites";
            Directory.CreateDirectory(dir);
            foreach (int r in Radii)
            {
                Write(dir + "/Rounded" + r + ".png", r, false);
                Write(dir + "/Border" + r + ".png", r, true);
            }
            Write(dir + "/Square.png", 0, false);
            AssetDatabase.Refresh();
            foreach (int r in Radii)
            {
                Import(dir + "/Rounded" + r + ".png", r + 2);
                Import(dir + "/Border" + r + ".png", r + 2);
            }
            Import(dir + "/Square.png", 2);
            AuiLog.Info("sprites generated in " + dir);
        }

        private static void Write(string path, int radius, bool outline)
        {
            int size = Mathf.Max(16, radius * 4 + 8);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float stroke = 1.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedRectDistance(x + 0.5f, y + 0.5f, size, size, radius);
                    float a;
                    if (!outline) a = Mathf.Clamp01(0.5f - d);
                    else a = Mathf.Clamp01(0.5f - Mathf.Abs(d + stroke * 0.5f) + stroke * 0.5f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>Signed distance from a point to a rounded rectangle's edge (negative inside).</summary>
        private static float RoundedRectDistance(float px, float py, float w, float h, float r)
        {
            float cx = px - w * 0.5f, cy = py - h * 0.5f;
            float qx = Mathf.Abs(cx) - (w * 0.5f - r), qy = Mathf.Abs(cy) - (h * 0.5f - r);
            float ox = Mathf.Max(qx, 0), oy = Mathf.Max(qy, 0);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }

        private static void Import(string path, int border)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            // 100 to match Canvas.referencePixelsPerUnit (which does not reliably take a new value on a
            // freshly added Canvas): Image.pixelsPerUnit = sprite PPU / canvas reference = 1, so a
            // 14 px slice border is 14 units, and 1 unit is 1 mm.
            imp.spritePixelsPerUnit = 100;
            imp.spriteBorder = new Vector4(border, border, border, border);
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }
    }
}
