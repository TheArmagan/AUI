using UnityEngine;
using VRC.SDKBase;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>Logic for SettingsDemo.aui. Bindings are properties on this class; handlers below are called by the generated half.</summary>
    public partial class SettingsDemo : AuiView
    {
        // A link to another world script: a normal public field, assigned in the inspector on the
        // scene instance. The generated half never touches it.
        [Tooltip("Receives slider changes. Optional: the view works without it.")]
        public DemoBrightness brightness;

        protected override void OnStart()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            PlayerName = lp != null ? lp.displayName : "nobody";
            Volume = 50f;
            // Brightness is pushed in by DemoBrightness.Start; fall back when it is not wired.
            if (brightness == null) Brightness = 70f;
        }

        public void Close()
        {
            Hide();
        }

        public void PageChanged(int value)
        {
            Debug.Log("[AUI/demo] page " + value);
        }

        public void BrightnessChanged(float value)
        {
            // UI -> script. Fires per frame while dragging; DemoBrightness only sets a float, so no
            // coalescing is needed here. Anything that writes PlayerData should debounce.
            if (brightness != null) brightness.SetPercent(value);
        }

        public void SolidChanged(bool value)
        {
            Debug.Log("[AUI/demo] solid furniture " + value);
        }

        public void QualityChanged(int value)
        {
            Debug.Log("[AUI/demo] quality " + value);
        }

        public void VolumeChanged(float value)
        {
        }

        public void MuteChanged(bool value)
        {
            Debug.Log("[AUI/demo] mute " + value);
        }

        public void NickSubmitted(string value)
        {
            // Untrusted text: the property already escaped it, and the greeting re-renders in every language.
            PlayerName = value;
        }

        public void Greet()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            PlayerName = lp != null ? lp.displayName : "nobody";
        }

        // ---- editor test hooks (parameterless so they can be sent as custom events)
        public void _TestEnglish() { if (lang != null) lang.SetLanguageByCode("en"); }
        public void _TestTurkish() { if (lang != null) lang.SetLanguageByCode("tr"); }
        public void _TestJapanese() { if (lang != null) lang.SetLanguageByCode("ja"); }
        public void _TestNick() { Nick = "<b>bold</b> nick"; }
        public void _TestAudioPage() { SelectMainPage(MAIN_AUDIO); }
        public void _TestLangPage() { SelectMainPage(MAIN_LANG); }
    }
}
