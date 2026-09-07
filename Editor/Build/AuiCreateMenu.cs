using System.IO;
using Armagan.AUI.Core;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// Project window "Create > AUI" items. A view's name attribute must equal its file stem,
    /// so the file text is written only after the player has finished typing the name; a plain
    /// ProjectWindowUtil.CreateAssetWithContent would bake a placeholder name in.
    /// </summary>
    public static class AuiCreateMenu
    {
        [MenuItem("Assets/Create/AUI/View", false, 80)]
        public static void CreateView()
        {
            Start("NewView.aui", true, false);
        }

        [MenuItem("Assets/Create/AUI/View with Stylesheet", false, 81)]
        public static void CreateViewWithStylesheet()
        {
            Start("NewView.aui", true, true);
        }

        [MenuItem("Assets/Create/AUI/Stylesheet", false, 82)]
        public static void CreateStylesheet()
        {
            Start("NewStyles.auis", false, true);
        }

        private static void Start(string defaultName, bool view, bool sheet)
        {
            var action = ScriptableObject.CreateInstance<CreateAction>();
            action.view = view;
            action.sheet = sheet;
            var icon = EditorGUIUtility.IconContent("TextAsset Icon").image as Texture2D;
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, action, defaultName, icon, null);
        }

        public static string ViewText(string name)
        {
            return "<view name=\"" + name + "\" width=\"300\" height=\"140\">\n" +
                   "  <text class=\"title\">" + name + "</text>\n" +
                   "</view>\n";
        }

        public static string StylesheetText(string stem, bool sibling)
        {
            string head = sibling
                ? "/* Styles for " + stem + ".aui. A sheet named like the view is linked automatically. */"
                : "/* " + stem + ".auis. Use it from a view with <link href=\"" + stem + ".auis\"/>. */";
            return head + "\n\n" +
                   ":root {\n" +
                   "  /* --accent: #F0B072; */\n" +
                   "}\n\n" +
                   ".primary { background: sprite(Rounded8) var(--accent); color: var(--base); icon-color: var(--base); }\n" +
                   ".primary:pressed { scale: 0.96; }\n";
        }

        private sealed class CreateAction : EndNameEditAction
        {
            public bool view;
            public bool sheet;

            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                string dir = (Path.GetDirectoryName(pathName) ?? "").Replace('\\', '/');
                string typed = Path.GetFileNameWithoutExtension(pathName);
                string stem = view ? AuiNaming.Identifier(typed) : typed;
                if (view && !AuiSchema.IsIdentifier(stem))
                {
                    AuiLog.Error("'" + typed + "' cannot name a view: it must be a C# identifier (letters, digits, underscore).");
                    return;
                }
                if (view && stem != typed) AuiLog.Warn("view named '" + stem + "' ('" + typed + "' is not a C# identifier).");

                string first = null;
                if (view)
                {
                    string p = dir + "/" + stem + ".aui";
                    if (Write(p, ViewText(stem))) first = p;
                }
                if (sheet)
                {
                    string p = dir + "/" + stem + ".auis";
                    if (Write(p, StylesheetText(stem, view)) && first == null) first = p;
                }
                if (first == null) return;
                AssetDatabase.Refresh();
                Object o = AssetDatabase.LoadAssetAtPath<Object>(first);
                if (o != null)
                {
                    Selection.activeObject = o;
                    EditorGUIUtility.PingObject(o);
                }
            }

            private static bool Write(string assetPath, string text)
            {
                if (File.Exists(assetPath))
                {
                    AuiLog.Error(assetPath + " already exists; nothing written.");
                    return false;
                }
                File.WriteAllText(assetPath, text);
                AuiLog.Info("created " + assetPath);
                return true;
            }
        }
    }
}
