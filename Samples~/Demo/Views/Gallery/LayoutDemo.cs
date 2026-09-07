using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>
    /// The stage's flex properties are bound style values (strings, the same text a stylesheet
    /// takes); toggles hide a box (SetActiveElem), grow one (AddClass) and pin a badge with
    /// position: absolute. The readout comes from the solved rects one frame later.
    /// </summary>
    public partial class LayoutDemo : AuiView
    {
        // Not _justify / _align: the generated half owns _<binding> backing fields.
        private readonly string[] _justifyNames = { "start", "center", "end", "space-between" };
        private readonly string[] _alignNames = { "stretch", "start", "center", "end" };

        protected override void OnStart()
        {
            Row = true; Dir = "row";
            Wrap = false; WrapMode = "false";
            JustifySel = 0; Justify = _justifyNames[0];
            AlignSel = 2; AlignMode = _alignNames[2];
            GapV = 8f; Gap = "8";
            PadV = 8f; Pad = "8";
            HideTwo = false; GrowThree = false; ShowBadge = true;
            X = "0"; Y = "0"; W = "0"; H = "0";
            Changed();
        }

        public void RowChanged(bool value) { Dir = value ? "row" : "column"; Changed(); }
        public void WrapChanged(bool value) { WrapMode = value ? "true" : "false"; Changed(); }
        public void JustifyChanged(int value) { Justify = _justifyNames[value]; Changed(); }
        public void AlignChanged(int value) { AlignMode = _alignNames[value]; Changed(); }
        public void GapChanged(float value) { Gap = FormatNumber(value, 0); Changed(); }
        public void PadChanged(float value) { Pad = FormatNumber(value, 0); Changed(); }
        public void HideChanged(bool value) { SetActiveElem(E_B2, !value); Changed(); }
        public void GrowChanged(bool value) { if (value) AddClass(E_B3, C_GROW); else RemoveClass(E_B3, C_GROW); Changed(); }
        public void BadgeChanged(bool value) { SetActiveElem(E_BADGE, value); Changed(); }

        private void Changed()
        {
            // The solve runs on the next frame; read the rects after it.
            SendCustomEventDelayedFrames("_Readout", 2);
        }

        public void _Readout()
        {
            X = FormatNumber(LayoutX(E_B1), 0);
            Y = FormatNumber(LayoutY(E_B1), 0);
            W = FormatNumber(LayoutW(E_B1), 0);
            H = FormatNumber(LayoutH(E_B1), 0);
            Emit("changed");
        }

        public string Summary() { return Dir + " " + Justify + " " + AlignMode + " gap " + Gap + " pad " + Pad; }

        // ---- for the gallery's self-test
        public void SetRow(bool on) { Row = on; Dir = on ? "row" : "column"; }
        public float BoxX(int box) { return LayoutX(box == 1 ? E_B1 : box == 2 ? E_B2 : box == 3 ? E_B3 : E_B4); }
    }
}
