using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// What the .aui importer produces: enough to rebuild the view without re-reading the
    /// source (the text itself, since the compile is cheap and the model is not serializable),
    /// plus the hash and diagnostics for the inspector and the build queue.
    /// </summary>
    public sealed class AuiViewAsset : ScriptableObject
    {
        public string viewName;
        public string hash;
        [TextArea(3, 30)] public string sourceText;
        public string[] childViews = new string[0];
        public string[] dependencies = new string[0];
        public string[] diagnostics = new string[0];
        public bool hasErrors;
        public int elementCount;
        public int signalCount;
        public float width;
        public float height;
    }
}
