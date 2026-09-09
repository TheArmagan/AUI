using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;

namespace Armagan.AUI
{
    /// <summary>
    /// The keyboard target behind every &lt;input&gt;. One per scene, built by AUI/Rebuild Input Panel.
    ///
    /// WHY IT WORKS THIS WAY (ported from the world's InputPanelManager, which earned these
    /// rules the hard way): there is no Udon API to open VRChat's keyboard. It appears only
    /// when the player physically clicks an InputField, and never for a field that was
    /// Instantiate()d or whose GameObject was ever inactive. So this panel is authored once,
    /// stays active forever, and is hidden by disabling its Canvas and collider. It draws
    /// NOTHING: the TMP_InputField fills an invisible 2.0 x 1.4 m canvas parked 0.6 m in
    /// front of the player's face, and VRChat's own keyboard is the whole visible UI.
    ///
    /// Opening ARMS the panel; the player's own click on it is what opens the keyboard (a
    /// second click, which the owner accepted as the price of a native keyboard). An armed
    /// panel nobody clicks would silently eat every click aimed through it, so it expires
    /// after clickTimeout seconds; the clock stops the moment the field has focus.
    ///
    /// TMP_InputField exposes no focus API to Udon, so focus is dropped with enabled = false
    /// (Unity's OnDisable deactivates the field). On End Edit is not raised reliably by
    /// VRChat for a TMP field; On Value Changed arriving while the field is NOT focused is
    /// the dependable submit signal, and both are wired so whichever fires first wins.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class AuiInputPanel : UdonSharpBehaviour
    {
        [Header("Wiring (built by AUI/Rebuild Input Panel)")]
        public Canvas canvas;
        public Collider clickCollider;
        public TMP_InputField field;

        [Header("Placement")]
        [Tooltip("Metres along the head ray. Centred on the crosshair; offsets belong on the canvas contents, never here.")]
        public float distance = 0.6f;

        [Tooltip("Seconds for the second click before the invisible panel dismisses itself.")]
        public float clickTimeout = 3f;

        private AuiView _target;
        private int _targetId = -1;
        private int _maxLength = -1;
        private float _armedAt = -1f;
        private bool _open;

        // ---------------------------------------------------------------- API

        /// <summary>
        /// OpenText with a character limit (&lt;input maxlength&gt;): the submitted text is cut to
        /// <paramref name="maxLength"/> characters, -1 for none. Enforced here rather than through
        /// TMP_InputField.characterLimit, whose Udon exposure is not verified.
        /// </summary>
        public void OpenTextMax(string initial, AuiView target, int id, int maxLength)
        {
            OpenText(initial, target, id);
            _maxLength = maxLength;
        }

        /// <summary>Arm the panel for one view element. Submit writes target.inputResult / inputResultId then calls target._OnInputResult().</summary>
        public void OpenText(string initial, AuiView target, int id)
        {
            _maxLength = -1;
            // A view still waiting on this panel (it showed a "typing" state after arming) is told
            // that it lost the keyboard, or that state would stay stuck forever.
            AuiView prev = _target;
            int prevId = _targetId;
            CloseInternal(false);
            _target = target;
            _targetId = id;
            if (prev != null && (prev != target || prevId != id)) prev._OnInputCancelled();
            if (field != null)
            {
                field.enabled = true;
                // SetTextWithoutNotify: a plain `text =` while unfocused reads as a submit (see OnValueChanged).
                field.SetTextWithoutNotify(initial == null ? "" : initial);
            }
            Show();
            _armedAt = _open ? Time.time : -1f;
        }

        /// <summary>Dismiss without a result. The pending view gets _OnInputCancelled().</summary>
        public void Cancel()
        {
            AuiView t = _target;
            ClearTarget();
            CloseInternal(true);
            if (t != null) t._OnInputCancelled();
        }

        public bool IsOpen()
        {
            return _open;
        }

        // ---------------------------------------------------------------- uGUI callbacks

        /// <summary>TMP_InputField > On Value Changed. A change while unfocused means the keyboard closed: submit.</summary>
        public void OnValueChanged()
        {
            if (field == null || field.isFocused) return;
            OnEndEdit();
        }

        /// <summary>TMP_InputField > On End Edit. Gated on a pending target, not on being open, because the arm timeout can close the panel while the keyboard is still up.</summary>
        public void OnEndEdit()
        {
            if (_target == null) return;
            string result = field != null ? field.text : "";
            if (result == null) result = "";
            if (_maxLength > 0 && result.Length > _maxLength) result = result.Substring(0, _maxLength);
            AuiView t = _target;
            int id = _targetId;
            ClearTarget();      // before CloseInternal: DeactivateInputField can re-raise onEndEdit
            CloseInternal(true);
            t.inputResult = result;
            t.inputResultId = id;
            t._OnInputResult();
        }

        // ---------------------------------------------------------------- internal

        private void Start()
        {
            HideAll();
            enabled = false; // Update runs only while a panel is armed
        }

        private void Update()
        {
            if (!_open) { enabled = false; return; }
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null) return;
            if (Vector3.Distance(lp.GetPosition(), transform.position) > 3f)
            {
                // Walked away: the keyboard may still be up; keep the target so Done still lands.
                CloseInternal(false);
                return;
            }
            if (_armedAt < 0f) return;
            if (field != null && field.isFocused) { _armedAt = -1f; return; }
            if (Time.time - _armedAt >= clickTimeout)
            {
                // Nobody clicked: dismiss and tell the view.
                AuiView t = _target;
                ClearTarget();
                CloseInternal(true);
                if (t != null) t._OnInputCancelled();
            }
        }

        private void Show()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null) return;
            VRCPlayerApi.TrackingData head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 forward = head.rotation * Vector3.forward;
            Vector3 up = head.rotation * Vector3.up;
            transform.position = head.position + forward * distance;
            transform.rotation = Quaternion.LookRotation(forward, up);
            if (canvas != null) canvas.enabled = true;
            if (clickCollider != null) clickCollider.enabled = true;
            _open = true;
            enabled = true;
        }

        private void CloseInternal(bool dropFocus)
        {
            if (dropFocus && field != null) field.enabled = false;
            HideAll();
            _armedAt = -1f;
            _open = false;
        }

        private void HideAll()
        {
            if (canvas != null) canvas.enabled = false;
            if (clickCollider != null) clickCollider.enabled = false;
        }

        private void ClearTarget()
        {
            _target = null;
            _targetId = -1;
        }
    }
}
