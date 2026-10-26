using UnityEngine;
using TMPro;

namespace Armagan.AUI
{
    /// <summary>
    /// The fonts AUI views render with, produced by AUI/Bake Fonts: static Inter atlases per
    /// weight holding exactly the glyphs the language files use, chained to Noto CJK atlases
    /// and a capped dynamic fallback for runtime strings. Assigned to AuiSettings.fontSet.
    /// </summary>
    public sealed class AuiFontSet : ScriptableObject
    {
        public TMP_FontAsset regular;
        public TMP_FontAsset medium;
        public TMP_FontAsset semiBold;
        public TMP_FontAsset bold;

        [Tooltip("Static fallbacks after Inter, in priority order (Noto JP, KR, SC, then Arabic). The name is historical: Arabic is in this chain too.")]
        public TMP_FontAsset[] cjkRegular = new TMP_FontAsset[0];
        public TMP_FontAsset[] cjkBold = new TMP_FontAsset[0];

        [Tooltip("Dynamic atlas for characters that were not baked (player names). One atlas, never grows past it.")]
        public TMP_FontAsset dynamicLatin;
        public TMP_FontAsset dynamicCjk;

        public int bakedCharCount;
        public string bakedHash = "";

        public TMP_FontAsset ForWeight(int weight)
        {
            if (weight >= 700 && bold != null) return bold;
            if (weight >= 600 && semiBold != null) return semiBold;
            if (weight >= 500 && medium != null) return medium;
            return regular != null ? regular : (medium ?? semiBold ?? bold);
        }
    }
}
