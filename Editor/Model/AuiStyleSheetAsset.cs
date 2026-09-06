using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>What the .auis importer produces: the text, the classes it defines, its diagnostics.</summary>
    public sealed class AuiStyleSheetAsset : ScriptableObject
    {
        [TextArea(3, 30)] public string sourceText;
        public string[] classNames = new string[0];
        public string[] diagnostics = new string[0];
        public bool hasErrors;
    }
}
