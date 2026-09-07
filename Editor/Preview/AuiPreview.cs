using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Armagan.AUI.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// The live preview: one view built as plain uGUI in the open scene, straight from the
    /// files on disk, a moment after they are saved. No import, no generated code, no
    /// UdonSharp, no prefab: a file watcher sees the save, the (cached) compiler runs, and
    /// AuiPrefabBuilder's preview mode rebuilds the tree under a DontSave object that sits
    /// where the view's scene instance is (the instance is hidden meanwhile) or in front of
    /// the scene camera. Works while Unity is unfocused: only the asset import waits for focus.
    ///
    /// WHAT THE PREFAB HIDES, THE PREVIEW FOLLOWS. Buttons do nothing here, so a page that is
    /// not the default and a select's popup would never be seen. Each save is diffed against
    /// the last text; the element around the change decides which page is shown and which
    /// popup is open, unless the window pinned a choice.
    ///
    /// Everything here avoids Undo and SetDirty: a session that only prototyped closes
    /// without a save prompt. State lives in SessionState so a domain reload resumes it.
    /// </summary>
    [InitializeOnLoad]
    public static class AuiPreview
    {
        private const string KeyView = "AUI.Preview.View";
        private const string KeyLive = "AUI.Preview.Live";
        private const string KeyLang = "AUI.Preview.Lang";
        private const string KeyRoot = "AUI.Preview.RootId";
        private const string KeyHidden = "AUI.Preview.HiddenId";
        private const string KeyHiddenWas = "AUI.Preview.HiddenWasHidden";
        private const string KeyPages = "AUI.Preview.Pages";
        private const string KeyOpen = "AUI.Preview.OpenSelects";
        private const string KeyOpenModals = "AUI.Preview.OpenModals";
        private const string KeyFollow = "AUI.Preview.Follow";
        private const string KeyPickable = "AUI.Preview.Pickable";
        private const string RootPrefix = "AUI Preview: ";
        private const double DebounceSeconds = 0.15;

        /// <summary>Raised after every rebuild, stop or option change; the window repaints on it.</summary>
        public static event System.Action Changed;

        public static AuiCompileResult LastResult { get; private set; }
        public static List<AuiDiagnostic> LastDiagnostics { get; private set; }
        public static long LastCompileMs { get; private set; }
        public static long LastBuildMs { get; private set; }
        public static System.DateTime LastBuilt { get; private set; }
        public static GameObject Root { get; private set; }

        private static readonly AuiPrefabBuilder.AuiPreviewOptions Options = new AuiPrefabBuilder.AuiPreviewOptions();
        private static readonly List<FileSystemWatcher> Watchers = new List<FileSystemWatcher>();
        private static readonly ConcurrentQueue<KeyValuePair<string, WatcherChangeTypes>> Events = new ConcurrentQueue<KeyValuePair<string, WatcherChangeTypes>>();
        private static readonly Dictionary<string, double> PendingAt = new Dictionary<string, double>();
        private static readonly Dictionary<string, WatcherChangeTypes> PendingKind = new Dictionary<string, WatcherChangeTypes>();
        private static readonly Dictionary<string, int> PendingTries = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> LastText = new Dictionary<string, string>();
        private static readonly HashSet<string> WatchedDirs = new HashSet<string>();
        private static string _lastDiagText;
        private static bool _watcherError;
        private static bool _hooked;

        static AuiPreview()
        {
            LoadOptions();
            EditorApplication.delayCall += OnLoad;
        }

        // ---------------------------------------------------------------- state

        public static string ViewPath
        {
            get { string v = SessionState.GetString(KeyView, ""); return v.Length == 0 ? null : v; }
            private set { SessionState.SetString(KeyView, value ?? ""); }
        }

        public static bool Live
        {
            get { return SessionState.GetBool(KeyLive, false); }
            private set { SessionState.SetBool(KeyLive, value); }
        }

        public static string LangCode
        {
            get { string v = SessionState.GetString(KeyLang, ""); return v.Length == 0 ? null : v; }
            set { SessionState.SetString(KeyLang, value ?? ""); Options.LangCode = value; if (Live) Rebuild(); }
        }

        public static bool FollowEdits
        {
            get { return SessionState.GetBool(KeyFollow, true); }
            set { SessionState.SetBool(KeyFollow, value); }
        }

        public static bool Pickable
        {
            get { return SessionState.GetBool(KeyPickable, false); }
            set { SessionState.SetBool(KeyPickable, value); ApplyPicking(); }
        }

        public static string ViewName
        {
            get { return ViewPath != null ? Path.GetFileNameWithoutExtension(ViewPath) : null; }
        }

        private static void LoadOptions()
        {
            Options.LangCode = LangCode;
            Options.Pages.Clear();
            foreach (string kv in SessionState.GetString(KeyPages, "").Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = kv.IndexOf('=');
                if (eq > 0) Options.Pages[kv.Substring(0, eq)] = kv.Substring(eq + 1);
            }
            Options.OpenSelects.Clear();
            foreach (string k in SessionState.GetString(KeyOpen, "").Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries)) Options.OpenSelects.Add(k);
            Options.OpenModals.Clear();
            foreach (string k in SessionState.GetString(KeyOpenModals, "").Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries)) Options.OpenModals.Add(k);
        }

        private static void SaveOptions()
        {
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, string> kv in Options.Pages) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            SessionState.SetString(KeyPages, sb.ToString());
            SessionState.SetString(KeyOpen, string.Join(";", Options.OpenSelects));
            SessionState.SetString(KeyOpenModals, string.Join(";", Options.OpenModals));
        }

        /// <summary>The page the preview shows for a group, or null for the markup default.</summary>
        public static string PageFor(string viewName, string groupId)
        {
            string p;
            return Options.Pages.TryGetValue(viewName + "/" + groupId, out p) ? p : null;
        }

        public static void SetPage(string viewName, string groupId, string pageId)
        {
            string key = viewName + "/" + groupId;
            if (pageId == null) Options.Pages.Remove(key); else Options.Pages[key] = pageId;
            SaveOptions();
            if (Live) Rebuild();
        }

        public static bool IsSelectOpen(string viewName, int nodeIndex)
        {
            return Options.OpenSelects.Contains(viewName + "/" + nodeIndex);
        }

        public static void SetSelectOpen(string viewName, int nodeIndex, bool open)
        {
            string key = viewName + "/" + nodeIndex;
            if (open) Options.OpenSelects.Add(key); else Options.OpenSelects.Remove(key);
            SaveOptions();
            if (Live) Rebuild();
        }

        public static bool IsModalOpen(string viewName, int nodeIndex)
        {
            return Options.OpenModals.Contains(viewName + "/" + nodeIndex);
        }

        public static void SetModalOpen(string viewName, int nodeIndex, bool open)
        {
            string key = viewName + "/" + nodeIndex;
            if (open) Options.OpenModals.Add(key); else Options.OpenModals.Remove(key);
            SaveOptions();
            if (Live) Rebuild();
        }

        // ---------------------------------------------------------------- lifecycle

        private static void OnLoad()
        {
            Hook();
            // Strays from a crash mid-build, or the tree from before the reload: gone, rebuilt below if live.
            DestroyRoots(null);
            Root = null;
            SessionState.SetInt(KeyRoot, 0);
            if (Live && ViewPath != null && File.Exists(ViewPath) && !EditorApplication.isPlayingOrWillChangePlaymode) Rebuild();
            else Unhide();
        }

        /// <summary>Every top-level preview root in memory, whatever made it (a reload, a crash, a build that raced another).</summary>
        private static List<GameObject> FindRoots()
        {
            var list = new List<GameObject>();
            foreach (AuiPreviewRoot m in Resources.FindObjectsOfTypeAll<AuiPreviewRoot>())
                if (m != null && !EditorUtility.IsPersistent(m.gameObject) && !list.Contains(m.gameObject)) list.Add(m.gameObject);
            // Roots from before the marker existed: top-level, not saved, named "AUI Preview: X" or plainly "X"
            // after the builder renamed them, carrying a Canvas but no view component.
            HashSet<string> views = null;
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || list.Contains(go) || go.transform.parent != null || EditorUtility.IsPersistent(go)) continue;
                if ((go.hideFlags & HideFlags.DontSaveInEditor) == 0) continue;
                if (go.GetComponent<AuiView>() != null || go.GetComponent<Canvas>() == null) continue;
                if (!go.name.StartsWith(RootPrefix))
                {
                    if (views == null) { views = new HashSet<string>(); AuiWorkspace ws = AuiEditorEnv.CachedWorkspace; if (ws != null) views.UnionWith(ws.Views.Keys); }
                    if (!views.Contains(go.name)) continue;
                }
                list.Add(go);
            }
            return list;
        }

        /// <summary>Destroy every preview root except <paramref name="keep"/>. ONE PREVIEW, ALWAYS.</summary>
        private static void DestroyRoots(GameObject keep)
        {
            foreach (GameObject go in FindRoots())
                if (go != keep) Object.DestroyImmediate(go);
        }

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += DisposeWatchers;
            EditorApplication.quitting += OnQuit;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorSceneManager.sceneClosing += (scene, removing) => Teardown();
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private static void OnQuit()
        {
            Teardown();
            DisposeWatchers();
        }

        private static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.ExitingEditMode) Teardown();
            else if (s == PlayModeStateChange.EnteredEditMode && Live && ViewPath != null) Rebuild();
        }

        private static void OnHierarchyChanged()
        {
            // The scene was switched under us (new scene, scene loaded): a DontSave object outlives
            // the scene it was made in, so it would linger as a stray. Drop it and build afresh.
            if (Root != null && (!Root.scene.IsValid() || !Root.scene.isLoaded))
            {
                Object.DestroyImmediate(Root);
                Root = null;
                SessionState.SetInt(KeyRoot, 0);
                Unhide();
                if (Live) Rebuild();
            }
        }

        /// <summary>Start previewing a view (absolute or asset path of its .aui).</summary>
        public static void Start(string auiPath)
        {
            string abs = Path.GetFullPath(auiPath);
            if (!File.Exists(abs)) { AuiLog.Error("preview: " + auiPath + " does not exist."); return; }
            Hook();
            if (ViewPath != null && Norm(ViewPath) != Norm(abs)) { Teardown(); Options.Pages.Clear(); Options.OpenSelects.Clear(); Options.OpenModals.Clear(); SaveOptions(); }
            ViewPath = abs;
            Live = true;
            LastText.Clear();
            Rebuild();
        }

        /// <summary>Stop: destroy the preview object, show the instance again, drop the watchers.</summary>
        public static void Stop()
        {
            Live = false;
            Teardown();
            DisposeWatchers();
            Raise();
        }

        /// <summary>Destroy the preview object and unhide the instance, keeping the live flag (play mode, scene close, reload).</summary>
        private static void Teardown()
        {
            if (Root != null) Object.DestroyImmediate(Root);
            DestroyRoots(null);
            Root = null;
            SessionState.SetInt(KeyRoot, 0);
            Unhide();
            SceneView.RepaintAll();
        }

        // ---------------------------------------------------------------- rebuild

        /// <summary>Compile from disk and rebuild the tree now.</summary>
        public static void Rebuild()
        {
            if (CompileNow()) BuildNow();
        }

        private static bool CanRun()
        {
            return ViewPath != null && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>Compile the previewed view from disk into <see cref="LastResult"/>; false when nothing was compiled.</summary>
        private static bool CompileNow()
        {
            if (!CanRun()) return false;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            AuiSettings settings = AuiEditorEnv.FindSettings();
            AuiWorkspace ws = AuiEditorEnv.ScanFor(ViewPath, settings);
            var files = new HashSet<string>();
            AuiCompileResult r;
            try
            {
                r = AuiEditorEnv.Compile(ViewPath, settings, ws, p => files.Add(Path.GetFullPath(p)));
            }
            catch (System.Exception e)
            {
                AuiLog.Error("preview: compile failed: " + e);
                return false;
            }
            LastCompileMs = clock.ElapsedMilliseconds;
            LastResult = r;
            LastDiagnostics = new List<AuiDiagnostic>(r.Diagnostics.Items);
            string diagText = string.Join("\n", LastDiagnostics.ConvertAll(d => d.ToString()));
            if (diagText != _lastDiagText) { _lastDiagText = diagText; AuiEditorEnv.Report(r); }

            // Snapshot every source the compile read (views and sheets) so the next save can be diffed.
            foreach (string f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".aui" && ext != ".auis") continue;
                string t = SafeRead(f);
                if (t != null) LastText[Norm(f)] = t;
            }
            Watch(files, settings);
            // What the window can switch, gathered once per compile (not per GUI frame).
            LastPageGroups.Clear();
            LastSelects.Clear();
            LastModals.Clear();
            Collect(r, LastPageGroups, LastSelects, settings, ws, 0);
            return true;
        }

        /// <summary>Rebuild the tree from <see cref="LastResult"/>.</summary>
        private static void BuildNow()
        {
            AuiCompileResult r = LastResult;
            if (r == null || !CanRun()) return;
            if (r.Doc == null || r.Doc.Root == null || r.Map == null || r.Layout == null)
            {
                // Nothing buildable: keep the last tree on screen; the window shows the errors.
                LastBuilt = System.DateTime.Now;
                Raise();
                return;
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            AuiSettings settings = AuiEditorEnv.FindSettings();
            AuiWorkspace ws = AuiEditorEnv.Scan(settings);
            try
            {
                GameObject root = EnsureRoot(r, settings);
                for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                AuiPrefabBuilder.BuildPreview(r, settings, ws, Options, root);
                root.name = RootPrefix + r.ViewName;
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
                ApplyPicking();
                Canvas.ForceUpdateCanvases();
                RepaintSceneViews();
            }
            catch (System.Exception e)
            {
                AuiLog.Error("preview: build failed: " + e);
            }
            LastBuildMs = clock.ElapsedMilliseconds;
            LastBuilt = System.DateTime.Now;
            Raise();
        }

        private static GameObject EnsureRoot(AuiCompileResult r, AuiSettings settings)
        {
            if (Root == null)
            {
                int id = SessionState.GetInt(KeyRoot, 0);
                if (id != 0) Root = EditorUtility.InstanceIDToObject(id) as GameObject;
                // Whatever survived a reload or a race is the root; never make a second one next to it.
                if (Root == null) { List<GameObject> found = FindRoots(); if (found.Count > 0) Root = found[0]; }
            }
            DestroyRoots(Root);
            if (Root != null)
            {
                Root.name = RootPrefix + r.ViewName;
                if (Root.GetComponent<AuiPreviewRoot>() == null) Root.AddComponent<AuiPreviewRoot>();
                SessionState.SetInt(KeyRoot, Root.GetInstanceID());
                return Root;
            }

            Root = new GameObject(RootPrefix + r.ViewName, typeof(RectTransform), typeof(AuiPreviewRoot));
            Root.hideFlags = HideFlags.DontSave;
            SessionState.SetInt(KeyRoot, Root.GetInstanceID());
            var rt = (RectTransform)Root.transform;
            rt.localScale = Vector3.one * 0.001f;

            GameObject instance = FindInstance(r);
            if (instance != null)
            {
                rt.position = instance.transform.position;
                rt.rotation = instance.transform.rotation;
                rt.localScale = instance.transform.lossyScale;
                Hide(instance);
            }
            else
            {
                SceneView sv = SceneView.lastActiveSceneView;
                if (sv != null && sv.camera != null)
                {
                    Transform cam = sv.camera.transform;
                    rt.position = cam.position + cam.forward * 1.5f;
                    rt.rotation = Quaternion.LookRotation(cam.forward, cam.up);
                }
            }
            return Root;
        }

        /// <summary>The first placed instance of this view's prefab in the open scene, by prefab asset (the generated type may not exist yet).</summary>
        private static GameObject FindInstance(AuiCompileResult r)
        {
            string prefabPath = AuiEditorEnv.AssetPath(Path.ChangeExtension(r.FilePath, ".prefab"));
            GameObject asset = prefabPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) : null;
            if (asset == null) return null;
            foreach (AuiView v in Object.FindObjectsOfType<AuiView>(true))
            {
                GameObject go = v.gameObject;
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(go)) continue;
                if (PrefabUtility.GetCorrespondingObjectFromSource(go) == asset) return go;
            }
            return null;
        }

        private static void Hide(GameObject instance)
        {
            Unhide();
            var vis = SceneVisibilityManager.instance;
            SessionState.SetInt(KeyHidden, instance.GetInstanceID());
            SessionState.SetBool(KeyHiddenWas, vis.IsHidden(instance, false));
            vis.Hide(instance, true);
        }

        private static void Unhide()
        {
            int id = SessionState.GetInt(KeyHidden, 0);
            if (id == 0) return;
            SessionState.SetInt(KeyHidden, 0);
            var go = EditorUtility.InstanceIDToObject(id) as GameObject;
            if (go == null || SessionState.GetBool(KeyHiddenWas, false)) return;
            SceneVisibilityManager.instance.Show(go, true);
        }

        private static void ApplyPicking()
        {
            if (Root == null) return;
            var vis = SceneVisibilityManager.instance;
            if (Pickable) vis.EnablePicking(Root, true); else vis.DisablePicking(Root, true);
        }

        private static System.Reflection.MethodInfo _repaintImmediately;

        /// <summary>
        /// Repaint every scene view NOW. A queued repaint (SceneView.RepaintAll) is only honoured
        /// when Unity is the foreground application; with the editor on a second monitor behind
        /// the code editor, the update loop still ticks and the tree is rebuilt, but nothing is
        /// drawn until the user clicks into Unity. EditorWindow.RepaintImmediately is internal
        /// and draws synchronously, background or not.
        /// </summary>
        private static void RepaintSceneViews()
        {
            if (_repaintImmediately == null)
                _repaintImmediately = typeof(EditorWindow).GetMethod("RepaintImmediately", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (SceneView sv in SceneView.sceneViews)
            {
                if (sv == null) continue;
                try
                {
                    if (_repaintImmediately != null) _repaintImmediately.Invoke(sv, null);
                    else sv.Repaint();
                }
                catch (System.Exception) { sv.Repaint(); }
            }
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        /// <summary>Move the scene view to the preview.</summary>
        public static void Frame()
        {
            SceneView sv = SceneView.lastActiveSceneView;
            if (sv == null || Root == null || LastResult == null || LastResult.Info == null) return;
            var rt = (RectTransform)Root.transform;
            Vector3 size = new Vector3(LastResult.Info.Width, LastResult.Info.Height, 10f);
            size.Scale(rt.lossyScale);
            sv.Frame(new Bounds(rt.position, size), false);
        }

        // ---------------------------------------------------------------- watching

        private static void Watch(HashSet<string> files, AuiSettings settings)
        {
            var dirs = new HashSet<string>();
            foreach (string f in files) { string d = Path.GetDirectoryName(f); if (d != null && Directory.Exists(d)) dirs.Add(Norm(d)); }
            if (settings != null)
                foreach (string f in settings.componentFolders)
                    if (!string.IsNullOrEmpty(f) && Directory.Exists(f)) dirs.Add(Norm(Path.GetFullPath(f)));
            if (dirs.SetEquals(WatchedDirs) && !_watcherError) return;
            DisposeWatchers();
            _watcherError = false;
            foreach (string d in dirs)
            {
                try
                {
                    var w = new FileSystemWatcher(d) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
                    w.Changed += (s, e) => Events.Enqueue(new KeyValuePair<string, WatcherChangeTypes>(e.FullPath, e.ChangeType));
                    w.Created += (s, e) => Events.Enqueue(new KeyValuePair<string, WatcherChangeTypes>(e.FullPath, e.ChangeType));
                    w.Deleted += (s, e) => Events.Enqueue(new KeyValuePair<string, WatcherChangeTypes>(e.FullPath, e.ChangeType));
                    w.Renamed += (s, e) => Events.Enqueue(new KeyValuePair<string, WatcherChangeTypes>(e.FullPath, e.ChangeType));
                    w.Error += (s, e) => Events.Enqueue(new KeyValuePair<string, WatcherChangeTypes>("", WatcherChangeTypes.All));
                    w.EnableRaisingEvents = true;
                    Watchers.Add(w);
                    WatchedDirs.Add(d);
                }
                catch (System.Exception e)
                {
                    AuiLog.Warn("preview: cannot watch " + d + ": " + e.Message);
                }
            }
        }

        private static void DisposeWatchers()
        {
            foreach (FileSystemWatcher w in Watchers)
            {
                try { w.EnableRaisingEvents = false; w.Dispose(); } catch { }
            }
            Watchers.Clear();
            WatchedDirs.Clear();
        }

        private static void Tick()
        {
            KeyValuePair<string, WatcherChangeTypes> ev;
            double now = EditorApplication.timeSinceStartup;
            while (Events.TryDequeue(out ev))
            {
                if (ev.Key.Length == 0)
                {
                    // Watcher buffer overflow (a checkout, a mass reimport): recreate it and recompile everything.
                    _watcherError = true;
                    if (ViewPath != null) { string vp = Norm(ViewPath); PendingAt[vp] = now; PendingKind[vp] = WatcherChangeTypes.Changed; }
                    continue;
                }
                string ext = Path.GetExtension(ev.Key).ToLowerInvariant();
                if (ext != ".aui" && ext != ".auis" && ext != ".json" && ext != ".svg") continue;
                string p = Norm(ev.Key);
                PendingAt[p] = now;
                WatcherChangeTypes prev;
                PendingKind[p] = PendingKind.TryGetValue(p, out prev) ? prev | ev.Value : ev.Value;
            }
            if (PendingAt.Count == 0 || !Live) { if (!Live) { PendingAt.Clear(); PendingKind.Clear(); } return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (double t in PendingAt.Values) if (now - t < DebounceSeconds) return;

            var changed = new List<string>(PendingAt.Keys);
            PendingAt.Clear();
            bool rescan = false, lang = false, theme = false, any = false;
            foreach (string p in changed)
            {
                WatcherChangeTypes kind = PendingKind[p];
                string ext = Path.GetExtension(p);
                bool structural = (kind & (WatcherChangeTypes.Created | WatcherChangeTypes.Deleted | WatcherChangeTypes.Renamed)) != 0;
                if ((ext == ".aui" || ext == ".svg") && structural) rescan = true;
                if (ext == ".json") { if (structural) rescan = true; else lang = true; }
                if (ext == ".auis" && AuiEditorEnv.IsThemePath(p)) theme = true;
                if (ext == ".svg") { AuiPrefabBuilder.ClearSpriteCache(); continue; }
                any = true;
            }
            PendingKind.Clear();
            if (rescan) AuiEditorEnv.InvalidateWorkspace("preview saw a file appear, vanish or rename");
            else { if (lang) AuiEditorEnv.InvalidateLanguages(); if (theme) AuiEditorEnv.InvalidateTheme(); }
            if (!any && !rescan) return;

            // Diff before the compile snapshots the new text, then follow the edit after it.
            var edits = new List<Edit>();
            foreach (string p in changed)
            {
                string ext = Path.GetExtension(p);
                if (ext != ".aui" && ext != ".auis") continue;
                string old;
                string text = SafeRead(p);
                if (text == null)
                {
                    // Locked or half-written: retry a few ticks, then let the compile report it.
                    int tries;
                    PendingTries.TryGetValue(p, out tries);
                    if (tries < 10) { PendingTries[p] = tries + 1; PendingAt[p] = now; PendingKind[p] = WatcherChangeTypes.Changed; }
                    else PendingTries.Remove(p);
                    continue;
                }
                PendingTries.Remove(p);
                if (LastText.TryGetValue(p, out old) && old != text)
                {
                    int first, last;
                    ChangedLines(old, text, out first, out last);
                    edits.Add(new Edit { Path = p, First = first, Last = last });
                }
            }
            if (!CompileNow()) return;
            if (FollowEdits && edits.Count > 0) Follow(edits);
            BuildNow();
        }

        // ---------------------------------------------------------------- follow the edit

        private struct Edit
        {
            public string Path;
            public int First, Last;   // 1-based lines in the NEW text
        }

        private static void ChangedLines(string oldText, string newText, out int first, out int last)
        {
            string[] a = oldText.Split('\n'), b = newText.Split('\n');
            int head = 0;
            while (head < a.Length && head < b.Length && a[head] == b[head]) head++;
            int tail = 0;
            while (tail < a.Length - head && tail < b.Length - head && a[a.Length - 1 - tail] == b[b.Length - 1 - tail]) tail++;
            first = head + 1;
            last = System.Math.Max(first, b.Length - tail);
        }

        /// <summary>Set page and popup overrides from the edited spans; true when something changed.</summary>
        private static bool Follow(List<Edit> edits)
        {
            AuiSettings settings = AuiEditorEnv.FindSettings();
            AuiWorkspace ws = AuiEditorEnv.Scan(settings);
            bool changed = false;
            foreach (Edit e in edits)
            {
                string ext = Path.GetExtension(e.Path);
                string stem = Path.GetFileNameWithoutExtension(e.Path);
                if (ext == ".aui")
                {
                    AuiCompileResult r = ResultFor(stem, ws, settings);
                    if (r == null || r.Doc == null) continue;
                    AuiNode n = DeepestNode(r.Doc, e.First, e.Last);
                    if (n != null && WalkUp(r, n)) changed = true;
                    if (Norm(r.FilePath) != Norm(ViewPath) && Host(LastResult, stem, ws, settings, 0)) changed = true;
                }
                else if (ext == ".auis")
                {
                    string text;
                    if (!LastText.TryGetValue(e.Path, out text)) continue;
                    AuiStyleSheet sheet = AuiStyleParser.Parse(text, e.Path, AuiStyleTier.Linked);
                    AuiStyleRule rule = null;
                    foreach (AuiStyleRule rr in sheet.Rules) if (rr.Line <= e.Last && (rule == null || rr.Line >= rule.Line)) rule = rr;
                    if (rule == null) continue;
                    // The sheet's view: the sibling .aui, else the previewed view.
                    string viewStem = ws.Views.ContainsKey(stem) ? stem : ViewName;
                    AuiCompileResult r = ResultFor(viewStem, ws, settings);
                    if (r == null || r.Doc == null) continue;
                    foreach (AuiNode n in r.Doc.Nodes)
                    {
                        bool match = false;
                        foreach (AuiSelector sel in rule.Selectors) if (sel.Matches(n.Tag, n.Classes)) { match = true; break; }
                        if (!match) continue;
                        if (WalkUp(r, n)) changed = true;
                        break;
                    }
                    if (viewStem != ViewName && Host(LastResult, viewStem, ws, settings, 0)) changed = true;
                }
            }
            if (changed) SaveOptions();
            return changed;
        }

        private static AuiCompileResult ResultFor(string stem, AuiWorkspace ws, AuiSettings settings)
        {
            if (LastResult != null && LastResult.ViewName == stem) return LastResult;
            string path;
            if (!ws.Views.TryGetValue(stem, out path)) return null;
            return AuiEditorEnv.Compile(Path.GetFullPath(path), settings, ws, null);   // a cache hit: the preview build compiled it
        }

        private static AuiNode DeepestNode(AuiMarkupDocument doc, int first, int last)
        {
            AuiNode best = null;
            foreach (AuiNode n in doc.Nodes)
            {
                if (n.Line > last || n.EndLine < first) continue;
                if (n.Line > first || n.EndLine < last) continue;
                if (best == null || n.Depth >= best.Depth) best = n;
            }
            return best;
        }

        /// <summary>Every page above the node becomes its group's shown page; an option opens its select. True when an override changed.</summary>
        private static bool WalkUp(AuiCompileResult r, AuiNode node)
        {
            bool changed = false;
            for (AuiNode p = node; p != null; p = p.Parent)
            {
                if (p.Tag == "page" && p.Id != null)
                {
                    foreach (AuiPageGroup g in r.Info.PageGroups)
                    {
                        if (!g.Pages.Contains(p) || g.Id == null) continue;
                        string key = r.ViewName + "/" + g.Id;
                        string cur;
                        bool isDefault = g.IndexOfPage(p.Id) == g.DefaultIndex;
                        if (isDefault) { if (Options.Pages.Remove(key)) changed = true; }
                        else if (!Options.Pages.TryGetValue(key, out cur) || cur != p.Id) { Options.Pages[key] = p.Id; changed = true; }
                    }
                }
                else if (p.Tag == "option" && p.Parent != null && p.Parent.Tag == "select")
                {
                    if (Options.OpenSelects.Add(r.ViewName + "/" + p.Parent.Index)) changed = true;
                }
                else if (p.Tag == "modal")
                {
                    if (Options.OpenModals.Add(r.ViewName + "/" + p.Index)) changed = true;
                }
            }
            return changed;
        }

        /// <summary>Show whatever hosts the nested view <paramref name="stem"/> inside <paramref name="r"/>, at any depth.</summary>
        private static bool Host(AuiCompileResult r, string stem, AuiWorkspace ws, AuiSettings settings, int depth)
        {
            if (r == null || r.Doc == null || depth > 8) return false;
            bool found = false;
            foreach (AuiNode n in r.Doc.Nodes)
            {
                if (!n.IsComponentTag) continue;
                if (n.Tag == stem) { WalkUp(r, n); found = true; }
                else
                {
                    string cp;
                    if (!ws.Views.TryGetValue(n.Tag, out cp)) continue;
                    AuiCompileResult cr = AuiEditorEnv.Compile(Path.GetFullPath(cp), settings, ws, null);
                    if (Host(cr, stem, ws, settings, depth + 1)) { WalkUp(r, n); found = true; }
                }
            }
            return found;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>The .aui behind the current selection (a scene instance or a .aui asset), or null.</summary>
        public static string PathFromSelection()
        {
            Object o = Selection.activeObject;
            var go = o as GameObject;
            if (go != null)
            {
                AuiView v = go.GetComponentInParent<AuiView>();
                if (v == null) return null;
                var script = MonoScript.FromMonoBehaviour(v);
                if (script == null) return null;
                string aui = Path.ChangeExtension(AssetDatabase.GetAssetPath(script), ".aui");
                return File.Exists(aui) ? Path.GetFullPath(aui) : null;
            }
            if (o is AuiViewAsset)
            {
                string p = AssetDatabase.GetAssetPath(o);
                return p.EndsWith(".aui") ? Path.GetFullPath(p) : null;
            }
            return null;
        }

        /// <summary>Every page group the preview can switch (view name, group): the view's own and its nested views', in tree order.</summary>
        public static readonly List<KeyValuePair<string, AuiPageGroup>> LastPageGroups = new List<KeyValuePair<string, AuiPageGroup>>();

        /// <summary>Every select whose popup the preview can open, as (view name, select node).</summary>
        public static readonly List<KeyValuePair<string, AuiNode>> LastSelects = new List<KeyValuePair<string, AuiNode>>();

        /// <summary>Every modal the preview can show open, as (view name, modal node).</summary>
        public static readonly List<KeyValuePair<string, AuiNode>> LastModals = new List<KeyValuePair<string, AuiNode>>();

        private static void Collect(AuiCompileResult r, List<KeyValuePair<string, AuiPageGroup>> groups, List<KeyValuePair<string, AuiNode>> selects, AuiSettings settings, AuiWorkspace ws, int depth)
        {
            if (r == null || r.Info == null || depth > 8) return;
            foreach (AuiPageGroup g in r.Info.PageGroups) if (g.Id != null) groups.Add(new KeyValuePair<string, AuiPageGroup>(r.ViewName, g));
            foreach (AuiSelectInfo s in r.Info.Selects) selects.Add(new KeyValuePair<string, AuiNode>(r.ViewName, s.Node));
            foreach (AuiModalInfo m in r.Info.Modals) LastModals.Add(new KeyValuePair<string, AuiNode>(r.ViewName, m.Node));
            foreach (AuiNestedView nv in r.Info.NestedViews)
            {
                string cp;
                if (!ws.Views.TryGetValue(nv.ViewName, out cp)) continue;
                Collect(AuiEditorEnv.Compile(Path.GetFullPath(cp), settings, ws, null), groups, selects, settings, ws, depth + 1);
            }
        }

        private static string Norm(string p)
        {
            return Path.GetFullPath(p).Replace('\\', '/').ToLowerInvariant();
        }

        private static string SafeRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string t = File.ReadAllText(path);
                return t.Length == 0 ? null : t;
            }
            catch (IOException) { return null; }
        }

        private static void Raise()
        {
            if (Changed != null) Changed();
        }
    }
}
