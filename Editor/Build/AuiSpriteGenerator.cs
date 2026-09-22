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
            Write(dir + "/Circle.png", 32, false);   // 64 px filled circle, used unsliced (slider thumb)
            AssetDatabase.Refresh();
            Import(dir + "/Circle.png", 0);
            foreach (int r in Radii)
            {
                Import(dir + "/Rounded" + r + ".png", r + 2);
                Import(dir + "/Border" + r + ".png", r + 2);
            }
            Import(dir + "/Square.png", 2);
            GenerateColorRamps(dir);
            AuiLog.Info("sprites generated in " + dir);
        }

        /// <summary>
        /// The three ramps a &lt;colorpicker&gt; is drawn from, stretched unsliced:
        ///   HueBar   8 x 256, hue 0 at the bottom row to 1 at the top, full saturation and value;
        ///   SatRamp  256 x 8, white, alpha 1 at the left to 0 at the right;
        ///   ValRamp  8 x 256, black, alpha 1 at the bottom to 0 at the top.
        /// Over a base tinted to the pure hue, SatRamp then ValRamp compose exactly HSV(h, s, v),
        /// which is why AuiColorPicker maps x to saturation and y to value with no other curve.
        /// The hue formula is AuiColorPicker.HsvToRgb's twin; change both or neither.
        /// </summary>
        public static void GenerateColorRamps(string dir)
        {
            const int N = 256;
            var hue = new Texture2D(8, N, TextureFormat.RGBA32, false);
            var sat = new Texture2D(N, 8, TextureFormat.RGBA32, false);
            var val = new Texture2D(8, N, TextureFormat.RGBA32, false);
            for (int i = 0; i < N; i++)
            {
                float t = i / (float)(N - 1);
                Color h = Hsv(t);
                for (int k = 0; k < 8; k++)
                {
                    hue.SetPixel(k, i, h);
                    sat.SetPixel(i, k, new Color(1f, 1f, 1f, 1f - t));
                    val.SetPixel(k, i, new Color(0f, 0f, 0f, 1f - t));
                }
            }
            foreach (var pair in new[] { new { n = "HueBar", t = hue }, new { n = "SatRamp", t = sat }, new { n = "ValRamp", t = val } })
            {
                pair.t.Apply();
                File.WriteAllBytes(dir + "/" + pair.n + ".png", pair.t.EncodeToPNG());
                Object.DestroyImmediate(pair.t);
            }
            AssetDatabase.Refresh();
            foreach (string n in new[] { "HueBar", "SatRamp", "ValRamp" })
            {
                Import(dir + "/" + n + ".png", 0);
                var imp = AssetImporter.GetAtPath(dir + "/" + n + ".png") as TextureImporter;
                if (imp == null) continue;
                // Clamp, or bilinear filtering wraps the red at hue 1 into the bottom row of hue 0
                // and the transparent end of a ramp into its opaque one.
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
        }

        /// <summary>Pure hue h (0..1) at full saturation and value.</summary>
        private static Color Hsv(float h)
        {
            float hp = (h - Mathf.Floor(h)) * 6f;
            float x = 1f - Mathf.Abs(hp - Mathf.Floor(hp / 2f) * 2f - 1f);
            if (hp < 1f) return new Color(1f, x, 0f, 1f);
            if (hp < 2f) return new Color(x, 1f, 0f, 1f);
            if (hp < 3f) return new Color(0f, 1f, x, 1f);
            if (hp < 4f) return new Color(0f, x, 1f, 1f);
            if (hp < 5f) return new Color(x, 0f, 1f, 1f);
            return new Color(1f, 0f, x, 1f);
        }

        private static void Write(string path, int radius, bool outline)
        {
            int size = path.EndsWith("Circle.png") ? 64 : Mathf.Max(16, radius * 4 + 8);
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
