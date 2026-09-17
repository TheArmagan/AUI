using System.Collections.Generic;
using System.IO;
using Armagan.AUI.Core;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRC.SDK3.Components;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// Turns a compiled view into Foo.prefab: a world-space canvas whose children are plain
    /// uGUI objects at the rectangles the layout solver produced, with the generated view
    /// class on the root and one AuiSignal per interactive element, every table on the view
    /// filled to the index map's element count.
    ///
    /// BUILT IN THE OPEN SCENE, THEN SAVED OUT. UdonSharpUndo.AddComponent (the only way to add
    /// a U# component that gets a program) works against scene objects, so the tree is built
    /// under a temporary root, saved with PrefabUtility.SaveAsPrefabAsset and destroyed.
    ///
    /// THE WALK ORDER IS THE INDEX MAP'S. Every element index written into a table comes from
    /// AuiIndexMap.Elem(node, row); the builder never counts on its own, so the generated
    /// constants and the baked arrays can only agree.
    ///
    /// A PREFAB CANNOT REFERENCE SCENE OBJECTS: lang and inputPanel stay null here and are found
    /// by name at runtime (AuiView._AuiStart).
    /// </summary>
    public static class AuiPrefabBuilder
    {
        private const float CanvasScale = 0.001f;
        private const float IconSize = 18f;

        /// <summary>Bumped whenever the builder's output changes for the same input, so existing prefabs rebuild.</summary>
        // 4: Middle vertical alignment (TMP's Midline moves with descenders: "Language" sat 2 mm above "Audio").
        // 5: labels no longer inset vertically (TMP renders nothing in a rect shorter than one line).
        // 6: circular slider thumb.
        // 8: rebuilds inside the existing prefab contents so scene overrides survive.
        // 12: runtime layout tables; every element placed with a centre pivot (no text widen/fitter hacks).
        // 13: cascade tables replace the per-signal style rows.
        // 16: <select max="N"> finally bakes its reserved rows, and a popup resizes to the rows
        //     that are showing (selectOptionShown / selectOptionH / selectPopupUp / selectPopupEdge).
        // 17: every select row is baked at the tallest row's height, so the well the runtime
        //     resizes to count * selectOptionH ends where the last row does.
        // 18: <text max-lines>, <text runtime>, <input maxlength> (elemInputMax) finally do something;
        //     the compile hash covers the rule table and the per-language widths.
        // 19: <input type="url"> (elemInputType): armed on the panel's VRCUrlInputField.
        // 20: <rawimage> (elemRaw): a RawImage outside the cascade's elemBg.
        // 21: <view attach>: an AuiAttach on the root (AuiView.attach), kept across rebuilds like the view itself.
        // 22: <icon size> reaches the runtime layout tables; it used to bake right and be re-solved to the sheet's 18.
        public const int BuilderVersion = 22;

        public static string BakeStamp(AuiCompileResult r)
        {
            return r.Hash + ":" + BuilderVersion;
        }

        private sealed class Ctx
        {
            public AuiCompileResult R;
            public AuiSettings Settings;
            public AuiWorkspace Ws;
            public AuiLanguageTable Lang;
            public AuiFontSet Fonts;
            public AuiView View;
            public AuiAttach Attach;
            public int N;
            public GameObject[] Objects;
            public RectTransform[] Rects;
            public CanvasGroup[] Groups;
            public Image[] Bg, Border, Icon;
            public TextMeshProUGUI[] Text;
            public Button[] Buttons;
            public Slider[] Sliders;
            public RawImage[] Raw;
            public Vector2[] BasePos;
            public int[] Signal, Key, Kind;
            public AuiSignal[] Signals;
            public int[] SigElem;
            public AuiView[] ChildView;
            public List<AuiNestedView> Nested = new List<AuiNestedView>();
            public Dictionary<AuiNestedView, AuiView> NestedInstances = new Dictionary<AuiNestedView, AuiView>();
            public Transform Content;
            public RectTransform ContentRect;
            public List<GameObject> Popups = new List<GameObject>();

            // Per SELECT NODE index, so SetSelectOptionCount can resize a popup at runtime:
            // one option row's height, whether the popup opens upward, and the edge that stays
            // put while it grows (its top when it hangs below the header, its bottom when up).
            public float[] SelectRowH;
            public bool[] SelectUp;
            public float[] SelectEdge;

            // PREVIEW MODE: the same tree with nothing that needs UdonSharp. No view component,
            // no signals, no tables, no prefab; nested views are compiled from source and built
            // inline; hidden pages and popups can be forced visible; texts use a chosen language.
            public bool Preview;
            public bool PreviewNested;
            public AuiPreviewOptions Opt;
            public int Depth;
        }

        /// <summary>What the live preview wants shown that the prefab hides at rest.</summary>
        public sealed class AuiPreviewOptions
        {
            /// <summary>Language code for every baked text; null = the table's default.</summary>
            public string LangCode;
            /// <summary>"ViewName/groupId" -> page id to show instead of the group's default.</summary>
            public readonly Dictionary<string, string> Pages = new Dictionary<string, string>();
            /// <summary>"ViewName/nodeIndex" of every select whose popup is left open.</summary>
            public readonly HashSet<string> OpenSelects = new HashSet<string>();
            /// <summary>"ViewName/nodeIndex" of every modal shown open.</summary>
            public readonly HashSet<string> OpenModals = new HashSet<string>();
        }

        // ================================================================ entry

        /// <summary>
        /// Build the view as plain uGUI under <paramref name="root"/> (a RectTransform object the
        /// caller owns), from the compile result alone. Nothing here touches Undo, the prefab or
        /// UdonSharp, so it runs at typing speed and never dirties the scene.
        /// </summary>
        public static void BuildPreview(AuiCompileResult r, AuiSettings settings, AuiWorkspace ws, AuiPreviewOptions opt, GameObject root)
        {
            BuildPreviewTree(r, settings, ws, opt ?? new AuiPreviewOptions(), root, false, 0);
        }

        private static void BuildPreviewTree(AuiCompileResult r, AuiSettings settings, AuiWorkspace ws, AuiPreviewOptions opt, GameObject root, bool nested, int depth)
        {
            var c = new Ctx { R = r, Settings = settings, Ws = ws, Fonts = settings != null ? settings.fontSet : null, Preview = true, PreviewNested = nested, Opt = opt, Depth = depth };
            c.Lang = AuiEditorEnv.LanguageTable(settings, ws, null);
            c.N = r.Map.ElementCount;
            Alloc(c);
            BuildTree(c, null, root);
        }

        public static bool Build(AuiCompileResult r, AuiSettings settings, AuiWorkspace ws, AuiView[] sceneViews = null)
        {
            string dir = Path.GetDirectoryName(r.FilePath) ?? "";
            string csAbs = Path.Combine(dir, r.ViewName + ".cs");
            string cs = AuiEditorEnv.AssetPath(csAbs);
            string prefabPath = AuiEditorEnv.AssetPath(Path.Combine(dir, r.ViewName + ".prefab"));
            if (cs == null || prefabPath == null) { AuiLog.Error(r.FilePath + " is outside the project; cannot build."); return true; }

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(cs);
            System.Type type = script != null ? script.GetClass() : null;
            if (type == null || !typeof(AuiView).IsAssignableFrom(type))
            {
                // Not compiled yet (first pass after the source was written) or a compile error in the author's file.
                AuiLog.Warn(AuiCodes.GeneratedClassMissing + " " + r.ViewName + ": class not available yet (" + cs + "); will retry after the next compile.");
                return false;
            }
            // The queue creates program assets and runs UdonSharp once for every view in a drain;
            // this is only the safety net for a direct Build call.
            AuiProgramAssets.Ensure(cs);
            UdonSharpProgramAsset program = AuiProgramAssets.Find(cs);
            if (program != null && (program.SerializedProgramAsset == null || program.SerializedProgramAsset.RetrieveProgram() == null))
                UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync(new UdonSharp.Compiler.UdonSharpCompileOptions());
            program = AuiProgramAssets.Find(cs);
            if (program == null || program.SerializedProgramAsset == null || program.SerializedProgramAsset.RetrieveProgram() == null)
            {
                AuiLog.Error(AuiCodes.ProgramAssetMissing + " " + r.ViewName + ": UdonSharp did not produce a program for " + cs + ". Fix the U# errors above (a missing handler method is the usual cause).");
                return false;
            }

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
            {
                var ev = existing.GetComponent<AuiView>();
                if (ev != null && ev.auiBakeHash == BakeStamp(r) && !AuiBuildQueue.Forced) return true;
            }

            var c = new Ctx { R = r, Settings = settings, Ws = ws, Fonts = settings != null ? settings.fontSet : null };
            // The cached table, the one the view was compiled against; reading the files again cost a table per view.
            c.Lang = AuiEditorEnv.LanguageTable(settings, ws, null);
            c.N = r.Map.ElementCount;
            Alloc(c);

            // REBUILD INSIDE THE EXISTING PREFAB, NEVER AS A FRESH FILE. A fresh save gives every object
            // a new internal id, and a scene instance's overrides (its position, the references you set
            // on the view) point at the old ids: they silently fall back to the prefab. Loading the
            // prefab's contents, stripping everything under the root and rebuilding there keeps the ids
            // of the root, its RectTransform and the view component, which is where overrides live.
            bool reuse = existing != null;
            GameObject root = null;
            try
            {
                if (reuse)
                {
                    root = PrefabUtility.LoadPrefabContents(prefabPath);
                    StripRoot(root, type);
                }
                root = BuildTree(c, type, root);
                FillTables(c);
                Finish(c, root);
                AuditComponents(root);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(prefabPath)) ?? ".");
                if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null) { AuiLog.Error(r.ViewName + ": prefab failed to save at " + prefabPath); return false; }
                RevertInstanceSizeOverrides(prefabPath, sceneViews ?? Object.FindObjectsOfType<AuiView>(true));
                AuiLog.Info("built " + prefabPath + " (" + c.N + " elements, " + r.Map.SignalCount + " signals, " + r.Info.Keys.Count + " keys)");
                return true;
            }
            catch (System.Exception e)
            {
                AuiLog.Error(AuiCodes.PrefabBuildFailed + " " + r.ViewName + ": build failed: " + e);
                return false;
            }
            finally
            {
                if (root != null)
                {
                    if (reuse) PrefabUtility.UnloadPrefabContents(root);
                    else Object.DestroyImmediate(root);
                }
            }
        }

        /// <summary>
        /// THE VIEW'S SIZE IS THE MARKUP'S, NEVER AN INSTANCE'S. Because the root keeps its ids across
        /// rebuilds, a scene instance can carry an override of the root RectTransform's size (Unity
        /// records one the moment the rect is touched in the scene) and the collider's, and then a
        /// view that grew in markup keeps its old card while its content runs past the bottom.
        /// Position, rotation, scale and author references stay as the instance set them.
        /// </summary>
        private static void RevertInstanceSizeOverrides(string prefabPath, AuiView[] sceneViews)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null) return;
            foreach (AuiView v in sceneViews)
            {
                if (v == null) continue;
                GameObject inst = v.gameObject;
                // Only instances placed in a scene: a view nested inside another view's prefab is
                // sized by its parent's build, and that size lives as an override on purpose.
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(inst) || PrefabUtility.GetCorrespondingObjectFromSource(inst) != asset) continue;
                var rt = inst.GetComponent<RectTransform>();
                if (rt != null)
                {
                    var so = new SerializedObject(rt);
                    // ONLY THE SIZE. A root RectTransform's anchored position IS its scene position;
                    // reverting it moved every placed view back to the origin.
                    PrefabUtility.RevertPropertyOverride(so.FindProperty("m_SizeDelta"), InteractionMode.AutomatedAction);
                }
                var bc = inst.GetComponent<BoxCollider>();
                if (bc != null)
                {
                    var so = new SerializedObject(bc);
                    PrefabUtility.RevertPropertyOverride(so.FindProperty("m_Size"), InteractionMode.AutomatedAction);
                    PrefabUtility.RevertPropertyOverride(so.FindProperty("m_Center"), InteractionMode.AutomatedAction);
                }
            }
        }

        /// <summary>Remove every child and every component the builder does not own on the root, keeping what carries scene overrides.</summary>
        private static void StripRoot(GameObject root, System.Type viewType)
        {
            for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            foreach (Component comp in root.GetComponents<Component>())
            {
                if (comp == null) continue;
                System.Type t = comp.GetType();
                if (t == typeof(RectTransform) || t == typeof(Transform) || t == typeof(Canvas) || t == typeof(GraphicRaycaster) ||
                    t == typeof(VRCUiShape) || t == typeof(BoxCollider) || t == viewType || t == typeof(VRC.Udon.UdonBehaviour) ||
                    // AuiAttach carries hand-tuned offsets and angles as instance overrides, the same reason the view is kept.
                    // BuildTree removes it (and its backing UdonBehaviour) when the markup no longer asks for one.
                    t == typeof(AuiAttach)) continue;
                Object.DestroyImmediate(comp);
            }
        }

        private static void Alloc(Ctx c)
        {
            int n = c.N;
            c.Objects = new GameObject[n];
            c.Rects = new RectTransform[n];
            c.Groups = new CanvasGroup[n];
            c.Bg = new Image[n];
            c.Border = new Image[n];
            c.Icon = new Image[n];
            c.Text = new TextMeshProUGUI[n];
            c.Buttons = new Button[n];
            c.Sliders = new Slider[n];
            c.Raw = new RawImage[n];
            c.BasePos = new Vector2[n];
            c.Signal = new int[n];
            c.Key = new int[n];
            c.Kind = new int[n];
            c.ChildView = new AuiView[n];
            for (int i = 0; i < n; i++) { c.Signal[i] = c.R.Map.SignalOf[i]; c.Key[i] = -1; c.Kind[i] = c.R.Map.KindOf[i]; }
            // Indexed by DOC NODE, not element: BuildSelect fills them and the table pass reads
            // them back per select.
            int nodes = c.R.Doc.Nodes.Count;
            c.SelectRowH = new float[nodes];
            c.SelectUp = new bool[nodes];
            c.SelectEdge = new float[nodes];
            c.Signals = new AuiSignal[c.R.Map.SignalCount];
            c.SigElem = new int[c.R.Map.SignalCount];
            for (int e = 0; e < n; e++) if (c.Signal[e] >= 0) c.SigElem[c.Signal[e]] = e;
        }

        // ================================================================ tree

        private static T Ensure<T>(GameObject go) where T : Component
        {
            var comp = go.GetComponent<T>();
            return comp != null ? comp : go.AddComponent<T>();
        }

        private static GameObject BuildTree(Ctx c, System.Type type, GameObject reuse)
        {
            AuiCompileResult r = c.R;
            float W = r.Info.Width, H = r.Info.Height;
            GameObject root = reuse;
            if (root == null)
            {
                root = new GameObject(r.ViewName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(root, "Build AUI view " + r.ViewName);
            }
            if (!c.Preview) root.name = r.ViewName;   // the preview names its own root
            var rrt = (RectTransform)root.transform;
            rrt.sizeDelta = new Vector2(W, H);
            if (reuse == null) rrt.localScale = Vector3.one * CanvasScale;
            if (!c.PreviewNested)
            {
                var canvas = Ensure<Canvas>(root);
                canvas.renderMode = RenderMode.WorldSpace;
            }
            // 1 unit = 1 mm everywhere. uGUI divides a sprite's PPU by the canvas reference (100)
            // when slicing, so the package sprites are imported at 100 PPU; setting the reference to
            // 1 here was measured NOT to survive into the saved prefab.
            if (!c.Preview)
            {
                Ensure<GraphicRaycaster>(root);
                // Without VRCUiShape the laser passes straight through and nothing can be pressed, with no symptom in the editor.
                Ensure<VRCUiShape>(root);
                var bc = Ensure<BoxCollider>(root);
                bc.isTrigger = true;
                bc.center = Vector3.zero;
                bc.size = new Vector3(W, H, 10f);

                c.View = root.GetComponent(type) as AuiView;
                if (c.View == null) c.View = (AuiView)UdonSharpUndo.AddComponent(root, type);

                // <view attach>. Present at all (attach="none" included) means the component exists, so
                // SetAttach has something to drive. UdonSharpEditorUtility.DestroyImmediate, NOT
                // Object.DestroyImmediate: the latter leaves the backing UdonBehaviour on the root,
                // running a program with no proxy.
                var attach = root.GetComponent<AuiAttach>();
                if (r.Info.HasAttach && attach == null) attach = (AuiAttach)UdonSharpUndo.AddComponent(root, typeof(AuiAttach));
                else if (!r.Info.HasAttach && attach != null) { UdonSharpEditorUtility.DestroyImmediate(attach); attach = null; }
                c.Attach = attach;
                // A display-only attached view never takes a laser: an enabled trigger strapped to the
                // player's arm swallows every click aimed past it.
                bc.enabled = attach == null || r.Map.SignalCount > 0;
            }

            // Content: what Show/Hide fades and deactivates. Element 0 (the <view>) lives on it.
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one; crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            crt.localScale = Vector3.one;
            var cg = content.AddComponent<CanvasGroup>();
            c.Content = content.transform;
            c.ContentRect = crt;
            if (c.View != null)
            {
                c.View.content = content;
                c.View.contentGroup = cg;
                c.View.contentRect = crt;
            }

            AuiNode rootNode = r.Doc.Root;
            int e0 = r.Map.Elem(rootNode, 0);
            Register(c, e0, content, crt, new Vector2(0, 0));
            c.Groups[e0] = cg;
            ApplyBackground(c, rootNode, e0, content.transform, false);
            foreach (AuiNode child in rootNode.Children) BuildNode(c, child, content.transform, 0, rootNode);
            foreach (GameObject popup in c.Popups) popup.transform.SetAsLastSibling();
            return root;
        }

        private static void Register(Ctx c, int e, GameObject go, RectTransform rt, Vector2 basePos)
        {
            c.Objects[e] = go;
            c.Rects[e] = rt;
            c.BasePos[e] = basePos;
        }

        /// <summary>Create the objects of one node (and its subtree) for one repeat row. Mirrors AuiIndexMap.Walk.</summary>
        private static void BuildNode(Ctx c, AuiNode n, Transform parent, int row, AuiNode parentNode)
        {
            if (n.Tag == "repeat" || n.Tag == "template") { BuildRepeat(c, n, parent); return; }
            if (n.IsComponentTag) { BuildNested(c, n, parent, row); return; }

            int e = c.R.Map.Elem(n, row);
            AuiLayoutNode ln = c.R.Layout[n.Index];
            AuiResolvedStyle s = c.R.Styles[n.Index];
            AuiRect local = ln.Local;
            if (row > 0 && c.R.Map.Stride[n.Index] > 0 && n.Parent != null && n.Parent.Tag == "repeat")
            {
                AuiLayoutNode rl = c.R.Layout[n.Parent.Index];
                if (rl.Instances != null && row < rl.Instances.Count)
                {
                    AuiRect r0 = rl.Instances[0], ri = rl.Instances[row];
                    local = new AuiRect(local.X + ri.X - r0.X, local.Y + ri.Y - r0.Y, ri.W, ri.H);
                }
            }

            // A modal's backdrop is its own element, baked just before the body so the body draws over it.
            GameObject scrimGo = n.Tag == "modal" ? BuildModalScrim(c, n, parent, s) : null;

            var go = new GameObject(Name(n), typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            AuiLayoutNode pl = parentNode != null ? c.R.Layout[parentNode.Index] : null;
            float pw = pl != null ? pl.Local.W : c.R.Info.Width, ph = pl != null ? pl.Local.H : c.R.Info.Height;
            // Never stretch anchors: the runtime solver writes sizeDelta / anchoredPosition, which
            // only mean the same thing as the baked rect under top-left anchors and a centre pivot.
            Place(rt, local, false, pw, ph);
            Register(c, e, go, rt, rt.anchoredPosition);
            if (ln.Hidden || (n.HasAttribute("hidden") && n.Get("hidden") != "false")) go.SetActive(false);

            bool interactive = AuiIndexMap.KindOfTag(n.Tag) != AuiIndexMap.KIND_NONE || c.Signal[e] >= 0;
            switch (n.Tag)
            {
                case "text": BuildText(c, n, e, go, s, local, parentNode); break;
                case "icon": BuildIcon(c, n, e, go, s); break;
                case "image": BuildImage(c, n, e, go, s); break;
                case "rawimage": BuildRawImage(c, n, e, go, s); break;
                case "spacer": break;
                case "slider": BuildSlider(c, n, e, go, s, local); break;
                case "input": BuildInput(c, n, e, go, s, local); break;
                case "select": BuildSelect(c, n, e, go, s, local, row); return; // options handled inside
                case "page":
                {
                    ApplyBackground(c, n, e, go.transform, false);
                    c.Groups[e] = go.AddComponent<CanvasGroup>();
                    // Bake the default page's visibility so the prefab previews the way it starts.
                    AuiPageGroup grp = null;
                    foreach (AuiPageGroup pg in c.R.Info.PageGroups) if (pg.Pages.Contains(n)) grp = pg;
                    if (grp != null && grp.IndexOfPage(n.Id) != ShownPage(c, grp))
                    {
                        c.Groups[e].alpha = 0f;
                        c.Groups[e].blocksRaycasts = false;
                        c.Groups[e].interactable = false;
                        go.SetActive(false);
                    }
                    break;
                }
                case "modal":
                    // The body swallows clicks (raycast) so nothing behind it reacts; it fades with its own group.
                    ApplyBackground(c, n, e, go.transform, true, s);
                    c.Groups[e] = go.AddComponent<CanvasGroup>();
                    c.Popups.Add(scrimGo);
                    c.Popups.Add(go);
                    break;
                default:
                    // panel/div, button, toggle, tab, tabs, pages, option, empty
                    if (c.Preview && n.Tag == "tab") s = PreviewTabStyle(c, n, s);
                    ApplyBackground(c, n, e, go.transform, interactive, s);
                    if (n.Tag == "button" || n.Tag == "toggle" || n.Tag == "tab" || n.Tag == "option")
                        BuildClickableContent(c, n, e, go, s, local);
                    break;
            }

            // An id means "scriptable": it gets a CanvasGroup so SetOpacity works on it.
            if ((NeedsGroup(s) || (n.Id != null && !c.Preview)) && c.Groups[e] == null)
            {
                c.Groups[e] = go.AddComponent<CanvasGroup>();
                c.Groups[e].alpha = s.Opacity;
            }
            if (s.Scale != 1f) rt.localScale = new Vector3(s.Scale, s.Scale, 1f);

            if (c.Signal[e] >= 0) AddSignal(c, n, e, go, s, interactive);

            foreach (AuiNode child in n.Children) BuildNode(c, child, go.transform, row, n);

            if (n.Tag == "modal" && !ModalShownAtRest(c, n))
            {
                go.SetActive(false);
                if (scrimGo != null) scrimGo.SetActive(false);
            }
        }

        /// <summary>A modal is baked open when enabled="true", or when the preview asks for it.</summary>
        private static bool ModalShownAtRest(Ctx c, AuiNode n)
        {
            if (c.Preview && c.Opt.OpenModals.Contains(c.R.ViewName + "/" + n.Index)) return true;
            foreach (AuiModalInfo m in c.R.Info.Modals) if (m.Node == n) return m.StartOpen;
            return false;
        }

        /// <summary>The dimming scrim behind a modal: a full-size image under Content that closes the modal on click.</summary>
        private static GameObject BuildModalScrim(Ctx c, AuiNode n, Transform parent, AuiResolvedStyle s)
        {
            var scrim = new GameObject(Name(n) + " backdrop", typeof(RectTransform));
            scrim.transform.SetParent(parent, false);
            var srt = (RectTransform)scrim.transform;
            Stretch(srt);
            var img = scrim.AddComponent<Image>();
            AuiBackground bg = s.Backdrop != null ? s.Backdrop.Background : new AuiBackground { IsNone = true };
            img.color = bg.IsNone ? new Color(0f, 0f, 0f, 0.6f) : ToColor(bg.Color);
            if (bg.Sprite != null) { img.sprite = NamedSprite(c, bg.Sprite); img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1f; }
            img.raycastTarget = true;
            int se;
            if (!c.R.Map.ModalScrimElem.TryGetValue(n.Index, out se)) return scrim;
            Register(c, se, scrim, srt, Vector2.zero);
            c.Kind[se] = AuiIndexMap.KIND_MODAL_SCRIM;
            var cg = scrim.AddComponent<CanvasGroup>();
            c.Groups[se] = cg;
            if (s.Backdrop != null && s.Backdrop.Opacity < 1f) cg.alpha = s.Backdrop.Opacity;
            if (!c.Preview)
            {
                var btn = scrim.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
                c.Buttons[se] = btn;
                AuiSignal sig = MakeSignal(c, scrim, c.Signal[se]);
                Wire(btn.onClick, sig, "_Click");
            }
            return scrim;
        }

        private static string Name(AuiNode n)
        {
            return n.Id != null ? n.Tag + "#" + n.Id : n.Tag;
        }

        /// <summary>The page a group shows at rest: its default, or the preview's pick for it.</summary>
        private static int ShownPage(Ctx c, AuiPageGroup grp)
        {
            if (c.Preview && grp.Id != null)
            {
                string ov;
                if (c.Opt.Pages.TryGetValue(c.R.ViewName + "/" + grp.Id, out ov))
                {
                    int i = grp.IndexOfPage(ov);
                    if (i >= 0) return i;
                }
            }
            return grp.DefaultIndex;
        }

        private static readonly string[] SelectedProps = { "background", "color", "border", "icon-color", "opacity", "scale" };

        /// <summary>The tab of the shown page drawn in its :selected look, so a preview reads like the panel with that tab pressed.</summary>
        private static AuiResolvedStyle PreviewTabStyle(Ctx c, AuiNode tab, AuiResolvedStyle s)
        {
            AuiPageGroup grp = null;
            foreach (AuiPageGroup pg in c.R.Info.PageGroups) if (pg.TabNodes.Contains(tab)) grp = pg;
            if (grp == null) return s;
            int shown = ShownPage(c, grp);
            if (shown < 0 || shown >= grp.Pages.Count || grp.Pages[shown].Id != tab.Get("page")) return s;
            Dictionary<string, string> sel;
            if (!s.States.TryGetValue("selected", out sel)) return s;
            var merged = new AuiResolvedStyle();
            foreach (KeyValuePair<string, string> kv in s.Values) merged.Values[kv.Key] = kv.Value;
            foreach (string p in SelectedProps) { string v; if (sel.TryGetValue(p, out v)) merged.Values[p] = v; }
            foreach (KeyValuePair<string, Dictionary<string, string>> st in s.States) merged.States[st.Key] = st.Value;
            return merged;
        }

        private static bool NeedsGroup(AuiResolvedStyle s)
        {
            if (s.Opacity < 1f) return true;
            foreach (KeyValuePair<string, Dictionary<string, string>> st in s.States)
                if (st.Value.ContainsKey("opacity")) return true;
            return false;
        }

        /// <summary>Anchors top-left, pivot centre (so scale tweens grow from the middle), position from the layout rect.</summary>
        private static void Place(RectTransform rt, AuiRect local, bool fill, float parentW, float parentH)
        {
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            if (fill)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                float left = local.X, top = local.Y, right = parentW - local.X - local.W, bottom = parentH - local.Y - local.H;
                rt.offsetMin = new Vector2(left, bottom);
                rt.offsetMax = new Vector2(-right, -top);
                return;
            }
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(local.W, local.H);
            rt.anchoredPosition = new Vector2(local.X + local.W * 0.5f, -(local.Y + local.H * 0.5f));
        }

        private static RectTransform Child(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.localScale = Vector3.one;
            return rt;
        }

        private static void Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        // ================================================================ pieces

        // SPRITE LOOKUPS ARE CACHED. A named sprite outside the package cost a project-wide
        // FindAssets per element per build; at a preview rebuild per save that was the slowest
        // step. Entries are dropped when a sprite or icon is imported (the postprocessor calls
        // ClearSpriteCache) and a destroyed object is treated as a miss.
        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();

        public static void ClearSpriteCache()
        {
            SpriteCache.Clear();
            MaterialCache.Clear();
        }

        private static Sprite CachedSprite(string key, System.Func<Sprite> load)
        {
            Sprite s;
            if (SpriteCache.TryGetValue(key, out s) && s != null) return s;
            s = load();
            SpriteCache[key] = s;
            return s;
        }

        private static Sprite PackageSprite(string name)
        {
            return CachedSprite("pkg:" + name, () => AssetDatabase.LoadAssetAtPath<Sprite>(AuiEditorEnv.PackageAssetRoot + "/Sprites/" + name + ".png"));
        }

        private static Sprite RoundedSprite(Ctx c, float radius, bool border)
        {
            int best = 0;
            foreach (int r in AuiSpriteGenerator.Radii) if (r <= radius + 0.01f) best = r;
            string name = radius <= 0 ? "Square" : (border ? "Border" : "Rounded") + (best == 0 ? AuiSpriteGenerator.Radii[0] : best);
            if (radius <= 0 && border) name = "Border4";
            return PackageSprite(name);
        }

        private static Sprite NamedSprite(Ctx c, string name)
        {
            // background: sprite(Name) -> package Sprites first, then any sprite asset with that name in the project
            var s = PackageSprite(name);
            if (s != null) return s;
            s = CachedSprite("named:" + name, () =>
            {
                foreach (string guid in AssetDatabase.FindAssets(name + " t:Sprite"))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<Sprite>(p);
                }
                return null;
            });
            if (s == null) AuiLog.Warn("sprite '" + name + "' not found");
            return s;
        }

        private static Color ToColor(AuiColor c)
        {
            return new Color(c.R, c.G, c.B, c.A);
        }

        private static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();
        public const string GeneratedMaterialDir = "Assets/AUI/Generated/Materials";

        /// <summary>background: material(Name) -> any Material asset of that name in the project.</summary>
        private static Material NamedMaterial(Ctx c, string name)
        {
            Material m;
            if (MaterialCache.TryGetValue("named:" + name, out m) && m != null) return m;
            foreach (string guid in AssetDatabase.FindAssets(name + " t:Material"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == name) { m = AssetDatabase.LoadAssetAtPath<Material>(p); break; }
            }
            if (m == null) AuiLog.Warn("material '" + name + "' not found");
            MaterialCache["named:" + name] = m;
            return m;
        }

        /// <summary>background: shader(Name) -> one Material asset per shader under Assets/AUI/Generated/Materials, created on first use.</summary>
        private static Material ShaderMaterial(Ctx c, string shaderName)
        {
            Material m;
            if (MaterialCache.TryGetValue("shader:" + shaderName, out m) && m != null) return m;
            Shader sh = Shader.Find(shaderName);
            if (sh == null) { AuiLog.Warn("shader '" + shaderName + "' not found"); return null; }
            string path = GeneratedMaterialDir + "/" + shaderName.Replace('/', '_').Replace(' ', '-') + ".mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Directory.CreateDirectory(GeneratedMaterialDir);
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
                AuiLog.Info("created " + path + " for shader(" + shaderName + ")");
            }
            MaterialCache["shader:" + shaderName] = m;
            return m;
        }

        private static Material BackgroundMaterial(Ctx c, AuiBackground bg)
        {
            if (bg.Material != null) return NamedMaterial(c, bg.Material);
            if (bg.Shader != null) return ShaderMaterial(c, bg.Shader);
            return null;
        }

        /// <summary>Background image on the element itself (elemBg) and an optional Border child (elemBorder).</summary>
        private static void ApplyBackground(Ctx c, AuiNode n, int e, Transform t, bool raycast, AuiResolvedStyle s = null)
        {
            if (s == null) s = c.R.Styles[n.Index];
            AuiBackground bg = s.Background;
            bool needsRaycast = raycast || c.Signal[e] >= 0;
            if (!bg.IsNone || needsRaycast)
            {
                var img = t.gameObject.AddComponent<Image>();
                if (bg.IsNone)
                {
                    // Transparent, but shaped by the element's radius: a state that later paints a
                    // colour (.ghost:hover) must get the same corners as a button that had one at rest.
                    img.color = new Color(0, 0, 0, 0);
                    img.sprite = RoundedSprite(c, s.Radius, false);
                    img.type = Image.Type.Sliced;
                    img.pixelsPerUnitMultiplier = 1f;
                }
                else
                {
                    img.sprite = bg.Sprite != null ? NamedSprite(c, bg.Sprite) : RoundedSprite(c, s.Radius, false);
                    img.type = Image.Type.Sliced;
                    img.pixelsPerUnitMultiplier = 1f;
                    img.color = ToColor(bg.Color);
                    // material() / shader(): the image keeps its (radius) sprite as _MainTex and its colour as the vertex tint.
                    if (bg.HasMaterial) img.material = BackgroundMaterial(c, bg);
                }
                img.raycastTarget = needsRaycast;
                c.Bg[e] = img;
            }
            AuiColor bc;
            if (s.Color("border", out bc) && !bc.IsTransparent)
            {
                RectTransform brt = Child(t, "Border");
                Stretch(brt);
                var bimg = brt.gameObject.AddComponent<Image>();
                bimg.sprite = RoundedSprite(c, s.Radius, true);
                bimg.type = Image.Type.Sliced;
                bimg.color = ToColor(bc);
                bimg.raycastTarget = false;
                c.Border[e] = bimg;
            }
        }

        private static TextMeshProUGUI MakeLabel(Ctx c, Transform parent, string name, AuiResolvedStyle s, string initial, TextAlignmentOptions align, bool wrap, bool ellipsis)
        {
            RectTransform rt = Child(parent, name);
            Stretch(rt);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = AuiTextMeasurer.FontFor(c.Fonts, s.FontWeight);
            t.fontSize = s.FontSize;
            AuiColor col;
            t.color = s.Color("color", out col) ? ToColor(col) : Color.white;
            t.alignment = align;
            t.enableWordWrapping = wrap;
            t.overflowMode = ellipsis ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            t.richText = false;
            t.raycastTarget = false;
            if (s.LineHeight > 0) t.lineSpacing = (s.LineHeight - 1f) * 100f;
            t.text = initial ?? "";
            return t;
        }

        private static TextAlignmentOptions Align(string h)
        {
            if (h == "center") return TextAlignmentOptions.Center;
            if (h == "right") return TextAlignmentOptions.Right;
            return TextAlignmentOptions.Left;
        }

        private static string Preview(Ctx c, string key, string fallback)
        {
            if (key == null) return fallback;
            if (c.Lang == null) return key;
            int lang = c.Lang.DefaultLanguage;
            if (c.Preview && c.Opt.LangCode != null)
            {
                int i = c.Lang.IndexOfLanguage(c.Opt.LangCode);
                if (i >= 0) lang = i;
            }
            return c.Lang.GetWithFallback(lang, key) ?? key;
        }

        private static int KeyIndex(Ctx c, string key)
        {
            return key == null || c.Lang == null ? -1 : c.Lang.IndexOfKey(key);
        }

        /// <param name="local">The element's rect for THIS row (a repeat row is offset from the template's).</param>
        private static void BuildText(Ctx c, AuiNode n, int e, GameObject go, AuiResolvedStyle s, AuiRect local, AuiNode parentNode)
        {
            bool wrap = Flag(n, "wrap");
            // A single-line text never overflows its box: keyed or bound, it ellipsises.
            bool ellipsis = Flag(n, "ellipsis") || !wrap;
            string key = n.Get("key");
            string initial = key != null ? Preview(c, key, key) : (n.Get("text") != null ? "" : n.Text);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = AuiTextMeasurer.FontFor(c.Fonts, s.FontWeight);
            t.fontSize = s.FontSize;
            AuiColor col;
            t.color = s.Color("color", out col) ? ToColor(col) : Color.white;
            t.alignment = Align(s.TextAlign);
            t.enableWordWrapping = wrap;
            t.overflowMode = ellipsis ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            t.richText = false;
            t.raycastTarget = false;
            if (s.LineHeight > 0) t.lineSpacing = (s.LineHeight - 1f) * 100f;
            // max-lines: a wrapped label stops after this many lines (TMP's own cap; with Ellipsis it ends in "...").
            int maxLines;
            if (wrap && int.TryParse(n.Get("max-lines") ?? "", out maxLines) && maxLines > 0) t.maxVisibleLines = maxLines;
            t.text = initial ?? "";
            c.Text[e] = t;
            c.Key[e] = KeyIndex(c, key);
            // RUNTIME TEXT IS RE-MEASURED IN-WORLD. The box baked here is the solver's sample width;
            // AuiView.Layout measures the real string through LayoutUtility and re-solves the row,
            // so no widen-to-parent or ContentSizeFitter hack is needed any more.
        }

        private static bool Flag(AuiNode n, string attr)
        {
            string v = n.Get(attr);
            return v != null && v != "false";
        }

        private static Sprite IconSprite(Ctx c, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Sprite s = CachedSprite("icon:" + name, () =>
            {
                foreach (string p in c.Ws.IconPaths)
                {
                    if (Path.GetFileNameWithoutExtension(p) != name) continue;
                    string ap = AuiEditorEnv.AssetPath(p);
                    if (ap == null) continue;
                    var sp = AssetDatabase.LoadAssetAtPath<Sprite>(ap);
                    if (sp != null) return sp;
                }
                return null;
            });
            if (s == null) AuiLog.Warn("icon '" + name + "' has no sprite (is the SVG imported as a Textured Sprite?)");
            return s;
        }

        private static Image MakeIcon(Ctx c, Transform parent, string name, string icon, AuiResolvedStyle s, float size)
        {
            RectTransform rt = Child(parent, name);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = IconSprite(c, icon);
            img.preserveAspect = true;
            img.raycastTarget = false;
            AuiColor col;
            img.color = s.Color("icon-color", out col) ? ToColor(col) : Color.white;
            return img;
        }

        private static void BuildIcon(Ctx c, AuiNode n, int e, GameObject go, AuiResolvedStyle s)
        {
            var img = go.AddComponent<Image>();
            img.sprite = IconSprite(c, n.Get("name"));
            img.preserveAspect = true;
            img.raycastTarget = false;
            AuiColor col;
            img.color = s.Color("icon-color", out col) ? ToColor(col) : Color.white;
            c.Icon[e] = img;
        }

        private static void BuildImage(Ctx c, AuiNode n, int e, GameObject go, AuiResolvedStyle s)
        {
            var img = go.AddComponent<Image>();
            string src = n.Get("src");
            string p = AuiEditorEnv.AssetPath(Path.Combine(Path.GetDirectoryName(c.R.FilePath) ?? "", src ?? ""));
            img.sprite = p != null ? AssetDatabase.LoadAssetAtPath<Sprite>(p) : null;
            if (img.sprite == null) AuiLog.Warn("image '" + src + "' not found or not a Sprite");
            string mode = n.Get("mode", "simple");
            img.type = mode == "sliced" ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = mode == "fit";
            img.raycastTarget = false;
            c.Bg[e] = img;
        }

        private static void BuildRawImage(Ctx c, AuiNode n, int e, GameObject go, AuiResolvedStyle s)
        {
            // THE RAWIMAGE GOES INTO c.Raw, NEVER c.Bg. The cascade writes sprite, material and
            // colour into elemBg on every state change; a texture a script set would be lost.
            // The tint stays white: `color` is inherited text colour, and taking it would dim
            // every thumbnail to the muted grey of the label beside it.
            var raw = go.AddComponent<RawImage>();
            raw.texture = null;
            raw.uvRect = new Rect(0f, 0f, 1f, 1f);
            raw.raycastTarget = false;
            raw.color = Color.white;
            // A RawImage with no texture draws a solid white quad; bake it off until a texture arrives
            // (SetRawTexture toggles it), so a parent's background shows through as the placeholder.
            raw.enabled = false;
            c.Raw[e] = raw;
        }

        /// <summary>Icon + label inside a button-like element, laid out left to right inside the padding.</summary>
        private static void BuildClickableContent(Ctx c, AuiNode n, int e, GameObject go, AuiResolvedStyle s, AuiRect local)
        {
            AuiBox p = s.Padding;
            string icon = n.Get("icon");
            string key = n.Get("key");
            bool hasLabel = key != null || (n.Text != null && n.Children.Count == 0);
            float x = p.Left;
            if (icon != null)
            {
                Image img = MakeIcon(c, go.transform, "Icon", icon, s, IconSize);
                float ix = hasLabel || n.Children.Count > 0 ? x : (local.W - IconSize) * 0.5f;
                ((RectTransform)img.transform).anchoredPosition = new Vector2(ix, 0f);
                c.Icon[e] = img;
                x += IconSize + s.Gap;
            }
            if (hasLabel)
            {
                TextMeshProUGUI t = MakeLabel(c, go.transform, "Label", s, key != null ? Preview(c, key, key) : n.Text,
                    icon != null || s.Justify != "center" ? TextAlignmentOptions.Left : TextAlignmentOptions.Center, false, true);
                // No vertical inset: a TMP rect shorter than one line renders NOTHING under Ellipsis
                // overflow, and padding 8 on a 36 mm control leaves 20 mm for an 18 pt line.
                Stretch((RectTransform)t.transform, x, 0f, p.Right, 0f);
                c.Text[e] = t;
                c.Key[e] = KeyIndex(c, key);
            }
        }

        private static void BuildSlider(Ctx c, AuiNode n, int e, GameObject go, AuiResolvedStyle s, AuiRect local)
        {
            AuiColor fillCol;
            AuiBackground bg = s.Background;
            Color track = !bg.IsNone ? ToColor(bg.Color) : new Color(1, 1, 1, 0.2f);
            Color fill = s.Color("color", out fillCol) ? ToColor(fillCol) : Color.white;
            string fmt = n.Get("format-key");
            float labelW = fmt != null ? Mathf.Min(72f, local.W * 0.35f) : 0f;
            float trackH = 6f, handle = Mathf.Min(local.H, 20f);

            var slider = go.AddComponent<Slider>();
            RectTransform trackRt = Child(go.transform, "Track");
            trackRt.anchorMin = new Vector2(0f, 0.5f); trackRt.anchorMax = new Vector2(1f, 0.5f); trackRt.pivot = new Vector2(0.5f, 0.5f);
            trackRt.offsetMin = new Vector2(handle * 0.5f, -trackH * 0.5f); trackRt.offsetMax = new Vector2(-(handle * 0.5f + labelW + (labelW > 0 ? s.Gap : 0)), trackH * 0.5f);
            var trackImg = trackRt.gameObject.AddComponent<Image>();
            trackImg.sprite = RoundedSprite(c, 4, false); trackImg.type = Image.Type.Sliced; trackImg.color = track; trackImg.raycastTarget = true;
            c.Bg[e] = trackImg;

            RectTransform fillArea = Child(go.transform, "Fill Area");
            fillArea.anchorMin = new Vector2(0f, 0.5f); fillArea.anchorMax = new Vector2(1f, 0.5f); fillArea.pivot = new Vector2(0.5f, 0.5f);
            fillArea.offsetMin = new Vector2(handle * 0.5f, -trackH * 0.5f); fillArea.offsetMax = new Vector2(-(handle * 0.5f + labelW + (labelW > 0 ? s.Gap : 0)), trackH * 0.5f);
            RectTransform fillRt = Child(fillArea, "Fill");
            Stretch(fillRt);
            var fillImg = fillRt.gameObject.AddComponent<Image>();
            fillImg.sprite = RoundedSprite(c, 4, false); fillImg.type = Image.Type.Sliced; fillImg.color = fill; fillImg.raycastTarget = false;

            RectTransform handleArea = Child(go.transform, "Handle Slide Area");
            handleArea.anchorMin = new Vector2(0f, 0f); handleArea.anchorMax = new Vector2(1f, 1f); handleArea.pivot = new Vector2(0.5f, 0.5f);
            handleArea.offsetMin = new Vector2(handle * 0.5f, 0f); handleArea.offsetMax = new Vector2(-(handle * 0.5f + labelW + (labelW > 0 ? s.Gap : 0)), 0f);
            RectTransform handleRt = Child(handleArea, "Handle");
            handleRt.anchorMin = handleRt.anchorMax = new Vector2(0f, 0.5f);
            handleRt.pivot = new Vector2(0.5f, 0.5f);
            handleRt.sizeDelta = new Vector2(handle, handle);
            var handleImg = handleRt.gameObject.AddComponent<Image>();
            // A dot: the unsliced circle sprite drawn square, never the 9-slice (its corners exceed a 20 mm box).
            handleImg.sprite = PackageSprite("Circle");
            // The handle is the slider's "icon": icon-color when the sheet sets one, else the fill colour.
            AuiColor handleCol;
            handleImg.type = Image.Type.Simple; handleImg.preserveAspect = true; handleImg.raycastTarget = true;
            handleImg.color = s.Color("icon-color", out handleCol) && !handleCol.IsTransparent ? ToColor(handleCol) : fill;
            c.Icon[e] = handleImg; // icon-color rows tint the handle

            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.transition = Selectable.Transition.None;
            var nav = slider.navigation; nav.mode = Navigation.Mode.None; slider.navigation = nav;
            slider.direction = Slider.Direction.LeftToRight;
            float min, max, step;
            AuiNum.TryParse(n.Get("min", "0"), out min);
            AuiNum.TryParse(n.Get("max", "1"), out max);
            AuiNum.TryParse(n.Get("step", "0"), out step);
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = step >= 1f;
            slider.value = min;
            c.Sliders[e] = slider;

            if (fmt != null)
            {
                RectTransform lrt = Child(go.transform, "Value");
                lrt.anchorMin = new Vector2(1f, 0f); lrt.anchorMax = new Vector2(1f, 1f); lrt.pivot = new Vector2(1f, 0.5f);
                lrt.offsetMin = new Vector2(-labelW, 0f); lrt.offsetMax = new Vector2(0f, 0f);
                var t = lrt.gameObject.AddComponent<TextMeshProUGUI>();
                t.font = AuiTextMeasurer.FontFor(c.Fonts, s.FontWeight);
                t.fontSize = s.FontSize;
                AuiColor col;
                t.color = s.Color("color", out col) ? ToColor(col) : Color.white;
                t.alignment = TextAlignmentOptions.Right;
                t.richText = false;
                t.raycastTarget = false;
                t.text = Preview(c, fmt, fmt).Replace("{value}", "0");
                c.Text[e] = t;
                c.Key[e] = KeyIndex(c, fmt);
            }
        }

        private static void BuildInput(Ctx c, AuiNode n, int e, GameObject go, AuiResolvedStyle s, AuiRect local)
        {
            ApplyBackground(c, n, e, go.transform, true);
            AuiBox p = s.Padding;
            string ph = n.Get("placeholder-key");
            TextMeshProUGUI t = MakeLabel(c, go.transform, "Label", s, Preview(c, ph, ""), TextAlignmentOptions.Left, false, true);
            Stretch((RectTransform)t.transform, p.Left, 0f, p.Right, 0f);
            c.Text[e] = t;
            c.Key[e] = KeyIndex(c, ph);
        }

        private static void BuildSelect(Ctx c, AuiNode n, int e, GameObject go, AuiResolvedStyle s, AuiRect local, int row)
        {
            ApplyBackground(c, n, e, go.transform, true);
            AuiBox p = s.Padding;
            Image chevron = MakeIcon(c, go.transform, "Chevron", "chevron-down", s, IconSize);
            ((RectTransform)chevron.transform).anchorMin = ((RectTransform)chevron.transform).anchorMax = new Vector2(1f, 0.5f);
            ((RectTransform)chevron.transform).pivot = new Vector2(1f, 0.5f);
            ((RectTransform)chevron.transform).anchoredPosition = new Vector2(-p.Right, 0f);
            c.Icon[e] = chevron;
            string firstLabel = n.Children.Count > 0 ? Preview(c, n.Children[0].Get("key"), n.Children[0].Text ?? "") : "";
            TextMeshProUGUI t = MakeLabel(c, go.transform, "Label", s, firstLabel, TextAlignmentOptions.Left, false, true);
            Stretch((RectTransform)t.transform, p.Left, 0f, p.Right + IconSize + s.Gap, 0f);
            c.Text[e] = t;
            if (c.Signal[e] >= 0) AddSignal(c, n, e, go, s, true);
            if (NeedsGroup(s)) { c.Groups[e] = go.AddComponent<CanvasGroup>(); c.Groups[e].alpha = s.Opacity; }

            // ---- popup: a sibling of everything, last under Content so it draws on top
            int pe = c.R.Map.PopupElem[n.Index];
            int se = c.R.Map.ScrimElem[n.Index];
            AuiLayoutNode hl = c.R.Layout[n.Index];

            // The RESTING height is the author's own options. Rows reserved by `max` are baked
            // inactive and the popup grows to them when SetSelectOptionCount says so — sizing it
            // for max here would leave a four-option filter standing in a twenty-row well.
            int authored = n.ReservedFrom >= 0 ? n.ReservedFrom : n.Children.Count;
            // ONE ROW HEIGHT FOR EVERY ROW, the tallest the solver measured. The rows do NOT all
            // measure the same: a reserved row carries no key, so it measures at the option's
            // min-height, shorter than an authored row whose label sets the height. The runtime
            // resizes the well to count * selectOptionH, so a row baked shorter than that leaves
            // the well running past the last row — a twenty-row popup drew 270 mm of empty
            // background under its options — and a runtime label written into a min-height row
            // would sit in a shorter chip than the row above it.
            float rowH = 0f;
            foreach (AuiNode o in n.Children)
            {
                float h = c.R.Layout[o.Index].Local.H;
                if (h > rowH) rowH = h;
            }
            float optH = authored * rowH;
            var popup = new GameObject(Name(n) + " popup", typeof(RectTransform));
            popup.transform.SetParent(c.Content, false);
            var prt = (RectTransform)popup.transform;
            // INSIDE THE VIEW, ALWAYS. The laser only reaches what the root BoxCollider covers, so a
            // popup hanging below the panel is visible and unclickable. Open upward when there is no
            // room below, and clamp horizontally.
            float popupH = optH + 8f;
            float viewW = c.R.Info.Width, viewH = c.R.Info.Height;
            float py = hl.Absolute.Y + hl.Absolute.H + 4f;
            // Which EDGE stays put when the row count changes: the top when the popup hangs below
            // the header, the bottom when it had to open upward. The runtime re-derives y from it.
            bool openUp = false;
            if (py + popupH > viewH - 4f) { py = hl.Absolute.Y - 4f - popupH; openUp = true; }
            if (py < 4f) py = 4f;
            c.SelectRowH[n.Index] = rowH;
            c.SelectUp[n.Index] = openUp;
            c.SelectEdge[n.Index] = openUp ? py + popupH : py;
            float px = Mathf.Clamp(hl.Absolute.X, 4f, Mathf.Max(4f, viewW - hl.Absolute.W - 4f));
            var popupRect = new AuiRect(px, py, hl.Absolute.W, popupH);
            Place(prt, popupRect, false, viewW, viewH);
            // Toward the viewer, and on its own sorting layer: whatever is built after it (nested
            // views, later rows) must never draw over an open dropdown.
            prt.anchoredPosition3D = new Vector3(prt.anchoredPosition.x, prt.anchoredPosition.y, -1f);
            var popupCanvas = popup.AddComponent<Canvas>();
            popupCanvas.overrideSorting = true;
            popupCanvas.sortingOrder = 100;
            popup.AddComponent<GraphicRaycaster>();   // a nested Canvas receives no input without its own raycaster
            Register(c, pe, popup, prt, prt.anchoredPosition);
            c.Kind[pe] = AuiIndexMap.KIND_NONE;
            var pg = popup.AddComponent<CanvasGroup>();
            c.Groups[pe] = pg;
            var pimg = popup.AddComponent<Image>();
            AuiBackground bg = s.Background;
            // Same corners as the header: the theme shapes a select with sprite(Rounded8), not radius,
            // so a popup shaped by radius alone came out square.
            pimg.sprite = bg.Sprite != null ? NamedSprite(c, bg.Sprite) : RoundedSprite(c, s.Radius, false);
            pimg.type = Image.Type.Sliced;
            pimg.pixelsPerUnitMultiplier = 1f;
            pimg.color = bg.IsNone ? new Color(0.1f, 0.1f, 0.1f, 1f) : ToColor(bg.Color);
            pimg.raycastTarget = true;
            c.Bg[pe] = pimg;
            c.Popups.Add(popup);

            // scrim: a click anywhere outside closes the popup. Child of the popup so it shows/hides with it.
            var scrim = new GameObject("Scrim", typeof(RectTransform));
            scrim.transform.SetParent(popup.transform, false);
            scrim.transform.SetAsFirstSibling();
            var srt = (RectTransform)scrim.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.sizeDelta = new Vector2(c.R.Info.Width * 3f, c.R.Info.Height * 3f);
            srt.anchoredPosition = Vector2.zero;
            var simg = scrim.AddComponent<Image>();
            simg.color = new Color(0, 0, 0, 0);
            simg.raycastTarget = true;
            Register(c, se, scrim, srt, Vector2.zero);
            c.Kind[se] = AuiIndexMap.KIND_SCRIM;
            if (!c.Preview)
            {
                var sbtn = scrim.AddComponent<Button>();
                sbtn.transition = Selectable.Transition.None;
                var snav = sbtn.navigation; snav.mode = Navigation.Mode.None; sbtn.navigation = snav;
                c.Buttons[se] = sbtn;
                AuiSignal ssig = MakeSignal(c, scrim, c.Signal[se]);
                Wire(sbtn.onClick, ssig, "_Click");
            }

            // options
            float y = 4f;
            int built = 0;
            foreach (AuiNode o in n.Children)
            {
                int oe = c.R.Map.Elem(o, row);
                AuiResolvedStyle os = c.R.Styles[o.Index];
                var ogo = new GameObject(Name(o), typeof(RectTransform));
                ogo.transform.SetParent(popup.transform, false);
                var ort = (RectTransform)ogo.transform;
                // rowH, not this row's own measured height — see above.
                Place(ort, new AuiRect(4f, y, popupRect.W - 8f, rowH), false, popupRect.W, popupRect.H);
                Register(c, oe, ogo, ort, ort.anchoredPosition);
                ApplyBackground(c, o, oe, ogo.transform, true);
                BuildClickableContent(c, o, oe, ogo, os, new AuiRect(0, 0, popupRect.W - 8f, rowH));
                if (c.Signal[oe] >= 0) AddSignal(c, o, oe, ogo, os, true);
                // A reserved row is placed like the rest — its y never moves — but starts
                // inactive, so a select shows only the options it actually has until the view
                // calls SetSelectOptionCount.
                if (built++ >= authored) ogo.SetActive(false);
                y += rowH;
            }
            if (!(c.Preview && c.Opt.OpenSelects.Contains(c.R.ViewName + "/" + n.Index))) popup.SetActive(false);
        }

        private static void BuildRepeat(Ctx c, AuiNode n, Transform parent)
        {
            AuiRepeatInfo r = null;
            foreach (AuiRepeatInfo ri in c.R.Info.Repeats) if (ri.Node == n) r = ri;
            int e = c.R.Map.Elem(n, 0);
            AuiLayoutNode ln = c.R.Layout[n.Index];
            var go = new GameObject(Name(n), typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            AuiLayoutNode pl = c.R.Layout[n.Parent.Index];
            Place(rt, ln.Local, false, pl.Local.W, pl.Local.H);
            Register(c, e, go, rt, rt.anchoredPosition);
            if (r == null || r.Template == null) return;
            if (r.IsTemplate)
            {
                // Parked instances: an inactive holder at zero size, each row inactive until CreateElement moves it out.
                go.SetActive(false);
                for (int row = 0; row < r.Max; row++)
                {
                    BuildNode(c, r.Template, go.transform, row, n);
                    int re = c.R.Map.Elem(r.Template, row);
                    if (re >= 0 && c.Objects[re] != null) c.Objects[re].SetActive(false);
                }
                return;
            }
            ApplyBackground(c, n, e, go.transform, false);
            for (int row = 0; row < r.Max; row++) BuildNode(c, r.Template, go.transform, row, n);
            if (r.Empty != null) BuildNode(c, r.Empty, go.transform, 0, n);
        }

        private static void BuildNested(Ctx c, AuiNode n, Transform parent, int row)
        {
            AuiNestedView nv = null;
            foreach (AuiNestedView x in c.R.Info.NestedViews) if (x.Node == n) nv = x;
            string cp;
            if (nv == null || !c.Ws.Views.TryGetValue(n.Tag, out cp)) return;
            if (c.Preview) { BuildNestedPreview(c, n, cp, parent, row); return; }
            string prefabPath = AuiEditorEnv.AssetPath(Path.ChangeExtension(cp, ".prefab"));
            var prefab = prefabPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) : null;
            if (prefab == null) { AuiLog.Error("nested view '" + n.Tag + "' has no prefab yet at " + prefabPath); return; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            inst.name = Name(n);
            var rt = (RectTransform)inst.transform;
            AuiLayoutNode ln = c.R.Layout[n.Index];
            AuiLayoutNode pl = c.R.Layout[n.Parent.Index];
            Place(rt, ln.Local, false, pl.Local.W, pl.Local.H);
            // A nested view is not its own canvas: drop the pieces that only make sense on a root.
            foreach (var comp in new Component[] { inst.GetComponent<VRCUiShape>(), inst.GetComponent<BoxCollider>(), inst.GetComponent<GraphicRaycaster>(), inst.GetComponent<Canvas>() })
                if (comp != null) Object.DestroyImmediate(comp);
            // Its attach goes with them (AUI123 told the author). Left in place it would drag a piece of
            // this view's layout off to the player's wrist.
            var childAttach = inst.GetComponent<AuiAttach>();
            if (childAttach != null) UdonSharpEditorUtility.DestroyImmediate(childAttach);
            var child = inst.GetComponent<AuiView>();
            if (child != null)
            {
                child.attach = null;
                child.parentView = c.View;
                EditorUtility.SetDirty(child);
                // A slot's parked copies have no single generated field; only a plain nested view does.
                AuiRepeatInfo rep = c.R.Info.RepeatContaining(n);
                if (rep == null || !rep.IsTemplate) c.NestedInstances[nv] = child;
            }
            // Element index reserved by the map for the component node
            int e = c.R.Map.Elem(n, row);
            if (e >= 0)
            {
                Register(c, e, inst, rt, rt.anchoredPosition);
                c.ChildView[e] = child;
            }
        }

        /// <summary>A nested view in the preview: compiled from its source (cached) and built inline, so its edits are live too.</summary>
        private static void BuildNestedPreview(Ctx c, AuiNode n, string childPath, Transform parent, int row)
        {
            var go = new GameObject(Name(n), typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.localScale = Vector3.one;
            int e = c.R.Map.Elem(n, row);
            if (c.Depth < 8)
            {
                AuiCompileResult cr = AuiEditorEnv.Compile(Path.GetFullPath(childPath), c.Settings, c.Ws, null);
                if (cr.Doc != null && cr.Doc.Root != null && cr.Map != null && cr.Layout != null)
                    BuildPreviewTree(cr, c.Settings, c.Ws, c.Opt, go, true, c.Depth + 1);
            }
            AuiLayoutNode ln = c.R.Layout[n.Index];
            AuiLayoutNode pl = c.R.Layout[n.Parent.Index];
            Place(rt, ln.Local, false, pl.Local.W, pl.Local.H);
            if (e >= 0) Register(c, e, go, rt, rt.anchoredPosition);
        }

        // ================================================================ signals

        private static AuiSignal MakeSignal(Ctx c, GameObject go, int id)
        {
            var sig = (AuiSignal)UdonSharpUndo.AddComponent(go, typeof(AuiSignal));
            sig.view = c.View;
            sig.id = id;
            c.Signals[id] = sig;
            return sig;
        }

        private static void Wire(UnityEngine.Events.UnityEventBase ev, AuiSignal sig, string method)
        {
            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(sig);
            UnityEventTools.AddStringPersistentListener(ev, ub.SendCustomEvent, method);
        }

        private static void AddSignal(Ctx c, AuiNode n, int e, GameObject go, AuiResolvedStyle s, bool clickable)
        {
            if (c.Preview) return;   // nothing to wire: no view, no Udon
            int id = c.Signal[e];
            if (id < 0 || c.Signals[id] != null) return;
            AuiSignal sig = MakeSignal(c, go, id);
            if (c.Bg[e] == null && n.Tag != "slider")
            {
                // Pointer events need a raycast target under the cursor.
                var img = go.AddComponent<Image>();
                img.color = new Color(0, 0, 0, 0);
                img.raycastTarget = true;
                c.Bg[e] = img;
            }
            else if (c.Bg[e] != null) c.Bg[e].raycastTarget = true;

            if (n.Tag == "slider")
            {
                Wire(c.Sliders[e].onValueChanged, sig, "_ValueChanged");
            }
            else if (clickable)
            {
                var btn = go.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
                btn.targetGraphic = c.Bg[e];
                Wire(btn.onClick, sig, "_Click");
                c.Buttons[e] = btn;
            }

            bool pointer = s.HasState("hover") || s.HasState("pressed") ||
                           n.HasAttribute("onhover") || n.HasAttribute("onleave") || n.HasAttribute("onpress") || n.HasAttribute("onrelease");
            if (!pointer) return;
            var trigger = go.AddComponent<EventTrigger>();
            AddEntry(trigger, EventTriggerType.PointerEnter, sig, "_Enter");
            AddEntry(trigger, EventTriggerType.PointerExit, sig, "_Exit");
            AddEntry(trigger, EventTriggerType.PointerDown, sig, "_Down");
            AddEntry(trigger, EventTriggerType.PointerUp, sig, "_Up");
        }

        private static void AddEntry(EventTrigger trigger, EventTriggerType type, AuiSignal sig, string method)
        {
            var entry = new EventTrigger.Entry { eventID = type, callback = new EventTrigger.TriggerEvent() };
            Wire(entry.callback, sig, method);
            trigger.triggers.Add(entry);
        }

        // ================================================================ tables

        private static void FillTables(Ctx c)
        {
            AuiView v = c.View;
            AuiCompileResult r = c.R;
            v.elemObjects = c.Objects; v.elemRects = c.Rects; v.elemGroups = c.Groups; v.elemBg = c.Bg; v.elemBorder = c.Border;
            v.elemIcon = c.Icon; v.elemText = c.Text; v.elemButtons = c.Buttons; v.elemSliders = c.Sliders; v.elemRaw = c.Raw; v.elemBasePos = c.BasePos;
            v.elemSignal = c.Signal; v.elemKey = c.Key; v.elemKind = c.Kind;
            v.sigElem = c.SigElem; v.signals = c.Signals;
            v.auiBakeHash = BakeStamp(r);

            // ---- <input maxlength> (-1 = no limit) and <input type> (0 text, 1 url), indexed like every elem* table.
            var inputMax = new int[c.N];
            var inputType = new int[c.N];
            for (int e = 0; e < c.N; e++)
            {
                inputMax[e] = -1;
                AuiNode node = e < r.Map.NodeOf.Length ? r.Map.NodeOf[e] : null;
                int ml;
                if (node != null && node.Tag == "input" && int.TryParse(node.Get("maxlength") ?? "", out ml) && ml > 0) inputMax[e] = ml;
                if (AuiAnalyzer.IsUrlInput(node)) inputType[e] = AuiView.INPUT_URL;
            }
            v.elemInputMax = inputMax;
            v.elemInputType = inputType;

            // ---- view transition
            AuiResolvedStyle rs = r.Styles[r.Doc.Root.Index];
            string show = r.Doc.Root.Get("show") ?? rs.Show;
            v.showKind = show == "none" ? AuiView.TRANS_NONE : show == "fade" ? AuiView.TRANS_FADE : show == "fade-slide" ? AuiView.TRANS_FADE_SLIDE : AuiView.TRANS_FADE_SCALE;
            v.startHidden = Flag(r.Doc.Root, "start-hidden");

            // ---- <view attach>: only what the markup says. Offsets, angles and speeds are the author's
            // to tune on the component and are left exactly as found.
            v.attach = c.Attach;
            if (c.Attach != null)
            {
                GameObject rootGo = v.gameObject;
                c.Attach.view = v;
                c.Attach.rootCanvas = rootGo.GetComponent<Canvas>();
                c.Attach.rootCollider = rootGo.GetComponent<BoxCollider>();
                c.Attach.interactive = r.Map.SignalCount > 0;
                c.Attach.mode = r.Info.Attach;
                c.Attach.corner = r.Info.AttachCorner;
                c.Attach.distance = r.Info.AttachDistance;
            }

            // ---- cascade tables (rules, declarations, class ids); the runtime re-resolves from these
            AuiRuleTable rt = r.Rules;
            v.classNames = rt.ClassNames; v.tagNames = rt.TagNames;
            v.ruleTag = rt.RuleTag; v.ruleClass = rt.RuleClass; v.ruleNode = rt.RuleNode; v.ruleState = rt.RuleState; v.ruleDeclStart = rt.RuleDeclStart; v.ruleDeclCount = rt.RuleDeclCount;
            v.declProp = rt.DeclProp; v.declRef = rt.DeclRef;
            var dv = new Vector4[rt.DeclProp.Length];
            for (int i = 0; i < dv.Length; i++) dv[i] = new Vector4(rt.DeclValue[4 * i], rt.DeclValue[4 * i + 1], rt.DeclValue[4 * i + 2], rt.DeclValue[4 * i + 3]);
            v.declValue = dv;
            var sprites = new Sprite[rt.RefNames.Length];
            var materials = new Material[rt.RefNames.Length];
            for (int i = 0; i < rt.RefNames.Length; i++)
            {
                int kind = rt.RefKind != null && i < rt.RefKind.Length ? rt.RefKind[i] : AuiRuleTable.REF_SPRITE;
                if (kind == AuiRuleTable.REF_SPRITE) sprites[i] = NamedSprite(c, rt.RefNames[i]);
                else if (kind == AuiRuleTable.REF_MATERIAL) materials[i] = NamedMaterial(c, rt.RefNames[i]);
                else materials[i] = ShaderMaterial(c, rt.RefNames[i]);
            }
            v.declSprite = sprites; v.declMaterial = materials;
            v.bucketStart = rt.BucketStart; v.bucketCount = rt.BucketCount; v.bucketRules = rt.BucketRules;
            v.tsStart = rt.TsStart; v.tsCount = rt.TsCount; v.tsProp = rt.TsProp; v.tsEase = rt.TsEase; v.tsDur = rt.TsDur;
            v.inheritedProps = rt.InheritedProps;
            v.elemTag = rt.ElemTag; v.elemNode = rt.ElemNode; v.elemSubtreeEnd = rt.ElemSubtreeEnd; v.elemClass0 = rt.ElemClass0; v.elemClass1 = rt.ElemClass1;
            v.classSlots = rt.ClassSlots;
            v.starNames = rt.StarNames; v.ruleStar = rt.RuleStar; v.elemStar = rt.ElemStar;
            v.refNames = rt.RefNames;
            // :root variables and the declarations built from them (SetStyleVariable re-encodes those in-world).
            v.varNames = rt.VarNames; v.varValues = rt.VarValues;
            var templates = new string[rt.DeclTemplate.Length];
            for (int i = 0; i < templates.Length; i++) templates[i] = rt.DeclTemplate[i] ?? "";   // Unity serialises a null string as "", so the runtime tests for empty
            v.declTemplate = templates;

            // ---- lookups for GetElement / SetIcon
            var idN = new List<string>(); var idE = new List<int>();
            foreach (AuiNode n in r.Doc.Nodes)
                if (n.Id != null && r.Map.Base[n.Index] >= 0) { idN.Add(n.Id); idE.Add(r.Map.Base[n.Index]); }
            v.idNames = idN.ToArray(); v.idElem = idE.ToArray();
            var icons = new List<string>();
            foreach (AuiNode n in r.Doc.Nodes)
            {
                string ic = n.Tag == "icon" ? n.Get("name") : n.Get("icon");
                if (ic != null && !icons.Contains(ic)) icons.Add(ic);
            }
            string preload = r.Doc.Root.Get("icons");
            if (preload != null)
                foreach (string ic in preload.Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries))
                    if (!icons.Contains(ic)) icons.Add(ic);
            var iconSprites = new Sprite[icons.Count];
            for (int i = 0; i < icons.Count; i++) iconSprites[i] = IconSprite(c, icons[i]);
            v.iconNames = icons.ToArray(); v.iconSprites = iconSprites;

            // ---- pages
            var gStart = new List<int>(); var gCount = new List<int>(); var gMode = new List<int>(); var gDefault = new List<int>();
            var pElem = new List<int>(); var pTab = new List<int>(); var pKind = new List<int>();
            foreach (AuiPageGroup g in r.Info.PageGroups)
            {
                gStart.Add(pElem.Count); gCount.Add(g.Pages.Count); gMode.Add(g.Mode == "sequential" ? AuiView.PAGE_SEQUENTIAL : AuiView.PAGE_CROSSFADE); gDefault.Add(g.DefaultIndex);
                foreach (AuiNode page in g.Pages)
                {
                    pElem.Add(r.Map.Elem(page, 0));
                    int tabSig = -1;
                    foreach (AuiNode tab in g.TabNodes) if (tab.Get("page") == page.Id) tabSig = r.Map.Signal(tab, 0);
                    pTab.Add(tabSig);
                    string pt = r.Styles[page.Index].PageTransition;
                    pKind.Add(pt == "none" ? AuiView.TRANS_NONE : pt == "slide-left" ? AuiView.TRANS_SLIDE_LEFT : pt == "slide-right" ? AuiView.TRANS_SLIDE_RIGHT : pt == "slide-up" ? AuiView.TRANS_SLIDE_UP : pt == "slide-down" ? AuiView.TRANS_SLIDE_DOWN : pt == "scale" ? AuiView.TRANS_SCALE : AuiView.TRANS_FADE);
                }
            }
            v.pageGroupStart = gStart.ToArray(); v.pageGroupCount = gCount.ToArray(); v.pageGroupMode = gMode.ToArray(); v.pageGroupDefault = gDefault.ToArray();
            v.pageElem = pElem.ToArray(); v.pageTabSig = pTab.ToArray(); v.pageKind = pKind.ToArray();

            // ---- repeats
            var rFirst = new List<int>(); var rStride = new List<int>(); var rMax = new List<int>(); var rEmpty = new List<GameObject>();
            var rTpl = new List<bool>(); var rElem = new List<int>();
            var pTag = new List<string>(); var pTpl = new List<int>();
            var sId = new List<string>(); var sView = new List<string>(); var sTpl = new List<int>();
            foreach (AuiRepeatInfo rep in r.Info.Repeats)
            {
                int ri = rFirst.Count;
                rFirst.Add(rep.Template != null ? r.Map.Elem(rep.Template, 0) : 0);
                rStride.Add(rep.Template != null ? r.Map.Stride[rep.Template.Index] : 1);
                rMax.Add(rep.Max);
                rEmpty.Add(rep.Empty != null ? c.Objects[r.Map.Elem(rep.Empty, 0)] : null);
                rTpl.Add(rep.IsTemplate);
                rElem.Add(r.Map.Elem(rep.Node, 0));
                if (rep.PoolTag != null) { pTag.Add(rep.PoolTag); pTpl.Add(ri); }
                if (rep.SlotId != null) { sId.Add(rep.SlotId); sView.Add(rep.SlotView); sTpl.Add(ri); }
            }
            v.repeatFirst = rFirst.ToArray(); v.repeatStride = rStride.ToArray(); v.repeatMax = rMax.ToArray(); v.repeatEmpty = rEmpty.ToArray();
            v.repeatIsTemplate = rTpl.ToArray(); v.repeatElem = rElem.ToArray();
            v.poolTag = pTag.ToArray(); v.poolTemplate = pTpl.ToArray();
            v.slotId = sId.ToArray(); v.slotView = sView.ToArray(); v.slotTemplate = sTpl.ToArray();

            // ---- selects
            var sElem = new List<int>(); var sPopup = new List<int>(); var sFirst = new List<int>(); var sCount = new List<int>(); var sScrim = new List<int>();
            var sShown = new List<int>(); var sRowH = new List<float>(); var sUp = new List<bool>(); var sEdge = new List<float>();
            foreach (AuiSelectInfo sel in r.Info.Selects)
            {
                sElem.Add(r.Map.Elem(sel.Node, 0));
                sPopup.Add(r.Map.PopupElem[sel.Node.Index]);
                sFirst.Add(sel.Options.Count > 0 ? r.Map.Elem(sel.Options[0], 0) : 0);
                // CAPACITY, not the resting count: every row `max` reserved is baked, and
                // SetSelectOptionLabel refuses any index past this. The resting count is
                // selectOptionShown below.
                sCount.Add(sel.Options.Count);
                sShown.Add(sel.Authored);
                sRowH.Add(c.SelectRowH[sel.Node.Index]);
                sUp.Add(c.SelectUp[sel.Node.Index]);
                sEdge.Add(c.SelectEdge[sel.Node.Index]);
                sScrim.Add(c.Signal[r.Map.ScrimElem[sel.Node.Index]]);
            }
            v.selectElem = sElem.ToArray(); v.selectPopupElem = sPopup.ToArray(); v.selectOptionFirst = sFirst.ToArray(); v.selectOptionCount = sCount.ToArray(); v.selectScrimSig = sScrim.ToArray();
            v.selectOptionShown = sShown.ToArray(); v.selectOptionH = sRowH.ToArray(); v.selectPopupUp = sUp.ToArray(); v.selectPopupEdge = sEdge.ToArray();

            // ---- modals
            var mElem = new List<int>(); var mScrim = new List<int>(); var mSig = new List<int>(); var mKind = new List<int>(); var mDis = new List<bool>(); var mOpen = new List<bool>();
            foreach (AuiModalInfo m in r.Info.Modals)
            {
                int se;
                if (!r.Map.ModalScrimElem.TryGetValue(m.Node.Index, out se)) continue;
                mElem.Add(r.Map.Elem(m.Node, 0));
                mScrim.Add(se);
                mSig.Add(c.Signal[se]);
                string mshow = m.Node.Get("show") ?? r.Styles[m.Node.Index].Show;
                mKind.Add(mshow == "none" ? AuiView.TRANS_NONE : mshow == "fade" ? AuiView.TRANS_FADE : mshow == "fade-slide" ? AuiView.TRANS_FADE_SLIDE : AuiView.TRANS_FADE_SCALE);
                mDis.Add(m.Dismissable);
                mOpen.Add(m.StartOpen);
            }
            v.modalElem = mElem.ToArray(); v.modalScrimElem = mScrim.ToArray(); v.modalScrimSig = mSig.ToArray(); v.modalKind = mKind.ToArray(); v.modalDismissable = mDis.ToArray(); v.modalStartOpen = mOpen.ToArray();

            // ---- layout tables for the runtime solver
            AuiLayoutTables lt = AuiLayoutTables.Build(r);
            v.layoutEnabled = true;
            v.layLangCount = lt.LangCount; v.layRootW = lt.RootW; v.layRootH = lt.RootH;
            v.layParent = lt.Parent; v.layFirstChild = lt.FirstChild; v.layNextSibling = lt.NextSibling; v.layKind = lt.Kind; v.layFlags = lt.Flags;
            v.layJustify = lt.Justify; v.layAlign = lt.Align; v.layAnchor = lt.Anchor; v.layRepeat = lt.Repeat; v.laySizeKind = lt.SizeKind; v.layEdgeKind = lt.EdgeKind;
            v.layGrow = lt.Grow; v.layGap = lt.Gap; v.layPad = lt.Pad; v.layMargin = lt.Margin; v.laySizeVal = lt.SizeVal; v.layEdgeVal = lt.EdgeVal;
            v.layIntrinsicW = lt.IntrinsicW; v.layIntrinsicH = lt.IntrinsicH; v.layLangW = lt.LangW; v.layLangH = lt.LangH; v.layContentW = lt.ContentW; v.layContentH = lt.ContentH;
            v.elemChildView = c.ChildView;

            // ---- nested view fields on the generated class
            foreach (KeyValuePair<AuiNestedView, AuiView> kv in c.NestedInstances)
            {
                var f = v.GetType().GetField(kv.Key.FieldName);
                if (f != null && f.FieldType.IsInstanceOfType(kv.Value)) f.SetValue(v, kv.Value);
                else AuiLog.Warn("could not assign nested view field '" + kv.Key.FieldName + "'");
            }
        }

        /// <summary>A destroyed or script-less component in the tree makes the prefab save throw from inside Odin; name it first.</summary>
        private static void AuditComponents(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                foreach (Component comp in t.GetComponents<Component>())
                {
                    if (comp == null) AuiLog.Warn("null/missing component on '" + t.name + "' (path " + HierarchyPath(t) + ")");
                }
                foreach (UdonSharpBehaviour ub in t.GetComponents<UdonSharpBehaviour>())
                {
                    if (ub == null) continue;
                    if (UdonSharpEditorUtility.GetBackingUdonBehaviour(ub) == null)
                        AuiLog.Warn("UdonSharp behaviour without a backing UdonBehaviour on '" + HierarchyPath(t) + "': " + ub.GetType().Name);
                }
            }
        }

        private static string HierarchyPath(Transform t)
        {
            string p = t.name;
            for (Transform x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
            return p;
        }

        // ================================================================ finish

        private static void Finish(Ctx c, GameObject root)
        {
            AuiView v = c.View;
            AuiProgramAssets.ApplySyncMode(v);
            EditorUtility.SetDirty(v);
            UdonSharpEditorUtility.CopyProxyToUdon(v);
            if (c.Attach != null)
            {
                AuiProgramAssets.ApplySyncMode(c.Attach);
                EditorUtility.SetDirty(c.Attach);
                UdonSharpEditorUtility.CopyProxyToUdon(c.Attach);
            }
            foreach (AuiSignal sig in c.Signals)
            {
                if (sig == null) continue;
                AuiProgramAssets.ApplySyncMode(sig);
                EditorUtility.SetDirty(sig);
                UdonSharpEditorUtility.CopyProxyToUdon(sig);
            }
            foreach (AuiView child in c.NestedInstances.Values) UdonSharpEditorUtility.CopyProxyToUdon(child);
            foreach (Selectable sel in root.GetComponentsInChildren<Selectable>(true))
            {
                var nav = sel.navigation; nav.mode = Navigation.Mode.None; sel.navigation = nav;
            }
            // The heap is the only thing worth believing: a table that did not reach it means an inert view.
            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(v);
            object probe;
            if (ub == null || !ub.publicVariables.TryGetVariableValue("elemObjects", out probe))
                AuiLog.Error(AuiCodes.ExportMissing + " " + c.R.ViewName + ": element tables did not reach the Udon heap (CopyProxyToUdon failed?).");
            var program = ub != null ? ub.programSource as UdonSharpProgramAsset : null;
            if (program != null && program.SerializedProgramAsset != null && program.SerializedProgramAsset.RetrieveProgram() != null)
            {
                var exports = new HashSet<string>(program.SerializedProgramAsset.RetrieveProgram().SymbolTable.GetExportedSymbols());
                foreach (string must in new[] { "inputResult", "inputResultId", "elemObjects", "elemRaw", "auiBakeHash", "layParent", "layLangW", "elemChildView", "ruleTag", "classNames", "declValue", "idNames", "iconSprites", "modalElem", "repeatIsTemplate", "poolTag", "ruleStar", "starNames", "varNames", "declTemplate" })
                    if (!exports.Contains(must)) AuiLog.Error(AuiCodes.ExportMissing + " " + c.R.ViewName + ": '" + must + "' is not an exported symbol; a cross-behaviour write to it would be a silent no-op.");
            }
        }
    }
}
