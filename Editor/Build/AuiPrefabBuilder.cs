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
    /// by name at runtime (AuiView._AuiStart) or wired by AUI/Wire Scene Views.
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
        public const int BuilderVersion = 11;

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
            public int N;
            public GameObject[] Objects;
            public RectTransform[] Rects;
            public CanvasGroup[] Groups;
            public Image[] Bg, Border, Icon;
            public TextMeshProUGUI[] Text;
            public Button[] Buttons;
            public Slider[] Sliders;
            public Vector2[] BasePos;
            public int[] Signal, Key, Kind;
            public AuiSignal[] Signals;
            public int[] SigElem;
            public List<AuiNestedView> Nested = new List<AuiNestedView>();
            public Dictionary<AuiNestedView, AuiView> NestedInstances = new Dictionary<AuiNestedView, AuiView>();
            public Transform Content;
            public RectTransform ContentRect;
            public List<GameObject> Popups = new List<GameObject>();
        }

        // ================================================================ entry

        public static bool Build(AuiCompileResult r, AuiSettings settings, AuiWorkspace ws)
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
                AuiLog.Warn(r.ViewName + ": class not available yet (" + cs + "); will retry after the next compile.");
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
                AuiLog.Error(r.ViewName + ": UdonSharp did not produce a program for " + cs + ". Fix the U# errors above.");
                return false;
            }

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
            {
                var ev = existing.GetComponent<AuiView>();
                if (ev != null && ev.auiBakeHash == BakeStamp(r) && !AuiBuildQueue.Forced) return true;
            }

            var c = new Ctx { R = r, Settings = settings, Ws = ws, Fonts = settings != null ? settings.fontSet : null };
            c.Lang = ws.LoadLanguageTable(settings != null ? settings.defaultLanguage : "en", null, null);
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
                RevertInstanceSizeOverrides(prefabPath, type);
                AuiLog.Info("built " + prefabPath + " (" + c.N + " elements, " + r.Map.SignalCount + " signals, " + r.Info.Keys.Count + " keys)");
                return true;
            }
            catch (System.Exception e)
            {
                AuiLog.Error(r.ViewName + ": build failed: " + e);
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
        private static void RevertInstanceSizeOverrides(string prefabPath, System.Type viewType)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null) return;
            foreach (Component v in Object.FindObjectsOfType(viewType, true))
            {
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
                    t == typeof(VRCUiShape) || t == typeof(BoxCollider) || t == viewType || t == typeof(VRC.Udon.UdonBehaviour)) continue;
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
            c.BasePos = new Vector2[n];
            c.Signal = new int[n];
            c.Key = new int[n];
            c.Kind = new int[n];
            for (int i = 0; i < n; i++) { c.Signal[i] = c.R.Map.SignalOf[i]; c.Key[i] = -1; c.Kind[i] = c.R.Map.KindOf[i]; }
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
            root.name = r.ViewName;
            var rrt = (RectTransform)root.transform;
            rrt.sizeDelta = new Vector2(W, H);
            if (reuse == null) rrt.localScale = Vector3.one * CanvasScale;
            var canvas = Ensure<Canvas>(root);
            canvas.renderMode = RenderMode.WorldSpace;
            // 1 unit = 1 mm everywhere. uGUI divides a sprite's PPU by the canvas reference (100)
            // when slicing, so the package sprites are imported at 100 PPU; setting the reference to
            // 1 here was measured NOT to survive into the saved prefab.
            Ensure<GraphicRaycaster>(root);
            // Without VRCUiShape the laser passes straight through and nothing can be pressed, with no symptom in the editor.
            Ensure<VRCUiShape>(root);
            var bc = Ensure<BoxCollider>(root);
            bc.isTrigger = true;
            bc.center = Vector3.zero;
            bc.size = new Vector3(W, H, 10f);

            c.View = root.GetComponent(type) as AuiView;
            if (c.View == null) c.View = (AuiView)UdonSharpUndo.AddComponent(root, type);

            // Content: what Show/Hide fades and deactivates. Element 0 (the <view>) lives on it.
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one; crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            crt.localScale = Vector3.one;
            var cg = content.AddComponent<CanvasGroup>();
            c.Content = content.transform;
            c.ContentRect = crt;
            c.View.content = content;
            c.View.contentGroup = cg;
            c.View.contentRect = crt;

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
            if (n.Tag == "repeat") { BuildRepeat(c, n, parent); return; }
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

            var go = new GameObject(Name(n), typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            AuiLayoutNode pl = parentNode != null ? c.R.Layout[parentNode.Index] : null;
            float pw = pl != null ? pl.Local.W : c.R.Info.Width, ph = pl != null ? pl.Local.H : c.R.Info.Height;
            if (parentNode != null && parentNode.Tag == "repeat" && row > 0) { }
            Place(rt, local, ln.AnchorFill, pw, ph);
            Register(c, e, go, rt, rt.anchoredPosition);
            if (n.HasAttribute("hidden") && n.Get("hidden") != "false") go.SetActive(false);

            bool interactive = AuiIndexMap.KindOfTag(n.Tag) != AuiIndexMap.KIND_NONE || c.Signal[e] >= 0;
            switch (n.Tag)
            {
                case "text": BuildText(c, n, e, go, s, local, parentNode); break;
                case "icon": BuildIcon(c, n, e, go, s); break;
                case "image": BuildImage(c, n, e, go, s); break;
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
                    if (grp != null && grp.IndexOfPage(n.Id) != grp.DefaultIndex)
                    {
                        c.Groups[e].alpha = 0f;
                        c.Groups[e].blocksRaycasts = false;
                        c.Groups[e].interactable = false;
                        go.SetActive(false);
                    }
                    break;
                }
                default:
                    // panel/div, button, toggle, tab, tabs, pages, option, empty
                    ApplyBackground(c, n, e, go.transform, interactive);
                    if (n.Tag == "button" || n.Tag == "toggle" || n.Tag == "tab" || n.Tag == "option")
                        BuildClickableContent(c, n, e, go, s, local);
                    break;
            }

            if (NeedsGroup(s) && c.Groups[e] == null)
            {
                c.Groups[e] = go.AddComponent<CanvasGroup>();
                c.Groups[e].alpha = s.Opacity;
            }
            if (s.Scale != 1f) rt.localScale = new Vector3(s.Scale, s.Scale, 1f);

            if (c.Signal[e] >= 0) AddSignal(c, n, e, go, s, interactive);

            foreach (AuiNode child in n.Children) BuildNode(c, child, go.transform, row, n);
        }

        private static string Name(AuiNode n)
        {
            return n.Id != null ? n.Tag + "#" + n.Id : n.Tag;
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

        private static Sprite RoundedSprite(Ctx c, float radius, bool border)
        {
            int best = 0;
            foreach (int r in AuiSpriteGenerator.Radii) if (r <= radius + 0.01f) best = r;
            string name = radius <= 0 ? "Square" : (border ? "Border" : "Rounded") + (best == 0 ? AuiSpriteGenerator.Radii[0] : best);
            if (radius <= 0 && border) name = "Border4";
            return AssetDatabase.LoadAssetAtPath<Sprite>(AuiEditorEnv.PackageAssetRoot + "/Sprites/" + name + ".png");
        }

        private static Sprite NamedSprite(Ctx c, string name)
        {
            // background: sprite(Name) -> package Sprites first, then any sprite asset with that name in the project
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(AuiEditorEnv.PackageAssetRoot + "/Sprites/" + name + ".png");
            if (s != null) return s;
            foreach (string guid in AssetDatabase.FindAssets(name + " t:Sprite"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<Sprite>(p);
            }
            AuiLog.Warn("sprite '" + name + "' not found");
            return null;
        }

        private static Color ToColor(AuiColor c)
        {
            return new Color(c.R, c.G, c.B, c.A);
        }

        /// <summary>Background image on the element itself (elemBg) and an optional Border child (elemBorder).</summary>
        private static void ApplyBackground(Ctx c, AuiNode n, int e, Transform t, bool raycast)
        {
            AuiResolvedStyle s = c.R.Styles[n.Index];
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
            string v = c.Lang != null ? c.Lang.GetWithFallback(c.Lang.DefaultLanguage, key) : null;
            return v ?? key;
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
            t.text = initial ?? "";
            c.Text[e] = t;
            c.Key[e] = KeyIndex(c, key);
            if (n.Get("text") != null && !s.Width.IsSet)
            {
                // RUNTIME TEXT IS BOXED, NOT GROWN. The solver reserved a sample width; a preferred-size
                // fitter let a long string run past the row and off the canvas. The box is the
                // reserved width, widened to the parent's remaining content width when nothing
                // follows the text in its row, and the text ellipsises inside it. max-width stays a cap.
                var rt = (RectTransform)go.transform;
                float w = local.W;
                if (!s.MaxWidth.IsSet && parentNode != null && IsLastFlowChild(c, n, parentNode))
                {
                    AuiLayoutNode pl = c.R.Layout[parentNode.Index];
                    float innerRight = pl.Local.W - c.R.Styles[parentNode.Index].Padding.Right;
                    w = Mathf.Max(w, innerRight - local.X);
                }
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(w, local.H);
                // The row's rect, not the template's: every row of a repeat used to stack on row 0.
                rt.anchoredPosition = new Vector2(local.X, -(local.Y + local.H * 0.5f));
                c.BasePos[e] = rt.anchoredPosition;
                if (wrap)
                {
                    // Wrapped runtime text keeps its width and grows downward within what the solver reserved.
                    var fit = go.AddComponent<ContentSizeFitter>();
                    fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                    fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                    rt.pivot = new Vector2(0f, 1f);
                    rt.anchoredPosition = new Vector2(local.X, -local.Y);
                    c.BasePos[e] = rt.anchoredPosition;
                }
            }
        }

        /// <summary>True when no in-flow sibling (anything but anchored elements and the repeat's &lt;empty&gt;) follows n in its parent.</summary>
        private static bool IsLastFlowChild(Ctx c, AuiNode n, AuiNode parentNode)
        {
            bool after = false;
            foreach (AuiNode sib in parentNode.Children)
            {
                if (sib == n) { after = true; continue; }
                if (!after || sib.Tag == "empty") continue;
                if (c.R.Styles[sib.Index].Anchor == "none") return false;
            }
            return true;
        }

        private static bool Flag(AuiNode n, string attr)
        {
            string v = n.Get(attr);
            return v != null && v != "false";
        }

        private static Sprite IconSprite(Ctx c, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (string p in c.Ws.IconPaths)
            {
                if (Path.GetFileNameWithoutExtension(p) != name) continue;
                string ap = AuiEditorEnv.AssetPath(p);
                if (ap == null) continue;
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(ap);
                if (sp != null) return sp;
            }
            AuiLog.Warn("icon '" + name + "' has no sprite (is the SVG imported as a Textured Sprite?)");
            return null;
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
            handleImg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AuiEditorEnv.PackageAssetRoot + "/Sprites/Circle.png");
            handleImg.type = Image.Type.Simple; handleImg.preserveAspect = true; handleImg.color = fill; handleImg.raycastTarget = true;
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
            float optH = 0f;
            foreach (AuiNode o in n.Children) optH += c.R.Layout[o.Index].Local.H;
            var popup = new GameObject(Name(n) + " popup", typeof(RectTransform));
            popup.transform.SetParent(c.Content, false);
            var prt = (RectTransform)popup.transform;
            // INSIDE THE VIEW, ALWAYS. The laser only reaches what the root BoxCollider covers, so a
            // popup hanging below the panel is visible and unclickable. Open upward when there is no
            // room below, and clamp horizontally.
            float popupH = optH + 8f;
            float viewW = c.R.Info.Width, viewH = c.R.Info.Height;
            float py = hl.Absolute.Y + hl.Absolute.H + 4f;
            if (py + popupH > viewH - 4f) py = hl.Absolute.Y - 4f - popupH;
            if (py < 4f) py = 4f;
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
            pimg.sprite = RoundedSprite(c, s.Radius, false);
            pimg.type = Image.Type.Sliced;
            AuiBackground bg = s.Background;
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
            var sbtn = scrim.AddComponent<Button>();
            sbtn.transition = Selectable.Transition.None;
            var snav = sbtn.navigation; snav.mode = Navigation.Mode.None; sbtn.navigation = snav;
            c.Buttons[se] = sbtn;
            AuiSignal ssig = MakeSignal(c, scrim, c.Signal[se]);
            Wire(sbtn.onClick, ssig, "_Click");

            // options
            float y = 4f;
            foreach (AuiNode o in n.Children)
            {
                int oe = c.R.Map.Elem(o, row);
                AuiLayoutNode ol = c.R.Layout[o.Index];
                AuiResolvedStyle os = c.R.Styles[o.Index];
                var ogo = new GameObject(Name(o), typeof(RectTransform));
                ogo.transform.SetParent(popup.transform, false);
                var ort = (RectTransform)ogo.transform;
                Place(ort, new AuiRect(4f, y, popupRect.W - 8f, ol.Local.H), false, popupRect.W, popupRect.H);
                Register(c, oe, ogo, ort, ort.anchoredPosition);
                ApplyBackground(c, o, oe, ogo.transform, true);
                BuildClickableContent(c, o, oe, ogo, os, new AuiRect(0, 0, popupRect.W - 8f, ol.Local.H));
                if (c.Signal[oe] >= 0) AddSignal(c, o, oe, ogo, os, true);
                y += ol.Local.H;
            }
            popup.SetActive(false);
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
            Place(rt, ln.Local, ln.AnchorFill, pl.Local.W, pl.Local.H);
            Register(c, e, go, rt, rt.anchoredPosition);
            ApplyBackground(c, n, e, go.transform, false);
            if (r == null || r.Template == null) return;
            for (int row = 0; row < r.Max; row++) BuildNode(c, r.Template, go.transform, row, n);
            if (r.Empty != null) BuildNode(c, r.Empty, go.transform, 0, n);
        }

        private static void BuildNested(Ctx c, AuiNode n, Transform parent, int row)
        {
            AuiNestedView nv = null;
            foreach (AuiNestedView x in c.R.Info.NestedViews) if (x.Node == n) nv = x;
            string cp;
            if (nv == null || !c.Ws.Views.TryGetValue(n.Tag, out cp)) return;
            string prefabPath = AuiEditorEnv.AssetPath(Path.ChangeExtension(cp, ".prefab"));
            var prefab = prefabPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) : null;
            if (prefab == null) { AuiLog.Error("nested view '" + n.Tag + "' has no prefab yet at " + prefabPath); return; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            inst.name = Name(n);
            var rt = (RectTransform)inst.transform;
            AuiLayoutNode ln = c.R.Layout[n.Index];
            AuiLayoutNode pl = c.R.Layout[n.Parent.Index];
            Place(rt, ln.Local, ln.AnchorFill, pl.Local.W, pl.Local.H);
            // A nested view is not its own canvas: drop the pieces that only make sense on a root.
            foreach (var comp in new Component[] { inst.GetComponent<VRCUiShape>(), inst.GetComponent<BoxCollider>(), inst.GetComponent<GraphicRaycaster>(), inst.GetComponent<Canvas>() })
                if (comp != null) Object.DestroyImmediate(comp);
            var child = inst.GetComponent<AuiView>();
            if (child != null)
            {
                child.parentView = c.View;
                EditorUtility.SetDirty(child);
                c.NestedInstances[nv] = child;
            }
            // Element index reserved by the map for the component node
            int e = c.R.Map.Elem(n, row);
            if (e >= 0) Register(c, e, inst, rt, rt.anchoredPosition);
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

        private static int PropId(string prop)
        {
            switch (prop)
            {
                case "background": return AuiView.PROP_BG;
                case "color": return AuiView.PROP_COLOR;
                case "icon-color": return AuiView.PROP_ICON;
                case "border": return AuiView.PROP_BORDER;
                case "opacity": return AuiView.PROP_OPACITY;
                case "scale": return AuiView.PROP_SCALE;
                case "offset": return AuiView.PROP_OFFSET;
            }
            return -1;
        }

        private static int EaseId(string ease)
        {
            string name = "";
            foreach (string part in ease.Split('-')) if (part.Length > 0) name += char.ToUpperInvariant(part[0]) + part.Substring(1);
            try { return (int)System.Enum.Parse(typeof(VRCTweenEase), name); }
            catch { return (int)VRCTweenEase.OutQuad; }
        }

        private static readonly string[] StateNames = { "normal", "hover", "pressed", "selected", "disabled" };

        private static void FillTables(Ctx c)
        {
            AuiView v = c.View;
            AuiCompileResult r = c.R;
            v.elemObjects = c.Objects; v.elemRects = c.Rects; v.elemGroups = c.Groups; v.elemBg = c.Bg; v.elemBorder = c.Border;
            v.elemIcon = c.Icon; v.elemText = c.Text; v.elemButtons = c.Buttons; v.elemSliders = c.Sliders; v.elemBasePos = c.BasePos;
            v.elemSignal = c.Signal; v.elemKey = c.Key; v.elemKind = c.Kind;
            v.sigElem = c.SigElem; v.signals = c.Signals;
            v.auiBakeHash = BakeStamp(r);

            // ---- view transition
            AuiResolvedStyle rs = r.Styles[r.Doc.Root.Index];
            string show = r.Doc.Root.Get("show") ?? rs.Show;
            v.showKind = show == "none" ? AuiView.TRANS_NONE : show == "fade" ? AuiView.TRANS_FADE : show == "fade-slide" ? AuiView.TRANS_FADE_SLIDE : AuiView.TRANS_FADE_SCALE;
            v.startHidden = Flag(r.Doc.Root, "start-hidden");

            // ---- style rows
            int sigCount = r.Map.SignalCount;
            var rangeStart = new int[sigCount * AuiView.STATE_COUNT];
            var rangeCount = new int[sigCount * AuiView.STATE_COUNT];
            var prop = new List<int>(); var val = new List<Vector4>(); var spr = new List<Sprite>(); var dur = new List<float>(); var ease = new List<int>(); var chan = new List<int>();
            int channels = 0;
            for (int id = 0; id < sigCount; id++)
            {
                int e = c.SigElem[id];
                AuiNode n = r.Map.NodeOf[e];
                if (n == null) { for (int st = 0; st < AuiView.STATE_COUNT; st++) rangeStart[id * AuiView.STATE_COUNT + st] = prop.Count; continue; }
                AuiResolvedStyle s = r.Styles[n.Index];
                // Which properties any state touches: those get a normal baseline row.
                var touched = new List<string>();
                foreach (KeyValuePair<string, Dictionary<string, string>> st in s.States)
                    foreach (string p in st.Value.Keys) if (PropId(p) >= 0 && !touched.Contains(p)) touched.Add(p);
                touched.Sort(string.CompareOrdinal);
                var channelOf = new Dictionary<string, int>();
                foreach (string p in touched) channelOf[p] = channels++;

                for (int st = 0; st < AuiView.STATE_COUNT; st++)
                {
                    int range = id * AuiView.STATE_COUNT + st;
                    rangeStart[range] = prop.Count;
                    Dictionary<string, string> values = null;
                    if (st == 0) { values = new Dictionary<string, string>(); foreach (string p in touched) { string nv = s.Get(p); if (nv != null || p == "scale" || p == "opacity" || p == "offset") values[p] = nv ?? (p == "scale" ? "1" : p == "opacity" ? "1" : "0 0"); } }
                    else s.States.TryGetValue(StateNames[st], out values);
                    if (values != null)
                        foreach (string p in touched)
                        {
                            string raw;
                            if (!values.TryGetValue(p, out raw)) continue;
                            float d; string es;
                            if (!s.TransitionFor(p, out d, out es)) { d = 0f; es = "out-quad"; }
                            Vector4 vec; Sprite sprite;
                            if (!RowValue(c, p, raw, s, out vec, out sprite)) continue;
                            prop.Add(PropId(p)); val.Add(vec); spr.Add(null); dur.Add(d); ease.Add(EaseId(es)); chan.Add(channelOf[p]);
                            if (sprite != null && p == "background")
                            {
                                prop.Add(AuiView.PROP_SPRITE); val.Add(Vector4.zero); spr.Add(sprite); dur.Add(0f); ease.Add(0); chan.Add(-1);
                            }
                        }
                    rangeCount[range] = prop.Count - rangeStart[range];
                }
            }
            v.styleRangeStart = rangeStart; v.styleRangeCount = rangeCount;
            v.styleProp = prop.ToArray(); v.styleValue = val.ToArray(); v.styleSprite = spr.ToArray(); v.styleDur = dur.ToArray(); v.styleEase = ease.ToArray(); v.styleChannel = chan.ToArray();
            v.channelCount = channels;

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
            foreach (AuiRepeatInfo rep in r.Info.Repeats)
            {
                rFirst.Add(rep.Template != null ? r.Map.Elem(rep.Template, 0) : 0);
                rStride.Add(rep.Template != null ? r.Map.Stride[rep.Template.Index] : 1);
                rMax.Add(rep.Max);
                rEmpty.Add(rep.Empty != null ? c.Objects[r.Map.Elem(rep.Empty, 0)] : null);
            }
            v.repeatFirst = rFirst.ToArray(); v.repeatStride = rStride.ToArray(); v.repeatMax = rMax.ToArray(); v.repeatEmpty = rEmpty.ToArray();

            // ---- selects
            var sElem = new List<int>(); var sPopup = new List<int>(); var sFirst = new List<int>(); var sCount = new List<int>(); var sScrim = new List<int>();
            foreach (AuiSelectInfo sel in r.Info.Selects)
            {
                sElem.Add(r.Map.Elem(sel.Node, 0));
                sPopup.Add(r.Map.PopupElem[sel.Node.Index]);
                sFirst.Add(sel.Options.Count > 0 ? r.Map.Elem(sel.Options[0], 0) : 0);
                sCount.Add(sel.Options.Count);
                sScrim.Add(c.Signal[r.Map.ScrimElem[sel.Node.Index]]);
            }
            v.selectElem = sElem.ToArray(); v.selectPopupElem = sPopup.ToArray(); v.selectOptionFirst = sFirst.ToArray(); v.selectOptionCount = sCount.ToArray(); v.selectScrimSig = sScrim.ToArray();

            // ---- nested view fields on the generated class
            foreach (KeyValuePair<AuiNestedView, AuiView> kv in c.NestedInstances)
            {
                var f = v.GetType().GetField(kv.Key.FieldName);
                if (f != null && f.FieldType.IsInstanceOfType(kv.Value)) f.SetValue(v, kv.Value);
                else AuiLog.Warn("could not assign nested view field '" + kv.Key.FieldName + "'");
            }
        }

        private static bool RowValue(Ctx c, string prop, string raw, AuiResolvedStyle s, out Vector4 vec, out Sprite sprite)
        {
            vec = Vector4.zero;
            sprite = null;
            switch (prop)
            {
                case "background":
                {
                    AuiBackground bg;
                    if (!AuiBackground.TryParse(raw, out bg)) return false;
                    Color col = bg.IsNone ? new Color(0, 0, 0, 0) : ToColor(bg.Color);
                    vec = new Vector4(col.r, col.g, col.b, col.a);
                    if (bg.Sprite != null) sprite = NamedSprite(c, bg.Sprite);
                    return true;
                }
                case "color":
                case "icon-color":
                case "border":
                {
                    AuiColor col;
                    if (!AuiColor.TryParse(raw, out col)) return false;
                    vec = new Vector4(col.R, col.G, col.B, col.A);
                    return true;
                }
                case "opacity":
                case "scale":
                {
                    float f;
                    if (!AuiNum.TryParse(raw, out f)) return false;
                    vec = new Vector4(f, 0, 0, 0);
                    return true;
                }
                case "offset":
                {
                    string[] parts = raw.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    float x, y;
                    if (parts.Length != 2 || !AuiNum.TryParse(parts[0], out x) || !AuiNum.TryParse(parts[1], out y)) return false;
                    vec = new Vector4(x, y, 0, 0);
                    return true;
                }
            }
            return false;
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
                AuiLog.Error(c.R.ViewName + ": element tables did not reach the Udon heap (CopyProxyToUdon failed?).");
            var program = ub != null ? ub.programSource as UdonSharpProgramAsset : null;
            if (program != null && program.SerializedProgramAsset != null && program.SerializedProgramAsset.RetrieveProgram() != null)
            {
                var exports = new HashSet<string>(program.SerializedProgramAsset.RetrieveProgram().SymbolTable.GetExportedSymbols());
                foreach (string must in new[] { "inputResult", "inputResultId", "elemObjects", "auiBakeHash" })
                    if (!exports.Contains(must)) AuiLog.Error(c.R.ViewName + ": '" + must + "' is not an exported symbol; a cross-behaviour write to it would be a silent no-op.");
            }
        }
    }
}
