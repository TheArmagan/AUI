using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>Hover, pressed, selected and disabled looks, then a card restyled through a class binding, bound style values and utility classes.</summary>
    public partial class StatesDemo : AuiView
    {
        private readonly string[] _toneIndexs = { "", "warm", "cool", "cool big" };
        private int _hitCount, _toneIndex;
        private bool _utilities;

        protected override void OnStart()
        {
            Sel = true;
            Off = true;
            Hits = "0";
            Tone = "";
            CardNote = "class=\"card {tone}\": tone is empty";
            Bg = new Color(0.24f, 0.19f, 0.15f);
            Sw = "40%";
            Alpha = 1f;
            SwatchNote = "width 40%, opacity 1";
        }

        public void Hit() { _hitCount++; Hits = _hitCount.ToString(); Emit("hit"); }
        public void FlipSelected() { Sel = !Sel; CardNote = "selected binding is " + Sel; Emit("changed"); }
        public void FlipDisabled() { Off = !Off; CardNote = "disabled binding is " + Off; Emit("changed"); }

        /// <summary>class="card {tone}": the binding holds any number of class names.</summary>
        public void CycleTone()
        {
            _toneIndex = (_toneIndex + 1) % _toneIndexs.Length;
            Tone = _toneIndexs[_toneIndex];
            CardNote = Tone.Length == 0 ? "tone is empty again" : "tone is \"" + Tone + "\"";
            Emit("changed");
        }

        /// <summary>style="background: {bg}; width: {sw}; opacity: {alpha}": a Color, a length string and a float.</summary>
        public void Randomize()
        {
            Bg = Color.HSVToRGB(Random.value, 0.6f, 0.9f);
            int pct = Random.Range(20, 100);
            Sw = pct + "%";
            Alpha = Random.Range(0.4f, 1f);
            SwatchNote = "width " + Sw + ", opacity " + FormatNumber(Alpha, 2);
            CardNote = "three bound style values changed";
            Emit("changed");
        }

        /// <summary>Utilities listed in &lt;view utilities="..."&gt; can be toggled at runtime without appearing in the markup.</summary>
        public void Utilities()
        {
            _utilities = !_utilities;
            if (_utilities) { AddClassNamed(E_CARD, "*bg-accent"); AddClassNamed(E_SWATCH, "*scale-110"); AddClassNamed(E_SWATCH, "*opacity-50"); }
            else { RemoveClassNamed(E_CARD, "*bg-accent"); RemoveClassNamed(E_SWATCH, "*scale-110"); RemoveClassNamed(E_SWATCH, "*opacity-50"); }
            CardNote = _utilities ? "*bg-accent on the card, *scale-110 *opacity-50 on the swatch" : "utilities removed";
            Emit("changed");
        }

        public int HitCount() { return _hitCount; }
    }
}
