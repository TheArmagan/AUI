using UnityEngine;
using UnityEngine.UI;

namespace Armagan.AUI
{
    /// <summary>
    /// Finding and driving elements from author code: a small selector ("#id", ".class",
    /// "tag"), text and icon setters, sprite and raw access. Element indices are the same
    /// ints the generated E_ constants hold; everything here is a thin layer over the tables.
    /// </summary>
    public partial class AuiView
    {
        [Header("Lookup tables (baked by the AUI builder)")]
        [HideInInspector] public string[] idNames;
        [HideInInspector] public int[] idElem;
        [HideInInspector] public string[] iconNames;
        [HideInInspector] public Sprite[] iconSprites;
        [HideInInspector] public string[] refNames;

        // ================================================================ selectors

        /// <summary>Element index of "#id" (or "id"), or -1. Repeat rows: the row 0 element; add row * stride for others.</summary>
        public int GetElement(string selector)
        {
            if (idNames == null || string.IsNullOrEmpty(selector)) return -1;
            string id = selector[0] == '#' ? selector.Substring(1) : selector;
            for (int i = 0; i < idNames.Length; i++) if (idNames[i] == id) return idElem[i];
            return -1;
        }

        /// <summary>Every element matching ".class" (effective runtime classes) or "tag"; "#id" gives at most one.</summary>
        public int[] GetElements(string selector)
        {
            if (string.IsNullOrEmpty(selector)) return new int[0];
            if (selector[0] == '#')
            {
                int e = GetElement(selector);
                return e < 0 ? new int[0] : new int[] { e };
            }
            int n = elemObjects == null ? 0 : elemObjects.Length;
            int[] scratch = new int[n];
            int count = 0;
            if (selector[0] == '.')
            {
                int cid = ClassId(selector.Substring(1));
                if (cid < 0 || cid >= STAR_BASE) return new int[0];   // star classes are style-only, never searchable
                for (int e = 0; e < n; e++) if (HasClass(e, cid)) scratch[count++] = e;
            }
            else
            {
                int tid = TagId(selector);
                if (tid < 0) return new int[0];
                for (int e = 0; e < n; e++) if (elemTag != null && e < elemTag.Length && elemTag[e] == tid) scratch[count++] = e;
            }
            int[] result = new int[count];
            for (int i = 0; i < count; i++) result[i] = scratch[i];
            return result;
        }

        public int[] GetElementsByTag(string tag) { return GetElements(tag); }

        public int TagId(string tag)
        {
            if (tagNames == null || tag == null) return -1;
            if (tag == "div") tag = "panel";
            for (int i = 0; i < tagNames.Length; i++) if (tagNames[i] == tag) return i;
            return -1;
        }

        public int ElementCount() { return elemObjects == null ? 0 : elemObjects.Length; }

        // ================================================================ content

        /// <summary>Write a label (escaped so it can never open a rich-text tag) and re-measure it.</summary>
        public void SetText(int elem, string value)
        {
            SetElemText(elem, Escape(value));
        }

        public string GetText(int elem)
        {
            return elemText != null && elem >= 0 && elem < elemText.Length && elemText[elem] != null ? elemText[elem].text : "";
        }

        /// <summary>Swap an element's icon to a preloaded one (every icon the view uses, plus &lt;view icons="..."&gt;).</summary>
        public void SetIcon(int elem, string iconName)
        {
            if (!_started) { _auiStashI = elem; _auiStashS = iconName; _AuiStart(); elem = _auiStashI; iconName = _auiStashS; }
            if (elemIcon == null || elem < 0 || elem >= elemIcon.Length || elemIcon[elem] == null) return;
            Sprite s = IconSprite(iconName);
            if (s == null) { Debug.LogWarning("[AUI] " + name + ": icon '" + iconName + "' is not baked into this view; list it in <view icons=\"...\">."); return; }
            elemIcon[elem].sprite = s;
        }

        public Sprite IconSprite(string iconName)
        {
            if (iconNames == null || iconName == null) return null;
            for (int i = 0; i < iconNames.Length; i++) if (iconNames[i] == iconName) return iconSprites[i];
            return null;
        }

        /// <summary>Background sprite by name (any sprite() the sheets mention, plus &lt;view sprites="..."&gt;); the colour stays.</summary>
        public void SetBackground(int elem, string spriteName)
        {
            if (!_started) { _auiStashI = elem; _auiStashS = spriteName; _AuiStart(); elem = _auiStashI; spriteName = _auiStashS; }
            if (elemBg == null || elem < 0 || elem >= elemBg.Length || elemBg[elem] == null) return;
            int rf = RefId(spriteName);
            if (rf < 0 || declSprite == null || rf >= declSprite.Length || declSprite[rf] == null) { Debug.LogWarning("[AUI] " + name + ": sprite '" + spriteName + "' is not baked into this view; list it in <view sprites=\"...\">."); return; }
            elemBg[elem].sprite = declSprite[rf];
        }

        /// <summary>Background material by name (any material()/shader() the sheets mention, plus &lt;view materials="..."&gt;); null name clears it.</summary>
        public void SetMaterial(int elem, string materialName)
        {
            if (!_started) { _auiStashI = elem; _auiStashS = materialName; _AuiStart(); elem = _auiStashI; materialName = _auiStashS; }
            if (elemBg == null || elem < 0 || elem >= elemBg.Length || elemBg[elem] == null) return;
            if (string.IsNullOrEmpty(materialName)) { elemBg[elem].material = null; return; }
            int rf = RefId(materialName);
            if (rf < 0 || declMaterial == null || rf >= declMaterial.Length || declMaterial[rf] == null) { Debug.LogWarning("[AUI] " + name + ": material '" + materialName + "' is not baked into this view; list it in <view materials=\"...\">."); return; }
            elemBg[elem].material = declMaterial[rf];
        }

        public int RefId(string refName)
        {
            if (refNames == null || refName == null) return -1;
            for (int i = 0; i < refNames.Length; i++) if (refNames[i] == refName) return i;
            return -1;
        }

        // ================================================================ raw access

        public RectTransform GetRect(int elem)
        {
            return elemRects != null && elem >= 0 && elem < elemRects.Length ? elemRects[elem] : null;
        }

        public GameObject GetObject(int elem)
        {
            return elemObjects != null && elem >= 0 && elem < elemObjects.Length ? elemObjects[elem] : null;
        }

        public Image GetBackgroundImage(int elem)
        {
            return elemBg != null && elem >= 0 && elem < elemBg.Length ? elemBg[elem] : null;
        }

        public bool IsElementShown(int elem)
        {
            if (_shown == null || elem < 0 || elem >= _shown.Length) return false;
            return _shown[elem] && !(_cssHidden != null && _cssHidden[elem]);
        }
    }
}
