using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>The shipped picker plus a readout of the active code; Reset forgets the saved pick.</summary>
    public partial class LangDemo : AuiView
    {
        protected override void OnStart() { Refresh(); }
        protected override void OnRetranslate() { Refresh(); Emit("changed"); }

        public void Reset() { if (lang != null) lang.ClearChoice(); }

        private void Refresh() { Code = lang != null ? lang.GetLanguageCode() : "?"; }
    }
}
