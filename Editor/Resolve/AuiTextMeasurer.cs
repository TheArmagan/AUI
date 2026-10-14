using System.Collections.Generic;
using Armagan.AUI.Core;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// Text metrics from TextMeshPro itself, on a hidden throwaway object, so the layout
    /// solver sizes boxes with the exact font the prefab will use. Falls back to the
    /// character heuristic when no font is available (first import before fonts are baked).
    ///
    /// ONE PROBE, ONE CACHE, FOR THE WHOLE SESSION. A view measures every text once per
    /// language on every compile, and the same strings come back compile after compile; the
    /// measurements are kept in a static table keyed by font, text and size. The probe used to
    /// be created per measurer and never destroyed, which leaked one hidden object per compile.
    /// A font rebake keeps the asset's identity (the baker copies over the existing asset), so
    /// the cache is cleared explicitly by <see cref="InvalidateCache"/>.
    /// </summary>
    public sealed class AuiTextMeasurer : IAuiTextMeasurer
    {
        private readonly AuiFontSet _fonts;
        private readonly AuiApproxTextMeasurer _approx = new AuiApproxTextMeasurer();
        private static TextMeshProUGUI _probe;
        private static bool _warned;
        private static readonly Dictionary<string, Vector2> Cache = new Dictionary<string, Vector2>();
        private static int _hits, _misses;

        public AuiTextMeasurer(AuiSettings settings)
        {
            _fonts = settings != null ? settings.fontSet : null;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            AssemblyReloadEvents.beforeAssemblyReload += DisposeProbe;
            EditorApplication.quitting += DisposeProbe;
        }

        public static TMP_FontAsset FontFor(AuiFontSet set, int weight)
        {
            if (set != null)
            {
                TMP_FontAsset f = set.ForWeight(weight);
                if (f != null) return f;
            }
            // TMP_Settings.instance is null while TMP Essentials are missing or not yet loadable
            // (first import of a project); defaultFontAsset would throw instead of returning null.
            return TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
        }

        /// <summary>Drop every cached measurement; call after fonts are rebaked or reimported.</summary>
        public static void InvalidateCache()
        {
            Cache.Clear();
        }

        /// <summary>Cache hits and misses since the last reload, for the timing lines.</summary>
        public static string CacheStats
        {
            get { return _hits + " hit(s), " + _misses + " miss(es), " + Cache.Count + " cached"; }
        }

        public void Measure(string text, float fontSize, int weight, float maxWidth, bool wrap, out float width, out float height)
        {
            TMP_FontAsset font = FontFor(_fonts, weight);
            if (font == null)
            {
                if (!_warned) { AuiLog.Warn("no font available for measurement; using the character heuristic. Run AUI/Bake Fonts."); _warned = true; }
                _approx.Measure(text, fontSize, weight, maxWidth, wrap, out width, out height);
                return;
            }
            float mw = wrap && maxWidth > 0 ? Mathf.Round(maxWidth * 100f) / 100f : 0f;
            string key = font.GetInstanceID() + "|" + fontSize.ToString("R") + "|" + (wrap ? "w" : "n") + mw.ToString("R") + "|" + (text ?? "");
            Vector2 cached;
            if (Cache.TryGetValue(key, out cached))
            {
                _hits++;
                width = cached.x;
                height = cached.y;
                return;
            }
            _misses++;
            try
            {
                if (_probe == null)
                {
                    var go = new GameObject("AUI text probe", typeof(RectTransform), typeof(TextMeshProUGUI));
                    go.hideFlags = HideFlags.HideAndDontSave;
                    _probe = go.GetComponent<TextMeshProUGUI>();
                    _probe.richText = false;
                }
                _probe.font = font;
                _probe.fontSize = fontSize;
                _probe.enableWordWrapping = wrap;
                _probe.text = text ?? "";
                Vector2 v = _probe.GetPreferredValues(text ?? "", wrap && maxWidth > 0 ? maxWidth : Mathf.Infinity, Mathf.Infinity);
                width = v.x;
                height = v.y;
                Cache[key] = v;
            }
            catch (System.Exception e)
            {
                if (!_warned) { AuiLog.Warn("TMP measurement failed (" + e.Message + "); using the character heuristic."); _warned = true; }
                _approx.Measure(text, fontSize, weight, maxWidth, wrap, out width, out height);
            }
        }

        private static void DisposeProbe()
        {
            if (_probe != null) Object.DestroyImmediate(_probe.gameObject);
            _probe = null;
            // Probes leaked by earlier versions survive reloads; sweep them too.
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.name == "AUI text probe" && (go.hideFlags & HideFlags.DontSave) != 0) Object.DestroyImmediate(go);
        }

        public void Dispose()
        {
            DisposeProbe();
        }
    }
}
