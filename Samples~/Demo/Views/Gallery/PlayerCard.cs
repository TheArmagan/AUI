using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>A nested view: Nick and Score are declared props (text, so a label can show them); Kick raises the typed "kicked" event with the points as an int.</summary>
    public partial class PlayerCard : AuiView
    {
        private int _points;

        /// <summary>The host sets the points once; the label shows them through the Score prop.</summary>
        public void SetPoints(int points) { _points = points; Score = points.ToString(); }

        public void Kick() { EmitInt("kicked", _points); }
    }
}
