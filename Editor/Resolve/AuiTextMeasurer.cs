using Armagan.AUI.Core;
using TMPro;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// Text metrics from TextMeshPro itself, on a hidden throwaway object, so the layout
    /// solver sizes boxes with the exact font the prefab will use. Falls back to the
    /// character heuristic when no font is available (first import before fonts are baked).
    /// </summary>
    public sealed class AuiTextMeasurer : IAuiTextMeasurer
    {
        private readonly AuiFontSet _fonts;
        private readonly AuiApproxTextMeasurer _approx = new AuiApproxTextMeasurer();
        private TextMeshProUGUI _probe;
        private static bool _warned;

        public AuiTextMeasurer(AuiSettings settings)
        {
            _fonts = settings != null ? settings.fontSet : null;
        }

        public static TMP_FontAsset FontFor(AuiFontSet set, int weight)
        {
            if (set != null)
            {
                TMP_FontAsset f = set.ForWeight(weight);
                if (f != null) return f;
            }
            return TMP_Settings.defaultFontAsset;
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
            }
            catch (System.Exception e)
            {
                if (!_warned) { AuiLog.Warn("TMP measurement failed (" + e.Message + "); using the character heuristic."); _warned = true; }
                _approx.Measure(text, fontSize, weight, maxWidth, wrap, out width, out height);
            }
        }

        public void Dispose()
        {
            if (_probe != null) Object.DestroyImmediate(_probe.gameObject);
            _probe = null;
        }
    }
}
