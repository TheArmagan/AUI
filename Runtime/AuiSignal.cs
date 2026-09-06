using UdonSharp;
using UnityEngine;

namespace Armagan.AUI
{
    /// <summary>
    /// One per interactive element. uGUI can only reach Udon through
    /// UdonBehaviour.SendCustomEvent(string), wired by the builder as persistent listeners
    /// (Button.onClick, EventTrigger enter/exit/down/up, Slider.onValueChanged), so this
    /// tiny behaviour receives those calls and forwards them as one typed call to the view
    /// with its id. The author never sees it.
    ///
    /// `view` and `id` are written by the BUILDER at edit time and by nothing else, but they
    /// still have to be public serialized fields: [NonSerialized] would drop their export
    /// and the whole element would go dead without a message.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class AuiSignal : UdonSharpBehaviour
    {
        [HideInInspector] public AuiView view;
        [HideInInspector] public int id;

        public void _Click()
        {
            if (view != null) view._OnSignal(id, AuiView.SIG_CLICK);
        }

        public void _Enter()
        {
            if (view != null) view._OnSignal(id, AuiView.SIG_ENTER);
        }

        public void _Exit()
        {
            if (view != null) view._OnSignal(id, AuiView.SIG_EXIT);
        }

        public void _Down()
        {
            if (view != null) view._OnSignal(id, AuiView.SIG_DOWN);
        }

        public void _Up()
        {
            if (view != null) view._OnSignal(id, AuiView.SIG_UP);
        }

        public void _ValueChanged()
        {
            if (view != null) view._OnSignal(id, AuiView.SIG_VALUE);
        }
    }
}
