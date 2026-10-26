using System.Collections.Generic;

namespace Armagan.AUI.Core
{
    /// <summary>One lang/&lt;code&gt;.json, flattened.</summary>
    public sealed class AuiLanguageFile
    {
        public string Code;      // "en", "tr", "ja", "zh-CN"
        public string File;
        public string Name;      // native display name from the reserved key "_name"
        public AuiJson.Result Data;
        /// <summary>Written right to left (AuiRtl.IsRtlCode). Its values are already shaped.</summary>
        public bool Rtl;

        public const string NameKey = "_name";

        public static AuiLanguageFile Load(string code, string text, string file)
        {
            var lf = new AuiLanguageFile { Code = code, File = file, Data = AuiJson.Flatten(text, file) };
            string name;
            lf.Name = lf.Data.Values.TryGetValue(NameKey, out name) ? name : code;
            lf.Rtl = AuiRtl.IsRtlCode(code);
            if (lf.Rtl)
            {
                // SHAPED HERE, ONCE, so everything downstream sees what TMP will draw: the runtime
                // table, the font baker's charset (the presentation forms, not the base letters)
                // and the measurer's widths. Values are logical order for an isRightToLeftText
                // label; the language's own name is shown in a left-to-right picker label, so it
                // goes in visual order.
                var keys = new List<string>(lf.Data.Values.Keys);
                foreach (string k in keys)
                    if (k != NameKey) lf.Data.Values[k] = AuiRtl.Prepare(lf.Data.Values[k]);
                lf.Name = AuiRtl.ToVisual(lf.Name);
            }
            return lf;
        }

        public bool Has(string key)
        {
            return Data.Values.ContainsKey(key);
        }

        public string Get(string key)
        {
            string v;
            return Data.Values.TryGetValue(key, out v) ? v : null;
        }
    }

    /// <summary>
    /// Every language of a project merged into one table: sorted keys, one column per
    /// language, a default language for fallback. The compiler bakes this onto the
    /// AuiLanguageService (flat values[lang * keyCount + key]) and hands each view the
    /// indices of the keys it uses, so the runtime never looks a string up by name.
    /// </summary>
    public sealed class AuiLanguageTable
    {
        public readonly List<AuiLanguageFile> Languages = new List<AuiLanguageFile>();
        public readonly List<string> Keys = new List<string>();
        public int DefaultLanguage;
        private readonly Dictionary<string, int> _keyIndex = new Dictionary<string, int>();

        public static AuiLanguageTable Build(IEnumerable<AuiLanguageFile> files, string defaultCode)
        {
            var t = new AuiLanguageTable();
            var keys = new HashSet<string>();
            foreach (AuiLanguageFile f in files)
            {
                t.Languages.Add(f);
                foreach (string k in f.Data.Order) if (k != AuiLanguageFile.NameKey) keys.Add(k);
            }
            t.Languages.Sort((a, b) => string.CompareOrdinal(a.Code, b.Code));
            t.Keys.AddRange(keys);
            t.Keys.Sort(string.CompareOrdinal);
            for (int i = 0; i < t.Keys.Count; i++) t._keyIndex[t.Keys[i]] = i;
            t.DefaultLanguage = 0;
            for (int i = 0; i < t.Languages.Count; i++) if (t.Languages[i].Code == defaultCode) t.DefaultLanguage = i;
            return t;
        }

        public int KeyCount { get { return Keys.Count; } }
        public int LanguageCount { get { return Languages.Count; } }

        public int IndexOfKey(string key)
        {
            int i;
            return _keyIndex.TryGetValue(key, out i) ? i : -1;
        }

        public int IndexOfLanguage(string code)
        {
            for (int i = 0; i < Languages.Count; i++) if (Languages[i].Code == code) return i;
            return -1;
        }

        public string Get(int lang, string key)
        {
            return lang < 0 || lang >= Languages.Count ? null : Languages[lang].Get(key);
        }

        /// <summary>The value in the language, or the default language's, or null.</summary>
        public string GetWithFallback(int lang, string key)
        {
            string v = Get(lang, key);
            if (string.IsNullOrEmpty(v)) v = Get(DefaultLanguage, key);
            return v;
        }

        /// <summary>Every translation of a key across all languages (with fallback applied). Empty when the key is unknown.</summary>
        public IEnumerable<string> AllTranslations(string key)
        {
            if (IndexOfKey(key) < 0) yield break;
            for (int i = 0; i < Languages.Count; i++)
            {
                string v = GetWithFallback(i, key);
                if (v != null) yield return v;
            }
        }

        /// <summary>values[lang * keyCount + key], "" for missing; the runtime falls back itself.</summary>
        public string[] Flat()
        {
            var flat = new string[Languages.Count * Keys.Count];
            for (int l = 0; l < Languages.Count; l++)
                for (int k = 0; k < Keys.Count; k++)
                    flat[l * Keys.Count + k] = Languages[l].Get(Keys[k]) ?? "";
            return flat;
        }

        /// <summary>Codes in table order.</summary>
        public string[] Codes()
        {
            var a = new string[Languages.Count];
            for (int i = 0; i < a.Length; i++) a[i] = Languages[i].Code;
            return a;
        }

        public string[] Names()
        {
            var a = new string[Languages.Count];
            for (int i = 0; i < a.Length; i++) a[i] = Languages[i].Name;
            return a;
        }

        /// <summary>Per language in table order: written right to left.</summary>
        public bool[] Rtl()
        {
            var a = new bool[Languages.Count];
            for (int i = 0; i < a.Length; i++) a[i] = Languages[i].Rtl;
            return a;
        }

        /// <summary>Distinct characters across every value in every language (font baking input).</summary>
        public HashSet<char> AllCharacters()
        {
            var set = new HashSet<char>();
            foreach (AuiLanguageFile f in Languages)
                foreach (string v in f.Data.Values.Values)
                    foreach (char c in v) set.Add(c);
            return set;
        }

        /// <summary>{name} placeholders in a translated string, in order, without duplicates.</summary>
        public static List<string> Placeholders(string text)
        {
            var list = new List<string>();
            if (text == null) return list;
            int i = 0;
            while (i < text.Length)
            {
                int o = text.IndexOf('{', i);
                if (o < 0) break;
                int c = text.IndexOf('}', o);
                if (c < 0) break;
                string name = text.Substring(o + 1, c - o - 1).Trim();
                if (AuiSchema.IsIdentifier(name) && !list.Contains(name)) list.Add(name);
                i = c + 1;
            }
            return list;
        }

        /// <summary>
        /// Checks every key a view uses: unknown key, missing per language (warning listing the
        /// languages), missing in the default language (error), placeholder/args mismatches.
        /// </summary>
        public void Check(AuiViewInfo info, AuiDiagnosticBag d)
        {
            if (Languages.Count == 0)
            {
                if (info.Keys.Count > 0) d.Error(AuiCodes.NoLanguages, "The view uses translation keys but no lang/*.json was found.", info.Keys[0].Node.Line, info.Keys[0].Node.Column, 1);
                return;
            }
            foreach (AuiKeyUse use in info.Keys)
            {
                AuiAttribute a = use.Attribute;
                if (IndexOfKey(use.Key) < 0)
                {
                    string hint = AuiSchema.Suggest(use.Key, Keys);
                    d.Error(AuiCodes.UnknownKey, "Unknown key '" + use.Key + "'." + (hint != null ? " Did you mean '" + hint + "'?" : ""), a.ValueLine, a.ValueColumn, use.Key.Length);
                    continue;
                }
                string def = Get(DefaultLanguage, use.Key);
                if (string.IsNullOrEmpty(def))
                    d.Error(AuiCodes.MissingDefaultTranslation, "'" + use.Key + "' has no text in the default language (" + Languages[DefaultLanguage].Code + ").", a.ValueLine, a.ValueColumn, use.Key.Length);
                var missing = new List<string>();
                for (int i = 0; i < Languages.Count; i++)
                    if (string.IsNullOrEmpty(Get(i, use.Key))) missing.Add(Languages[i].Code);
                if (missing.Count > 0 && !(missing.Count == 1 && missing[0] == Languages[DefaultLanguage].Code))
                    d.Warning(AuiCodes.MissingTranslation, "'" + use.Key + "' is not translated in: " + string.Join(", ", missing) + ".", a.ValueLine, a.ValueColumn, use.Key.Length);

                if (a.Name != "key") continue;
                // placeholders vs args, checked against every language
                var argNames = new List<string>();
                foreach (AuiBinding b in use.Args) argNames.Add(b.Name);
                for (int i = 0; i < Languages.Count; i++)
                {
                    string v = Get(i, use.Key);
                    if (string.IsNullOrEmpty(v)) continue;
                    foreach (string ph in Placeholders(v))
                    {
                        if (ph == "value" && use.Node.Tag == "slider") continue; // format-key's own placeholder
                        if (!argNames.Contains(ph))
                            d.Error(AuiCodes.PlaceholderMismatch, "'" + use.Key + "' (" + Languages[i].Code + ") uses {" + ph + "} but the element has no args=\"{" + ph + "}\".", a.ValueLine, a.ValueColumn, use.Key.Length);
                    }
                }
                foreach (string an in argNames)
                {
                    bool usedAnywhere = false;
                    for (int i = 0; i < Languages.Count && !usedAnywhere; i++)
                        if (Placeholders(Get(i, use.Key)).Contains(an)) usedAnywhere = true;
                    if (!usedAnywhere)
                        d.Warning(AuiCodes.PlaceholderMismatch, "args {" + an + "} is not used by any translation of '" + use.Key + "'.", a.ValueLine, a.ValueColumn, use.Key.Length);
                }
            }
        }
    }
}
