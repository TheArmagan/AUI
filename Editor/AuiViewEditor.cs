using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// Inspector for every compiled view. The builder fills dozens of tables on the base class
    /// and the generated half; none of them is meant to be read or edited by hand, so only the
    /// fields the AUTHOR declared in their own partial file are shown, under a one-line summary
    /// and a Rebuild button. The UdonSharp header (program asset, sync mode, utilities) stays.
    /// </summary>
    public static class AuiViewEditor
    {
        /// <summary>Called by the per-view inspector each generated file declares (through AuiInspectorBridge).</summary>
        public static void Draw(UnityEditor.Editor editor)
        {
            Object[] targets = editor.targets;
            Object target = editor.target;
            SerializedObject serializedObject = editor.serializedObject;
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(targets)) return;

            var view = target as AuiView;
            if (view != null && targets.Length == 1)
            {
                int elements = view.elemObjects != null ? view.elemObjects.Length : 0;
                int signals = view.signals != null ? view.signals.Length : 0;
                string stamp = string.IsNullOrEmpty(view.auiBakeHash) ? "not built" : "build " + view.auiBakeHash.Substring(view.auiBakeHash.IndexOf(':') + 1);
                EditorGUILayout.HelpBox(view.GetType().Name + ".aui: " + elements + " elements, " + signals + " signals, " + stamp + ". Baked wiring is hidden; edit the .aui and .auis files.", MessageType.None);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Rebuild view")) Rebuild(view);
                    if (GUILayout.Button("Open .aui")) OpenSource(view, ".aui");
                    if (GUILayout.Button("Open .cs")) OpenSource(view, ".cs");
                }
                EditorGUILayout.Space(4);
            }

            // Author-declared fields only: anything declared on AuiView itself is builder output,
            // and generated fields carry [HideInInspector], which SerializedProperty honours.
            serializedObject.Update();
            var own = new HashSet<string>();
            System.Type t = target.GetType();
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.IsDefined(typeof(HideInInspector), true)) continue;
                if (!f.IsPublic && !f.IsDefined(typeof(SerializeField), true)) continue;
                own.Add(f.Name);
            }
            bool any = false;
            SerializedProperty p = serializedObject.GetIterator();
            bool enter = true;
            while (p.NextVisible(enter))
            {
                enter = false;
                if (!own.Contains(p.name)) continue;
                EditorGUILayout.PropertyField(p, true);
                any = true;
            }
            if (!any) EditorGUILayout.LabelField("No fields declared in the author file.", EditorStyles.miniLabel);
            serializedObject.ApplyModifiedProperties();
        }

        private static string SourcePath(AuiView view, string extension)
        {
            var script = MonoScript.FromMonoBehaviour(view);
            if (script == null) return null;
            string cs = AssetDatabase.GetAssetPath(script);
            return Path.ChangeExtension(cs, extension);
        }

        private static void Rebuild(AuiView view)
        {
            string aui = SourcePath(view, ".aui");
            if (aui == null || !File.Exists(aui)) { AuiLog.Error("no .aui next to " + view.GetType().Name + ".cs"); return; }
            AuiBuildQueue.RebuildOne(aui);
        }

        private static void OpenSource(AuiView view, string extension)
        {
            string p = SourcePath(view, extension);
            if (p != null && File.Exists(p)) EditorUtility.OpenWithDefaultApp(p);
        }
    }
}
