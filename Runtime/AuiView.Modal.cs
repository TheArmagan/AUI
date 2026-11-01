using UnityEngine;
using VRC.SDK3.Components;

namespace Armagan.AUI
{
    /// <summary>
    /// Modals: a body over a dimming scrim, both baked as the LAST children of Content so they
    /// draw over everything without a Canvas or sorting order of their own. The scrim swallows
    /// clicks and, when the modal is dismissable, closes it. A select's popup keeps its own
    /// Canvas (sorting order 100), so an open dropdown still draws above an open modal.
    ///
    /// Opening moves the scrim and body to the end of Content again, so the last opened modal
    /// is on top; Back() closes the open select first, then the top dismissable modal.
    /// Hide() leaves the modal stack alone: the view comes back with its modal still open.
    /// </summary>
    public partial class AuiView
    {
        public const int KIND_MODAL_SCRIM = 9;

        [Header("Modals (baked)")]
        [HideInInspector] public int[] modalElem;        // body element
        [HideInInspector] public int[] modalScrimElem;   // scrim element
        [HideInInspector] public int[] modalScrimSig;    // scrim's signal id
        [HideInInspector] public int[] modalKind;        // TRANS_* for the body
        [HideInInspector] public bool[] modalDismissable;
        [HideInInspector] public bool[] modalStartOpen;
        [HideInInspector] public float modalDur = 0.16f;

        private bool[] _modalOpen;
        private int[] _modalStack;
        private int _modalDepth;
        private int _modalSeq;
        /// <summary>When the snap trailing each modal's latest open/close is due (see _PageSnap for the same guard).</summary>
        private float[] _modalSnapAt;

        private void _AuiModalsInit()
        {
            int count = modalElem == null ? 0 : modalElem.Length;
            _modalOpen = new bool[count];
            _modalStack = new int[count];
            _modalSnapAt = new float[count];
            _modalDepth = 0;
            for (int m = 0; m < count; m++)
            {
                if (elemObjects[modalElem[m]] != null) elemObjects[modalElem[m]].SetActive(false);
                if (elemObjects[modalScrimElem[m]] != null) elemObjects[modalScrimElem[m]].SetActive(false);
            }
            for (int m = 0; m < count; m++) if (modalStartOpen != null && m < modalStartOpen.Length && modalStartOpen[m]) SetModal(m, true, false);
        }

        public int ModalCount() { return modalElem == null ? 0 : modalElem.Length; }

        public bool IsModalOpen(int modal)
        {
            modal = EnsureStartedI(modal);
            return _modalOpen != null && modal >= 0 && modal < _modalOpen.Length && _modalOpen[modal];
        }

        /// <summary>The topmost open modal, or -1.</summary>
        public int TopModal()
        {
            EnsureStarted();
            return _modalDepth > 0 ? _modalStack[_modalDepth - 1] : -1;
        }

        /// <summary>Open or close a modal. notify = run the generated hook (binding + onopen/onclose); false when set from a binding.</summary>
        public void SetModal(int modal, bool open, bool notify)
        {
            if (!_started) { _auiStashI = modal; _auiStashB = open; _auiStashI2 = notify ? 1 : 0; _AuiStart(); modal = _auiStashI; open = _auiStashB; notify = _auiStashI2 != 0; }
            if (_modalOpen == null || modal < 0 || modal >= _modalOpen.Length) return;
            if (_modalOpen[modal] == open) return;
            _modalOpen[modal] = open;
            _modalSeq++;
            int body = modalElem[modal], scrim = modalScrimElem[modal];
            if (open)
            {
                CloseSelect();
                _modalStack[_modalDepth++] = modal;
                // Last opened on top: both move to the end of Content again.
                if (elemObjects[scrim] != null) { elemObjects[scrim].transform.SetAsLastSibling(); elemObjects[scrim].SetActive(true); }
                if (elemObjects[body] != null) { elemObjects[body].transform.SetAsLastSibling(); elemObjects[body].SetActive(true); }
                if (elemGroups[scrim] != null)
                {
                    elemGroups[scrim].blocksRaycasts = true;
                    elemGroups[scrim].interactable = true;
                    if (Tweens()) { elemGroups[scrim].alpha = 0f; VRCTween.TweenFade(elemGroups[scrim], 1f, modalDur, VRCTweenEase.OutQuad); }
                    else elemGroups[scrim].alpha = 1f;
                }
                VRCTweenHandle a = new VRCTweenHandle(), b = new VRCTweenHandle(), c = new VRCTweenHandle();
                Enter(elemGroups[body], elemRects[body], modalKind[modal], modalDur, new Vector2(0f, -16f), 0.94f, elemBasePos[body], ref a, ref b, ref c);
                MarkLayoutDirty();   // texts that changed while the modal was closed get measured now
            }
            else
            {
                RemoveFromStack(modal);
                if (elemGroups[scrim] != null)
                {
                    elemGroups[scrim].blocksRaycasts = false;
                    elemGroups[scrim].interactable = false;
                    if (Tweens()) VRCTween.TweenFade(elemGroups[scrim], 0f, modalDur, VRCTweenEase.InQuad); else elemGroups[scrim].alpha = 0f;
                }
                // Off now, not at the snap, as Hide and PageExit do. Exit only fades, so a closing body
                // otherwise stays on top taking clicks; nothing else protects the modal below it.
                if (elemGroups[body] != null)
                {
                    elemGroups[body].blocksRaycasts = false;
                    elemGroups[body].interactable = false;
                }
                VRCTweenHandle a = new VRCTweenHandle(), b = new VRCTweenHandle(), c = new VRCTweenHandle();
                Exit(elemGroups[body], elemRects[body], modalKind[modal], modalDur, new Vector2(0f, -16f), 0.94f, elemBasePos[body], ref a, ref b, ref c);
                ResetPointerStatesUnder(body);
            }
            // Per-modal due time: an open followed by a close inside modalDur used to have the
            // open's snap kill the exit tween and deactivate the body mid-animation.
            _modalSnapAt[modal] = Time.time + modalDur + 0.02f;
            SendCustomEventDelayedSeconds(nameof(_ModalSnap), modalDur + 0.02f);
            if (notify) _OnModalChanged(modal, open);
        }

        private void RemoveFromStack(int modal)
        {
            int w = 0;
            for (int i = 0; i < _modalDepth; i++) if (_modalStack[i] != modal) _modalStack[w++] = _modalStack[i];
            _modalDepth = w;
        }

        /// <summary>
        /// Trails every open/close: closed modals are deactivated once their exit tween is over.
        ///
        /// A SNAP THAT FINDS A MODAL NOT DUE MUST BOOK ANOTHER. "Not due" does not prove a newer snap
        /// is coming: this can be the modal's own and last one, fired early. Udon's scheduler adds
        /// Time.deltaTime in UdonManager.Update, and a click arrives from EventSystem.Update, which
        /// runs before it (order -1000 against 0), so the event is booked one frame's delta behind
        /// the Time.time recorded as due. Measured with tweens on: 6 of 12 closes from a click
        /// skipped their only snap, 0 of 12 from LateUpdate. The closed modal then stayed active
        /// on top of the one below it, invisible, until the next open or close.
        /// </summary>
        public void _ModalSnap()
        {
            if (_modalOpen == null || _modalSnapAt == null) return;
            float rearm = 0f;
            for (int m = 0; m < _modalOpen.Length; m++)
            {
                float left = _modalSnapAt[m] - Time.time;
                if (left > 0.005f)   // still animating
                {
                    if (left > rearm) rearm = left;
                    continue;
                }
                int body = modalElem[m], scrim = modalScrimElem[m];
                if (_modalOpen[m])
                {
                    SnapShown(elemGroups[body], elemRects[body], elemBasePos[body]);
                    if (elemGroups[scrim] != null) elemGroups[scrim].alpha = 1f;
                    continue;
                }
                if (elemObjects[body] != null && elemObjects[body].activeSelf)
                {
                    VRCTween.KillAllTweens(elemObjects[body]);
                    SnapHidden(elemGroups[body]);
                    if (elemRects[body] != null) { elemRects[body].localScale = Vector3.one; elemRects[body].anchoredPosition = elemBasePos[body]; }
                    elemObjects[body].SetActive(false);
                }
                if (elemObjects[scrim] != null && elemObjects[scrim].activeSelf)
                {
                    VRCTween.KillAllTweens(elemObjects[scrim]);
                    SnapHidden(elemGroups[scrim]);
                    elemObjects[scrim].SetActive(false);
                }
            }
            if (rearm > 0f) SendCustomEventDelayedSeconds(nameof(_ModalSnap), rearm);
        }

        /// <summary>Generated: writes the enabled binding and calls onopen / onclose.</summary>
        protected virtual void _OnModalChanged(int modal, bool open)
        {
        }

        /// <summary>A click on a modal's backdrop.</summary>
        private void ClickModalScrim(int scrimElem)
        {
            if (_modalOpen == null) return;
            for (int m = 0; m < _modalOpen.Length; m++)
            {
                if (modalScrimElem[m] != scrimElem) continue;
                if (modalDismissable[m]) SetModal(m, false, true);
                return;
            }
        }

        /// <summary>The escape gesture: closes an open select, else the top dismissable modal. True when it consumed the press.</summary>
        public bool Back()
        {
            EnsureStarted();
            // An armed keyboard panel is invisible and eats the next click aimed through it.
            if (inputPanel != null && inputPanel.IsOpen()) { inputPanel.Cancel(); return true; }
            if (_openSelect >= 0) { CloseSelect(); return true; }
            for (int i = _modalDepth - 1; i >= 0; i--)
            {
                int m = _modalStack[i];
                if (!modalDismissable[m]) continue;
                SetModal(m, false, true);
                return true;
            }
            return false;
        }

        private void ResetPointerStatesUnder(int root)
        {
            if (elemSubtreeEnd == null || root < 0 || root >= elemSubtreeEnd.Length) return;
            int end = elemSubtreeEnd[root];
            for (int e = root; e < end; e++)
            {
                int id = elemSignal[e];
                if (id < 0 || (!_hover[id] && !_press[id])) continue;
                _hover[id] = false; _press[id] = false;
                ApplyState(id);
            }
        }
    }
}
