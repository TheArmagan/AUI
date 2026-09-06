using System.Collections.Generic;
using System.IO;
using Armagan.AUI.Core;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// Bakes the fonts AUI views render with.
    ///
    /// THE COMPILER KNOWS EVERY STRING, so the atlases hold exactly the glyphs the language
    /// files use (plus the configured ranges for runtime text such as player names): a few
    /// hundred CJK glyphs instead of twenty thousand, and no runtime rasterisation. Per
    /// weight, Inter takes what it has; whatever is missing goes to Noto Sans JP, then KR,
    /// then SC (Han unification: a glyph present in all three renders with the JP shape, which
    /// is the accepted v1 caveat). Each result is switched to Static, which also drops the
    /// source font reference so the multi-megabyte OTFs never ship in a build. A single
    /// 1024 dynamic Inter atlas is chained last as the fallback for characters nobody baked.
    ///
    /// Output: Assets/AUI/Fonts/*.asset and an AuiFontSet, assigned to AuiSettings.fontSet.
    /// Rebaking overwrites the same paths so prefabs keep their references.
    /// </summary>
    public static class AuiFontBaker
    {
        public const string OutDir = "Assets/AUI/Fonts";
        private const int SamplingPointSize = 48;
        private const int Padding = 6;

        private static readonly string[] Weights = { "Regular", "Medium", "SemiBold", "Bold" };
        private static readonly string[] Cjk = { "JP", "KR", "SC" };

        [MenuItem("AUI/Bake Fonts", false, 4)]
        public static void Bake()
        {
            AuiSettings settings = AuiEditorEnv.EnsureSettings();
            AuiWorkspace ws = AuiEditorEnv.Scan(settings);
            uint[] codepoints = AuiCharsetCollector.Collect(settings, ws);
            string hash = AuiCharsetCollector.Hash(codepoints);
            AuiLog.Info("baking fonts for " + codepoints.Length + " characters");

            Directory.CreateDirectory(OutDir);
            AuiFontSet set = AssetDatabase.LoadAssetAtPath<AuiFontSet>(OutDir + "/AuiFontSet.asset");
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<AuiFontSet>();
                AssetDatabase.CreateAsset(set, OutDir + "/AuiFontSet.asset");
            }

            var missingEverywhere = new HashSet<uint>();
            var cjkRegular = new List<TMP_FontAsset>();
            var cjkBold = new List<TMP_FontAsset>();
            TMP_FontAsset[] latin = new TMP_FontAsset[Weights.Length];

            for (int w = 0; w < Weights.Length; w++)
            {
                Font src = Source("Inter-" + Weights[w] + ".ttf");
                if (src == null) { AuiLog.Error("font source missing: Fonts/src/Inter-" + Weights[w] + ".ttf"); return; }
                uint[] missing;
                latin[w] = BakeStatic(src, "AUI Inter " + Weights[w], codepoints, 1024, out missing);
                if (w == 0 || w == 3)
                {
                    // CJK: Regular feeds Regular/Medium, Bold feeds SemiBold/Bold.
                    string cw = w == 0 ? "Regular" : "Bold";
                    uint[] left = missing;
                    var chain = new List<TMP_FontAsset>();
                    foreach (string lang in Cjk)
                    {
                        if (left.Length == 0) break;
                        Font cjkSrc = Source("NotoSans" + lang + "-" + cw + ".otf");
                        if (cjkSrc == null) { AuiLog.Warn("font source missing: Fonts/src/NotoSans" + lang + "-" + cw + ".otf"); continue; }
                        uint[] stillMissing;
                        TMP_FontAsset fa = BakeStatic(cjkSrc, "AUI Noto " + lang + " " + cw, left, 2048, out stillMissing);
                        if (fa.characterTable.Count > 0) chain.Add(fa);
                        else DeleteBaked(fa);
                        left = stillMissing;
                    }
                    foreach (uint u in left) missingEverywhere.Add(u);
                    if (w == 0) cjkRegular = chain; else cjkBold = chain;
                }
            }

            TMP_FontAsset dynamic = BakeDynamic(Source("Inter-Regular.ttf"), "AUI Dynamic Latin");

            for (int w = 0; w < Weights.Length; w++)
            {
                List<TMP_FontAsset> chain = w < 2 ? cjkRegular : cjkBold;
                latin[w].fallbackFontAssetTable = new List<TMP_FontAsset>(chain);
                if (dynamic != null) latin[w].fallbackFontAssetTable.Add(dynamic);
                EditorUtility.SetDirty(latin[w]);
            }

            set.regular = latin[0];
            set.medium = latin[1];
            set.semiBold = latin[2];
            set.bold = latin[3];
            set.cjkRegular = cjkRegular.ToArray();
            set.cjkBold = cjkBold.ToArray();
            set.dynamicLatin = dynamic;
            set.bakedCharCount = codepoints.Length;
            set.bakedHash = hash;
            EditorUtility.SetDirty(set);
            settings.fontSet = set;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            if (missingEverywhere.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                int n = 0;
                foreach (uint u in missingEverywhere) { if (n++ > 40) { sb.Append("..."); break; } sb.Append(char.ConvertFromUtf32((int)u)).Append(' '); }
                AuiLog.Warn(missingEverywhere.Count + " character(s) exist in no source font and will render as boxes: " + sb);
            }
            AuiLog.Info("fonts baked: Inter x4 (" + latin[0].characterTable.Count + " glyphs), CJK regular x" + cjkRegular.Count + ", bold x" + cjkBold.Count + ". Rebuilding views.");
            AuiBuildQueue.EnqueueAll();
        }

        private static Font Source(string file)
        {
            return AssetDatabase.LoadAssetAtPath<Font>(AuiEditorEnv.PackageAssetRoot + "/Fonts/src/" + file);
        }

        private static string PathFor(string name)
        {
            return OutDir + "/" + name.Replace(' ', '-') + ".asset";
        }

        /// <summary>Create a dynamic atlas, add the characters, freeze it as Static and save it in place of any previous bake.</summary>
        private static TMP_FontAsset BakeStatic(Font src, string name, uint[] codepoints, int atlasSize, out uint[] missing)
        {
            TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(src, SamplingPointSize, Padding, GlyphRenderMode.SDFAA, atlasSize, atlasSize, AtlasPopulationMode.Dynamic, true);
            fa.name = name;
            if (!fa.TryAddCharacters(codepoints, out missing, false)) missing = missing ?? new uint[0];
            if (missing == null) missing = new uint[0];
            fa.atlasPopulationMode = AtlasPopulationMode.Static;
            Save(fa, PathFor(name));
            return fa;
        }

        private static TMP_FontAsset BakeDynamic(Font src, string name)
        {
            if (src == null) return null;
            TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(src, SamplingPointSize, Padding, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            fa.name = name;
            fa.isMultiAtlasTexturesEnabled = false;   // one atlas: the cap on runtime rasterisation
            // Seed ASCII so the common case never rasterises at runtime.
            uint[] ascii = new uint[95];
            for (int i = 0; i < 95; i++) ascii[i] = (uint)(32 + i);
            uint[] miss;
            fa.TryAddCharacters(ascii, out miss, false);
            Save(fa, PathFor(name));
            return fa;
        }

        /// <summary>Write the font asset with its material and atlas textures as sub-assets, replacing an earlier bake at the same path.</summary>
        private static void Save(TMP_FontAsset fa, string path)
        {
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null)
            {
                // Keep the GUID: prefabs reference it. Copy the new asset over the old one.
                EditorUtility.CopySerialized(fa, existing);
                foreach (Object sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(path)) Object.DestroyImmediate(sub, true);
                AddSubAssets(existing, fa);
                existing.name = fa.name;
                existing.ReadFontAssetDefinition();
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(fa);
                return;
            }
            AssetDatabase.CreateAsset(fa, path);
            AddSubAssets(fa, fa);
            fa.ReadFontAssetDefinition();
            EditorUtility.SetDirty(fa);
        }

        private static void AddSubAssets(TMP_FontAsset host, TMP_FontAsset source)
        {
            Texture2D[] atlases = source.atlasTextures;
            var kept = new List<Texture2D>();
            for (int i = 0; i < atlases.Length; i++)
            {
                if (atlases[i] == null) continue;
                Texture2D tex = atlases[i];
                if (tex.width == 0 || tex.height == 0) continue;
                tex.name = host.name + " Atlas" + (i > 0 ? " " + i : "");
                AssetDatabase.AddObjectToAsset(tex, host);
                kept.Add(tex);
            }
            host.atlasTextures = kept.ToArray();
            Material mat = source.material;
            mat.name = host.name + " Material";
            if (kept.Count > 0) mat.SetTexture(ShaderUtilities.ID_MainTex, kept[0]);
            AssetDatabase.AddObjectToAsset(mat, host);
            host.material = mat;
        }

        private static void DeleteBaked(TMP_FontAsset fa)
        {
            string p = AssetDatabase.GetAssetPath(fa);
            if (!string.IsNullOrEmpty(p)) AssetDatabase.DeleteAsset(p);
        }
    }

    /// <summary>The set of characters to bake: every translated string, the configured ranges, the extras.</summary>
    public static class AuiCharsetCollector
    {
        public static uint[] Collect(AuiSettings settings, AuiWorkspace ws)
        {
            var set = new HashSet<uint>();
            for (uint u = 0x20; u <= 0x7E; u++) set.Add(u);
            set.Add(0x200B);   // zero-width space, inserted by Escape
            set.Add(0x25A1);   // TMP's missing-glyph box
            AuiLanguageTable table = ws.LoadLanguageTable(settings != null ? settings.defaultLanguage : "en", null, null);
            foreach (char c in table.AllCharacters()) if (!char.IsSurrogate(c)) set.Add(c);
            // literal text in every view
            foreach (string p in ws.Views.Values)
            {
                AuiMarkupDocument doc = AuiParser.Parse(File.ReadAllText(p), p);
                foreach (AuiNode n in doc.Nodes) if (n.Text != null) foreach (char c in n.Text) if (!char.IsSurrogate(c)) set.Add(c);
            }
            if (settings != null)
            {
                foreach (char c in settings.extraCharacters ?? "") if (!char.IsSurrogate(c)) set.Add(c);
                foreach (string range in (settings.unicodeRanges ?? "").Split(new[] { ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = range.Split('-');
                    uint a, b;
                    if (!uint.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out a)) continue;
                    if (parts.Length == 1) { set.Add(a); continue; }
                    if (!uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out b)) continue;
                    if (b - a > 4096) { AuiLog.Warn("unicode range " + range + " is too large to bake; skipped"); continue; }
                    for (uint u = a; u <= b; u++) set.Add(u);
                }
            }
            set.Remove(0x0A); set.Remove(0x0D); set.Remove(0x09);
            var list = new List<uint>(set);
            list.Sort();
            return list.ToArray();
        }

        public static string Hash(uint[] codepoints)
        {
            var sb = new System.Text.StringBuilder();
            foreach (uint u in codepoints) sb.Append(u).Append(',');
            return AuiCompiler.Sha1(sb.ToString());
        }
    }
}
