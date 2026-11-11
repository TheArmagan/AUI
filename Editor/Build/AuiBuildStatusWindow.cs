using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// AUI/Build Status: what the build queue holds and why, and what the last drain did with each
    /// view and where its time went. Everything shown comes from SessionState (the queue, its
    /// attempts and reasons, and the drain record), so it survives the domain reloads a build goes
    /// through.
    /// </summary>
    public sealed class AuiBuildStatusWindow : EditorWindow
    {
        private Vector2 _scroll;
        private bool _showPhases = true;

        public static void Open()
        {
            var w = GetWindow<AuiBuildStatusWindow>();
            w.titleContent = new GUIContent("AUI Build");
            w.minSize = new Vector2(520, 240);
            w.Show();
        }

        private void OnEnable()
        {
            AuiBuildQueue.Drained += Repaint;
        }

        private void OnDisable()
        {
            AuiBuildQueue.Drained -= Repaint;
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(new GUIContent("Rebuild All", "Only views whose prefab is out of date, and the views nesting them."), EditorStyles.toolbarButton)) AuiMenu.RebuildAll();
                if (GUILayout.Button(new GUIContent("Force Rebuild All", "Every view, even when its bake hash matches."), EditorStyles.toolbarButton)) AuiMenu.ForceRebuildAll();
                using (new EditorGUI.DisabledScope(AuiMenu.SelectedViews().Count == 0))
                    if (GUILayout.Button(new GUIContent("Rebuild Selected", "The views the Project or Hierarchy selection stands for."), EditorStyles.toolbarButton)) AuiMenu.RebuildSelected();
                GUILayout.FlexibleSpace();
                bool auto = GUILayout.Toggle(AuiBuildQueue.AutoBuild, "Auto-build", EditorStyles.toolbarButton);
                if (auto != AuiBuildQueue.AutoBuild) AuiBuildQueue.AutoBuild = auto;
                _showPhases = GUILayout.Toggle(_showPhases, "Phases", EditorStyles.toolbarButton);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawQueue();
            EditorGUILayout.Space(8);
            DrawLastDrain();
            EditorGUILayout.EndScrollView();
        }

        private void DrawQueue()
        {
            List<string> pending = AuiBuildQueue.Pending();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Queue (" + pending.Count + ")", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(pending.Count == 0))
                {
                    if (GUILayout.Button("Drain now", GUILayout.Width(80))) AuiBuildQueue.DrainNow();
                    if (GUILayout.Button("Clear", GUILayout.Width(60))) AuiBuildQueue.ClearQueue();
                }
            }
            if (pending.Count == 0)
            {
                EditorGUILayout.LabelField("Empty.", EditorStyles.miniLabel);
                return;
            }
            if (!AuiBuildQueue.AutoBuild) EditorGUILayout.HelpBox("Auto-build is off: saved views wait here until an explicit rebuild.", MessageType.Info);
            foreach (string p in pending)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string label = Path.GetFileNameWithoutExtension(p);
                    if (AuiBuildQueue.IsForced(p)) label += "  (forced)";
                    int attempts = AuiBuildQueue.Attempts(p);
                    if (attempts > 0) label += "  attempt " + attempts;
                    EditorGUILayout.LabelField(new GUIContent(label, p), GUILayout.MinWidth(160));
                    EditorGUILayout.LabelField(AuiBuildQueue.Reason(p) ?? "queued", EditorStyles.miniLabel);
                    if (GUILayout.Button("Open", GUILayout.Width(48))) OpenAui(p);
                }
            }
        }

        private void DrawLastDrain()
        {
            AuiDrainRecord d = AuiDrainRecord.Load();
            EditorGUILayout.LabelField("Last drain", EditorStyles.boldLabel);
            if (d == null)
            {
                EditorGUILayout.LabelField("Nothing has drained in this editor session yet.", EditorStyles.miniLabel);
                return;
            }
            EditorGUILayout.LabelField(d.time + ": " + d.views + " view(s), " + d.built + " built" + (d.sourcesWritten ? ", sources written (continues after the reload)" : "") + ", " + d.totalMs + " ms", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("scan " + d.scanMs + ", compile " + d.compileMs + " (" + d.cached + " cached), udonsharp " + d.usharpMs + ", build " + d.buildMs + " (batched import " + d.importMs + "), language table " + d.langMs + ", hooks " + d.hooksMs + " ms", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);
            foreach (AuiViewBuildRecord r in d.records)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Color prev = GUI.color;
                    GUI.color = StatusColor(r.status);
                    EditorGUILayout.LabelField(r.status ?? "?", GUILayout.Width(64));
                    GUI.color = prev;
                    string name = (r.view ?? Path.GetFileNameWithoutExtension(r.path)) + (r.forced ? " (forced)" : "");
                    EditorGUILayout.LabelField(new GUIContent(name, r.path), GUILayout.MinWidth(160));
                    string detail = r.status == AuiViewBuildRecord.Built ? r.totalMs + " ms, " + r.elements + " elements" : (r.reason ?? "");
                    EditorGUILayout.LabelField(detail, EditorStyles.miniLabel, GUILayout.MinWidth(120));
                    if (GUILayout.Button("Rebuild", GUILayout.Width(60))) AuiBuildQueue.RebuildOne(r.path);
                    if (GUILayout.Button("Prefab", GUILayout.Width(52))) PingPrefab(r.path);
                    if (GUILayout.Button("Open", GUILayout.Width(48))) OpenAui(r.path);
                }
                if (_showPhases && r.status == AuiViewBuildRecord.Built)
                {
                    using (new EditorGUI.IndentLevelScope(4))
                        EditorGUILayout.LabelField(r.Phases() + " ms", EditorStyles.miniLabel);
                }
            }
        }

        private static Color StatusColor(string status)
        {
            switch (status)
            {
                case AuiViewBuildRecord.Built: return new Color(0.55f, 0.9f, 0.55f);
                case AuiViewBuildRecord.Skipped: return new Color(0.75f, 0.75f, 0.75f);
                case AuiViewBuildRecord.Waiting: return new Color(1f, 0.85f, 0.4f);
                default: return new Color(1f, 0.5f, 0.45f);
            }
        }

        private static void OpenAui(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) EditorUtility.OpenWithDefaultApp(path);
        }

        private static void PingPrefab(string auiPath)
        {
            if (string.IsNullOrEmpty(auiPath)) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path.ChangeExtension(auiPath, ".prefab"));
            if (prefab == null) return;
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
        }
    }
}
