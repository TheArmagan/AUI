using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>A repeat with a fixed pool: add, remove, clear, pick a row, empty state.</summary>
    public partial class ListDemo : AuiView
    {
        private int _n;
        private int _picked = -1;

        protected override void OnStart()
        {
            SetSize(0);
        }

        public void Add() { if (_n < ITEMS_MAX) SetSize(_n + 1); Emit("changed"); }
        public void Remove() { if (_n > 0) SetSize(_n - 1); Emit("changed"); }
        public void Clear() { SetSize(0); Emit("changed"); }

        public void Pick(int row)
        {
            _picked = row;
            for (int i = 0; i < _n; i++) SetItemsPicked(i, i == row);
            Emit("picked");
        }

        /// <summary>Also used by the gallery's self-test.</summary>
        public void SetSize(int n)
        {
            _n = n;
            ItemsCount = n;
            Count = n.ToString();
            for (int i = 0; i < n; i++)
            {
                SetItemsLabel(i, "Item " + (i + 1));
                SetItemsPicked(i, i == _picked);
            }
        }

        public int Size() { return _n; }
        public int Picked() { return _picked; }

        /// <summary>How many row roots are actually active, read from the baked table.</summary>
        public int VisibleRows()
        {
            int visible = 0;
            for (int i = 0; i < ITEMS_MAX; i++)
            {
                int e = repeatFirst[0] + i * repeatStride[0];
                if (elemObjects[e] != null && elemObjects[e].activeSelf) visible++;
            }
            return visible;
        }
    }
}
