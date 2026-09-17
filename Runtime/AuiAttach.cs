using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Armagan.AUI
{
    /// <summary>
    /// Ties an outermost view to the local player: &lt;view attach="..."&gt;. Added to the prefab root by
    /// the builder when the attribute is present, and driven at runtime through AuiView.SetAttach.
    ///
    /// A SEPARATE BEHAVIOUR ON PURPOSE. AuiView's contract is that nothing in it runs per frame; a
    /// followed panel has to. This is the same escape hatch AuiInputPanel uses: a sibling behaviour
    /// whose `enabled` is false whenever it has nothing to move, so a hidden view, a `front` view
    /// after its one placement, and attach="none" all cost nothing.
    ///
    /// IT MOVES THE ROOT AND NOTHING ELSE. AuiView never moves or deactivates its own GameObject,
    /// only `content`, so the two cannot fight. A wrist view out of sight is hidden by disabling the
    /// root Canvas and collider, never through content or its CanvasGroup: those belong to Show/Hide
    /// and its tweens, and touching them here would snap a fade half way.
    ///
    /// PostLateUpdate, NOT Update. Tracking data for the frame is final only after VRChat's own
    /// late update; placing a head-locked panel in Update leaves it one frame behind the head, which
    /// reads as a panel that swims whenever the player turns.
    ///
    /// THE WRIST MODE DOES NOT USE THE HAND'S ROTATION. The orientation of hand tracking data differs
    /// between controllers, and avatar bones differ between rigs, so a panel glued to a hand axis
    /// would sit differently for every player. Instead the panel sits at the hand's position, faces
    /// the head, and shows while the player is looking at that hand from close by: the "glance at
    /// your watch" gesture, which needs positions only.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class AuiAttach : UdonSharpBehaviour
    {
        // Index = position in AuiSchema.AttachModes / AttachCorners. Append-only, like those lists.
        public const int MODE_NONE = 0, MODE_WRIST_LEFT = 1, MODE_WRIST_RIGHT = 2, MODE_CORNER = 3, MODE_FOLLOW = 4, MODE_FRONT = 5;
        public const int CORNER_BOTTOM_LEFT = 0, CORNER_BOTTOM_RIGHT = 1, CORNER_TOP_LEFT = 2, CORNER_TOP_RIGHT = 3;

        [Header("Wiring (baked by the AUI builder)")]
        [HideInInspector] public AuiView view;
        [HideInInspector] public Canvas rootCanvas;
        [HideInInspector] public Collider rootCollider;
        /// <summary>The view has at least one control. A display-only view keeps its collider off so it never blocks a laser.</summary>
        [HideInInspector] public bool interactive;
        [HideInInspector] public int mode;
        [HideInInspector] public int corner;
        [HideInInspector] public float distance = 0.6f;

        [Header("Wrist (VR)")]
        [Tooltip("Metres from the hand, in head space: x right, y up, z toward the head.")]
        public Vector3 wristOffset = new Vector3(0f, 0.07f, 0.03f);
        [Tooltip("The hand must be this close to the head (metres) to show the view.")]
        public float wristRange = 0.6f;
        [Tooltip("Degrees between the gaze and the hand at which the view appears.")]
        public float wristShowAngle = 24f;
        [Tooltip("Degrees at which it disappears again. Larger than the show angle so it does not flicker at the edge.")]
        public float wristHideAngle = 36f;

        [Header("Corner (desktop, and the wrist fallback)")]
        [Tooltip("Degrees from the centre of the view: x sideways, y up or down.")]
        public Vector2 cornerAngles = new Vector2(27f, 15f);

        [Header("Follow")]
        [Tooltip("Metres below eye level.")]
        public float followDrop = 0.12f;
        [Tooltip("The view recentres once the player has turned this far from it (degrees).")]
        public float followAngle = 38f;
        [Tooltip("...or moved this far from where it was placed for (metres).")]
        public float followSlack = 0.45f;
        [Tooltip("Higher is snappier. 0 jumps.")]
        public float followSpeed = 5f;

        private bool _viewShown;
        private bool _wristVisible;
        private bool _moving;          // follow: gliding to a new rest pose
        private bool _placed;          // follow / front: has a pose at all
        private Vector3 _restHead;     // follow: where the head was when the rest pose was chosen
        // A DELAYED EVENT CANNOT CARRY STATE, so the sleep after a hide is guarded by a sequence number:
        // Hide then Show inside the fade would otherwise put a visible view to sleep.
        private int _hideSeq, _pendingHideSeq;

        // ---------------------------------------------------------------- called by AuiView

        /// <summary>Show / ShowInstant. `front` places here, once.</summary>
        public void _OnViewShown()
        {
            _hideSeq++;
            _viewShown = true;
            _placed = false;
            _Apply();
        }

        /// <summary>Hide / HideInstant. Keeps following through the fade, then sleeps.</summary>
        public void _OnViewHidden()
        {
            _viewShown = false;
            _hideSeq++;
            _pendingHideSeq = _hideSeq;
            SendCustomEventDelayedSeconds(nameof(_SleepAfterHide), view != null ? view.hideDur + 0.05f : 0.05f);
        }

        public void _SleepAfterHide()
        {
            if (_pendingHideSeq != _hideSeq || _viewShown) return;
            enabled = false;
        }

        /// <summary>A setter changed mode, corner or distance.</summary>
        public void _OnChanged()
        {
            _placed = false;
            _Apply();
        }

        // ---------------------------------------------------------------- internal

        private void Start()
        {
            // A NESTED VIEW HAS NO CANVAS TO MOVE. The builder does not add this component to one, but a
            // prefab dragged under another view by hand would otherwise fly to the player's wrist.
            if (view != null && view.parentView != null) { mode = MODE_NONE; enabled = false; return; }
            if (view != null)
            {
                view.EnsureStarted();
                _viewShown = view.IsVisible();
            }
            _Apply();
        }

        private void _Apply()
        {
            int m = EffectiveMode();
            if (m != MODE_WRIST_LEFT && m != MODE_WRIST_RIGHT) SetRootVisible(true);
            if (m == MODE_NONE || !_viewShown) { enabled = false; return; }
            if (m == MODE_FRONT)
            {
                PlaceFront();
                enabled = false;
                return;
            }
            _wristVisible = true;   // forces the first Tick to write the real state
            enabled = true;
            Tick(true);
        }

        /// <summary>A wrist view has no wrist to sit on outside VR.</summary>
        private int EffectiveMode()
        {
            if (mode != MODE_WRIST_LEFT && mode != MODE_WRIST_RIGHT) return mode;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null || !lp.IsUserInVR()) return MODE_CORNER;
            return mode;
        }

        public override void PostLateUpdate()
        {
            Tick(false);
        }

        private void Tick(bool instant)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null) return;
            VRCPlayerApi.TrackingData head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            int m = EffectiveMode();
            if (m == MODE_CORNER) TickCorner(head);
            else if (m == MODE_FOLLOW) TickFollow(head, instant);
            else if (m == MODE_WRIST_LEFT) TickWrist(head, lp.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand));
            else if (m == MODE_WRIST_RIGHT) TickWrist(head, lp.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand));
        }

        private void TickCorner(VRCPlayerApi.TrackingData head)
        {
            float yaw = (corner == CORNER_BOTTOM_LEFT || corner == CORNER_TOP_LEFT) ? -cornerAngles.x : cornerAngles.x;
            // Positive pitch looks down in Unity's Euler convention.
            float pitch = (corner == CORNER_BOTTOM_LEFT || corner == CORNER_BOTTOM_RIGHT) ? cornerAngles.y : -cornerAngles.y;
            Quaternion rot = head.rotation * Quaternion.Euler(pitch, yaw, 0f);
            Vector3 dir = rot * Vector3.forward;
            // A uGUI canvas is read looking ALONG its +Z, so facing the player means forward = away from the head.
            transform.SetPositionAndRotation(head.position + dir * distance, Quaternion.LookRotation(dir, head.rotation * Vector3.up));
        }

        private void TickWrist(VRCPlayerApi.TrackingData head, VRCPlayerApi.TrackingData hand)
        {
            Vector3 toHand = hand.position - head.position;
            float range = toHand.magnitude;
            Vector3 gaze = head.rotation * Vector3.forward;
            float angle = range > 0.001f ? Vector3.Angle(gaze, toHand) : 180f;
            bool want = range <= wristRange && angle <= (_wristVisible ? wristHideAngle : wristShowAngle);
            if (want != _wristVisible)
            {
                _wristVisible = want;
                SetRootVisible(want);
            }
            if (!want) return;

            Vector3 right = head.rotation * Vector3.right;
            Vector3 up = head.rotation * Vector3.up;
            Vector3 back = range > 0.001f ? -toHand / range : -gaze;
            Vector3 pos = hand.position + right * wristOffset.x + up * wristOffset.y + back * wristOffset.z;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position, up));
        }

        private void TickFollow(VRCPlayerApi.TrackingData head, bool instant)
        {
            Vector3 flat = head.rotation * Vector3.forward;
            flat.y = 0f;
            // Looking straight up or down leaves no heading; keep the last pose rather than spin.
            if (flat.sqrMagnitude < 0.0001f) { if (_placed) return; flat = Vector3.forward; }
            flat = flat.normalized;
            Vector3 target = head.position + flat * distance + Vector3.down * followDrop;

            if (!_placed)
            {
                _placed = true;
                _moving = false;
                _restHead = head.position;
                transform.SetPositionAndRotation(target, Quaternion.LookRotation(target - head.position, Vector3.up));
                return;
            }

            if (!_moving)
            {
                Vector3 toPanel = transform.position - head.position;
                toPanel.y = 0f;
                bool turned = toPanel.sqrMagnitude > 0.0001f && Vector3.Angle(flat, toPanel) > followAngle;
                bool walked = Vector3.Distance(head.position, _restHead) > followSlack;
                if (!turned && !walked) return;
                _moving = true;
            }

            float t = (instant || followSpeed <= 0f) ? 1f : Mathf.Clamp01(followSpeed * Time.deltaTime);
            Vector3 pos = Vector3.Lerp(transform.position, target, t);
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position, Vector3.up));
            if (Vector3.Distance(pos, target) < 0.01f)
            {
                _moving = false;
                _restHead = head.position;
            }
        }

        /// <summary>The AuiInputPanel placement: once, straight ahead, then the view stays in the world.</summary>
        private void PlaceFront()
        {
            if (_placed) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null) return;
            VRCPlayerApi.TrackingData head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 forward = head.rotation * Vector3.forward;
            transform.SetPositionAndRotation(head.position + forward * distance, Quaternion.LookRotation(forward, head.rotation * Vector3.up));
            _placed = true;
        }

        private void SetRootVisible(bool visible)
        {
            if (rootCanvas != null) rootCanvas.enabled = visible;
            if (rootCollider != null) rootCollider.enabled = visible && interactive;
        }
    }
}
