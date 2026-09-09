using System.Collections.Generic;
using System.IO;
using Armagan.AUI.Core;
using UnityEditor;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// Enqueues every imported .aui for the build queue. Nothing else happens here: an
    /// asset postprocessor runs inside the import pipeline, where writing source files and
    /// compiling UdonSharp are not allowed.
    /// </summary>
    internal sealed class AuiPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // PRECISE INVALIDATION. Dropping the whole workspace on every import made each save
            // pay a rescan of Assets/ plus a reload of every language file. Only what a file can
            // change is dropped: the scan for a new/removed/moved view or icon (or the settings),
            // the language table for a language file's content, the theme for the theme file.
            // A re-saved view, a stylesheet or a font touches none of the three.
            bool lang = false, theme = false, fonts = false, sprites = false;
            string rescan = null;   // the path that made the scan stale, for the log
            foreach (string p in deleted)
            {
                if (p.EndsWith(".aui") || p.EndsWith(".svg") || p.EndsWith(".json")) rescan = "deleted " + p;
                if (p.EndsWith(".aui")) AuiEditorEnv.ForgetCompile(Path.GetFullPath(p));
            }
            foreach (string p in movedFrom) if (p.EndsWith(".aui")) AuiEditorEnv.ForgetCompile(Path.GetFullPath(p));
            foreach (string p in moved) if (p.EndsWith(".aui") || p.EndsWith(".svg") || p.EndsWith(".json")) rescan = "moved " + p;
            foreach (string p in imported)
            {
                if (p.EndsWith("AuiSettings.asset")) rescan = "settings";
                else if (p.EndsWith(".aui") || p.EndsWith(".svg")) { if (!AuiEditorEnv.WorkspaceKnows(Path.GetFullPath(p))) rescan = "new " + p; }
                else if (p.EndsWith(".json"))
                {
                    if (AuiEditorEnv.WorkspaceKnows(Path.GetFullPath(p))) lang = true;
                    else if (Path.GetFileName(Path.GetDirectoryName(p) ?? "").ToLowerInvariant() == "lang") rescan = "new " + p;
                }
                else if (p.EndsWith(".auis") && AuiEditorEnv.IsThemePath(Path.GetFullPath(p))) theme = true;
                else if (p.EndsWith(".asset") && p.StartsWith(AuiFontBaker.OutDir)) fonts = true;
                if (p.EndsWith(".png") || p.EndsWith(".svg")) sprites = true;
            }
            foreach (string p in deleted) if (p.EndsWith(".png") || p.EndsWith(".svg")) sprites = true;
            if (rescan != null) AuiEditorEnv.InvalidateWorkspace(rescan);
            else
            {
                if (lang) AuiEditorEnv.InvalidateLanguages();
                if (theme) AuiEditorEnv.InvalidateTheme();
            }
            if (fonts) { AuiTextMeasurer.InvalidateCache(); AuiEditorEnv.ForgetAllCompiles(); }
            if (sprites) AuiPrefabBuilder.ClearSpriteCache();

            bool any = false;
            foreach (string p in imported)
            {
                if (!p.EndsWith(".aui")) continue;
                AuiBuildQueue.Enqueue(p);
                any = true;
            }
            // A language file that APPEARED, went away or moved changes the key list (the sorted
            // union across every language), so every baked key index may have shifted. No view
            // declared the new file as a dependency (it did not exist), so nothing re-imported:
            // queue them all. Non-forced: the hash decides what actually rebuilds.
            if (rescan != null && rescan.EndsWith(".json"))
            {
                AuiBuildQueue.EnqueueAll(false);
                any = true;
            }
            foreach (string p in deleted)
            {
                if (!p.EndsWith(".aui")) continue;
                string stem = p.Substring(0, p.Length - 4);
                AuiLog.Warn(p + " was deleted. Its generated files are left for you to remove: " + stem + ".g.cs, " + stem + ".cs, " + stem + ".asset, " + stem + ".prefab");
            }
            if (any) AuiBuildQueue.Kick(false);
        }
    }

    /// <summary>
    /// The post-import build of a view, in two steps that straddle a domain reload:
    ///
    ///   A. write Foo.g.cs (and scaffold Foo.cs once). If anything was written, stop: Unity
    ///      recompiles, the domain reloads, and the queue (kept in SessionState) resumes.
    ///   B. make sure Foo.asset exists and UdonSharp has compiled it, then build Foo.prefab
    ///      unless its bake hash already matches.
    ///
    /// Children build before parents (a parent instantiates the child's prefab). Everything
    /// runs from delayCall, never while Unity is compiling or importing, and a path that keeps
    /// failing is dropped after a few attempts rather than looping the editor forever.
    /// </summary>
    public static class AuiBuildQueue
    {
        private const string QueueKey = "AUI.BuildQueue";
        private const string AttemptsKey = "AUI.BuildAttempts.";
        private const string ReasonKey = "AUI.BuildReason.";
        private const int MaxAttempts = 4;
        private const string ForceKey = "AUI.BuildForce";
        private const string ExplicitKey = "AUI.BuildExplicit";
        private static bool _scheduled;

        /// <summary>True while a user-driven Rebuild All is draining: prefabs rebuild even when their bake hash matches.</summary>
        public static bool Forced
        {
            get { return SessionState.GetBool(ForceKey, false); }
        }

        // AUTO-BUILD CAN BE PAUSED. While prototyping with the live preview, every save of a
        // view that changes its generated code still writes Foo.g.cs and reloads the domain.
        // With auto-build off, the drains the postprocessor starts leave the queue untouched;
        // an explicit rebuild (menu, inspector, preview window, font bake) always runs, and
        // turning the toggle back on drains what accumulated. Per project: EditorPrefs is global.
        private static string AutoBuildKey
        {
            get { return "AUI.AutoBuild." + Application.dataPath.GetHashCode().ToString("x"); }
        }

        public static bool AutoBuild
        {
            get { return EditorPrefs.GetBool(AutoBuildKey, true); }
            set
            {
                EditorPrefs.SetBool(AutoBuildKey, value);
                if (value && Pending().Count > 0) Kick(true);
            }
        }

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            // A queue left by an explicit run (sources written, reload) resumes as explicit.
            if (Pending().Count > 0) Kick(SessionState.GetBool(ExplicitKey, false));
        }

        public static List<string> Pending()
        {
            string s = SessionState.GetString(QueueKey, "");
            var list = new List<string>();
            foreach (string p in s.Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries)) list.Add(p);
            return list;
        }

        private static void Save(List<string> list)
        {
            SessionState.SetString(QueueKey, string.Join("|", list));
        }

        public static void Enqueue(string assetPath)
        {
            List<string> list = Pending();
            if (!list.Contains(assetPath)) list.Add(assetPath);
            Save(list);
        }

        /// <param name="explicitRun">True for a user-driven drain, which runs even while auto-build is paused.</param>
        public static void Kick(bool explicitRun = true)
        {
            if (explicitRun) SessionState.SetBool(ExplicitKey, true);
            if (_scheduled) return;
            _scheduled = true;
            EditorApplication.delayCall += Drain;
        }

        /// <summary>Drain synchronously (menu items, tests, tools driving the editor): delayCall waits for an editor tick that an unfocused editor may not deliver.</summary>
        public static void DrainNow()
        {
            SessionState.SetBool(ExplicitKey, true);
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { Kick(true); return; }
            Drain();
        }

        private static void Drain()
        {
            _scheduled = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Kick(SessionState.GetBool(ExplicitKey, false));
                return;
            }
            List<string> list = Pending();
            if (list.Count == 0) { SessionState.SetBool(ExplicitKey, false); return; }
            bool explicitRun = SessionState.GetBool(ExplicitKey, false);
            if (!explicitRun && !AutoBuild)
            {
                // Paused: the queue keeps the paths; AutoBuild = true or an explicit rebuild drains them.
                return;
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            AuiSettings settings = AuiEditorEnv.FindSettings();
            AuiWorkspace ws = AuiEditorEnv.Scan(settings);
            long scanMs = clock.ElapsedMilliseconds;

            // Compile everything first so parents can be ordered after their children. Most of
            // these are cache hits: the importer just compiled the same text.
            var results = new Dictionary<string, AuiCompileResult>();
            int cached = 0;
            foreach (string p in list)
            {
                if (!File.Exists(p)) continue;
                results[p] = AuiEditorEnv.Compile(Path.GetFullPath(p), settings, ws, null);
                if (AuiEditorEnv.LastCompileCached) cached++;
                else AuiLog.Info(p + ": compiled again in the drain (" + AuiEditorEnv.LastCompileMiss + ")");
            }
            list.Sort((a, b) => Depth(a, results, ws).CompareTo(Depth(b, results, ws)));
            long compileMs = clock.ElapsedMilliseconds - scanMs;

            // ONE UDONSHARP COMPILE PER DRAIN. CompileSync compiles every U# script in the project
            // and costs seconds; a first build of six views used to pay it six times. Every view
            // whose class exists gets its program asset now, then U# runs once for all of them.
            long usharpMs = 0;
            bool needUsharp = false;
            foreach (string p in list)
            {
                AuiCompileResult r;
                if (!results.TryGetValue(p, out r) || r.HasErrors || r.ViewName == null) continue;
                string cs = AuiEditorEnv.AssetPath(Path.Combine(Path.GetDirectoryName(r.FilePath) ?? "", r.ViewName + ".cs"));
                if (cs == null || !File.Exists(cs)) continue;
                if (AuiProgramAssets.Ensure(cs)) needUsharp = true;
                UdonSharp.UdonSharpProgramAsset pa = AuiProgramAssets.Find(cs);
                if (pa != null && (pa.SerializedProgramAsset == null || pa.SerializedProgramAsset.RetrieveProgram() == null)) needUsharp = true;
            }
            if (needUsharp)
            {
                long t0 = clock.ElapsedMilliseconds;
                UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync(new UdonSharp.Compiler.UdonSharpCompileOptions());
                usharpMs = clock.ElapsedMilliseconds - t0;
            }

            bool wroteSource = false;
            int built = 0;
            long buildStart = clock.ElapsedMilliseconds;
            var remaining = new List<string>(list);
            // Children a parent found missing during THIS drain. ChildrenBuilt used to Enqueue them
            // straight into SessionState, which Save(remaining) then overwrote with the pre-drain
            // list: the child never entered the queue and the parent failed four times.
            var lateChildren = new List<string>();
            // One scene sweep for the whole drain: RevertInstanceSizeOverrides used to do one per built view.
            AuiView[] sceneViews = Object.FindObjectsOfType<AuiView>(true);
            foreach (string p in list)
            {
                AuiCompileResult r;
                if (!results.TryGetValue(p, out r)) { remaining.Remove(p); continue; }
                int attempts = SessionState.GetInt(AttemptsKey + p, 0) + 1;
                SessionState.SetInt(AttemptsKey + p, attempts);
                if (attempts > MaxAttempts)
                {
                    AuiLog.Error(AuiCodes.PrefabBuildFailed + " " + p + ": giving up after " + MaxAttempts + " build attempts (last step: " + SessionState.GetString(ReasonKey + p, "unknown") + "). Fix the errors above and save the file again.");
                    remaining.Remove(p);
                    SessionState.EraseInt(AttemptsKey + p);   // the next save starts a fresh count
                    SessionState.EraseString(ReasonKey + p);
                    continue;
                }
                if (r.HasErrors)
                {
                    AuiLog.Error(p + ": not built, the view has errors (see the import log).");
                    remaining.Remove(p);
                    SessionState.EraseInt(AttemptsKey + p);
                    continue;
                }
                // Step A
                if (AuiSourceWriter.Write(r, settings)) { SessionState.SetString(ReasonKey + p, "waiting for the generated source to compile"); wroteSource = true; continue; }
                // A parent needs its children's prefabs; leave it queued until they exist. Waiting
                // for a child is not a failed attempt of the parent's.
                string missingChild = MissingChild(r, ws, lateChildren);
                if (missingChild != null)
                {
                    SessionState.SetString(ReasonKey + p, "waiting for the child prefab " + missingChild);
                    SessionState.SetInt(AttemptsKey + p, attempts - 1);
                    continue;
                }
                // Step B
                if (AuiPrefabBuilder.Build(r, settings, ws, sceneViews))
                {
                    built++;
                    remaining.Remove(p);
                    SessionState.EraseInt(AttemptsKey + p);
                    SessionState.EraseString(ReasonKey + p);
                }
                else SessionState.SetString(ReasonKey + p, "prefab build failed (see the error above)");
            }
            foreach (string cp in lateChildren) if (!remaining.Contains(cp)) remaining.Add(cp);
            Save(remaining);
            if (remaining.Count == 0) { SessionState.SetBool(ForceKey, false); SessionState.SetBool(ExplicitKey, false); }
            // The views carry key indices into the CURRENT language files; the scene's service must
            // carry the same table or every key added since the last bake shows "??". Checked on
            // every drain, not only when a prefab was built: a translation edit that does not move
            // the widest width leaves every hash alone and builds nothing, yet the service's strings
            // are what the player reads. One string compare per cell when nothing changed.
            AuiLanguageTableBuilder.ApplyIfChanged(settings, ws);
            long buildMs = clock.ElapsedMilliseconds - buildStart;
            AuiLog.Info("drain: " + list.Count + " view(s), " + built + " built, " + (wroteSource ? "sources written, " : "") +
                        clock.ElapsedMilliseconds + " ms (scan " + scanMs + ", compile " + compileMs + " with " + cached + " cached, udonsharp " + usharpMs + ", build " + buildMs + ")");
            if (wroteSource)
            {
                // Sources changed: let Unity compile; the queue resumes after the reload.
                AssetDatabase.Refresh();
                return;
            }
            if (remaining.Count > 0 && remaining.Count < list.Count) Kick(explicitRun);
            else if (remaining.Count > 0)
            {
                // Nothing progressed this pass (children missing or program assets pending): try once more after a refresh.
                AssetDatabase.Refresh();
                Kick(explicitRun);
            }
        }

        private static int Depth(string path, Dictionary<string, AuiCompileResult> results, AuiWorkspace ws)
        {
            AuiCompileResult r;
            if (!results.TryGetValue(path, out r) || r.Info == null) return 0;
            int d = 0;
            foreach (AuiNestedView nv in r.Info.NestedViews)
            {
                string cp;
                if (ws.Views.TryGetValue(nv.ViewName, out cp))
                {
                    string ap = AuiEditorEnv.AssetPath(cp);
                    d = System.Math.Max(d, 1 + (ap != null && results.ContainsKey(ap) ? Depth(ap, results, ws) : 0));
                }
            }
            return d;
        }

        /// <summary>The first nested view whose prefab does not exist yet (its asset path is added to <paramref name="enqueue"/>), or null when every child is built.</summary>
        private static string MissingChild(AuiCompileResult r, AuiWorkspace ws, List<string> enqueue)
        {
            foreach (AuiNestedView nv in r.Info.NestedViews)
            {
                string cp;
                if (!ws.Views.TryGetValue(nv.ViewName, out cp)) return nv.ViewName + " (no such view)";
                string prefab = AuiEditorEnv.AssetPath(Path.ChangeExtension(cp, ".prefab"));
                if (prefab == null || AssetDatabase.LoadAssetAtPath<GameObject>(prefab) == null)
                {
                    string ap = AuiEditorEnv.AssetPath(cp);
                    if (ap != null && !enqueue.Contains(ap)) enqueue.Add(ap);
                    return nv.ViewName;
                }
            }
            return null;
        }

        /// <summary>Force-rebuild one view even when its bake hash matches.</summary>
        public static void RebuildOne(string assetPath)
        {
            SessionState.SetBool(ForceKey, true);
            SessionState.EraseInt(AttemptsKey + assetPath);
            Enqueue(assetPath);
            Kick(true);
        }

        /// <summary>Queue every .aui under Assets (and the package's own views). <paramref name="force"/> rebuilds prefabs whose bake hash still matches.</summary>
        public static void EnqueueAll(bool force = true)
        {
            if (force) SessionState.SetBool(ForceKey, true);
            AuiWorkspace ws = AuiEditorEnv.Scan(AuiEditorEnv.FindSettings());
            foreach (string p in ws.Views.Values)
            {
                string ap = AuiEditorEnv.AssetPath(p);
                if (ap != null) { SessionState.EraseInt(AttemptsKey + ap); Enqueue(ap); }
            }
            Kick(true);
        }
    }
}
