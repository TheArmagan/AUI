using UnityEngine;
using VRC.SDKBase;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>
    /// The overview page: live numbers in stat tiles, a progress bar whose width is a bound
    /// style value, a once-a-second timer through SendCustomEventDelayedSeconds, and a typed
    /// event that asks the hub to switch pages.
    /// </summary>
    public partial class HomeDemo : AuiView
    {
        // Page indices of the hub's <pages id="main">, in markup order (MAIN_* constants live in AuiGallery.g.cs).
        private const int PAGE_TEXTS = 1, PAGE_STYLES = 5, PAGE_THEME = 6, PAGE_RUNTIME = 7;

        private int _seconds;
        private bool _ticking;

        protected override void OnStart()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            Player = lp != null ? lp.displayName : "visitor";
            ElemCount = ElementCount().ToString();
            LangCount = lang != null ? lang.LanguageCount().ToString() : "?";
            Players = VRCPlayerApi.GetPlayerCount().ToString();
            Uptime = "0s";
            Progress = "0%";
            ProgressLabel = "0%";
            if (!_ticking) { _ticking = true; SendCustomEventDelayedSeconds("_Tick", 1f); }
        }

        /// <summary>Once a second: the uptime tile, the player count and the tour bar (which fills over two minutes).</summary>
        public void _Tick()
        {
            _seconds++;
            int m = _seconds / 60, s = _seconds % 60;
            Uptime = m > 0 ? m + "m " + s + "s" : s + "s";
            Players = VRCPlayerApi.GetPlayerCount().ToString();
            int pct = Mathf.Min(100, _seconds * 100 / 120);
            Progress = pct + "%";            // style="width: {progress}" on the fill: a length string
            ProgressLabel = pct + "%";
            SendCustomEventDelayedSeconds("_Tick", 1f);
        }

        // Typed event: the hub's on-navigate="Navigate" handler takes the int.
        public void GoTexts()   { EmitInt("navigate", PAGE_TEXTS); }
        public void GoStyles()  { EmitInt("navigate", PAGE_STYLES); }
        public void GoTheme()   { EmitInt("navigate", PAGE_THEME); }
        public void GoRuntime() { EmitInt("navigate", PAGE_RUNTIME); }
    }
}
