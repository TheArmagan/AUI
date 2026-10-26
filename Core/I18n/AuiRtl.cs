using System.Collections.Generic;
using System.Text;

namespace Armagan.AUI.Core
{
    /// <summary>
    /// Right-to-left text for TextMeshPro, done at bake time.
    ///
    /// TMP DOES NOT SHAPE ARABIC. It draws each code point as it comes, so the base letters
    /// (U+0621..U+064A) render as isolated, unjoined forms. What it does do, with
    /// <c>isRightToLeftText</c>, is lay a line out from the right edge in LOGICAL order and wrap
    /// it correctly (measured: lines stay in reading order top to bottom). So the bake does the two
    /// things TMP cannot:
    ///
    /// 1. SHAPING. Each Arabic letter is replaced by its contextual presentation form (Arabic
    ///    Presentation Forms-B, U+FE70..U+FEFC; a few Persian letters from Forms-A): isolated,
    ///    final, initial or medial, from whether its neighbours join. Lam followed by an alef
    ///    becomes one lam-alef ligature. Harakat are transparent: skipped when looking for a
    ///    neighbour, kept where they are.
    ///
    /// 2. LEFT-TO-RIGHT RUNS ARE PRE-REVERSED. In RTL mode TMP places every character right to
    ///    left, so "VIP" or "500" inside an Arabic sentence would read "PIV", "005". A run (strong
    ///    LTR characters and the neutrals between them) is written backwards here so TMP's
    ///    placement turns it the right way round. A {placeholder} counts as one strong-LTR atom
    ///    and is kept intact: its VALUE is reversed at runtime instead (AuiView.RtlArg), because
    ///    only the runtime knows it. A rich-text tag &lt;...&gt; is never reversed and breaks a run.
    ///
    /// Paired brackets outside LTR runs are mirrored, as the bidi algorithm would draw them.
    ///
    /// The result is LOGICAL order with shaped glyphs: right for a label with isRightToLeftText on.
    /// <see cref="ToVisual"/> is for the one place text is drawn left to right instead (a
    /// language's own name in a picker, which is a bound label).
    /// </summary>
    public static class AuiRtl
    {
        /// <summary>Languages written right to left, by base code.</summary>
        public static bool IsRtlCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            int d = code.IndexOf('-');
            string b = (d < 0 ? code : code.Substring(0, d)).ToLowerInvariant();
            return b == "ar" || b == "fa" || b == "he" || b == "ur" || b == "ps" || b == "ug" || b == "yi" || b == "ckb";
        }

        // letter -> isolated, final, initial, medial (0 = none). Two forms only = right-joining.
        private static readonly Dictionary<char, char[]> Forms = new Dictionary<char, char[]>
        {
            { 'ء', new[] { 'ﺀ', '\0', '\0', '\0' } },
            { 'آ', new[] { 'ﺁ', 'ﺂ', '\0', '\0' } },
            { 'أ', new[] { 'ﺃ', 'ﺄ', '\0', '\0' } },
            { 'ؤ', new[] { 'ﺅ', 'ﺆ', '\0', '\0' } },
            { 'إ', new[] { 'ﺇ', 'ﺈ', '\0', '\0' } },
            { 'ئ', new[] { 'ﺉ', 'ﺊ', 'ﺋ', 'ﺌ' } },
            { 'ا', new[] { 'ﺍ', 'ﺎ', '\0', '\0' } },
            { 'ب', new[] { 'ﺏ', 'ﺐ', 'ﺑ', 'ﺒ' } },
            { 'ة', new[] { 'ﺓ', 'ﺔ', '\0', '\0' } },
            { 'ت', new[] { 'ﺕ', 'ﺖ', 'ﺗ', 'ﺘ' } },
            { 'ث', new[] { 'ﺙ', 'ﺚ', 'ﺛ', 'ﺜ' } },
            { 'ج', new[] { 'ﺝ', 'ﺞ', 'ﺟ', 'ﺠ' } },
            { 'ح', new[] { 'ﺡ', 'ﺢ', 'ﺣ', 'ﺤ' } },
            { 'خ', new[] { 'ﺥ', 'ﺦ', 'ﺧ', 'ﺨ' } },
            { 'د', new[] { 'ﺩ', 'ﺪ', '\0', '\0' } },
            { 'ذ', new[] { 'ﺫ', 'ﺬ', '\0', '\0' } },
            { 'ر', new[] { 'ﺭ', 'ﺮ', '\0', '\0' } },
            { 'ز', new[] { 'ﺯ', 'ﺰ', '\0', '\0' } },
            { 'س', new[] { 'ﺱ', 'ﺲ', 'ﺳ', 'ﺴ' } },
            { 'ش', new[] { 'ﺵ', 'ﺶ', 'ﺷ', 'ﺸ' } },
            { 'ص', new[] { 'ﺹ', 'ﺺ', 'ﺻ', 'ﺼ' } },
            { 'ض', new[] { 'ﺽ', 'ﺾ', 'ﺿ', 'ﻀ' } },
            { 'ط', new[] { 'ﻁ', 'ﻂ', 'ﻃ', 'ﻄ' } },
            { 'ظ', new[] { 'ﻅ', 'ﻆ', 'ﻇ', 'ﻈ' } },
            { 'ع', new[] { 'ﻉ', 'ﻊ', 'ﻋ', 'ﻌ' } },
            { 'غ', new[] { 'ﻍ', 'ﻎ', 'ﻏ', 'ﻐ' } },
            { 'ف', new[] { 'ﻑ', 'ﻒ', 'ﻓ', 'ﻔ' } },
            { 'ق', new[] { 'ﻕ', 'ﻖ', 'ﻗ', 'ﻘ' } },
            { 'ك', new[] { 'ﻙ', 'ﻚ', 'ﻛ', 'ﻜ' } },
            { 'ل', new[] { 'ﻝ', 'ﻞ', 'ﻟ', 'ﻠ' } },
            { 'م', new[] { 'ﻡ', 'ﻢ', 'ﻣ', 'ﻤ' } },
            { 'ن', new[] { 'ﻥ', 'ﻦ', 'ﻧ', 'ﻨ' } },
            { 'ه', new[] { 'ﻩ', 'ﻪ', 'ﻫ', 'ﻬ' } },
            { 'و', new[] { 'ﻭ', 'ﻮ', '\0', '\0' } },
            { 'ى', new[] { 'ﻯ', 'ﻰ', '\0', '\0' } },
            { 'ي', new[] { 'ﻱ', 'ﻲ', 'ﻳ', 'ﻴ' } },
            // Persian / Urdu letters, Presentation Forms-A
            { 'پ', new[] { 'ﭖ', 'ﭗ', 'ﭘ', 'ﭙ' } },
            { 'چ', new[] { 'ﭺ', 'ﭻ', 'ﭼ', 'ﭽ' } },
            { 'ژ', new[] { 'ﮊ', 'ﮋ', '\0', '\0' } },
            { 'ک', new[] { 'ﮎ', 'ﮏ', 'ﮐ', 'ﮑ' } },
            { 'گ', new[] { 'ﮒ', 'ﮓ', 'ﮔ', 'ﮕ' } },
            { 'ی', new[] { 'ﯼ', 'ﯽ', 'ﯾ', 'ﯿ' } },
        };

        private const char Lam = 'ل';
        private const char Tatweel = 'ـ';

        /// <summary>Harakat and other marks that sit on a letter and do not break joining.</summary>
        private static bool IsTransparent(char c)
        {
            return (c >= 'ً' && c <= 'ٟ') || c == 'ٰ' || (c >= 'ۖ' && c <= 'ۭ');
        }

        private static bool IsLetter(char c) { return Forms.ContainsKey(c) || c == Tatweel; }

        /// <summary>Joins to the letter after it: a dual-joining letter or tatweel.</summary>
        private static bool JoinsForward(char c)
        {
            if (c == Tatweel) return true;
            char[] f;
            return Forms.TryGetValue(c, out f) && f[2] != '\0';
        }

        private static char LamAlef(char alef, bool final)
        {
            switch (alef)
            {
                case 'آ': return final ? 'ﻶ' : 'ﻵ';
                case 'أ': return final ? 'ﻸ' : 'ﻷ';
                case 'إ': return final ? 'ﻺ' : 'ﻹ';
                case 'ا': return final ? 'ﻼ' : 'ﻻ';
            }
            return '\0';
        }

        /// <summary>Contextual forms and lam-alef ligatures; order unchanged.</summary>
        public static string Shape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!Forms.ContainsKey(c)) { sb.Append(c); continue; }

                int p = Prev(s, i);
                bool prevJoins = p >= 0 && JoinsForward(s[p]);

                if (c == Lam)
                {
                    int a = Next(s, i);
                    char lig = a >= 0 ? LamAlef(s[a], prevJoins) : '\0';
                    if (lig != '\0')
                    {
                        sb.Append(lig);
                        // Marks between the lam and the alef ride along; the alef is consumed.
                        for (int k = i + 1; k < a; k++) sb.Append(s[k]);
                        i = a;
                        continue;
                    }
                }

                char[] f = Forms[c];
                bool dual = f[2] != '\0';
                int n = Next(s, i);
                bool nextJoins = dual && n >= 0 && IsLetter(s[n]);

                char outC;
                if (prevJoins && nextJoins) outC = f[3];
                else if (prevJoins && f[1] != '\0') outC = f[1];
                else if (nextJoins) outC = f[2];
                else outC = f[0];
                sb.Append(outC == '\0' ? c : outC);
            }
            return sb.ToString();
        }

        private static int Prev(string s, int i)
        {
            for (int k = i - 1; k >= 0; k--) if (!IsTransparent(s[k])) return IsLetter(s[k]) ? k : -1;
            return -1;
        }

        private static int Next(string s, int i)
        {
            for (int k = i + 1; k < s.Length; k++) if (!IsTransparent(s[k])) return IsLetter(s[k]) ? k : -1;
            return -1;
        }

        // ------------------------------------------------------------------ runs

        private static bool IsStrongLtr(char c)
        {
            if (c >= '0' && c <= '9') return true;
            if (c >= 'A' && c <= 'Z') return true;
            if (c >= 'a' && c <= 'z') return true;
            if (c >= 'À' && c <= 'ɏ' && c != '×' && c != '÷') return true;
            if (c >= 'Ͱ' && c <= 'ԯ') return true;    // Greek, Cyrillic
            if (c >= '぀' && c <= '鿿') return true;    // kana, CJK
            if (c >= '가' && c <= '힯') return true;    // Hangul
            return false;
        }

        private static bool IsStrongRtl(char c)
        {
            return (c >= '֐' && c <= 'ࣿ') || (c >= 'יִ' && c <= '﷿') || (c >= 'ﹰ' && c <= '﻿');
        }

        private enum AtomKind { Ltr, Rtl, Neutral, Tag }

        private struct Atom
        {
            public string Text;
            public AtomKind Kind;
        }

        /// <summary>Characters, with {identifier} placeholders and &lt;tags&gt; kept whole.</summary>
        private static List<Atom> Atoms(string s)
        {
            var list = new List<Atom>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '{')
                {
                    int e = s.IndexOf('}', i);
                    if (e > i && AuiSchema.IsIdentifier(s.Substring(i + 1, e - i - 1).Trim()))
                    {
                        list.Add(new Atom { Text = s.Substring(i, e - i + 1), Kind = AtomKind.Ltr });
                        i = e + 1;
                        continue;
                    }
                }
                if (c == '<')
                {
                    int e = s.IndexOf('>', i);
                    if (e > i)
                    {
                        list.Add(new Atom { Text = s.Substring(i, e - i + 1), Kind = AtomKind.Tag });
                        i = e + 1;
                        continue;
                    }
                }
                AtomKind k = IsStrongLtr(c) ? AtomKind.Ltr : IsStrongRtl(c) ? AtomKind.Rtl : AtomKind.Neutral;
                list.Add(new Atom { Text = c.ToString(), Kind = k });
                i++;
            }
            return list;
        }

        private static string Mirror(string t)
        {
            switch (t)
            {
                case "(": return ")";
                case ")": return "(";
                case "[": return "]";
                case "]": return "[";
                case "«": return "»";
                case "»": return "«";
            }
            return t;
        }

        /// <summary>
        /// Prepare a string for a label with isRightToLeftText: shaped, LTR runs reversed so TMP's
        /// right-to-left placement shows them the right way round, brackets mirrored.
        /// </summary>
        public static string Prepare(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            List<Atom> atoms = Atoms(Shape(s));
            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < atoms.Count)
            {
                Atom a = atoms[i];
                if (a.Kind != AtomKind.Ltr)
                {
                    sb.Append(a.Kind == AtomKind.Neutral ? Mirror(a.Text) : a.Text);
                    i++;
                    continue;
                }
                // A run: from this strong LTR atom to the last strong LTR atom reachable through
                // neutrals only. A tag or an RTL character ends it; trailing neutrals stay outside.
                int end = i;
                for (int k = i + 1; k < atoms.Count; k++)
                {
                    if (atoms[k].Kind == AtomKind.Ltr) end = k;
                    else if (atoms[k].Kind != AtomKind.Neutral) break;
                }
                for (int k = end; k >= i; k--)
                {
                    string t = atoms[k].Text;
                    // A placeholder stays as written (its value is reversed at runtime); a plain
                    // character is one char, so appending in reverse order reverses the run.
                    sb.Append(t);
                }
                i = end + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// For a label drawn LEFT to right (isRightToLeftText off): shaped, then the whole line in
        /// visual order. Single-line use only; a wrapped visual string would wrap backwards.
        /// </summary>
        public static string ToVisual(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string logical = Prepare(s);
            char[] a = logical.ToCharArray();
            System.Array.Reverse(a);
            return new string(a);
        }

        /// <summary>Whether the string has anything right-to-left in it at all.</summary>
        public static bool HasRtl(string s)
        {
            if (s == null) return false;
            foreach (char c in s) if (IsStrongRtl(c)) return true;
            return false;
        }
    }
}
