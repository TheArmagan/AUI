using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// Authors the one AuiInputPanel a scene needs: a root that is parked in front of the
    /// player, holding two invisible world-space click targets of 2.0 x 1.4 m, one with a
    /// TMP_InputField (text) and one with a VRCUrlInputField (url).
    ///
    /// A TMP_InputField is four objects with three cross-references (field, viewport with a
    /// RectMask2D, text, placeholder); one missing and it renders nothing while every property
    /// looks healthy. A VRCUrlInputField is a legacy uGUI InputField and needs a UI.Text child.
    /// Hence a builder, re-runnable, that also asserts the wiring at the end.
    /// Everything stays ACTIVE: VRChat's keyboard refuses to open for a field that was ever on
    /// an inactive object. The panel hides by disabling each canvas and collider.
    ///
    /// Each field sits on its OWN canvas with its own collider: two raycast targets on one
    /// canvas would fight for the click, and a nested canvas under a disabled parent does not
    /// draw. A scene built before the url panel existed has the text canvas on the root; the
    /// builder moves it under a "Text Panel" child and leaves the root as a plain holder.
    /// </summary>
    public static class AuiInputPanelBuilder
    {
        public const string ObjectName = "AUI Input Panel";
        private const float PanelW = 2000f, PanelH = 1400f;
        private const float PanelScale = 0.001f;

        public static AuiInputPanel FindOrCreate()
        {
            AuiInputPanel p = Object.FindObjectOfType<AuiInputPanel>(true);
            if (p != null) return p;
            return Rebuild();
        }

        public static AuiInputPanel Rebuild()
        {
            AuiProgramAssets.EnsurePackageRuntime();
            AuiInputPanel panel = Object.FindObjectOfType<AuiInputPanel>(true);
            GameObject root;
            if (panel == null)
            {
                root = new GameObject(ObjectName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(root, "Create " + ObjectName);
                panel = (AuiInputPanel)UdonSharpUndo.AddComponent(root, typeof(AuiInputPanel));
            }
            else root = panel.gameObject;

            var rt = root.GetComponent<RectTransform>() ?? root.AddComponent<RectTransform>();
            MigrateLegacyRoot(rt);
            rt.sizeDelta = new Vector2(PanelW, PanelH);
            rt.localScale = Vector3.one;

            TMP_FontAsset font = AuiTextMeasurer.FontFor(AuiEditorEnv.FindSettings() != null ? AuiEditorEnv.FindSettings().fontSet : null, 400);
            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(panel);

            // ---- text panel
            RectTransform textPanel = Panel(rt, "Text Panel");
            TMP_InputField field = BuildTextField(textPanel, font);
            ClearPersistent(field.onEndEdit);
            UnityEventTools.AddStringPersistentListener(field.onEndEdit, ub.SendCustomEvent, "OnEndEdit");
            ClearPersistent(field.onValueChanged);
            UnityEventTools.AddStringPersistentListener(field.onValueChanged, ub.SendCustomEvent, "OnValueChanged");
            panel.canvas = textPanel.GetComponent<Canvas>();
            panel.clickCollider = textPanel.GetComponent<BoxCollider>();
            panel.field = field;

            // ---- url panel
            RectTransform urlPanel = Panel(rt, "Url Panel");
            VRCUrlInputField urlField = BuildUrlField(urlPanel);
            ClearPersistent(urlField.onEndEdit);
            UnityEventTools.AddStringPersistentListener(urlField.onEndEdit, ub.SendCustomEvent, "OnUrlEndEdit");
            panel.urlCanvas = urlPanel.GetComponent<Canvas>();
            panel.urlCollider = urlPanel.GetComponent<BoxCollider>();
            panel.urlField = urlField;

            panel.canvas.enabled = false;
            panel.clickCollider.enabled = false;
            panel.urlCanvas.enabled = false;
            panel.urlCollider.enabled = false;

            AuiProgramAssets.ApplySyncMode(panel);
            EditorUtility.SetDirty(panel);
            UdonSharpEditorUtility.CopyProxyToUdon(panel);
            EditorSceneManager.MarkSceneDirty(root.scene);

            bool bad = false;
            if (field.textViewport == null || field.textComponent == null || field.fontAsset == null)
            { bad = true; AuiLog.Error("input panel: the TMP_InputField is missing a viewport, text component or font; it would render nothing."); }
            if (urlField.textComponent == null || urlField.targetGraphic == null)
            { bad = true; AuiLog.Error("input panel: the VRCUrlInputField is missing its text component or target graphic; nothing could click it."); }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (!t.gameObject.activeSelf) { bad = true; AuiLog.Error("input panel: '" + t.name + "' is inactive; VRChat's keyboard will not open for a field that was ever on an inactive object."); }
            if (!bad) AuiLog.Info("input panel rebuilt: text + url click targets, " + PanelW * PanelScale + " x " + PanelH * PanelScale + " m each.");
            return panel;
        }

        /// <summary>A scene from before the url panel: the root itself was the text canvas. Move that stack under "Text Panel" so the root is a holder.</summary>
        private static void MigrateLegacyRoot(RectTransform root)
        {
            var canvas = root.GetComponent<Canvas>();
            if (canvas == null) return;
            var oldField = root.Find("Field");
            RectTransform textPanel = Panel(root, "Text Panel");
            if (oldField != null) oldField.SetParent(textPanel, false);
            foreach (var type in new[] { typeof(BoxCollider), typeof(VRCUiShape), typeof(GraphicRaycaster), typeof(Canvas) })
            {
                var c = root.GetComponent(type);
                if (c != null) Undo.DestroyObjectImmediate(c);
            }
            AuiLog.Info("input panel: moved the text field under 'Text Panel' (the root used to be the canvas).");
        }

        /// <summary>A world-space canvas child sized to the click target, with its own raycaster, VRCUiShape and trigger collider.</summary>
        private static RectTransform Panel(RectTransform parent, string name)
        {
            RectTransform t = Child(parent, name);
            t.anchorMin = t.anchorMax = new Vector2(0.5f, 0.5f);
            t.pivot = new Vector2(0.5f, 0.5f);
            t.anchoredPosition = Vector2.zero;
            t.sizeDelta = new Vector2(PanelW, PanelH);
            t.localScale = Vector3.one * PanelScale;
            t.localRotation = Quaternion.identity;
            var canvas = Get<Canvas>(t.gameObject);
            canvas.renderMode = RenderMode.WorldSpace;
            Get<GraphicRaycaster>(t.gameObject);
            if (t.GetComponent<VRCUiShape>() == null) t.gameObject.AddComponent<VRCUiShape>();
            var bc = Get<BoxCollider>(t.gameObject);
            bc.isTrigger = true;
            bc.center = Vector3.zero;
            bc.size = new Vector3(PanelW, PanelH, 10f);
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            return t;
        }

        private static TMP_InputField BuildTextField(RectTransform panel, TMP_FontAsset font)
        {
            RectTransform frt = Child(panel, "Field");
            Stretch(frt);
            var img = Get<Image>(frt.gameObject);
            img.sprite = null;
            img.color = new Color(0, 0, 0, 0);
            img.raycastTarget = true;

            RectTransform area = Child(frt, "Text Area");
            Stretch(area, 10f);
            Get<RectMask2D>(area.gameObject);
            TextMeshProUGUI placeholder = Label(area, "Placeholder", font);
            placeholder.text = "";
            TextMeshProUGUI text = Label(area, "Text", font);
            text.text = "";
            text.color = new Color(0, 0, 0, 0);

            var field = Get<TMP_InputField>(frt.gameObject);
            var nav = field.navigation; nav.mode = Navigation.Mode.None; field.navigation = nav;
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.fontAsset = font;
            field.pointSize = 34;
            field.targetGraphic = img;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.readOnly = false;
            field.richText = false;
            field.caretColor = new Color(0, 0, 0, 0);
            field.customCaretColor = true;
            field.selectionColor = new Color(0, 0, 0, 0);
            field.transition = Selectable.Transition.None;
            return field;
        }

        /// <summary>
        /// VRChat's own URL field: a legacy uGUI InputField subclass, so it wants a UI.Text (not
        /// TMP) for its text and an Image to click. Invisible like the text one: the keyboard
        /// VRChat opens on top is the whole visible UI.
        /// </summary>
        private static VRCUrlInputField BuildUrlField(RectTransform panel)
        {
            RectTransform frt = Child(panel, "Url Field");
            Stretch(frt);
            var img = Get<Image>(frt.gameObject);
            img.sprite = null;
            img.color = new Color(0, 0, 0, 0);
            img.raycastTarget = true;

            RectTransform trt = Child(frt, "Text");
            Stretch(trt, 10f);
            var text = Get<Text>(trt.gameObject);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 34;
            text.color = new Color(0, 0, 0, 0);
            text.raycastTarget = false;
            text.supportRichText = false;

            var field = Get<VRCUrlInputField>(frt.gameObject);
            var nav = field.navigation; nav.mode = Navigation.Mode.None; field.navigation = nav;
            field.textComponent = text;
            field.targetGraphic = img;
            field.lineType = VRCUrlInputField.LineType.SingleLine;   // its own enum, not InputField's
            field.readOnly = false;
            field.caretColor = new Color(0, 0, 0, 0);
            field.customCaretColor = true;
            field.selectionColor = new Color(0, 0, 0, 0);
            field.transition = Selectable.Transition.None;
            return field;
        }

        private static RectTransform Child(RectTransform parent, string name)
        {
            var t = parent.Find(name) as RectTransform;
            if (t != null) return t;
            var go = new GameObject(name, typeof(RectTransform));
            t = (RectTransform)go.transform;
            t.SetParent(parent, false);
            return t;
        }

        private static TextMeshProUGUI Label(RectTransform parent, string name, TMP_FontAsset font)
        {
            RectTransform rt = Child(parent, name);
            Stretch(rt);
            var t = Get<TextMeshProUGUI>(rt.gameObject);
            t.font = font;
            t.fontSize = 34;
            t.color = Color.white;
            t.raycastTarget = false;
            t.richText = false;
            return t;
        }

        private static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }

        private static T Get<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static void ClearPersistent(UnityEngine.Events.UnityEventBase ev)
        {
            for (int i = ev.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(ev, i);
        }
    }
}
