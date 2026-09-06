using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>Keyed, bound, placeholder and wrapped text, plus the escape path for untrusted strings.</summary>
    public partial class TextsDemo : AuiView
    {
        private int _bumps;

        protected override void OnStart()
        {
            Status = "ready";
            Who = "Someone";
            Count = "0";
        }

        public void SetHello() { Status = "hello"; Emit("changed"); }
        public void SetLong() { Status = "a much longer runtime string that must ellipsise, not overflow the row"; Emit("changed"); }
        public void Inject() { Status = "<b>bold</b> <color=red>red</color> </noparse>"; Emit("changed"); }
        public void Bump() { _bumps++; Count = _bumps.ToString(); Emit("changed"); }

        // ---- read-backs for the gallery's self-test
        public string RenderedBound() { return elemText[E_BOUND] != null ? elemText[E_BOUND].text : ""; }
        public string RenderedArgs() { return elemText[E_ARGS] != null ? elemText[E_ARGS].text : ""; }
    }
}
