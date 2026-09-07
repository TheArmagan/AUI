using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>A leaderboard repeat (rows are buttons with a toggle inside) and a chip grid; add, remove, shuffle, sort, pick, favourite, empty state.</summary>
    public partial class ListDemo : AuiView
    {
        private readonly string[] _pool = { "Ada", "Grace", "Linus", "Yukihiro", "Hedy", "Ayşe", "Ken", "Barbara" };
        private readonly string[] _tagNames = { "vr", "desktop", "quest", "creator", "music", "photo", "event", "beta" };
        private string[] _names = new string[BOARD_MAX];
        private int[] _scores = new int[BOARD_MAX];
        private bool[] _favs = new bool[BOARD_MAX];
        private bool[] _tagOn = new bool[TAGS_MAX];
        private int _n;
        private int _pickedRow = -1;

        protected override void OnStart()
        {
            for (int i = 0; i < 3; i++) { _names[i] = _pool[i]; _scores[i] = 90 - i * 17; }
            HoverHint = "";
            SetSize(3);
            TagsCount = TAGS_MAX;
            for (int i = 0; i < TAGS_MAX; i++) { SetTagsLabel(i, _tagNames[i]); SetTagsOn(i, _tagOn[i]); }
        }

        public void Add()
        {
            if (_n >= BOARD_MAX) return;
            _names[_n] = _pool[Random.Range(0, _pool.Length)];
            _scores[_n] = Random.Range(5, 100);
            _favs[_n] = false;
            SetSize(_n + 1);
            Emit("changed");
        }

        public void Remove() { if (_n > 0) { if (_pickedRow == _n - 1) _pickedRow = -1; SetSize(_n - 1); } Emit("changed"); }
        public void Clear() { _pickedRow = -1; SetSize(0); Emit("changed"); }

        public void Shuffle()
        {
            for (int i = _n - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                string tn = _names[i]; _names[i] = _names[j]; _names[j] = tn;
                int ts = _scores[i]; _scores[i] = _scores[j]; _scores[j] = ts;
                bool tf = _favs[i]; _favs[i] = _favs[j]; _favs[j] = tf;
            }
            _pickedRow = -1;
            Render();
            Emit("changed");
        }

        public void Sort()
        {
            for (int i = 1; i < _n; i++)
                for (int j = i; j > 0 && _scores[j] > _scores[j - 1]; j--)
                {
                    string tn = _names[j]; _names[j] = _names[j - 1]; _names[j - 1] = tn;
                    int ts = _scores[j]; _scores[j] = _scores[j - 1]; _scores[j - 1] = ts;
                    bool tf = _favs[j]; _favs[j] = _favs[j - 1]; _favs[j - 1] = tf;
                }
            _pickedRow = -1;
            Render();
            Emit("changed");
        }

        /// <summary>onselect on the repeat: the row root (the button) was clicked.</summary>
        public void Pick(int row)
        {
            _pickedRow = row;
            Render();
            EmitInt("picked", row);
        }

        /// <summary>A handler inside a template gets the row first, then the control's value.</summary>
        public void FavChanged(int row, bool value) { _favs[row] = value; Emit("changed"); }

        // Icon-only buttons explain themselves on hover (onhover / onleave are plain pointer handlers).
        public void HintRemove() { HoverHint = "remove the last row"; }
        public void HintShuffle() { HoverHint = "shuffle the rows"; }
        public void HintSort() { HoverHint = "sort by score"; }
        public void HintClearAll() { HoverHint = "clear the board"; }
        public void HintClear() { HoverHint = ""; }

        public void PickTag(int row) { _tagOn[row] = !_tagOn[row]; SetTagsOn(row, _tagOn[row]); Emit("changed"); }

        /// <summary>Also used by the gallery's self-test.</summary>
        public void SetSize(int n)
        {
            _n = n;
            if (_pickedRow >= _n) _pickedRow = -1;
            Render();
        }

        private void Render()
        {
            SetAutoRelayout(false);               // one solve for the whole batch
            BoardCount = _n;
            for (int i = 0; i < _n; i++)
            {
                SetBoardRank(i, "#" + (i + 1));
                SetBoardName(i, _names[i] ?? "?");
                SetBoardScore(i, _scores[i].ToString());
                SetBoardFav(i, _favs[i]);
                SetBoardPicked(i, i == _pickedRow);
            }
            Count = _n.ToString();
            Picked = _pickedRow < 0 ? "none" : (_pickedRow + 1).ToString();
            SetAutoRelayout(true);
        }

        public int Size() { return _n; }
        public int PickedRow() { return _pickedRow; }

        /// <summary>How many row roots are actually active, read from the baked table.</summary>
        public int VisibleRows()
        {
            int visible = 0;
            for (int i = 0; i < BOARD_MAX; i++)
            {
                int e = RowElem(0, i, 0);
                if (elemObjects[e] != null && elemObjects[e].activeSelf) visible++;
            }
            return visible;
        }
    }
}
