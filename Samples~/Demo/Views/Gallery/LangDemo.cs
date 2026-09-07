using UnityEngine;
using VRC.SDKBase;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>The shipped picker plus the service's read-backs; Next cycles, Reset forgets the saved pick.</summary>
    public partial class LangDemo : AuiView
    {
        protected override void OnStart()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            Player = lp != null ? lp.displayName : "visitor";
            Refresh();
        }

        protected override void OnRetranslate() { Refresh(); Emit("changed"); }

        public void Next() { if (lang != null) lang.SetLanguage((lang.GetLanguage() + 1) % lang.LanguageCount()); }
        public void Reset() { if (lang != null) lang.ClearChoice(); }

        private void Refresh()
        {
            if (lang == null) { Code = "?"; Native = "?"; Total = "0"; return; }
            Code = lang.GetLanguageCode();
            Native = lang.LanguageName(lang.GetLanguage());
            Total = lang.LanguageCount().ToString();
        }
    }
}
