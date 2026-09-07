using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>Every control, its handler signature, runtime-filled options, pointer handlers and an if binding.</summary>
    public partial class ControlsDemo : AuiView
    {
        private readonly string[] _regions = { "Europe", "US West", "US East", "Japan", "Australia" };

        protected override void OnStart()
        {
            Volume = 35f;
            Speed = 1f;
            Last = "";
            // A select with max="6" and one baked option: the rest of the rows are filled here.
            SetRegionOptionCount(1 + _regions.Length);
            for (int i = 0; i < _regions.Length; i++) SetRegionOptionLabel(1 + i, _regions[i]);
        }

        public void VolumeChanged(float value) { Last = "volume " + FormatNumber(value, 0); Emit("changed"); }
        public void SpeedChanged(float value) { Last = "speed x" + FormatNumber(value, 2); Emit("changed"); }
        public void MuteChanged(bool value) { Last = value ? "muted" : "unmuted"; Emit("changed"); }
        public void QualityChanged(int value) { Last = "quality " + value; Emit("changed"); }
        public void RegionChanged(int value) { Last = "region " + (value == 0 ? "auto" : _regions[value - 1]); Emit("changed"); }
        public void NickSubmitted(string value) { Last = "nick " + value; Emit("changed"); }
        public void Ping() { Last = "ping"; Emit("ping"); }
        public void ToggleLock() { Locked = !Locked; Last = Locked ? "locked" : "unlocked"; Emit("changed"); }
        public void Hovering() { Last = "hovering the ping button"; }
        public void Unhover() { Last = ""; }

        // ---- read-backs for the gallery's self-test
        public string VolumeLabel() { return elemText[E_VOLUME] != null ? elemText[E_VOLUME].text : ""; }
        public string QualityLabel() { return elemText[E_QUALITY] != null ? elemText[E_QUALITY].text : ""; }
        public bool MuteShown() { return IsSelected(S_MUTE); }
        public bool PingEnabled() { return elemButtons[E_PING] != null && elemButtons[E_PING].interactable; }
        public bool MutedBannerHidden() { return !IsElementShown(E_BANNER); }
        public int RegionOptions() { return 1 + _regions.Length; }
    }
}
