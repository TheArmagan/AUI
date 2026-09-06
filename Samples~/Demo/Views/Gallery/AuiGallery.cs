using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>
    /// The gallery: five feature views nested in pages, their Emit()s routed into a log, and a
    /// self-test that drives every child through its typed API and checks what rendered.
    /// Everything a view can do to another view is in here: typed child fields, property
    /// writes, read-back methods, page routing, language switching, event bubbling.
    /// </summary>
    public partial class AuiGallery : AuiView
    {
        private const int LOG_MAX = 9;
        private string[] _lines = new string[LOG_MAX];
        private int _lineCount;
        private int _passCount, _failCount;
        private bool _pinged;

        protected override void OnStart()
        {
            Pass = "0";
            Fail = "0";
            Status = "";
            LinesCount = 0;
        }

        // ---- child events (on-<event> attributes in the markup)
        public void TextsChanged() { Log("texts: " + (childTextsDemo != null ? childTextsDemo.Status : "?")); }
        public void ControlsChanged() { Log("controls: " + (childControlsDemo != null ? childControlsDemo.Last : "?")); }
        public void ControlsPinged() { _pinged = true; Log("controls: ping"); }
        public void ListChanged() { Log("list: " + (childListDemo != null ? childListDemo.Size().ToString() : "?") + " rows"); }
        public void ListPicked() { Log("list: picked " + (childListDemo != null ? childListDemo.Picked().ToString() : "?")); }
        public void StatesHit() { Log("states: hit " + (childStatesDemo != null ? childStatesDemo.HitCount().ToString() : "?")); }
        public void StatesChanged() { Log("states: flipped"); }
        public void LangChanged() { Log("language: " + (childLangDemo != null ? childLangDemo.Code : "?")); }

        // ---- log
        public void ClearLog()
        {
            _lineCount = 0;
            LinesCount = 0;
        }

        private void Log(string line)
        {
            // newest first, oldest falls off the end
            for (int i = LOG_MAX - 1; i > 0; i--) _lines[i] = _lines[i - 1];
            _lines[0] = line;
            if (_lineCount < LOG_MAX) _lineCount++;
            LinesCount = _lineCount;
            for (int i = 0; i < _lineCount; i++) SetLinesLine(i, _lines[i]);
        }

        // ---- self-test
        public void RunTests()
        {
            _passCount = 0; _failCount = 0; _pinged = false;
            Status = "";
            if (childTextsDemo == null || childControlsDemo == null || childListDemo == null || childStatesDemo == null || childLangDemo == null)
            {
                Log("children not wired");
                Fail = "1";
                return;
            }

            // texts: a property write reaches the label, and untrusted text is escaped
            childTextsDemo.Status = "probe";
            Check("bound text", childTextsDemo.RenderedBound() == "probe");
            childTextsDemo.Status = "<b>x";
            Check("escape", childTextsDemo.RenderedBound().IndexOf("<​", 0) == 0);
            childTextsDemo.Who = "Ada"; childTextsDemo.Count = "7";
            Check("placeholders", childTextsDemo.RenderedArgs().IndexOf("Ada", 0) >= 0 && childTextsDemo.RenderedArgs().IndexOf("7", 0) >= 0);

            // controls: two-way bindings from the code side
            childControlsDemo.Volume = 42f;
            Check("slider value", Mathf.Abs(childControlsDemo.Volume - 42f) < 0.01f);
            Check("slider label", childControlsDemo.VolumeLabel().IndexOf("42", 0) >= 0);
            childControlsDemo.Mute = true;
            Check("toggle selected", childControlsDemo.MuteShown());
            childControlsDemo.Mute = false;
            Check("toggle cleared", !childControlsDemo.MuteShown());
            childControlsDemo.Quality = 2;
            Check("select value", childControlsDemo.Quality == 2 && childControlsDemo.QualityLabel().Length > 0);
            childControlsDemo.Locked = true;
            Check("disabled button", !childControlsDemo.PingEnabled());
            childControlsDemo.Locked = false;
            Check("enabled button", childControlsDemo.PingEnabled());
            childControlsDemo.Ping();
            Check("child emit", _pinged);

            // lists: pool size and visibility
            childListDemo.SetSize(4);
            Check("repeat count", childListDemo.Size() == 4 && childListDemo.VisibleRows() == 4);
            childListDemo.SetSize(0);
            Check("repeat empty", childListDemo.VisibleRows() == 0);

            // states
            childStatesDemo.Sel = false;
            childStatesDemo.Off = false;
            Check("state flags", !childStatesDemo.Sel && !childStatesDemo.Off);

            // pages: switch and read back
            SelectMainPage(MAIN_LISTS);
            Check("page select", GetMainPage() == MAIN_LISTS);

            // language: round trip through the service, then restore
            if (lang != null && lang.LanguageCount() > 1)
            {
                int before = lang.GetLanguage();
                int other = before == 0 ? 1 : 0;
                lang.SetLanguage(other);
                Check("language switch", lang.GetLanguage() == other && childLangDemo.Code == lang.GetLanguageCode());
                lang.SetLanguage(before);
                Check("language restore", lang.GetLanguage() == before);
            }

            SelectMainPage(MAIN_LOG);
            Status = _failCount == 0 ? "all passed" : _failCount + " failed";
        }

        private void Check(string name, bool ok)
        {
            if (ok) _passCount++; else _failCount++;
            Pass = _passCount.ToString();
            Fail = _failCount.ToString();
            Log((ok ? "PASS " : "FAIL ") + name);
            // The log page holds nine lines; the console keeps the whole run.
            Debug.Log("[AUI gallery] " + (ok ? "PASS " : "FAIL ") + name);
        }

        // ---- editor hook
        public void _TestRun() { RunTests(); }
    }
}
