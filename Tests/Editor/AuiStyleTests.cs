using System.Collections.Generic;
using System.Linq;
using Armagan.AUI.Core;
using NUnit.Framework;

namespace Armagan.AUI.Tests
{
    public class AuiStyleParserTests
    {
        private static AuiStyleSheet Parse(string s, AuiStyleTier tier = AuiStyleTier.Linked)
        {
            return AuiStyleParser.Parse(s, "Test.auis", tier);
        }

        private static string Codes(AuiStyleSheet s)
        {
            return string.Join(",", s.Diagnostics.Items.Select(x => x.Code + "@" + x.Line + ":" + x.Column));
        }

        [Test]
        public void ParsesRulesAndVariables()
        {
            var s = Parse(":root { --accent: #E0A86A; --pad: 12; }\nbutton { padding: 8 14; background: var(--accent); }\n.row, panel.card { gap: 8 }");
            Assert.IsFalse(s.Diagnostics.HasErrors, Codes(s));
            Assert.AreEqual(2, s.Variables.Count);
            Assert.AreEqual("--accent", s.Variables[0].Name);
            Assert.AreEqual("#E0A86A", s.Variables[0].Value);
            Assert.AreEqual(2, s.Rules.Count);
            Assert.AreEqual("button", s.Rules[0].Selectors[0].Tag);
            Assert.AreEqual("8 14", s.Rules[0].Find("padding").Value);
            Assert.AreEqual(2, s.Rules[1].Selectors.Count);
            Assert.AreEqual("row", s.Rules[1].Selectors[0].Class);
            Assert.AreEqual("panel", s.Rules[1].Selectors[1].Tag);
            Assert.AreEqual("card", s.Rules[1].Selectors[1].Class);
        }

        [Test]
        public void StateSelectorsAndSpecificity()
        {
            var s = Parse("button:hover { scale: 1.03 } .chip:selected { color: #fff } button.chip { gap: 4 } * { gap: 1 }");
            Assert.IsFalse(s.Diagnostics.HasErrors, Codes(s));
            Assert.AreEqual("hover", s.Rules[0].Selectors[0].State);
            Assert.AreEqual(11, s.Rules[0].Selectors[0].Specificity);
            Assert.AreEqual(12, s.Rules[1].Selectors[0].Specificity);
            Assert.AreEqual(3, s.Rules[2].Selectors[0].Specificity);
            Assert.AreEqual(0, s.Rules[3].Selectors[0].Specificity);
        }

        [Test]
        public void ReportsUnknownStateWithHint()
        {
            var s = Parse("button:hovr { scale: 1 }");
            var e = s.Diagnostics.Items.First(x => x.Code == AuiCodes.UnknownState);
            StringAssert.Contains("hover", e.Message);
            Assert.AreEqual(1, e.Line);
            Assert.AreEqual(8, e.Column);
        }

        [Test]
        public void ReportsUnknownPropertyWithHint()
        {
            var s = Parse("button { paddng: 8; gap: 4 }");
            var e = s.Diagnostics.Items.First(x => x.Code == AuiCodes.UnknownStyleProperty);
            StringAssert.Contains("padding", e.Message);
            Assert.AreEqual(10, e.Column);
            Assert.AreEqual(1, s.Rules[0].Declarations.Count);
        }

        [Test]
        public void RejectsDescendantSelectors()
        {
            var s = Parse(".card .title { gap: 1 }\nbutton { gap: 2 }");
            Assert.IsTrue(s.Diagnostics.Items.Any(x => x.Code == AuiCodes.BadSelector));
            Assert.AreEqual(1, s.Rules.Count);
            Assert.AreEqual("button", s.Rules[0].Selectors[0].Tag);
        }

        [Test]
        public void CommentsAndMissingSemicolon()
        {
            var s = Parse("/* c */ button { gap: 4 /* inline */\n  padding: 2\n}");
            Assert.IsFalse(s.Diagnostics.HasErrors, Codes(s));
            Assert.AreEqual("4", s.Rules[0].Find("gap").Value);
            Assert.AreEqual("2", s.Rules[0].Find("padding").Value);
            Assert.IsTrue(s.Diagnostics.Items.Any(x => x.Code == AuiCodes.StyleSyntax && x.Severity == AuiSeverity.Warning));
        }

        [Test]
        public void MissingBraceIsReported()
        {
            var s = Parse("button { gap: 4;");
            Assert.IsTrue(s.Diagnostics.Items.Any(x => x.Code == AuiCodes.StyleSyntax));
            Assert.AreEqual(1, s.Rules.Count);
        }

        [Test]
        public void EmbeddedBlockOffsetsPositions()
        {
            var s = AuiStyleParser.Parse("\n.a { gapp: 1 }", "V.aui", AuiStyleTier.Inline, 10, 1);
            var e = s.Diagnostics.Items.First();
            Assert.AreEqual(11, e.Line);
            Assert.AreEqual(6, e.Column);
        }

        [Test]
        public void ValuesWithParensKeepSemicolonsInside()
        {
            var s = Parse("button { background: sprite(Rounded8) rgba(1, 2, 3, 0.5); gap: 1 }");
            Assert.AreEqual("sprite(Rounded8) rgba(1, 2, 3, 0.5)", s.Rules[0].Find("background").Value);
        }

        [Test]
        public void DeclarationsOnlyParse()
        {
            var bag = new AuiDiagnosticBag("V.aui");
            var r = AuiStyleParser.ParseDeclarations("gap: 4; color: #fff", "V.aui", 3, 20, bag);
            Assert.AreEqual(2, r.Declarations.Count);
            Assert.AreEqual(0, bag.Items.Count);
        }
    }

    public class AuiValueTests
    {
        [Test]
        public void ColorForms()
        {
            AuiColor c;
            Assert.IsTrue(AuiColor.TryParse("#E0A86A", out c));
            Assert.AreEqual("#E0A86A", c.ToHex());
            Assert.IsTrue(AuiColor.TryParse("#fff", out c));
            Assert.AreEqual("#FFFFFF", c.ToHex());
            Assert.IsTrue(AuiColor.TryParse("#12345680", out c));
            Assert.AreEqual("#12345680", c.ToHex());
            Assert.IsTrue(AuiColor.TryParse("rgba(255, 0, 0, 0.5)", out c));
            Assert.AreEqual(1f, c.R);
            Assert.AreEqual(0.5f, c.A);
            Assert.IsTrue(AuiColor.TryParse("transparent", out c));
            Assert.IsTrue(c.IsTransparent);
            Assert.IsFalse(AuiColor.TryParse("#12", out c));
            Assert.IsFalse(AuiColor.TryParse("red", out c));
        }

        [Test]
        public void LengthForms()
        {
            AuiLength l;
            Assert.IsTrue(AuiLength.TryParse("12", out l)); Assert.AreEqual(12f, l.Value);
            Assert.IsTrue(AuiLength.TryParse("12.5mm", out l)); Assert.AreEqual(12.5f, l.Value);
            Assert.IsTrue(AuiLength.TryParse("50%", out l)); Assert.IsTrue(l.IsPercent); Assert.AreEqual(100f, l.Resolve(200, 0));
            Assert.IsTrue(AuiLength.TryParse("auto", out l)); Assert.IsTrue(l.IsAuto); Assert.AreEqual(7f, l.Resolve(200, 7));
            Assert.IsFalse(AuiLength.TryParse("12px", out l));
        }

        [Test]
        public void BoxForms()
        {
            AuiBox b;
            Assert.IsTrue(AuiBox.TryParse("8", out b)); Assert.AreEqual(8, b.Left); Assert.AreEqual(8, b.Top);
            Assert.IsTrue(AuiBox.TryParse("8 14", out b)); Assert.AreEqual(8, b.Top); Assert.AreEqual(14, b.Left); Assert.AreEqual(28, b.Horizontal);
            Assert.IsTrue(AuiBox.TryParse("1 2 3", out b)); Assert.AreEqual(3, b.Bottom); Assert.AreEqual(2, b.Right);
            Assert.IsTrue(AuiBox.TryParse("1 2 3 4", out b)); Assert.AreEqual(4, b.Left);
            Assert.IsFalse(AuiBox.TryParse("1 2 3 4 5", out b));
        }

        [Test]
        public void BackgroundForms()
        {
            AuiBackground bg;
            Assert.IsTrue(AuiBackground.TryParse("none", out bg)); Assert.IsTrue(bg.IsNone);
            Assert.IsTrue(AuiBackground.TryParse("#111", out bg)); Assert.IsNull(bg.Sprite);
            Assert.IsTrue(AuiBackground.TryParse("sprite(Rounded8) #E0A86A", out bg)); Assert.AreEqual("Rounded8", bg.Sprite); Assert.AreEqual("#E0A86A", bg.Color.ToHex());
            Assert.IsTrue(AuiBackground.TryParse("sprite(X)", out bg)); Assert.AreEqual("#FFFFFF", bg.Color.ToHex());
        }

        [Test]
        public void TransitionForms()
        {
            AuiTransition t; string err;
            Assert.IsTrue(AuiTransition.TryParse("background 0.12s out-quad", out t, out err));
            Assert.AreEqual("background", t.Property); Assert.AreEqual(0.12f, t.Duration, 1e-5); Assert.AreEqual("out-quad", t.Ease);
            Assert.IsTrue(AuiTransition.TryParse("scale 80ms", out t, out err)); Assert.AreEqual(0.08f, t.Duration, 1e-5);
            Assert.IsTrue(AuiTransition.TryParse("color", out t, out err)); Assert.AreEqual(0.15f, t.Duration, 1e-5);
            Assert.IsFalse(AuiTransition.TryParse("color 1s wobble", out t, out err));
        }
    }

    public class AuiStyleResolverTests
    {
        private static AuiStyleSheet Theme(string s)
        {
            return AuiStyleParser.Parse(s, "Theme.auis", AuiStyleTier.Theme);
        }

        private static AuiStyleSheet Linked(string s)
        {
            return AuiStyleParser.Parse(s, "V.auis", AuiStyleTier.Linked);
        }

        private static AuiResolvedStyle[] Resolve(string markup, IEnumerable<AuiStyleSheet> sheets, out AuiMarkupDocument doc, out AuiDiagnosticBag bag)
        {
            doc = AuiParser.Parse(markup, "V.aui");
            var all = new List<AuiStyleSheet>(sheets);
            foreach (AuiStyleBlock b in doc.Styles) all.Add(AuiStyleParser.Parse(b.Text, "V.aui", AuiStyleTier.Inline, b.Line, b.Column));
            bag = new AuiDiagnosticBag("V.aui");
            bag.AddRange(doc.Diagnostics);
            foreach (AuiStyleSheet s in all) bag.AddRange(s.Diagnostics);
            return new AuiStyleResolver(all).Resolve(doc, bag);
        }

        [Test]
        public void CascadeOrderTierThenSpecificityThenOrder()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var rs = Resolve("<view name=\"V\"><button class=\"a b\"/></view>", new[]
            {
                Theme("button { gap: 1; padding: 1 } .a { gap: 2 }"),
                Linked("button { gap: 3 } .b { gap: 4 } .a { gap: 5 } button.a { padding: 9 }"),
            }, out doc, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join(",", bag.Items));
            var b = rs[1];
            // .a (5) declared after .b (4) at same specificity in the same tier -> 5
            Assert.AreEqual("5", b.Get("gap"));
            Assert.AreEqual("9", b.Get("padding"));
        }

        [Test]
        public void InlineStyleBlockAndAttributeWin()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var rs = Resolve("<view name=\"V\"><style>button { gap: 7 }</style><button style=\"gap: 8\"/><button/></view>", new[] { Linked("button { gap: 3 }") }, out doc, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join(",", bag.Items));
            Assert.AreEqual("8", rs[1].Get("gap"));
            Assert.AreEqual("7", rs[2].Get("gap"));
        }

        [Test]
        public void VariablesSubstituteAcrossTiers()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var rs = Resolve("<view name=\"V\"><text/></view>", new[]
            {
                Theme(":root { --accent: #111111; --x: 4 } text { color: var(--accent); gap: var(--missing, 2) }"),
                Linked(":root { --accent: #222222 }"),
            }, out doc, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join(",", bag.Items));
            Assert.AreEqual("#222222", rs[1].Get("color"));
            Assert.AreEqual("2", rs[1].Get("gap"));
        }

        [Test]
        public void UndefinedVariableIsErrorOnTheSheetFile()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var rs = Resolve("<view name=\"V\"><text/></view>", new[] { Linked("text { color: var(--acent) }\n:root { --accent: #fff }") }, out doc, out bag);
            var e = bag.Items.First(x => x.Code == AuiCodes.UndefinedVariable);
            Assert.AreEqual("V.auis", e.File);
            StringAssert.Contains("--accent", e.Message);
            Assert.IsNull(rs[1].Get("color"));
        }

        [Test]
        public void InheritanceOnlyForInheritedProps()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var rs = Resolve("<view name=\"V\"><panel><text/></panel></view>", new[] { Linked("view { color: #abcdef; padding: 10; font: 600 20 } text { font-size: 12 }") }, out doc, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join(",", bag.Items));
            var text = rs[2];
            Assert.AreEqual("#abcdef", text.Get("color"));
            Assert.IsNull(text.Get("padding"));
            Assert.AreEqual(600, text.FontWeight);
            Assert.AreEqual(12f, text.FontSize);
            Assert.AreEqual(20f, rs[1].FontSize);
        }

        [Test]
        public void StateRulesCollectOnlyAnimatable()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var rs = Resolve("<view name=\"V\"><button/></view>", new[] { Linked("button { color: #fff; transition: color 0.2s out-cubic } button:hover { color: #000; padding: 4 } button:pressed { scale: 0.9 }") }, out doc, out bag);
            var b = rs[1];
            Assert.AreEqual("#000", b.GetState("hover", "color"));
            Assert.IsNull(b.GetState("hover", "padding"));
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.NonAnimatableTransition));
            Assert.AreEqual("0.9", b.GetState("pressed", "scale"));
            float dur; string ease;
            Assert.IsTrue(b.TransitionFor("color", out dur, out ease));
            Assert.AreEqual(0.2f, dur, 1e-5);
            Assert.AreEqual("out-cubic", ease);
            Assert.IsFalse(b.TransitionFor("scale", out dur, out ease));
        }

        [Test]
        public void BadValuesAreReportedOnce()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            Resolve("<view name=\"V\"><button/><button/></view>", new[] { Linked("button { gap: lots; justify: centre }") }, out doc, out bag);
            var bad = bag.Items.Where(x => x.Code == AuiCodes.BadStyleValue).ToList();
            Assert.AreEqual(2, bad.Count);
            StringAssert.Contains("center", bad[1].Message);
        }

        [Test]
        public void UnknownAndUnusedClasses()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            Resolve("<view name=\"V\"><panel class=\"rwo\"/></view>", new[] { Theme(".row { gap: 1 }"), Linked(".hero { gap: 2 }") }, out doc, out bag);
            var unknown = bag.Items.First(x => x.Code == AuiCodes.UnknownClass);
            StringAssert.Contains("row", unknown.Message);
            Assert.AreEqual("V.aui", unknown.File);
            var unused = bag.Items.First(x => x.Code == AuiCodes.UnusedClass);
            Assert.AreEqual("V.auis", unused.File);
        }

        [Test]
        public void DefaultThemeParsesClean()
        {
            string path = System.IO.Path.Combine(TestPaths.PackageRoot, "Theme/Default.auis");
            var theme = AuiStyleParser.Parse(System.IO.File.ReadAllText(path), "Default.auis", AuiStyleTier.Theme);
            Assert.IsFalse(theme.Diagnostics.HasErrors, string.Join("\n", theme.Diagnostics.Items));
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var rs = Resolve("<view name=\"V\"><button class=\"ghost\"><icon name=\"x\"/><text key=\"k\"/></button><slider/></view>", new[] { theme }, out doc, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items));
            Assert.AreEqual("none", rs[1].Get("background"));
            Assert.IsTrue(rs[1].HasState("hover"));
            Assert.AreEqual("#EEE1D2", rs[3].Get("color"));
            Assert.AreEqual(500, rs[3].FontWeight);
        }
    }

    internal static class TestPaths
    {
        public static string PackageRoot
        {
            get
            {
                // Tests/Editor/<this>.cs -> package root. Works both under Packages/ and in the standalone repo.
                string file = new System.Diagnostics.StackTrace(true).GetFrame(0).GetFileName();
                if (!string.IsNullOrEmpty(file))
                    return System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file), "../.."));
                return System.IO.Path.GetFullPath("Packages/rest.armagan.aui");
            }
        }
    }
}
