using UnityEngine;
using Armagan.AUI;

namespace Armagan.AUI.Generated
{
    /// <summary>
    /// &lt;view attach&gt; from code: one button per mode, a corner cycle and a distance slider. The markup
    /// says attach="none", which is what bakes the AuiAttach component these calls drive; without
    /// the attribute SetAttach warns once and does nothing.
    /// </summary>
    public partial class AttachDemo : AuiView
    {
        // Index = ATTACH_* / CORNER_*. The markup's own words, so they are not translated.
        private readonly string[] _modeNames = { "none", "wrist-left", "wrist-right", "corner", "follow", "front" };
        private readonly string[] _cornerNames = { "bottom-left", "bottom-right", "top-left", "top-right" };
        private int _corner;

        protected override void OnStart()
        {
            Metres = 0.9f;   // the markup's attach-distance; setting the binding does not call the handler
            Render();
        }

        public void ModeNone() { Apply(ATTACH_NONE); }
        public void ModeWristLeft() { Apply(ATTACH_WRIST_LEFT); }
        public void ModeWristRight() { Apply(ATTACH_WRIST_RIGHT); }
        public void ModeCorner() { Apply(ATTACH_CORNER); }
        public void ModeFollow() { Apply(ATTACH_FOLLOW); }
        public void ModeFront() { Apply(ATTACH_FRONT); }

        /// <summary>Where a corner view sits, and where a wrist view sits for a player who is not in VR.</summary>
        public void NextCorner()
        {
            _corner = _corner == CORNER_TOP_RIGHT ? CORNER_BOTTOM_LEFT : _corner + 1;
            SetAttachCorner(_corner);
            Render();
        }

        /// <summary>Fires on every frame of a drag; SetAttachDistance is one field write and a re-place, so no debounce.</summary>
        public void DistanceChanged(float value)
        {
            SetAttachDistance(value);
        }

        private void Apply(int mode)
        {
            SetAttach(mode);
            Render();
        }

        private void Render()
        {
            int mode = GetAttach();
            IsNone = mode == ATTACH_NONE;
            IsWristLeft = mode == ATTACH_WRIST_LEFT;
            IsWristRight = mode == ATTACH_WRIST_RIGHT;
            IsCorner = mode == ATTACH_CORNER;
            IsFollow = mode == ATTACH_FOLLOW;
            IsFront = mode == ATTACH_FRONT;
            ModeName = _modeNames[mode];
            CornerName = _cornerNames[_corner];
        }
    }
}
