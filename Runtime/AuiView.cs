using UdonSharp;
using UnityEngine;

namespace Armagan.AUI
{
    /// <summary>
    /// Base class of every compiled AUI view. Step-1 stub: proves the package Runtime assembly
    /// compiles as UdonSharp under Packages/. The element tables, state-style engine,
    /// show/hide transitions and signal dispatch land in step 7.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class AuiView : UdonSharpBehaviour
    {
        /// <summary>Hash of the compiled view IR this prefab was built from. Baked; lets the
        /// builder skip a rebuild whose output would be identical.</summary>
        [HideInInspector] public string auiBakeHash;

        public virtual void _Retranslate()
        {
        }
    }
}
