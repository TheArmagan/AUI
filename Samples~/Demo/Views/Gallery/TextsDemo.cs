using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>Keyed, bound, placeholder, wrapped and runtime text, the escape path for untrusted strings, and a text-align change from code.</summary>
    public partial class TextsDemo : AuiView
    {
        private readonly string[] _names = { "Ada", "Grace", "Linus", "Yukihiro", "Hedy", "Ayşe" };
        private int _bumps, _name, _align;

        protected override void OnStart()
        {
            Status = "ready";
            Who = _names[0];
            Count = "0";
        }

        public void SetHello() { Status = "hello"; Emit("changed"); }
        public void SetLong() { Status = "a much longer runtime string that must ellipsise, not overflow the row"; Emit("changed"); }
        public void Inject() { Status = "<b>bold</b> <color=red>red</color> </noparse>"; Emit("changed"); }   // rendered literally
        public void Bump() { _bumps++; Count = _bumps.ToString(); Status = "count " + _bumps; Emit("changed"); }
        public void Rename() { _name = (_name + 1) % _names.Length; Who = _names[_name]; Status = "who " + Who; Emit("changed"); }

        public void Align()
        {
            _align = (_align + 1) % 3;
            SetTextAlign(E_WRAPPED, _align);          // 0 left, 1 center, 2 right; stays until the element's next class change
            Status = _align == 0 ? "align left" : _align == 1 ? "align center" : "align right";
            Emit("changed");
        }

        // ---- read-backs for the gallery's self-test
        public string RenderedBound() { return elemText[E_BOUND] != null ? elemText[E_BOUND].text : ""; }
        public string RenderedArgs() { return elemText[E_ARGS] != null ? elemText[E_ARGS].text : ""; }
    }
}
