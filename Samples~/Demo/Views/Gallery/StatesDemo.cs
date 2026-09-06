using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>Hover, pressed, selected and disabled styling, including offset and opacity states from an inline sheet.</summary>
    public partial class StatesDemo : AuiView
    {
        private int _hitCount;

        protected override void OnStart()
        {
            Sel = true;
            Off = true;
            Hits = "0";
        }

        public void Hit() { _hitCount++; Hits = _hitCount.ToString(); Emit("hit"); }
        public void FlipSelected() { Sel = !Sel; Emit("changed"); }
        public void FlipDisabled() { Off = !Off; Emit("changed"); }

        public int HitCount() { return _hitCount; }
    }
}
