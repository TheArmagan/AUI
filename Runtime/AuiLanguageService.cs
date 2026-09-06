using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.Persistence;

namespace Armagan.AUI
{
    /// <summary>
    /// The one language table of a scene, and the switch every view listens to.
    ///
    /// STORAGE: a flat string table, values[lang * keyCount + key], baked by AUI/Rebuild
    /// Language Table from every lang/*.json. Views carry key INDICES (baked into their
    /// elemKey arrays), so a lookup is one array read; nothing is searched by name at runtime.
    /// "" means missing; Get falls back to the default language, then to the key text so a
    /// hole is visible in-world rather than blank.
    ///
    /// CHOICE: the player's VRChat client language is the starting point
    /// (VRCPlayerApi.GetCurrentLanguage), matched exactly, then by base code (pt-BR -> pt),
    /// then the default. A pick from the picker is remembered in PlayerData under "aui.lang"
    /// as the CODE (never the index: the table order changes when a language is added). The
    /// save happens only after OnPlayerRestored, because writes before it are dropped by the
    /// platform; a pick made earlier is parked and written then. A saved code that no longer
    /// exists is ignored, so removing a language never traps a player.
    ///
    /// TWEEN PROBE: VRCTween is inert under ClientSim, and nothing in AUI may depend on a tween
    /// completing. A VRCTween.DelayedCall at 0.05 s sets the flag where tweens run; a plain
    /// delayed event at 1 s closes the probe everywhere. Until it closes, views apply their
    /// final states instantly, which is also what you want during load.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class AuiLanguageService : UdonSharpBehaviour
    {
        public const string PD_KEY = "aui.lang";

        // Baked by AUI/Rebuild Language Table; hidden because a 3 x 200 string table is noise in
        // the inspector and editing it by hand would desync the key indices views carry.
        [HideInInspector] public string[] langCodes;
        [HideInInspector] public string[] langNames;
        [HideInInspector] public int defaultLang;
        [HideInInspector] public int keyCount;
        [HideInInspector] public string[] keys;
        [HideInInspector] public string[] values;

        private AuiView[] _views = new AuiView[16];
        private int _viewCount;
        private int _lang = -1;
        private bool _userChosen;
        private bool _restored;
        private bool _pendingSave;
        private bool _tweensOk;
        private bool _probeDone;

        // ---------------------------------------------------------------- lookup

        public int GetLanguage()
        {
            EnsureInit();
            return _lang;
        }

        public string GetLanguageCode()
        {
            EnsureInit();
            return langCodes != null && _lang >= 0 && _lang < langCodes.Length ? langCodes[_lang] : "";
        }

        public int LanguageCount()
        {
            return langCodes == null ? 0 : langCodes.Length;
        }

        public string LanguageName(int index)
        {
            return langNames != null && index >= 0 && index < langNames.Length ? langNames[index] : "";
        }

        public string Get(int key)
        {
            EnsureInit();
            if (key < 0 || key >= keyCount || values == null) return "??";
            int i = _lang * keyCount + key;
            string s = i < values.Length ? values[i] : "";
            if (string.IsNullOrEmpty(s))
            {
                int d = defaultLang * keyCount + key;
                if (d < values.Length) s = values[d];
            }
            if (string.IsNullOrEmpty(s)) s = keys != null && key < keys.Length ? keys[key] : "??";
            return s;
        }

        /// <summary>Slow path for code that only has a key name (editor tools, debugging). Views use indices.</summary>
        public int IndexOfKey(string key)
        {
            if (keys == null) return -1;
            for (int i = 0; i < keys.Length; i++) if (keys[i] == key) return i;
            return -1;
        }

        // ---------------------------------------------------------------- fan-out

        public void Register(AuiView v)
        {
            if (v == null) return;
            for (int i = 0; i < _viewCount; i++) if (_views[i] == v) return;
            if (_viewCount >= _views.Length)
            {
                AuiView[] grown = new AuiView[_views.Length * 2];
                for (int i = 0; i < _viewCount; i++) grown[i] = _views[i];
                _views = grown;
            }
            _views[_viewCount++] = v;
        }

        public void SetLanguage(int index)
        {
            if (index < 0 || index >= LanguageCount()) return;
            _userChosen = true;
            Apply(index);
            if (_restored) PlayerData.SetString(PD_KEY, langCodes[index]);
            else _pendingSave = true;
        }

        public void SetLanguageByCode(string code)
        {
            int i = ResolveIndex(code);
            if (i >= 0) SetLanguage(i);
        }

        /// <summary>Forget the player's pick and follow the client language again.</summary>
        public void ClearChoice()
        {
            _userChosen = false;
            if (_restored) PlayerData.SetString(PD_KEY, "");
            Apply(ResolveIndex(VRCPlayerApi.GetCurrentLanguage()));
        }

        private void Apply(int index)
        {
            if (index == _lang) return;
            _lang = index;
            for (int i = 0; i < _viewCount; i++)
                if (_views[i] != null) _views[i].SendCustomEvent("_Retranslate");
        }

        // ---------------------------------------------------------------- detection

        private void EnsureInit()
        {
            if (_lang >= 0) return;
            if (langCodes == null || langCodes.Length == 0 || values == null || values.Length != langCodes.Length * keyCount)
            {
                Debug.LogError("[AUI] AuiLanguageService table is inconsistent; run AUI/Rebuild Language Table.");
                _lang = 0;
                return;
            }
            if (defaultLang < 0 || defaultLang >= langCodes.Length) defaultLang = 0;
            // NOT VRCPlayerApi.GetCurrentLanguage() here: under ClientSim that static is wired only
            // once ClientSim has started, which is after every Start(), and a null delegate halts
            // this behaviour for the whole session. The client language is read when the local
            // player joins (OnPlayerJoined), which is after the hook exists on both platforms.
            _lang = defaultLang;
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal || _userChosen) return;
            EnsureInit();
            Apply(ResolveIndex(VRCPlayerApi.GetCurrentLanguage()));
        }

        private int ResolveIndex(string code)
        {
            if (langCodes == null || langCodes.Length == 0) return 0;
            if (string.IsNullOrEmpty(code)) return defaultLang;
            for (int i = 0; i < langCodes.Length; i++) if (langCodes[i] == code) return i;
            string b = Base(code);
            for (int i = 0; i < langCodes.Length; i++) if (langCodes[i] == b) return i;
            for (int i = 0; i < langCodes.Length; i++) if (Base(langCodes[i]) == b) return i;
            return defaultLang;
        }

        private string Base(string c)
        {
            int d = c.IndexOf("-", 0);
            return d < 0 ? c : c.Substring(0, d);
        }

        public override void OnLanguageChanged(string language)
        {
            if (_userChosen) return;
            EnsureInit();
            Apply(ResolveIndex(language));
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal) return;
            _restored = true;
            EnsureInit();
            if (_pendingSave)
            {
                _pendingSave = false;
                PlayerData.SetString(PD_KEY, langCodes[_lang]);
                return;
            }
            string saved;
            if (!PlayerData.HasKey(player, PD_KEY)) return;
            if (!PlayerData.TryGetString(player, PD_KEY, out saved)) return;
            if (string.IsNullOrEmpty(saved)) return;
            for (int i = 0; i < langCodes.Length; i++)
            {
                if (langCodes[i] != saved) continue;
                _userChosen = true;
                Apply(i);
                return;
            }
        }

        // ---------------------------------------------------------------- tween probe

        private void Start()
        {
            EnsureInit();
            VRCTween.DelayedCall(this, nameof(_OnTweenProbe), 0.05f);
            SendCustomEventDelayedSeconds(nameof(_OnProbeDeadline), 1.0f);
        }

        public void _OnTweenProbe()
        {
            _tweensOk = true;
            _probeDone = true;
        }

        public void _OnProbeDeadline()
        {
            _probeDone = true;
        }

        public bool ProbeDone()
        {
            return _probeDone;
        }

        public bool TweensAvailable()
        {
            return _tweensOk;
        }
    }
}
