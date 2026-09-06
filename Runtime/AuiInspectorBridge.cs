#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System.Reflection;

namespace Armagan.AUI
{
    /// <summary>
    /// Lets a generated view's inspector (which lives next to the view, possibly inside this
    /// runtime assembly) reach the drawer in Armagan.AUI.Editor without a reference a runtime
    /// assembly cannot have. Reflection once, cached.
    /// </summary>
    public static class AuiInspectorBridge
    {
        private static MethodInfo _draw;
        private static bool _looked;

        public static void Draw(UnityEditor.Editor editor)
        {
            if (!_looked)
            {
                _looked = true;
                System.Type t = System.Type.GetType("Armagan.AUI.Editor.AuiViewEditor, Armagan.AUI.Editor");
                _draw = t != null ? t.GetMethod("Draw", BindingFlags.Public | BindingFlags.Static) : null;
            }
            if (_draw != null) _draw.Invoke(null, new object[] { editor });
            else editor.DrawDefaultInspector();
        }
    }
}
#endif
