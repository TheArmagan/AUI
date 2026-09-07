using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>
    /// Re-theming through :root variables. A preset writes the ten palette variables of the
    /// package theme; the sliders derive the accent from a hue, the density from --pad and the
    /// card colour from a lerp between the preset's surface and chip. Local calls restyle this
    /// page and its nested views; the service fans the same call out to every view in the world
    /// and repeats it for views that start later.
    /// </summary>
    public partial class ThemeDemo : AuiView
    {
        // base, nav, surface, panel, card, border, chip, accent, text, muted
        private readonly string[] _vars = { "base", "nav", "surface", "panel", "card", "border", "chip", "accent", "text", "muted" };
        private readonly string[] _ember  = { "#150F0B", "#17120D", "#1E1610", "#291E17", "#241B14", "#2B231C", "#3E3126", "#E0A86A", "#EEE1D2", "#A38C76" };
        private readonly string[] _ocean  = { "#0B1220", "#0E1628", "#121C30", "#1A2740", "#16213A", "#22304C", "#2A3A5C", "#5FB4E8", "#DDE8F5", "#7F93AE" };
        private readonly string[] _forest = { "#0C130E", "#0F1711", "#142017", "#1C2C20", "#18261B", "#24352A", "#2E4535", "#7FD08A", "#E0EEDF", "#85A08A" };
        private readonly string[] _rose   = { "#170D12", "#1B1016", "#24151D", "#301C27", "#2A1822", "#3A2431", "#4A2E3E", "#F08CB0", "#F5E1E8", "#B0899A" };
        private readonly string[] _mono   = { "#101010", "#141414", "#1A1A1A", "#242424", "#202020", "#2C2C2C", "#383838", "#E6E6E6", "#F0F0F0", "#9A9A9A" };
        private string[] _current;
        private string _presetName = "Ember";

        protected override void OnStart()
        {
            _current = _ember;
            Global = true;
            Hue = 33f;
            Pad = 12f;
            Lift = 0f;
            AccentNow = "#E0A86A";
        }

        public void Ember()  { Apply(_ember, "Ember", 33f); }
        public void Ocean()  { Apply(_ocean, "Ocean", 203f); }
        public void Forest() { Apply(_forest, "Forest", 128f); }
        public void Rose()   { Apply(_rose, "Rose", 338f); }
        public void Mono()   { Apply(_mono, "Mono", 0f); }
        public void Reset()  { Pad = 12f; Set("pad", "12"); Lift = 0f; Apply(_ember, "Ember", 33f); }
        public void Sample() { }
        public void GlobalChanged(bool value) { Apply(_current, _presetName, Hue); }

        private void Apply(string[] preset, string presetName, float hue)
        {
            _current = preset;
            _presetName = presetName;
            Hue = hue;
            for (int i = 0; i < _vars.Length; i++) Set(_vars[i], preset[i]);
            AccentNow = preset[7];
            LiftChanged(Lift);
            EmitString("changed", presetName + (Global ? " (every view)" : " (this page)"));
        }

        /// <summary>The accent from a hue; the slider fires per frame, and every call re-resolves the tree, so this is also the cost demo.</summary>
        public void HueChanged(float value)
        {
            Color c = Color.HSVToRGB(value / 360f, 0.6f, 0.92f);
            if (Global && lang != null) lang.SetStyleVariable("accent", ColorToHex(c)); else SetStyleVariableColor("accent", c);
            AccentNow = ColorToHex(c);
        }

        /// <summary>--pad drives view padding and card padding in the shared sheet: a variable that changes layout.</summary>
        public void PadChanged(float value) { Set("pad", FormatNumber(value, 0)); }

        /// <summary>--card between the preset's surface and its chip colour.</summary>
        public void LiftChanged(float value)
        {
            Vector4 a = Vector4.zero, b = Vector4.zero;
            if (!ParseColor(_current[2], ref a) || !ParseColor(_current[6], ref b)) return;
            Vector4 m = Vector4.Lerp(a, b, value / 100f);
            Set("card", ColorToHex(new Color(m.x, m.y, m.z, m.w)));
        }

        private void Set(string varName, string value)
        {
            if (Global && lang != null) lang.SetStyleVariable(varName, value);
            else SetStyleVariable(varName, value);
        }
    }
}
