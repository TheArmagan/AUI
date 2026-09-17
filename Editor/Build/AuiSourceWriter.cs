using System.IO;
using System.Text;
using Armagan.AUI.Core;
using UnityEditor;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// Writes Foo.g.cs next to Foo.aui, and scaffolds Foo.cs once so the first compile
    /// passes with every handler present as an empty stub. Returns true when something was
    /// written (the caller must then let Unity recompile before building the prefab).
    ///
    /// Foo.g.cs is only rewritten when its text differs: rewriting an identical file would
    /// trigger a recompile and a reload for nothing, and with the postprocessor re-queuing
    /// the view that would loop.
    /// </summary>
    public static class AuiSourceWriter
    {
        public static bool Write(AuiCompileResult r, AuiSettings settings)
        {
            string dir = Path.GetDirectoryName(r.FilePath) ?? "";
            string name = r.ViewName;
            string gen = Path.Combine(dir, name + ".g.cs");
            string user = Path.Combine(dir, name + ".cs");
            bool wrote = false;

            string genText = r.GeneratedSource.Replace("\r\n", "\n");
            string existing = File.Exists(gen) ? File.ReadAllText(gen).Replace("\r\n", "\n") : null;
            if (existing != genText)
            {
                File.WriteAllText(gen, genText, new UTF8Encoding(false));
                wrote = true;
                AuiLog.Info("wrote " + AuiEditorEnv.AssetPath(gen));
            }

            if (!File.Exists(user))
            {
                File.WriteAllText(user, Scaffold(r, settings), new UTF8Encoding(false));
                wrote = true;
                AuiLog.Info("scaffolded " + AuiEditorEnv.AssetPath(user) + " with " + r.Info.Handlers.Count + " handler stub(s); write your logic there.");
            }

            if (wrote)
            {
                string ap = AuiEditorEnv.AssetPath(gen);
                if (ap != null) AssetDatabase.ImportAsset(ap);
                ap = AuiEditorEnv.AssetPath(user);
                if (ap != null) AssetDatabase.ImportAsset(ap);
            }
            return wrote;
        }

        /// <summary>The author's half, written once: one empty method per handler the markup names.</summary>
        public static string Scaffold(AuiCompileResult r, AuiSettings settings)
        {
            var sb = new StringBuilder();
            // Same namespace as the generated half: the two files are one partial class.
            string ns = AuiCodegenContext.Namespace;
            sb.Append("using UnityEngine;\n");
            sb.Append("using VRC.SDKBase;\n");   // VRCUrl, for a url input's onsubmit
            sb.Append("using Armagan.AUI;\n\n");
            string indent = "";
            if (!string.IsNullOrEmpty(ns)) { sb.Append("namespace ").Append(ns).Append("\n{\n"); indent = "    "; }
            sb.Append(indent).Append("/// <summary>Logic for ").Append(r.ViewName).Append(".aui. Bindings are properties on this class; handlers below are called by the generated half.</summary>\n");
            sb.Append(indent).Append("public partial class ").Append(r.ViewName).Append(" : AuiView\n");
            sb.Append(indent).Append("{\n");
            var seen = new System.Collections.Generic.HashSet<string>();
            // A HANDLER MAY NAME A GENERATED METHOD: onclick="CloseConfirm" on a modal's cancel button is
            // the documented way to close it. The generated half already defines those, so a stub here
            // is a duplicate member (CS0111) in a file the author has not even opened yet.
            foreach (AuiModalInfo m in r.Info.Modals)
            {
                seen.Add("Open" + m.PascalId);
                seen.Add("Close" + m.PascalId);
                seen.Add("Is" + m.PascalId + "Open");
            }
            foreach (AuiHandler h in r.Info.Handlers)
            {
                if (!seen.Add(h.Name)) continue;
                sb.Append(indent).Append("    public ").Append(h.Signature).Append("\n");
                sb.Append(indent).Append("    {\n");
                sb.Append(indent).Append("    }\n\n");
            }
            sb.Append(indent).Append("}\n");
            if (!string.IsNullOrEmpty(ns)) sb.Append("}\n");
            return sb.ToString();
        }
    }
}
