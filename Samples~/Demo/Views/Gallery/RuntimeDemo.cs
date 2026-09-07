using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>Runtime layout, runtime classes, a modal and an absolute badge in one view.</summary>
    public partial class RuntimeDemo : AuiView
    {
        private int _n;

        protected override void OnStart()
        {
            Status = "0 rows";
            RowsCount = 0;
        }

        public void Add()
        {
            if (_n >= ROWS_MAX) return;
            _n++;
            RowsCount = _n;
            SetRowsLabel(_n - 1, "Row " + _n);
            Status = _n + " rows";
        }

        public void Clear() { Asking = true; }
        public void ConfirmClear() { _n = 0; RowsCount = 0; Status = "0 rows"; CloseConfirm(); }
        public void Cancelled() { Status = "kept " + _n + " rows"; }
        public void Pick(int row) { Status = "picked row " + row; }
        public void Ask() { OpenConfirm(); }

        public void ToggleWarm()
        {
            ToggleClass(E_CARD, C_WARM);
            Tone = HasClass(E_CARD, C_WARM) ? "big" : "";
        }

        private int _lastLine = -1;

        public void Log()
        {
            int line = CreateLine(E_LOG);
            if (line < 0) return;
            SetLineMsg(line, "line " + (ChildCount(E_LOG)));
            _lastLine = line;
        }

        public void Note()
        {
            int note = CreateElementByTag("text", E_LOG, "muted", 0);
            if (note >= 0) SetText(note, "note at top");
        }

        public void DropLast()
        {
            if (_lastLine >= 0) { DestroyElement(_lastLine); _lastLine = -1; }
        }

        public void LiftLast()
        {
            if (_lastLine >= 0) MoveElement(_lastLine, E_LOG, 0);
        }

        private bool _dim;

        public void Dim()
        {
            _dim = !_dim;
            LogAlpha = _dim ? 0.4f : 1f;      // style="opacity: {logAlpha}"
            LogPad = _dim ? "24" : "12";      // style="padding: {logPad}"
            ToggleClassNamed(E_LOG, "*gap-4");
        }
    }
}
