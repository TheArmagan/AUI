using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// Marks the live preview's root object so it can always be found again (after a reload,
    /// a crash, a rename) and never duplicated. Lives on a DontSave object only; never saved.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class AuiPreviewRoot : MonoBehaviour
    {
    }
}
