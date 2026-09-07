using System.Collections.Generic;
using System.Text;

namespace Armagan.AUI.Core
{
    /// <summary>
    /// The cascade as flat tables the runtime can re-run: every rule of every sheet plus every
    /// style="" attribute, already sorted by (tier, specificity, source order) so the Udon
    /// resolver only walks forward and lets later rules overwrite. Declarations are typed into
    /// float4 values plus a reference index (sprite name, transition set) here, once, at bake.
    ///
    /// Mirrored by Runtime/AuiView.Styles.cs (PROP_*, encodings, the matching rule). The
    /// reference resolver at the bottom is the same algorithm in C#, for the parity tests.
    /// </summary>
    public sealed class AuiRuleTable
    {
        // ---- property ids (runtime-changeable properties only)
        public const int PROP_BG = 0, PROP_COLOR = 1, PROP_ICON = 2, PROP_BORDER = 3, PROP_OPACITY = 4, PROP_SCALE = 5, PROP_OFFSET = 6, PROP_SPRITE = 7,
            PROP_MATERIAL = 8, PROP_FONT_SIZE = 9, PROP_FONT = 10, PROP_TEXT_ALIGN = 11, PROP_LINE_HEIGHT = 12, PROP_DISPLAY = 13, PROP_POSITION = 14,
            PROP_WIDTH = 15, PROP_HEIGHT = 16, PROP_MIN_WIDTH = 17, PROP_MIN_HEIGHT = 18, PROP_MAX_WIDTH = 19, PROP_MAX_HEIGHT = 20, PROP_ANCHOR = 21,
            PROP_PADDING = 22, PROP_MARGIN = 23, PROP_GAP = 24, PROP_FLEX_DIRECTION = 25, PROP_JUSTIFY = 26, PROP_ALIGN = 27, PROP_GROW = 28, PROP_WRAP = 29,
            PROP_TOP = 30, PROP_RIGHT = 31, PROP_BOTTOM = 32, PROP_LEFT = 33, PROP_TRANSITION = 34;
        public const int PROP_COUNT = 35;
        /// <summary>Properties that tween: one VRCTween channel per (element, animatable) at runtime.</summary>
        public const int ANIM_COUNT = 7; // BG COLOR ICON BORDER OPACITY SCALE OFFSET
        public const int MAX_CLASSES = 64;
        /// <summary>Star (style-only) classes: 8 int words per element.</summary>
        public const int MAX_STARS = 256, STAR_WORDS = 8;
        /// <summary>Runtime class ids at or above this are star classes (id - STAR_BASE indexes StarNames).</summary>
        public const int STAR_BASE = 1000;
        public const int STATE_NORMAL = 0, STATE_HOVER = 1, STATE_PRESSED = 2, STATE_SELECTED = 3, STATE_DISABLED = 4;

        /// <summary>Class names in id order (sorted), for C_ constants and the runtime name lookup.</summary>
        public string[] ClassNames;
        /// <summary>Star class names the view references (with their leading '*'), in id order; only these utilities are baked.</summary>
        public string[] StarNames;
        /// <summary>Per rule: star class id, or -1 (a rule on a star class the view never references is dropped).</summary>
        public int[] RuleStar;
        /// <summary>Per element: STAR_WORDS int words of static star classes.</summary>
        public int[] ElemStar;
        /// <summary>Element tags in id order (div is panel).</summary>
        public string[] TagNames;
        /// <summary>Names the decl refs point at: sprites (and later materials), in one table.</summary>
        public string[] RefNames;
        /// <summary>Which ref names are sprites (true) versus materials / shaders (false).</summary>
        public bool[] RefIsSprite;
        /// <summary>REF_SPRITE, REF_MATERIAL (a Material asset by name) or REF_SHADER (a shader name; the builder makes the material).</summary>
        public int[] RefKind;
        public const int REF_SPRITE = 0, REF_MATERIAL = 1, REF_SHADER = 2;

        // per rule, in cascade order
        public int[] RuleTag, RuleClass, RuleNode, RuleState, RuleDeclStart, RuleDeclCount;
        // per declaration
        public int[] DeclProp, DeclRef;
        public float[] DeclValue;   // 4 per declaration
        // rule buckets per tag id: rules whose tag is -1 or that tag, in cascade order
        public int[] BucketStart, BucketCount, BucketRules;
        // transition sets: each is a list of (prop or -1 for all, duration, ease id)
        public int[] TsStart, TsCount, TsProp, TsEase;
        public float[] TsDur;
        /// <summary>Property ids that inherit from the parent element.</summary>
        public int[] InheritedProps;
        /// <summary>:root variables the cascade merged (tier order, later wins), sorted by name ("--accent"), with their baked values.</summary>
        public string[] VarNames, VarValues;
        /// <summary>Per declaration: the raw value when it references var(), else null. SetStyleVariable re-substitutes and re-encodes it in-world.</summary>
        public string[] DeclTemplate;

        // per element (runtime element index)
        public int[] ElemTag, ElemNode, ElemSubtreeEnd, ElemClass0, ElemClass1, ElemClassSlots;
        public int ClassSlots;

        public int RuleCount { get { return RuleTag.Length; } }

        // ================================================================ encoding

        public static int PropId(string name)
        {
            switch (name)
            {
                case "background": return PROP_BG;
                case "color": return PROP_COLOR;
                case "icon-color": return PROP_ICON;
                case "border": return PROP_BORDER;
                case "opacity": return PROP_OPACITY;
                case "scale": return PROP_SCALE;
                case "offset": return PROP_OFFSET;
                case "font-size": return PROP_FONT_SIZE;
                case "font": return PROP_FONT;
                case "text-align": return PROP_TEXT_ALIGN;
                case "line-height": return PROP_LINE_HEIGHT;
                case "display": return PROP_DISPLAY;
                case "position": return PROP_POSITION;
                case "width": return PROP_WIDTH;
                case "height": return PROP_HEIGHT;
                case "min-width": return PROP_MIN_WIDTH;
                case "min-height": return PROP_MIN_HEIGHT;
                case "max-width": return PROP_MAX_WIDTH;
                case "max-height": return PROP_MAX_HEIGHT;
                case "anchor": return PROP_ANCHOR;
                case "padding": return PROP_PADDING;
                case "margin": return PROP_MARGIN;
                case "gap": return PROP_GAP;
                case "flex-direction": return PROP_FLEX_DIRECTION;
                case "justify": return PROP_JUSTIFY;
                case "align": return PROP_ALIGN;
                case "grow": return PROP_GROW;
                case "wrap": return PROP_WRAP;
                case "top": return PROP_TOP;
                case "right": return PROP_RIGHT;
                case "bottom": return PROP_BOTTOM;
                case "left": return PROP_LEFT;
                case "transition": return PROP_TRANSITION;
            }
            return -1;
        }

        public static int AnimIndex(int prop)
        {
            return prop >= PROP_BG && prop <= PROP_OFFSET ? prop : -1;
        }

        /// <summary>Encode one validated, substituted value. Returns false for bake-only or unknown properties.</summary>
        public static bool Encode(string prop, string v, out float[] value, out string refName)
        {
            value = new float[4];
            refName = null;
            switch (prop)
            {
                case "background":
                {
                    AuiBackground bg;
                    if (!AuiBackground.TryParse(v, out bg)) return false;
                    AuiColor c = bg.IsNone ? AuiColor.Transparent : bg.Color;
                    value[0] = c.R; value[1] = c.G; value[2] = c.B; value[3] = c.A;
                    refName = bg.Sprite;
                    return true;
                }
                case "color": case "icon-color": case "border":
                {
                    AuiColor c;
                    if (!AuiColor.TryParse(v, out c)) return false;
                    value[0] = c.R; value[1] = c.G; value[2] = c.B; value[3] = c.A;
                    return true;
                }
                case "opacity": case "scale": case "grow": case "line-height":
                {
                    float f;
                    if (!AuiNum.TryParse(v, out f)) return false;
                    value[0] = f;
                    return true;
                }
                case "offset":
                {
                    string[] parts = v.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    float x, y;
                    if (parts.Length != 2 || !AuiNum.TryParse(parts[0], out x) || !AuiNum.TryParse(parts[1], out y)) return false;
                    value[0] = x; value[1] = y;
                    return true;
                }
                case "font-size": case "width": case "height": case "min-width": case "min-height": case "max-width": case "max-height":
                case "top": case "right": case "bottom": case "left": case "gap":
                {
                    AuiLength l;
                    if (!AuiLength.TryParse(v, out l)) return false;
                    value[0] = l.IsSet && !l.IsAuto ? l.Value : 0;
                    value[1] = !l.IsSet ? AuiLayoutTables.SIZE_UNSET : l.IsAuto ? AuiLayoutTables.SIZE_AUTO : l.IsPercent ? AuiLayoutTables.SIZE_PCT : AuiLayoutTables.SIZE_MM;
                    return true;
                }
                case "font":
                {
                    // Only the size half is runtime-changeable; the weight is baked into the font asset.
                    string[] parts = v.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    float size;
                    if (parts.Length < 2 || !AuiNum.TryParse(parts[1], out size)) return false;
                    value[0] = size; value[1] = AuiLayoutTables.SIZE_MM;
                    return true;
                }
                case "padding": case "margin":
                {
                    AuiBox b;
                    if (!AuiBox.TryParse(v, out b)) return false;
                    value[0] = b.Top; value[1] = b.Right; value[2] = b.Bottom; value[3] = b.Left;
                    return true;
                }
                case "display": value[0] = v == "none" ? 1 : 0; return true;
                case "position": value[0] = v == "absolute" ? 1 : 0; return true;
                case "anchor": value[0] = AuiLayoutTables.AnchorOf(v); return true;
                case "flex-direction": value[0] = v == "row" ? 1 : 0; return true;
                case "justify": value[0] = v == "center" ? AuiLayoutTables.JUSTIFY_CENTER : v == "end" ? AuiLayoutTables.JUSTIFY_END : v == "space-between" ? AuiLayoutTables.JUSTIFY_BETWEEN : AuiLayoutTables.JUSTIFY_START; return true;
                case "align": value[0] = v == "start" ? AuiLayoutTables.ALIGN_START : v == "center" ? AuiLayoutTables.ALIGN_CENTER : v == "end" ? AuiLayoutTables.ALIGN_END : AuiLayoutTables.ALIGN_STRETCH; return true;
                case "wrap": value[0] = v == "true" ? 1 : 0; return true;
                case "text-align": value[0] = v == "center" ? 1 : v == "right" ? 2 : 0; return true;
                case "transition": refName = "transition:" + v; return true;
            }
            return false;
        }

        // ================================================================ build

        private sealed class Cand
        {
            public AuiStyleRule Rule;
            public AuiSelector Sel;
            public int Tier;
            public int Node = -1;
        }

        private static int Compare(Cand a, Cand b)
        {
            if (a.Tier != b.Tier) return a.Tier.CompareTo(b.Tier);
            int sa = a.Sel != null ? a.Sel.Specificity : 0, sb = b.Sel != null ? b.Sel.Specificity : 0;
            if (sa != sb) return sa.CompareTo(sb);
            return a.Rule.Order.CompareTo(b.Rule.Order);
        }

        private static string NormTag(string tag)
        {
            return tag == "div" ? "panel" : tag;
        }

        /// <summary>Build from the resolver's rules and the document. The index map decides element order.</summary>
        public static AuiRuleTable Build(AuiStyleResolver resolver, AuiMarkupDocument doc, AuiIndexMap map, AuiDiagnosticBag d)
        {
            var t = new AuiRuleTable();

            // ---- names: regular classes (every one any sheet defines) and star classes (only the referenced ones)
            var classes = new List<string>();
            foreach (string c in resolver.DefinedClasses()) if (!AuiUtilityClasses.IsStar(c)) classes.Add(c);
            foreach (AuiNode n in doc.Nodes) foreach (string c in n.Classes) if (!AuiUtilityClasses.IsStar(c) && !classes.Contains(c)) classes.Add(c);
            classes.Sort(string.CompareOrdinal);
            if (classes.Count > MAX_CLASSES && d != null)
                d.Error(AuiCodes.TooManyClasses, "This view can see " + classes.Count + " classes; the runtime cascade supports " + MAX_CLASSES + ". Split the stylesheet.", doc.Root != null ? doc.Root.Line : 1, doc.Root != null ? doc.Root.Column : 1, 4);
            t.ClassNames = classes.ToArray();
            var classId = new Dictionary<string, int>();
            for (int i = 0; i < t.ClassNames.Length; i++) classId[t.ClassNames[i]] = i;

            var stars = new List<string>();
            foreach (AuiNode n in doc.Nodes) foreach (string c in n.Classes) if (AuiUtilityClasses.IsStar(c) && !stars.Contains(c)) stars.Add(c);
            if (doc.Root != null && doc.Root.Get("utilities") != null)
                foreach (string u in doc.Root.Get("utilities").Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    string sc = u[0] == '*' ? u : "*" + u;
                    if (!stars.Contains(sc)) stars.Add(sc);
                }
            stars.Sort(string.CompareOrdinal);
            if (stars.Count > MAX_STARS && d != null)
                d.Error(AuiCodes.TooManyStarClasses, "This view references " + stars.Count + " star classes; the runtime supports " + MAX_STARS + ".", doc.Root != null ? doc.Root.Line : 1, doc.Root != null ? doc.Root.Column : 1, 4);
            t.StarNames = stars.ToArray();
            var starId = new Dictionary<string, int>();
            for (int i = 0; i < t.StarNames.Length; i++) starId[t.StarNames[i]] = i;

            var tags = new List<string>();
            foreach (AuiNode n in doc.Nodes) { string tg = NormTag(n.Tag); if (!tags.Contains(tg)) tags.Add(tg); }
            t.TagNames = tags.ToArray();
            var tagId = new Dictionary<string, int>();
            for (int i = 0; i < t.TagNames.Length; i++) tagId[t.TagNames[i]] = i;

            // ---- candidates: (rule, selector) pairs plus style="" attributes, in cascade order
            var cands = new List<Cand>();
            int order = 0;
            foreach (AuiStyleRule r in resolver.Rules)
            {
                foreach (AuiSelector sel in r.Selectors)
                {
                    if (sel.IsRoot) continue;
                    // modal::backdrop styles the scrim, an element without a node; as a plain "modal" rule it would paint the body.
                    if (sel.Pseudo != null) continue;
                    if (sel.Tag != null && !tagId.ContainsKey(NormTag(sel.Tag)) && !char.IsUpper(sel.Tag[0])) continue;   // no element of that tag: never matches
                    cands.Add(new Cand { Rule = r, Sel = sel, Tier = (int)r.Sheet.Tier });
                }
                order = System.Math.Max(order, r.Order + 1);
            }
            foreach (AuiNode n in doc.Nodes)
            {
                string inline = n.Get("style");
                if (string.IsNullOrEmpty(inline)) continue;
                AuiAttribute a = n.FindAttribute("style");
                AuiStyleRule rule = AuiStyleParser.ParseDeclarations(inline, doc.File, a.ValueLine, a.ValueColumn, null);
                rule.Order = order++;
                cands.Add(new Cand { Rule = rule, Sel = null, Tier = (int)AuiStyleTier.Attribute, Node = n.Index });
            }
            cands.Sort(Compare);

            // ---- transition sets and refs
            var refNames = new List<string>();
            var refIsSprite = new List<bool>();
            var refKind = new List<int>();
            var refIndex = new Dictionary<string, int>();
            var tsStart = new List<int>(); var tsCount = new List<int>(); var tsProp = new List<int>(); var tsDur = new List<float>(); var tsEase = new List<int>();
            var tsIndex = new Dictionary<string, int>();

            var ruleTag = new List<int>(); var ruleClass = new List<int>(); var ruleNode = new List<int>(); var ruleState = new List<int>(); var ruleStar = new List<int>();
            var ruleStart = new List<int>(); var ruleCount = new List<int>();
            var declProp = new List<int>(); var declRef = new List<int>(); var declValue = new List<float>();
            var declTemplate = new List<string>();

            foreach (Cand c in cands)
            {
                // A star rule is baked only when the view references its class (the utility sheet is large).
                bool isStar = c.Sel != null && c.Sel.Class != null && AuiUtilityClasses.IsStar(c.Sel.Class);
                if (isStar && !starId.ContainsKey(c.Sel.Class)) continue;
                int start = declProp.Count;
                foreach (AuiDeclaration decl in c.Rule.Declarations)
                {
                    AuiPropDef def = AuiSchema.FindProp(decl.Property);
                    if (def == null || def.BakeOnly) continue;
                    if (c.Sel != null && c.Sel.State != null && !def.Animatable) continue;
                    string v;
                    if (!resolver.TryResolveValue(decl, out v)) continue;
                    int prop = PropId(decl.Property);
                    if (prop < 0) continue;
                    float[] val; string refName;
                    if (!Encode(decl.Property, v, out val, out refName)) continue;

                    int refIdx = -1;
                    if (prop == PROP_TRANSITION)
                    {
                        if (!tsIndex.TryGetValue(v, out refIdx))
                        {
                            refIdx = tsStart.Count;
                            tsIndex[v] = refIdx;
                            tsStart.Add(tsProp.Count);
                            int cnt = 0;
                            if (v != "none")
                                foreach (string item in v.Split(','))
                                {
                                    AuiTransition tr; string err;
                                    if (!AuiTransition.TryParse(item, out tr, out err)) continue;
                                    tsProp.Add(tr.Property == "all" ? -1 : PropId(tr.Property));
                                    tsDur.Add(tr.Duration);
                                    tsEase.Add(EaseId(tr.Ease));
                                    cnt++;
                                }
                            tsCount.Add(cnt);
                        }
                        declProp.Add(prop); declRef.Add(refIdx); declValue.AddRange(val); declTemplate.Add(null);
                        continue;
                    }
                    declProp.Add(prop); declRef.Add(-1); declValue.AddRange(val);
                    // A value built from var() keeps its raw text so SetStyleVariable can re-encode it in-world.
                    declTemplate.Add(decl.Value.IndexOf("var(", System.StringComparison.Ordinal) >= 0 ? decl.Value : null);
                    if (prop == PROP_BG && refName != null)
                    {
                        // The sprite half of a background is its own instant declaration.
                        int si;
                        if (!refIndex.TryGetValue("sprite:" + refName, out si)) { si = refNames.Count; refIndex["sprite:" + refName] = si; refNames.Add(refName); refIsSprite.Add(true); refKind.Add(REF_SPRITE); }
                        declProp.Add(PROP_SPRITE); declRef.Add(si); declValue.AddRange(new float[4]); declTemplate.Add(null);
                    }
                    if (prop == PROP_BG)
                    {
                        // material() / shader(): the image's material, also its own instant declaration.
                        AuiBackground bgm;
                        if (AuiBackground.TryParse(v, out bgm) && bgm.HasMaterial)
                        {
                            string key = bgm.Material != null ? "material:" + bgm.Material : "shader:" + bgm.Shader;
                            int mi;
                            if (!refIndex.TryGetValue(key, out mi)) { mi = refNames.Count; refIndex[key] = mi; refNames.Add(bgm.Material ?? bgm.Shader); refIsSprite.Add(false); refKind.Add(bgm.Material != null ? REF_MATERIAL : REF_SHADER); }
                            declProp.Add(PROP_MATERIAL); declRef.Add(mi); declValue.AddRange(new float[4]); declTemplate.Add(null);
                        }
                    }
                }
                ruleTag.Add(c.Sel != null && c.Sel.Tag != null ? (tagId.ContainsKey(NormTag(c.Sel.Tag)) ? tagId[NormTag(c.Sel.Tag)] : -2) : -1);
                ruleClass.Add(c.Sel != null && c.Sel.Class != null && !isStar ? (classId.ContainsKey(c.Sel.Class) ? classId[c.Sel.Class] : -2) : -1);
                ruleStar.Add(isStar ? starId[c.Sel.Class] : -1);
                ruleNode.Add(c.Node);
                ruleState.Add(c.Sel != null && c.Sel.State != null ? StateId(c.Sel.State) : STATE_NORMAL);
                ruleStart.Add(start);
                ruleCount.Add(declProp.Count - start);
            }
            // <view sprites="A B" materials="M N"> preload refs that only SetBackground / SetMaterial will use.
            if (doc.Root != null && doc.Root.Get("sprites") != null)
                foreach (string sp in doc.Root.Get("sprites").Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries))
                    if (!refIndex.ContainsKey("sprite:" + sp)) { refIndex["sprite:" + sp] = refNames.Count; refNames.Add(sp); refIsSprite.Add(true); refKind.Add(REF_SPRITE); }
            if (doc.Root != null && doc.Root.Get("materials") != null)
                foreach (string mn in doc.Root.Get("materials").Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries))
                    if (!refIndex.ContainsKey("material:" + mn)) { refIndex["material:" + mn] = refNames.Count; refNames.Add(mn); refIsSprite.Add(false); refKind.Add(REF_MATERIAL); }
            t.RefKind = refKind.ToArray();
            t.RuleTag = ruleTag.ToArray(); t.RuleClass = ruleClass.ToArray(); t.RuleNode = ruleNode.ToArray(); t.RuleState = ruleState.ToArray(); t.RuleStar = ruleStar.ToArray();
            t.RuleDeclStart = ruleStart.ToArray(); t.RuleDeclCount = ruleCount.ToArray();
            t.DeclProp = declProp.ToArray(); t.DeclRef = declRef.ToArray(); t.DeclValue = declValue.ToArray(); t.DeclTemplate = declTemplate.ToArray();
            t.RefNames = refNames.ToArray(); t.RefIsSprite = refIsSprite.ToArray();

            // ---- :root variables, merged by the resolver (later tier wins), sorted for a stable table
            var vars = new List<AuiVariable>(resolver.Variables);
            vars.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            t.VarNames = new string[vars.Count]; t.VarValues = new string[vars.Count];
            for (int i = 0; i < vars.Count; i++) { t.VarNames[i] = vars[i].Name; t.VarValues[i] = vars[i].Value; }
            t.TsStart = tsStart.ToArray(); t.TsCount = tsCount.ToArray(); t.TsProp = tsProp.ToArray(); t.TsDur = tsDur.ToArray(); t.TsEase = tsEase.ToArray();

            // ---- buckets per tag
            var bStart = new List<int>(); var bCount = new List<int>(); var bRules = new List<int>();
            for (int tg = 0; tg < t.TagNames.Length; tg++)
            {
                bStart.Add(bRules.Count);
                for (int r = 0; r < t.RuleTag.Length; r++)
                    if ((t.RuleTag[r] == -1 || t.RuleTag[r] == tg) && t.RuleClass[r] != -2) bRules.Add(r);
                bCount.Add(bRules.Count - bStart[tg]);
            }
            t.BucketStart = bStart.ToArray(); t.BucketCount = bCount.ToArray(); t.BucketRules = bRules.ToArray();

            // ---- inherited
            var inh = new List<int>();
            foreach (AuiPropDef p in AuiSchema.Props) if (p.Inherited && !p.BakeOnly && PropId(p.Name) >= 0) inh.Add(PropId(p.Name));
            t.InheritedProps = inh.ToArray();

            // ---- per element
            int count = map.ElementCount;
            t.ElemTag = new int[count]; t.ElemNode = new int[count]; t.ElemSubtreeEnd = new int[count]; t.ElemClass0 = new int[count]; t.ElemClass1 = new int[count]; t.ElemClassSlots = new int[count];
            t.ElemStar = new int[count * STAR_WORDS];
            int slots = 1;
            for (int e = 0; e < count; e++)
            {
                AuiNode node = map.NodeOf[e];
                t.ElemSubtreeEnd[e] = e + 1;
                if (node == null) { t.ElemTag[e] = -1; t.ElemNode[e] = -1; continue; }
                t.ElemTag[e] = tagId[NormTag(node.Tag)];
                t.ElemNode[e] = node.Index;
                foreach (string c in node.Classes)
                {
                    int id;
                    if (starId.TryGetValue(c, out id)) { t.ElemStar[e * STAR_WORDS + (id >> 5)] |= 1 << (id & 31); continue; }
                    if (!classId.TryGetValue(c, out id)) continue;
                    if (id < 32) t.ElemClass0[e] |= 1 << id; else if (id < 64) t.ElemClass1[e] |= 1 << (id - 32);
                }
                t.ElemClassSlots[e] = node.ClassBindings.Length;
                slots = System.Math.Max(slots, node.ClassBindings.Length);
            }
            t.ClassSlots = slots;
            // Subtree end: pre-order, so the end of e is the first later element whose parent chain misses e.
            var parent = new int[count];
            for (int e = 0; e < count; e++)
            {
                AuiNode node = map.NodeOf[e];
                int row = map.RowOf[e] < 0 ? 0 : map.RowOf[e];
                // A node-less element (a select's popup or scrim, a modal's backdrop) hangs off the root, so the
                // root's subtree stays contiguous and reaches the elements baked after it (the modal body).
                parent[e] = node != null && node.Parent != null ? map.Elem(node.Parent, row) : (e > 0 ? 0 : -1);
            }
            for (int e = 0; e < count; e++)
            {
                int end = e + 1;
                for (int i = e + 1; i < count; i++)
                {
                    bool inside = false;
                    for (int p = parent[i]; p >= 0; p = parent[p]) if (p == e) { inside = true; break; }
                    if (!inside) break;
                    end = i + 1;
                }
                t.ElemSubtreeEnd[e] = end;
            }
            return t;
        }

        /// <summary>
        /// Replace var(--x) and var(--x, fallback) in a declaration template from a name/value table;
        /// an unknown variable without a fallback becomes empty text. The Udon resolver runs the same
        /// loop (AuiView.Styles.cs SubstituteVars), so this is the reference for the parity tests.
        /// </summary>
        public static string Substitute(string template, string[] names, string[] values)
        {
            if (template == null || template.IndexOf("var(", System.StringComparison.Ordinal) < 0) return template;
            var sb = new StringBuilder();
            int i = 0, guard = 0;
            while (i < template.Length)
            {
                int at = template.IndexOf("var(", i, System.StringComparison.Ordinal);
                if (at < 0) { sb.Append(template, i, template.Length - i); break; }
                sb.Append(template, i, at - i);
                int close = template.IndexOf(')', at);
                if (close < 0) break;
                string inner = template.Substring(at + 4, close - at - 4);
                string name = inner, fallback = null;
                int comma = inner.IndexOf(',');
                if (comma >= 0) { name = inner.Substring(0, comma); fallback = inner.Substring(comma + 1).Trim(); }
                name = name.Trim();
                int id = -1;
                if (names != null) for (int k = 0; k < names.Length; k++) if (names[k] == name) { id = k; break; }
                if (id >= 0) sb.Append(values[id]);
                else if (fallback != null) sb.Append(fallback);
                i = close + 1;
                if (++guard > 64) break;
            }
            return sb.ToString();
        }

        public static int StateId(string state)
        {
            switch (state)
            {
                case "hover": return STATE_HOVER;
                case "pressed": return STATE_PRESSED;
                case "selected": return STATE_SELECTED;
                case "disabled": return STATE_DISABLED;
            }
            return STATE_NORMAL;
        }

        /// <summary>Same mapping as the builder's EaseId: VRCTweenEase names in PascalCase, in enum order.</summary>
        public static int EaseId(string ease)
        {
            string name = "";
            foreach (string part in (ease ?? "out-quad").Split('-')) if (part.Length > 0) name += char.ToUpperInvariant(part[0]) + part.Substring(1);
            for (int i = 0; i < EaseNames.Length; i++) if (EaseNames[i] == name) return i;
            return 6;
        }

        /// <summary>VRC.SDK3.Components.VRCTweenEase members in declaration order (verified by AuiRuleTableTests against the enum).</summary>
        public static readonly string[] EaseNames =
        {
            "None", "Linear", "InSine", "OutSine", "InOutSine", "InQuad", "OutQuad", "InOutQuad", "InCubic", "OutCubic", "InOutCubic",
            "InQuart", "OutQuart", "InOutQuart", "InQuint", "OutQuint", "InOutQuint", "InExpo", "OutExpo", "InOutExpo",
            "InCirc", "OutCirc", "InOutCirc", "InElastic", "OutElastic", "InOutElastic", "InBack", "OutBack", "InOutBack",
            "InBounce", "OutBounce", "InOutBounce",
        };

        // ================================================================ reference resolver (tests)

        /// <summary>One element's resolved normal-state values: prop -> (float4, ref).</summary>
        public sealed class Resolved
        {
            public readonly float[][] Value = new float[PROP_COUNT][];
            public readonly int[] Ref = new int[PROP_COUNT];
            public readonly List<int> StateRules = new List<int>();
        }

        /// <summary>The algorithm the Udon resolver runs, in C#: inherit, then walk the tag bucket letting later matching rules overwrite.</summary>
        public Resolved Resolve(int tag, int class0, int class1, int node, Resolved parent)
        {
            return Resolve(tag, class0, class1, null, node, parent);
        }

        public Resolved Resolve(int tag, int class0, int class1, int[] stars, int node, Resolved parent)
        {
            var r = new Resolved();
            for (int p = 0; p < PROP_COUNT; p++) r.Ref[p] = -1;
            if (parent != null)
                foreach (int p in InheritedProps) { r.Value[p] = parent.Value[p]; r.Ref[p] = parent.Ref[p]; }
            if (tag < 0) return r;
            int start = BucketStart[tag], count = BucketCount[tag];
            for (int i = start; i < start + count; i++)
            {
                int rule = BucketRules[i];
                int cls = RuleClass[rule];
                if (cls >= 0 && !HasBit(class0, class1, cls)) continue;
                int st = RuleStar[rule];
                if (st >= 0 && (stars == null || (stars[st >> 5] & (1 << (st & 31))) == 0)) continue;
                if (RuleNode[rule] >= 0 && RuleNode[rule] != node) continue;
                if (RuleState[rule] != STATE_NORMAL) { r.StateRules.Add(rule); continue; }
                int ds = RuleDeclStart[rule], dc = RuleDeclCount[rule];
                for (int k = ds; k < ds + dc; k++)
                {
                    int prop = DeclProp[k];
                    r.Value[prop] = new[] { DeclValue[4 * k], DeclValue[4 * k + 1], DeclValue[4 * k + 2], DeclValue[4 * k + 3] };
                    r.Ref[prop] = DeclRef[k];
                }
            }
            return r;
        }

        public static bool HasBit(int class0, int class1, int id)
        {
            if (id < 32) return (class0 & (1 << id)) != 0;
            if (id < 64) return (class1 & (1 << (id - 32))) != 0;
            return false;
        }
    }
}
