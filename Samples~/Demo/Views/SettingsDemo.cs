using UnityEngine;
using VRC.SDKBase;
using Armagan.AUI;

/// <summary>Logic for SettingsDemo.aui. Bindings are properties on this class; handlers below are called by the generated half.</summary>
public partial class SettingsDemo : AuiView
{
    protected override void OnStart()
    {
        VRCPlayerApi lp = Networking.LocalPlayer;
        PlayerName = lp != null ? lp.displayName : "nobody";
        Brightness = 70f;
        Volume = 50f;
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
