using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// The sibling .asset every UdonSharpBehaviour needs, and the sync-mode re-assertion:
    /// a port of the world's ArmaganProgramAssets so the package stands alone.
    ///
    /// WITHOUT THE ASSET THE COMPONENT IS SILENTLY INERT: it adds, it serialises, the inspector
    /// looks normal, and its program is null. For a generated view the asset must point at
    /// Foo.cs (the author's file, whose name matches the class), never Foo.g.cs.
    /// </summary>
    public static class AuiProgramAssets
    {
        /// <summary>Ensure the program asset for one script. Returns true when it was just created (a U# compile is then needed).</summary>
        public static bool Ensure(string csAssetPath)
        {
            string assetPath = csAssetPath.Substring(0, csAssetPath.Length - 3) + ".asset";
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null) return false;
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(csAssetPath);
            if (script == null || script.GetClass() == null) return false;
            if (!typeof(UdonSharpBehaviour).IsAssignableFrom(script.GetClass())) return false;
            var program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            program.sourceCsScript = script;
            AssetDatabase.CreateAsset(program, assetPath);
            AssetDatabase.SaveAssets();
            AuiLog.Info("created " + assetPath);
            return true;
        }

        public static UdonSharpProgramAsset Find(string csAssetPath)
        {
            string assetPath = csAssetPath.Substring(0, csAssetPath.Length - 3) + ".asset";
            return AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath);
        }

        /// <summary>Every runtime behaviour in the package gets its asset; the package ships them, this is the safety net.</summary>
        public static void EnsurePackageRuntime()
        {
            string dir = AuiEditorEnv.PackageAssetRoot + "/Runtime/";
            foreach (string n in new[] { "AuiView", "AuiSignal", "AuiLanguageService", "AuiInputPanel", "AuiAttach" }) Ensure(dir + n + ".cs");
        }

        /// <summary>
        /// Push the class's [UdonBehaviourSyncMode] onto the backing UdonBehaviour. UdonSharp
        /// applies it only when the component is first added; the heap is the only thing worth believing.
        /// </summary>
        public static void ApplySyncMode(UdonSharpBehaviour behaviour)
        {
            if (behaviour == null) return;
            VRC.Udon.UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
            if (ub == null) return;
            object[] attrs = behaviour.GetType().GetCustomAttributes(typeof(UdonBehaviourSyncModeAttribute), true);
            if (attrs.Length == 0) return;
            BehaviourSyncMode mode = ((UdonBehaviourSyncModeAttribute)attrs[0]).behaviourSyncMode;
            VRC.SDKBase.Networking.SyncType want;
            if (mode == BehaviourSyncMode.Continuous) want = VRC.SDKBase.Networking.SyncType.Continuous;
            else if (mode == BehaviourSyncMode.Manual) want = VRC.SDKBase.Networking.SyncType.Manual;
            else want = VRC.SDKBase.Networking.SyncType.None;
            if (ub.SyncMethod == want) return;
            ub.SyncMethod = want;
            EditorUtility.SetDirty(ub);
        }
    }
}
