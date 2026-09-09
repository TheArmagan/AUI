using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Armagan.AUI.Core
{
    /// <summary>What the host supplies to compile one view: the theme, how to read files, the language table, metrics.</summary>
    public sealed class AuiCompileOptions
    {
        public AuiStyleSheet Theme;
        /// <summary>Reads a file by absolute path; null = File.ReadAllText. Hosts use it to record dependencies.</summary>
        public System.Func<string, string> ReadFile;
        public AuiLanguageTable Lang;
        public IAuiTextMeasurer Measurer;
        public System.Func<string, bool> ViewExists;
        public System.Func<string, bool> IconExists;
        public AuiImageSizeLookup ImageSize;
        /// <summary>Analyzed model of a nested view by name (for typed child properties).</summary>
        public System.Func<string, AuiViewInfo> ChildInfo;
        public string BoundTextSample = "Mmmmmmmmmmm日";
    }

    public sealed class AuiCompileResult
    {
        public string FilePath;
        public string SourceText;
        public AuiMarkupDocument Doc;
        public readonly List<AuiStyleSheet> Sheets = new List<AuiStyleSheet>();
        public AuiResolvedStyle[] Styles;
        public AuiViewInfo Info;
        public AuiIndexMap Map;
        public AuiLayoutNode[] Layout;
        /// <summary>The cascade as runtime tables (rules, declarations, class ids, per-element tags and masks).</summary>
        public AuiRuleTable Rules;
        public string GeneratedSource;
        /// <summary>Hash of everything the prefab depends on; equal hashes mean an identical prefab.</summary>
        public string Hash;
        public AuiDiagnosticBag Diagnostics;
        /// <summary>Absolute paths of every file this compile read besides the source (theme, linked sheets).</summary>
        public readonly List<string> Dependencies = new List<string>();

        public bool HasErrors
        {
            get { return Diagnostics != null && Diagnostics.HasErrors; }
        }

        public string ViewName
        {
            get { return Info != null ? Info.Name : null; }
        }
    }

    /// <summary>
    /// The one pipeline both hosts run: parse, cascade, analyze, check keys, map indices, lay
    /// out, generate. The Unity importer and the language server differ only in what they pass
    /// in (a TMP measurer vs a heuristic, dependency-recording file reads vs plain ones) and in
    /// what they do with the result.
    /// </summary>
    public static class AuiCompiler
    {
        public static AuiCompileResult Compile(string text, string filePath, AuiCompileOptions opt)
        {
            if (opt == null) opt = new AuiCompileOptions();
            var r = new AuiCompileResult { FilePath = filePath, SourceText = text };
            string stem = Path.GetFileNameWithoutExtension(filePath);
            r.Doc = AuiParser.Parse(text, filePath);
            r.Diagnostics = new AuiDiagnosticBag(filePath);
            r.Diagnostics.AddRange(r.Doc.Diagnostics);

            // ---- sheets in tier order
            if (opt.Theme != null) r.Sheets.Add(opt.Theme);
            r.Sheets.Add(AuiUtilityClasses.Sheet());
            string dir = Path.GetDirectoryName(filePath) ?? "";
            bool linked = false;
            foreach (AuiLink link in r.Doc.Links)
            {
                string p = Path.GetFullPath(Path.Combine(dir, link.Href));
                string content = Read(opt, p);
                if (content == null)
                {
                    r.Diagnostics.Error(AuiCodes.StylesheetNotFound, "Stylesheet '" + link.Href + "' not found.", link.Line, link.Column, link.Href.Length + 6);
                    continue;
                }
                r.Dependencies.Add(p);
                AuiStyleSheet s = AuiStyleParser.Parse(content, p, AuiStyleTier.Linked);
                r.Diagnostics.AddRange(s.Diagnostics);
                r.Sheets.Add(s);
                linked = true;
            }
            if (!linked)
            {
                string sibling = Path.Combine(dir, stem + ".auis");
                string content = Read(opt, sibling);
                if (content != null)
                {
                    r.Dependencies.Add(Path.GetFullPath(sibling));
                    AuiStyleSheet s = AuiStyleParser.Parse(content, sibling, AuiStyleTier.Linked);
                    r.Diagnostics.AddRange(s.Diagnostics);
                    r.Sheets.Add(s);
                }
            }
            foreach (AuiStyleBlock b in r.Doc.Styles)
            {
                AuiStyleSheet s = AuiStyleParser.Parse(b.Text, filePath, AuiStyleTier.Inline, b.Line, b.Column);
                r.Diagnostics.AddRange(s.Diagnostics);
                r.Sheets.Add(s);
            }

            // ---- cascade, semantics, keys
            // Inline components become ordinary elements before anything looks at the tree;
            // pools and slots become templates.
            AuiTemplateExpander.Expand(r.Doc, r.Diagnostics);
            AuiPoolSynthesizer.Synthesize(r.Doc, r.Diagnostics);
            var resolver = new AuiStyleResolver(r.Sheets);
            r.Styles = resolver.Resolve(r.Doc, r.Diagnostics);
            r.Info = AuiAnalyzer.Analyze(r.Doc, new AuiAnalysisContext { FileStem = stem, ViewExists = opt.ViewExists, IconExists = opt.IconExists, ChildInfo = opt.ChildInfo }, r.Diagnostics);
            if (opt.Lang != null) opt.Lang.Check(r.Info, r.Diagnostics);
            if (r.Doc.Root == null) return r;

            // ---- indices, layout, code
            r.Map = AuiIndexMap.Build(r.Doc, r.Info, r.Styles);
            r.Rules = AuiRuleTable.Build(resolver, r.Doc, r.Map, r.Diagnostics);
            var lctx = new AuiLayoutContext
            {
                Measurer = opt.Measurer ?? new AuiApproxTextMeasurer(),
                Translations = opt.Lang != null ? (System.Func<string, IEnumerable<string>>)(k => opt.Lang.AllTranslations(k)) : null,
                ImageSize = opt.ImageSize,
                BoundTextSample = opt.BoundTextSample,
                ChildInfo = opt.ChildInfo,
                LanguageCount = opt.Lang != null ? opt.Lang.LanguageCount : 0,
                TranslationAt = opt.Lang != null ? (System.Func<string, int, string>)((k, l) => opt.Lang.IndexOfKey(k) < 0 ? null : opt.Lang.GetWithFallback(l, k)) : null,
            };
            r.Layout = new AuiLayoutSolver(r.Doc, r.Styles, r.Info, lctx, r.Diagnostics).Solve();

            // ONE CODEGEN PASS. The header carries a hash of the SOURCE ONLY: the prefab stamp
            // (r.Hash) also covers layout and styles, and putting that in the header would rewrite
            // Foo.g.cs, and so force a script reload, for a change that only moves a rectangle.
            // The text is emitted once with an empty hash and the header line patched afterwards.
            var cg = new AuiCodegenContext { Hash = "", ChildInfo = opt.ChildInfo, ClassNames = r.Rules.ClassNames };
            string unhashed = AuiCodegen.Emit(r.Info, r.Map, cg, r.Diagnostics);
            r.Hash = ComputeHash(r, unhashed);
            r.GeneratedSource = AuiCodegen.WithHeaderHash(unhashed, Sha1(unhashed));
            return r;
        }

        private static string Read(AuiCompileOptions opt, string path)
        {
            try
            {
                if (opt.ReadFile != null) return opt.ReadFile(path);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Everything that shapes the prefab: generated code, layout rects (including the
        /// per-language rows), resolved styles, key indices, the node tree and the runtime rule
        /// table. The builder skips a prefab whose baked hash matches, so anything it copies onto
        /// the view and is NOT in here silently never reaches the prefab. Two such holes shipped:
        /// a rule that matches no resting element (`.warn { color }` behind a class binding) and a
        /// translation edit that did not change the widest language.
        /// </summary>
        private static string ComputeHash(AuiCompileResult r, string generated)
        {
            var sb = new StringBuilder();
            sb.Append(generated);
            foreach (AuiLayoutNode ln in r.Layout)
            {
                sb.Append(ln.Local.ToString()).Append(ln.AnchorFill ? "F" : "").Append(ln.ContentSized ? "C" : "").Append(ln.Hidden ? "H" : "").Append(ln.OutOfFlow ? "A" : "").Append('|');
                if (ln.Instances != null) foreach (AuiRect i in ln.Instances) sb.Append(i.ToString());
                if (ln.LangW != null) foreach (float f in ln.LangW) sb.Append(f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                if (ln.LangH != null) foreach (float f in ln.LangH) sb.Append(f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            }
            AppendRules(sb, r.Rules);
            foreach (AuiResolvedStyle s in r.Styles)
            {
                var keys = new List<string>(s.Values.Keys);
                keys.Sort(string.CompareOrdinal);
                foreach (string k in keys) sb.Append(k).Append('=').Append(s.Values[k]).Append(';');
                var states = new List<string>(s.States.Keys);
                states.Sort(string.CompareOrdinal);
                foreach (string st in states)
                {
                    sb.Append(':').Append(st).Append('{');
                    var sk = new List<string>(s.States[st].Keys);
                    sk.Sort(string.CompareOrdinal);
                    foreach (string k in sk) sb.Append(k).Append('=').Append(s.States[st][k]).Append(';');
                    sb.Append('}');
                }
                sb.Append('|');
            }
            foreach (AuiKeyUse k in r.Info.Keys) sb.Append(k.Key).Append(',');
            foreach (AuiNode n in r.Doc.Nodes)
            {
                sb.Append(n.Tag);
                foreach (AuiAttribute a in n.Attributes) sb.Append(' ').Append(a.Name).Append('=').Append(a.Value);
                sb.Append('>').Append(n.Text).Append('|');
            }
            return Sha1(sb.ToString());
        }

        private static void AppendRules(StringBuilder sb, AuiRuleTable t)
        {
            if (t == null) return;
            sb.Append("#rules");
            AppendInts(sb, t.RuleTag); AppendInts(sb, t.RuleClass); AppendInts(sb, t.RuleStar); AppendInts(sb, t.RuleNode); AppendInts(sb, t.RuleState);
            AppendInts(sb, t.RuleDeclStart); AppendInts(sb, t.RuleDeclCount);
            AppendInts(sb, t.DeclProp); AppendInts(sb, t.DeclRef);
            if (t.DeclValue != null) foreach (float f in t.DeclValue) sb.Append(f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            AppendStrings(sb, t.DeclTemplate);
            AppendStrings(sb, t.RefNames); AppendInts(sb, t.RefKind);
            AppendInts(sb, t.TsStart); AppendInts(sb, t.TsCount); AppendInts(sb, t.TsProp); AppendInts(sb, t.TsEase);
            if (t.TsDur != null) foreach (float f in t.TsDur) sb.Append(f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            AppendStrings(sb, t.VarNames); AppendStrings(sb, t.VarValues);
            AppendStrings(sb, t.ClassNames); AppendStrings(sb, t.StarNames); AppendStrings(sb, t.TagNames);
            AppendInts(sb, t.ElemTag); AppendInts(sb, t.ElemNode); AppendInts(sb, t.ElemClass0); AppendInts(sb, t.ElemClass1); AppendInts(sb, t.ElemClassSlots); AppendInts(sb, t.ElemStar);
            AppendInts(sb, t.InheritedProps);
        }

        private static void AppendInts(StringBuilder sb, int[] a)
        {
            sb.Append('[');
            if (a != null) foreach (int i in a) sb.Append(i).Append(',');
            sb.Append(']');
        }

        private static void AppendStrings(StringBuilder sb, string[] a)
        {
            sb.Append('[');
            if (a != null) foreach (string s in a) sb.Append(s).Append('');
            sb.Append(']');
        }

        public static string Sha1(string s)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(40);
                for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
