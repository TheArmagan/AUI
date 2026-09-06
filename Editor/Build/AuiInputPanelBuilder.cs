using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// Authors the one AuiInputPanel a scene needs: an invisible world-space canvas with a
    /// TMP_InputField spanning 2.0 x 1.4 m, parked in front of the player when armed.
    ///
    /// A TMP_InputField is four objects with three cross-references (field, viewport with a
    /// RectMask2D, text, placeholder); one missing and it renders nothing while every property
    /// looks healthy. Hence a builder, re-runnable, that also asserts the wiring at the end.
    /// Everything stays ACTIVE: VRChat's keyboard refuses to open for a field that was ever on
    /// an inactive object. The panel hides by disabling its Canvas and collider.
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
            rt.sizeDelta = new Vector2(PanelW, PanelH);
            rt.localScale = Vector3.one * PanelScale;

            var canvas = Get<Canvas>(root);
            canvas.renderMode = RenderMode.WorldSpace;
            Get<GraphicRaycaster>(root);
            if (root.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null) root.AddComponent<VRC.SDK3.Components.VRCUiShape>();
            var bc = Get<BoxCollider>(root);
            bc.isTrigger = true;
            bc.center = Vector3.zero;
            bc.size = new Vector3(PanelW, PanelH, 10f);

            // ---- the field: an invisible click target
            RectTransform frt = Child(rt, "Field");
            Stretch(frt);
            var img = Get<Image>(frt.gameObject);
            img.sprite = null;
            img.color = new Color(0, 0, 0, 0);
            img.raycastTarget = true;

            RectTransform area = Child(frt, "Text Area");
            Stretch(area, 10f);
            Get<RectMask2D>(area.gameObject);
            TMP_FontAsset font = AuiTextMeasurer.FontFor(AuiEditorEnv.FindSettings() != null ? AuiEditorEnv.FindSettings().fontSet : null, 400);
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

            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(panel);
            ClearPersistent(field.onEndEdit);
            UnityEventTools.AddStringPersistentListener(field.onEndEdit, ub.SendCustomEvent, "OnEndEdit");
            ClearPersistent(field.onValueChanged);
            UnityEventTools.AddStringPersistentListener(field.onValueChanged, ub.SendCustomEvent, "OnValueChanged");

            panel.canvas = canvas;
            panel.clickCollider = bc;
            panel.field = field;
            canvas.enabled = false;
            bc.enabled = false;

            AuiProgramAssets.ApplySyncMode(panel);
            EditorUtility.SetDirty(panel);
            UdonSharpEditorUtility.CopyProxyToUdon(panel);
            EditorSceneManager.MarkSceneDirty(root.scene);

            if (field.textViewport == null || field.textComponent == null || field.fontAsset == null)
                AuiLog.Error("input panel: the TMP_InputField is missing a viewport, text component or font; it would render nothing.");
            else AuiLog.Info("input panel rebuilt (" + PanelW * PanelScale + " x " + PanelH * PanelScale + " m click target).");
            return panel;
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
