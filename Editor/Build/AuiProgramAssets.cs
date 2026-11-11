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

        // RETRIEVEPROGRAM DESERIALIZES THE WHOLE UDON PROGRAM, about 50 ms for a world-sized view
        // (measured 2026-11-11), and a build asked for it six times. The answer only changes when
        // UdonSharp compiles, so it is kept per program asset until ForgetPrograms (called after every
        // CompileSync AUI runs and at the start of every drain, since U# may have compiled in between).
        private static readonly System.Collections.Generic.Dictionary<UdonSharpProgramAsset, VRC.Udon.Common.Interfaces.IUdonProgram> Programs =
            new System.Collections.Generic.Dictionary<UdonSharpProgramAsset, VRC.Udon.Common.Interfaces.IUdonProgram>();
        private static readonly System.Collections.Generic.Dictionary<UdonSharpProgramAsset, System.Collections.Generic.HashSet<string>> Exports =
            new System.Collections.Generic.Dictionary<UdonSharpProgramAsset, System.Collections.Generic.HashSet<string>>();

        public static void ForgetPrograms()
        {
            Programs.Clear();
            Exports.Clear();
        }

        /// <summary>The compiled program, or null when UdonSharp has not produced one (cached; see ForgetPrograms).</summary>
        public static VRC.Udon.Common.Interfaces.IUdonProgram Program(UdonSharpProgramAsset asset)
        {
            if (asset == null) return null;
            VRC.Udon.Common.Interfaces.IUdonProgram p;
            if (Programs.TryGetValue(asset, out p) && p != null) return p;
            p = asset.SerializedProgramAsset != null ? asset.SerializedProgramAsset.RetrieveProgram() : null;
            if (p != null) Programs[asset] = p;   // a null is not cached: the next CompileSync may fill it
            return p;
        }

        public static bool HasProgram(UdonSharpProgramAsset asset)
        {
            return Program(asset) != null;
        }

        /// <summary>The program's exported symbols, or null when there is no program (cached with it).</summary>
        public static System.Collections.Generic.HashSet<string> ExportedSymbols(UdonSharpProgramAsset asset)
        {
            System.Collections.Generic.HashSet<string> s;
            if (asset != null && Exports.TryGetValue(asset, out s)) return s;
            var p = Program(asset);
            if (p == null) return null;
            s = new System.Collections.Generic.HashSet<string>(p.SymbolTable.GetExportedSymbols());
            Exports[asset] = s;
            return s;
        }

        /// <summary>Every runtime behaviour in the package gets its asset; the package ships them, this is the safety net.</summary>
        public static void EnsurePackageRuntime()
        {
            string dir = AuiEditorEnv.PackageAssetRoot + "/Runtime/";
            foreach (string n in new[] { "AuiView", "AuiSignal", "AuiLanguageService", "AuiInputPanel", "AuiAttach", "AuiColorPicker" }) Ensure(dir + n + ".cs");
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
