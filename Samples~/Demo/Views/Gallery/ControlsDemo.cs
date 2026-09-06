using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>Every control: slider with a format key, toggle, dropdown, keyboard input, a disable-able button.</summary>
    public partial class ControlsDemo : AuiView
    {
        protected override void OnStart()
        {
            Volume = 35f;
            Last = "";
        }

        public void VolumeChanged(float value) { Last = "volume " + FormatNumber(value, 0); Emit("changed"); }
        public void MuteChanged(bool value) { Last = "mute " + value; Emit("changed"); }
        public void QualityChanged(int value) { Last = "quality " + value; Emit("changed"); }
        public void NickSubmitted(string value) { Last = "nick " + value; Emit("changed"); }
        public void Ping() { Last = "ping"; Emit("ping"); }
        public void ToggleLock() { Locked = !Locked; Last = Locked ? "locked" : "unlocked"; Emit("changed"); }

        // ---- read-backs for the gallery's self-test
        public string VolumeLabel() { return elemText[E_VOLUME] != null ? elemText[E_VOLUME].text : ""; }
        public string QualityLabel() { return elemText[E_QUALITY] != null ? elemText[E_QUALITY].text : ""; }
        public bool MuteShown() { return IsSelected(S_MUTE); }
        public bool PingEnabled() { return elemButtons[E_LOCKED] != null && elemButtons[E_LOCKED].interactable; }
    }
}
