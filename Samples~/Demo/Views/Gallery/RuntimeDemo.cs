using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>
    /// Runtime creation without allocation: template instances, pooled texts, a nested view in a
    /// template with a typed event back to this view, reorder and reparent, and a confirm modal.
    /// </summary>
    public partial class RuntimeDemo : AuiView
    {
        private readonly string[] _who = { "Ada", "Linus", "Grace", "Hedy" };
        private readonly string[] _msgs = { "hello everyone", "who built this?", "the log is a template", "notes come from a pool", "cards are nested views", "click a card's x" };
        private int _said, _dealt;
        private int _last = -1;

        protected override void OnStart() { Refresh(); }

        /// <summary>A template instance under the log; when all eight are in use the oldest line goes back to the pool.</summary>
        public void Say()
        {
            int line = CreateLine(E_LOG);
            if (line < 0) { DestroyElement(ChildAt(E_LOG, 0)); line = CreateLine(E_LOG); }
            if (line < 0) return;
            SetLineWho(line, _who[_said % _who.Length]);
            SetLineMsg(line, _msgs[_said % _msgs.Length]);
            _said++;
            _last = line;
            Refresh();
        }

        /// <summary>A plain text from the view's pool (pool="text:6"), inserted at the top with classes applied.</summary>
        public void Note()
        {
            int note = CreateElementByTag("text", E_LOG, "hint", 0);
            if (note < 0) { Status = "the note pool is empty"; Emit("changed"); return; }
            SetText(note, "note " + ChildCount(E_LOG) + " at the top");
            _last = note;
            Refresh();
        }

        public void Lift() { if (_last >= 0) MoveElement(_last, E_LOG, 0); Refresh(); }
        public void Drop() { if (_last >= 0) { DestroyElement(_last); _last = -1; } Refresh(); }

        /// <summary>A parked PlayerCard linked under the deck; the cast gives the child's typed properties.</summary>
        public void Deal()
        {
            AuiView v = CreateView(T_CARDS, E_DECK, -1);
            if (v == null) { Status = "all three cards are dealt"; Emit("changed"); return; }
            PlayerCard card = (PlayerCard)v;
            card.Nick = _who[_dealt % _who.Length];
            card.SetPoints(Random.Range(10, 99));
            _dealt++;
            Refresh();
        }

        /// <summary>on-kicked on a view inside a template: the instance element comes first, then the int payload.</summary>
        public void CardKicked(int elem, int score)
        {
            DestroyElement(elem);
            Status = "kicked a card holding " + score + " points";
            Emit("changed");
        }

        // ---- modal
        public void AskClear() { Asking = true; }                          // the binding opens it
        public void Cancelled() { Status = "kept " + ChildCount(E_LOG) + " lines"; Emit("changed"); }
        public void ConfirmClear()
        {
            while (ChildCount(E_LOG) > 0) DestroyElement(ChildAt(E_LOG, 0));
            _last = -1;
            CloseConfirm();
            Refresh();
        }

        private void Refresh()
        {
            Status = ChildCount(E_LOG) + " lines, " + ChildCount(E_DECK) + " cards";
            Emit("changed");
        }

        // ---- for the gallery's self-test
        public int LineCount() { return ChildCount(E_LOG); }
        public int CardCount() { return ChildCount(E_DECK); }
        public void DropCards() { while (ChildCount(E_DECK) > 0) DestroyElement(ChildAt(E_DECK, 0)); Refresh(); }
    }
}
