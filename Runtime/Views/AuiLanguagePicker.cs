using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>
    /// The language picker every world gets for free: one chip per shipped language, the
    /// current one highlighted. Tapping a chip calls AuiLanguageService.SetLanguage, which
    /// retranslates every live view and remembers the choice in PlayerData.
    /// </summary>
    public partial class AuiLanguagePicker : AuiView
    {
        protected override void OnStart()
        {
            Refresh();
        }

        protected override void OnRetranslate()
        {
            Refresh();
        }

        public void Pick(int row)
        {
            if (lang != null) lang.SetLanguage(row);
        }

        private void Refresh()
        {
            if (lang == null) return;
            int n = lang.LanguageCount();
            LangsCount = n;
            int current = lang.GetLanguage();
            for (int i = 0; i < n && i < LANGS_MAX; i++)
            {
                SetLangsName(i, lang.LanguageName(i));
                SetLangsCurrent(i, i == current);
            }
        }
    }
}
