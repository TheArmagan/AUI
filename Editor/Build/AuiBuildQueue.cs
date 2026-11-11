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
        private const string ForcedKey = "AUI.BuildForced";
        private const string ExplicitKey = "AUI.BuildExplicit";
        private const string BuiltSinceHooksKey = "AUI.BuiltSinceHooks";
        private static bool _scheduled;

        /// <summary>Raised at the end of every finished drain that built something, after the [AuiAfterBuild] hooks.</summary>
        public static event System.Action<AuiBuildReport> Built;

        /// <summary>Raised at the end of every drain, built or not (the Build Status window repaints on it).</summary>
        public static event System.Action Drained;

        // FORCE IS PER PATH. It used to be one bool for the whole queue, so the inspector's "Rebuild
        // view" forced every other view that happened to be queued with it. A path leaves the set when
        // it leaves the queue.
        private static List<string> ForcedPaths()
        {
            var list = new List<string>();
            foreach (string p in SessionState.GetString(ForcedKey, "").Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries)) list.Add(p);
            return list;
        }

        /// <summary>True when <paramref name="assetPath"/> (a .aui asset path) rebuilds even though its bake hash matches.</summary>
        public static bool IsForced(string assetPath)
        {
            return ForcedPaths().Contains(assetPath);
        }

        private static void SetForced(string assetPath, bool on)
        {
            List<string> list = ForcedPaths();
            bool has = list.Contains(assetPath);
            if (on == has) return;
            if (on) list.Add(assetPath); else list.Remove(assetPath);
            SessionState.SetString(ForcedKey, string.Join("|", list));
        }

        public static int Attempts(string assetPath)
        {
            return SessionState.GetInt(AttemptsKey + assetPath, 0);
        }

        /// <summary>Why a queued path is still queued (the last step it reached), or null.</summary>
        public static string Reason(string assetPath)
        {
            string s = SessionState.GetString(ReasonKey + assetPath, "");
            return s.Length > 0 ? s : null;
        }

        /// <summary>Drop every queued path, its force flag, attempts and reason.</summary>
        public static void ClearQueue()
        {
            foreach (string p in Pending())
            {
                SessionState.EraseInt(AttemptsKey + p);
                SessionState.EraseString(ReasonKey + p);
            }
            Save(new List<string>());
            SessionState.EraseString(ForcedKey);
            SessionState.SetBool(ExplicitKey, false);
            if (Drained != null) Drained();
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
            var drain = new AuiDrainRecord { time = System.DateTime.Now.ToString("HH:mm:ss"), views = list.Count };
            // UdonSharp may have compiled since the last drain (any script save does it).
            AuiProgramAssets.ForgetPrograms();
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
                if (pa != null && !AuiProgramAssets.HasProgram(pa)) needUsharp = true;
            }
            if (needUsharp)
            {
                long t0 = clock.ElapsedMilliseconds;
                UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync(new UdonSharp.Compiler.UdonSharpCompileOptions());
                AuiProgramAssets.ForgetPrograms();
                usharpMs = clock.ElapsedMilliseconds - t0;
            }

            bool wroteSource = false;
            bool progressed = false;
            int built = 0;
            long buildStart = clock.ElapsedMilliseconds;
            var remaining = new List<string>(list);
            // Children a parent found missing during THIS drain. ChildrenBuilt used to Enqueue them
            // straight into SessionState, which Save(remaining) then overwrote with the pre-drain
            // list: the child never entered the queue and the parent failed four times.
            var lateChildren = new List<string>();
            // Parents of a view built in this drain that were not queued with it (see ForceParents).
            var lateParents = new List<string>();
            Dictionary<string, List<string>> parentsOf = null;
            // One scene sweep for the whole drain: RevertInstanceSizeOverrides used to do one per built view.
            AuiView[] sceneViews = Object.FindObjectsOfType<AuiView>(true);

            // ONE IMPORT PER DEPTH LEVEL, NOT ONE PER PREFAB. Saving a prefab makes Unity re-import
            // every prefab that nests it and re-merge the scene's instances right away: a 9-element
            // pager nested by the console and by a world prefab took 5 s to save, against 0.2 s for a
            // prefab nobody nests (measured 2026-11-11, 23 views: 77 s of 79 in SaveAsPrefabAsset).
            // Views at the same depth never nest each other, so a level's saves go inside one
            // StartAssetEditing and are imported together at its StopAssetEditing, before the next
            // level (whose parents instantiate these prefabs) starts.
            var depthOf = new Dictionary<string, int>();
            foreach (string p in list) depthOf[p] = Depth(p, results, ws);
            int level = int.MinValue;
            bool editing = false;
            long importMs = 0;
            var deferredReverts = new List<string>();
            try
            {
            foreach (string p in list)
            {
                int pd;
                if (!depthOf.TryGetValue(p, out pd)) pd = 0;
                if (pd != level)
                {
                    EndLevel(ref editing, deferredReverts, sceneViews, ref importMs);
                    level = pd;
                }
                var rec = new AuiViewBuildRecord { path = p, view = System.IO.Path.GetFileNameWithoutExtension(p) };
                drain.records.Add(rec);
                AuiCompileResult r;
                if (!results.TryGetValue(p, out r)) { remaining.Remove(p); SetForced(p, false); rec.status = AuiViewBuildRecord.Failed; rec.reason = "file not found"; progressed = true; continue; }
                int attempts = SessionState.GetInt(AttemptsKey + p, 0) + 1;
                SessionState.SetInt(AttemptsKey + p, attempts);
                if (attempts > MaxAttempts)
                {
                    rec.status = AuiViewBuildRecord.GaveUp;
                    rec.reason = SessionState.GetString(ReasonKey + p, "unknown");
                    AuiLog.Error(AuiCodes.PrefabBuildFailed + " " + p + ": giving up after " + MaxAttempts + " build attempts (last step: " + rec.reason + "). Fix the errors above and save the file again.");
                    remaining.Remove(p);
                    SetForced(p, false);
                    SessionState.EraseInt(AttemptsKey + p);   // the next save starts a fresh count
                    SessionState.EraseString(ReasonKey + p);
                    progressed = true;
                    continue;
                }
                if (r.HasErrors)
                {
                    rec.status = AuiViewBuildRecord.Errors;
                    rec.reason = "the view has errors (see the import log)";
                    AuiLog.Error(p + ": not built, the view has errors (see the import log).");
                    remaining.Remove(p);
                    SetForced(p, false);
                    SessionState.EraseInt(AttemptsKey + p);
                    progressed = true;
                    continue;
                }
                // Step A
                if (AuiSourceWriter.Write(r, settings))
                {
                    rec.status = AuiViewBuildRecord.Waiting;
                    rec.reason = "waiting for the generated source to compile";
                    SessionState.SetString(ReasonKey + p, rec.reason);
                    wroteSource = true;
                    continue;
                }
                // A parent needs its children's prefabs; leave it queued until they exist. Waiting
                // for a child is not a failed attempt of the parent's.
                string missingChild = MissingChild(r, ws, lateChildren);
                if (missingChild != null)
                {
                    rec.status = AuiViewBuildRecord.Waiting;
                    rec.reason = "waiting for the child prefab " + missingChild;
                    SessionState.SetString(ReasonKey + p, rec.reason);
                    SessionState.SetInt(AttemptsKey + p, attempts - 1);
                    continue;
                }
                // Step B
                if (!editing && BatchImports)
                {
                    AssetDatabase.StartAssetEditing();
                    editing = true;
                    AuiPrefabBuilder.DeferredReverts = deferredReverts;
                }
                if (AuiPrefabBuilder.Build(r, settings, ws, sceneViews, IsForced(p), rec))
                {
                    if (rec.status == AuiViewBuildRecord.Built)
                    {
                        built++;
                        NoteBuilt(r.ViewName, p);
                        if (parentsOf == null) parentsOf = ParentMap(ws);
                        ForceParents(r.ViewName, parentsOf, remaining, lateParents);
                    }
                    remaining.Remove(p);
                    SetForced(p, false);
                    SessionState.EraseInt(AttemptsKey + p);
                    SessionState.EraseString(ReasonKey + p);
                    progressed = true;
                }
                else
                {
                    rec.status = AuiViewBuildRecord.Failed;
                    rec.reason = "prefab build failed (see the error above)";
                    SessionState.SetString(ReasonKey + p, rec.reason);
                }
            }
            }
            finally
            {
                EndLevel(ref editing, deferredReverts, sceneViews, ref importMs);
            }
            foreach (string cp in lateChildren) if (!remaining.Contains(cp)) remaining.Add(cp);
            foreach (string pp in lateParents) if (!remaining.Contains(pp)) { remaining.Add(pp); progressed = true; }
            Save(remaining);
            if (remaining.Count == 0) { SessionState.EraseString(ForcedKey); SessionState.SetBool(ExplicitKey, false); }
            long buildMs = clock.ElapsedMilliseconds - buildStart;
            // The views carry key indices into the CURRENT language files; the scene's service must
            // carry the same table or every key added since the last bake shows "??". Checked on
            // every drain, not only when a prefab was built: a translation edit that does not move
            // the widest width leaves every hash alone and builds nothing, yet the service's strings
            // are what the player reads. One string compare per cell when nothing changed.
            long langStart = clock.ElapsedMilliseconds;
            AuiLanguageTableBuilder.ApplyIfChanged(settings, ws);
            long langMs = clock.ElapsedMilliseconds - langStart;

            // HOOKS RUN ONCE THE QUEUE IS EMPTY, not after every drain: a drain that wrote sources
            // continues after a domain reload, and a hook running in between would wire half a build.
            long hooksMs = 0;
            bool finished = !wroteSource && remaining.Count == 0;
            if (finished && !AuiBuildHooks.Running) hooksMs = RunHooks();

            drain.built = built;
            drain.sourcesWritten = wroteSource;
            drain.cached = cached;
            drain.scanMs = scanMs; drain.compileMs = compileMs; drain.usharpMs = usharpMs; drain.buildMs = buildMs; drain.importMs = importMs; drain.langMs = langMs; drain.hooksMs = hooksMs;
            drain.totalMs = clock.ElapsedMilliseconds;
            drain.Save();
            AuiLog.Info("drain: " + list.Count + " view(s), " + built + " built, " + (wroteSource ? "sources written, " : "") +
                        clock.ElapsedMilliseconds + " ms (scan " + scanMs + ", compile " + compileMs + " with " + cached + " cached, udonsharp " + usharpMs + ", build " + buildMs + " of which batched import " + importMs + ", language table " + langMs + ", hooks " + hooksMs + ")");
            if (Drained != null) { try { Drained(); } catch (System.Exception e) { AuiLog.Error("Drained listener threw: " + e); } }
            if (wroteSource)
            {
                // Sources changed: let Unity compile; the queue resumes after the reload.
                AssetDatabase.Refresh();
                return;
            }
            if (remaining.Count > 0 && progressed) Kick(explicitRun);
            else if (remaining.Count > 0)
            {
                // Nothing progressed this pass (children missing or program assets pending): try once more after a refresh.
                AssetDatabase.Refresh();
                Kick(explicitRun);
            }
        }

        /// <summary>Off only to measure the difference (or to rule the batching out when chasing a bug). Per project.</summary>
        public static bool BatchImports
        {
            get { return EditorPrefs.GetBool("AUI.BatchImports." + Application.dataPath.GetHashCode().ToString("x"), true); }
            set { EditorPrefs.SetBool("AUI.BatchImports." + Application.dataPath.GetHashCode().ToString("x"), value); }
        }

        /// <summary>Import the level's saved prefabs, then put back the scene instances' sizes (which needed the import).</summary>
        private static void EndLevel(ref bool editing, List<string> deferredReverts, AuiView[] sceneViews, ref long importMs)
        {
            if (!editing) return;
            editing = false;
            AuiPrefabBuilder.DeferredReverts = null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            AssetDatabase.StopAssetEditing();
            foreach (string prefabPath in deferredReverts) AuiPrefabBuilder.RevertInstanceSizeOverrides(prefabPath, sceneViews);
            deferredReverts.Clear();
            importMs += clock.ElapsedMilliseconds;
        }

        // ================================================================ parents and hooks

        /// <summary>Child view name -> asset paths of the views that nest it, read off the imported AuiViewAssets.</summary>
        private static Dictionary<string, List<string>> ParentMap(AuiWorkspace ws)
        {
            var map = new Dictionary<string, List<string>>();
            foreach (string abs in ws.Views.Values)
            {
                string ap = AuiEditorEnv.AssetPath(abs);
                if (ap == null) continue;
                var va = AssetDatabase.LoadAssetAtPath<AuiViewAsset>(ap);
                if (va == null || va.childViews == null) continue;
                foreach (string child in va.childViews)
                {
                    List<string> parents;
                    if (!map.TryGetValue(child, out parents)) map[child] = parents = new List<string>();
                    if (!parents.Contains(ap)) parents.Add(ap);
                }
            }
            return map;
        }

        /// <summary>
        /// A BUILT CHILD FORCES ITS PARENTS. A parent's prefab carries overrides on objects inside the
        /// child's instance (its placement, re-homed circle masks, the parentView link), and a child
        /// rebuild recreates those objects while the parent's own bake hash stays the same, so a
        /// hash-based pass would leave the parent pointing at objects that no longer exist. Only the
        /// direct parents are forced here; each of them, once built, forces its own.
        /// </summary>
        private static void ForceParents(string viewName, Dictionary<string, List<string>> parentsOf, List<string> remaining, List<string> lateParents)
        {
            List<string> parents;
            if (viewName == null || !parentsOf.TryGetValue(viewName, out parents)) return;
            foreach (string pp in parents)
            {
                SetForced(pp, true);
                SessionState.EraseInt(AttemptsKey + pp);
                if (!remaining.Contains(pp) && !lateParents.Contains(pp)) lateParents.Add(pp);
            }
        }

        /// <summary>Kept across the reloads a multi-drain build goes through, so hooks see every view built since they last ran.</summary>
        private static void NoteBuilt(string viewName, string assetPath)
        {
            string entry = viewName + "\t" + assetPath;
            string s = SessionState.GetString(BuiltSinceHooksKey, "");
            foreach (string e in s.Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries)) if (e == entry) return;
            SessionState.SetString(BuiltSinceHooksKey, s.Length == 0 ? entry : s + "|" + entry);
        }

        private static long RunHooks()
        {
            string s = SessionState.GetString(BuiltSinceHooksKey, "");
            if (s.Length == 0) return 0;
            SessionState.EraseString(BuiltSinceHooksKey);
            var views = new List<string>();
            var paths = new List<string>();
            foreach (string e in s.Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                int tab = e.IndexOf('\t');
                if (tab < 0) continue;
                views.Add(e.Substring(0, tab));
                paths.Add(e.Substring(tab + 1));
            }
            var report = new AuiBuildReport(views, paths);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            AuiBuildHooks.Run(report);
            if (Built != null)
            {
                try { Built(report); }
                catch (System.Exception e) { AuiLog.Error("AuiBuildQueue.Built listener threw: " + e); }
            }
            return clock.ElapsedMilliseconds;
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

        /// <summary>Force-rebuild one view even when its bake hash matches (the views that nest it follow, see ForceParents).</summary>
        public static void RebuildOne(string assetPath)
        {
            RebuildViews(new[] { assetPath });
        }

        /// <summary>
        /// Queue the given .aui asset paths and drain now. <paramref name="force"/> rebuilds them even
        /// when their bake hash matches; only these paths are forced, not the rest of the queue. Every
        /// view nesting one that is rebuilt follows on its own.
        /// </summary>
        public static void RebuildViews(IEnumerable<string> auiAssetPaths, bool force = true)
        {
            int n = 0;
            foreach (string ap in auiAssetPaths)
            {
                if (string.IsNullOrEmpty(ap) || !ap.EndsWith(".aui")) continue;
                SessionState.EraseInt(AttemptsKey + ap);
                Enqueue(ap);
                if (force) SetForced(ap, true);
                n++;
            }
            if (n == 0) return;
            DrainNow();
        }

        /// <summary>Queue every .aui under Assets (and the package's own views). <paramref name="force"/> rebuilds prefabs whose bake hash still matches.</summary>
        public static void EnqueueAll(bool force = true)
        {
            AuiWorkspace ws = AuiEditorEnv.Scan(AuiEditorEnv.FindSettings());
            foreach (string p in ws.Views.Values)
            {
                string ap = AuiEditorEnv.AssetPath(p);
                if (ap == null) continue;
                SessionState.EraseInt(AttemptsKey + ap);
                Enqueue(ap);
                if (force) SetForced(ap, true);
            }
            Kick(true);
        }
    }
}
