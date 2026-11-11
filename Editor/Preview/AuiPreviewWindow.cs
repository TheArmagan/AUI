using System.Collections.Generic;
using System.IO;
using System.Linq;
using Armagan.AUI.Core;
using UnityEditor;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// The control panel for <see cref="AuiPreview"/>: which view, live or not, which language,
    /// which page of each group and which popups are open, the diagnostics of the last compile,
    /// and the two buttons that reach the real pipeline (Build prefab, Auto-build).
    /// </summary>
    public sealed class AuiPreviewWindow : EditorWindow
    {
        private const string FollowSelectionKey = "AUI.Preview.FollowSelection";
        private Vector2 _scroll;
        private string[] _viewNames;
        private string[] _viewPaths;
        private AuiLanguageTable _table;
        private double _viewsScannedAt = -1;

        [MenuItem("AUI/Preview", false, 0)]
        public static void Open()
        {
            var w = GetWindow<AuiPreviewWindow>("AUI Preview");
            w.minSize = new Vector2(280, 200);
            w.Show();
        }

        private void OnEnable()
        {
            AuiPreview.Changed += Repaint;
            Selection.selectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            AuiPreview.Changed -= Repaint;
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            if (!SessionState.GetBool(FollowSelectionKey, false)) return;
            string p = AuiPreview.PathFromSelection();
            if (p != null && (AuiPreview.ViewPath == null || Path.GetFullPath(AuiPreview.ViewPath) != p)) AuiPreview.Start(p);
        }

        private void RefreshViews()
        {
            if (_viewNames != null && EditorApplication.timeSinceStartup - _viewsScannedAt < 2.0) return;
            AuiSettings settings = AuiEditorEnv.FindSettings();
            AuiWorkspace ws = AuiEditorEnv.Scan(settings);
            try { _table = AuiEditorEnv.LanguageTable(settings, ws, null); } catch { _table = null; }
            var names = new List<string>(ws.Views.Keys);
            names.Sort(string.CompareOrdinal);
            _viewNames = names.ToArray();
            _viewPaths = new string[_viewNames.Length];
            for (int i = 0; i < _viewNames.Length; i++) _viewPaths[i] = Path.GetFullPath(ws.Views[_viewNames[i]]);
            _viewsScannedAt = EditorApplication.timeSinceStartup;
        }

        private void OnGUI()
        {
            RefreshViews();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // ---- which view
            int current = -1;
            if (AuiPreview.ViewPath != null)
                for (int i = 0; i < _viewPaths.Length; i++)
                    if (string.Equals(_viewPaths[i], Path.GetFullPath(AuiPreview.ViewPath), System.StringComparison.OrdinalIgnoreCase)) current = i;
            using (new EditorGUILayout.HorizontalScope())
            {
                int pick = EditorGUILayout.Popup("View", current, _viewNames);
                if (pick != current && pick >= 0) AuiPreview.Start(_viewPaths[pick]);
                bool follow = GUILayout.Toggle(SessionState.GetBool(FollowSelectionKey, false), "Follow selection", "Button", GUILayout.Width(110));
                if (follow != SessionState.GetBool(FollowSelectionKey, false)) { SessionState.SetBool(FollowSelectionKey, follow); if (follow) OnSelectionChanged(); }
            }

            // ---- live, language, actions
            using (new EditorGUILayout.HorizontalScope())
            {
                bool live = GUILayout.Toggle(AuiPreview.Live, AuiPreview.Live ? "Live" : "Off", "Button", GUILayout.Width(60));
                if (live != AuiPreview.Live)
                {
                    if (live) { if (AuiPreview.ViewPath != null) AuiPreview.Start(AuiPreview.ViewPath); else if (_viewPaths.Length > 0) AuiPreview.Start(_viewPaths[0]); }
                    else AuiPreview.Stop();
                }
                using (new EditorGUI.DisabledScope(!AuiPreview.Live))
                {
                    if (GUILayout.Button("Rebuild now")) AuiPreview.Rebuild();
                    if (GUILayout.Button("Frame")) AuiPreview.Frame();
                }
            }

            AuiLanguageTable table = _table;
            if (table != null && table.LanguageCount > 0)
            {
                string[] codes = table.Codes();
                string[] names = table.Names();
                var labels = new string[codes.Length + 1];
                labels[0] = "Default (" + codes[table.DefaultLanguage] + ")";
                for (int i = 0; i < codes.Length; i++) labels[i + 1] = codes[i] + (string.IsNullOrEmpty(names[i]) ? "" : "  " + names[i]);
                int cur = 0;
                if (AuiPreview.LangCode != null) { int idx = table.IndexOfLanguage(AuiPreview.LangCode); if (idx >= 0) cur = idx + 1; }
                int pick = EditorGUILayout.Popup("Language", cur, labels);
                if (pick != cur) AuiPreview.LangCode = pick == 0 ? null : codes[pick - 1];
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                bool fe = GUILayout.Toggle(AuiPreview.FollowEdits, "Follow edits");
                if (fe != AuiPreview.FollowEdits) AuiPreview.FollowEdits = fe;
                bool pk = GUILayout.Toggle(AuiPreview.Pickable, "Pickable in scene");
                if (pk != AuiPreview.Pickable) AuiPreview.Pickable = pk;
            }

            // ---- pages and popups
            if (AuiPreview.Live && AuiPreview.LastResult != null)
            {
                // Copies: picking a page rebuilds the preview, which refills these lists mid-draw.
                var groups = AuiPreview.LastPageGroups.ToArray();
                var selects = AuiPreview.LastSelects.ToArray();
                var modals = AuiPreview.LastModals.ToArray();
                if (groups.Length > 0 || selects.Length > 0 || modals.Length > 0)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Shown", EditorStyles.boldLabel);
                }
                foreach (KeyValuePair<string, AuiPageGroup> kv in groups)
                {
                    AuiPageGroup g = kv.Value;
                    var labels = new string[g.Pages.Count + 1];
                    labels[0] = "default";
                    for (int i = 0; i < g.Pages.Count; i++) labels[i + 1] = g.Pages[i].Id ?? ("page " + i);
                    string cur = AuiPreview.PageFor(kv.Key, g.Id);
                    int curIdx = cur == null ? 0 : g.IndexOfPage(cur) + 1;
                    string label = (kv.Key == AuiPreview.ViewName ? "" : kv.Key + ".") + "pages#" + g.Id;
                    int pick = EditorGUILayout.Popup(label, curIdx, labels);
                    if (pick != curIdx) AuiPreview.SetPage(kv.Key, g.Id, pick == 0 ? null : g.Pages[pick - 1].Id);
                }
                foreach (KeyValuePair<string, AuiNode> kv in selects)
                {
                    AuiNode n = kv.Value;
                    string label = (kv.Key == AuiPreview.ViewName ? "" : kv.Key + ".") + "select" + (n.Id != null ? "#" + n.Id : " (line " + n.Line + ")") + " popup";
                    bool open = AuiPreview.IsSelectOpen(kv.Key, n.Index);
                    bool pick = EditorGUILayout.Toggle(label, open);
                    if (pick != open) AuiPreview.SetSelectOpen(kv.Key, n.Index, pick);
                }
                foreach (KeyValuePair<string, AuiNode> kv in modals)
                {
                    AuiNode n = kv.Value;
                    string label = (kv.Key == AuiPreview.ViewName ? "" : kv.Key + ".") + "modal" + (n.Id != null ? "#" + n.Id : " (line " + n.Line + ")");
                    bool open = AuiPreview.IsModalOpen(kv.Key, n.Index);
                    bool pick = EditorGUILayout.Toggle(label, open);
                    if (pick != open) AuiPreview.SetModalOpen(kv.Key, n.Index, pick);
                }
            }

            // ---- the real pipeline
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Prefab", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(AuiPreview.ViewPath == null))
                {
                    if (GUILayout.Button("Build prefab"))
                    {
                        string ap = AuiEditorEnv.AssetPath(AuiPreview.ViewPath);
                        if (ap != null) AuiBuildQueue.RebuildOne(ap);
                    }
                }
                bool auto = GUILayout.Toggle(AuiBuildQueue.AutoBuild, "Auto-build prefabs");
                if (auto != AuiBuildQueue.AutoBuild) AuiBuildQueue.AutoBuild = auto;
            }
            if (!AuiBuildQueue.AutoBuild)
            {
                int pending = AuiBuildQueue.Pending().Count;
                EditorGUILayout.HelpBox("Auto-build is off: saved views are queued" + (pending > 0 ? " (" + pending + " waiting)" : "") + ". Build prefab, Rebuild All Views or turning auto-build on drains them.", MessageType.Info);
            }

            // ---- status and diagnostics
            EditorGUILayout.Space(6);
            if (AuiPreview.LastResult != null)
            {
                string status = "compiled " + AuiPreview.LastBuilt.ToString("HH:mm:ss") + ", " + AuiPreview.LastCompileMs + " ms compile, " + AuiPreview.LastBuildMs + " ms build";
                EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
            }
            List<AuiDiagnostic> diags = AuiPreview.LastDiagnostics;
            if (diags != null && diags.Count > 0)
            {
                EditorGUILayout.LabelField("Diagnostics", EditorStyles.boldLabel);
                foreach (AuiDiagnostic d in diags)
                {
                    MessageType mt = d.Severity == AuiSeverity.Error ? MessageType.Error : d.Severity == AuiSeverity.Warning ? MessageType.Warning : MessageType.Info;
                    string where = string.IsNullOrEmpty(d.File) ? "" : Path.GetFileName(d.File) + (d.Line > 0 ? ":" + d.Line : "") + "  ";
                    EditorGUILayout.HelpBox(where + d.Code + ": " + d.Message, mt);
                    Rect r = GUILayoutUtility.GetLastRect();
                    if (!string.IsNullOrEmpty(d.File) && Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                    {
                        OpenAt(d.File, d.Line);
                        Event.current.Use();
                    }
                }
            }
            else if (AuiPreview.LastResult != null) EditorGUILayout.LabelField("No diagnostics.", EditorStyles.miniLabel);

            EditorGUILayout.EndScrollView();
        }

        private static void OpenAt(string file, int line)
        {
            string ap = AuiEditorEnv.AssetPath(file);
            Object asset = ap != null ? AssetDatabase.LoadMainAssetAtPath(ap) : null;
            if (asset != null && AssetDatabase.OpenAsset(asset, System.Math.Max(1, line))) return;
            if (File.Exists(file)) EditorUtility.OpenWithDefaultApp(file);
        }
    }
}
