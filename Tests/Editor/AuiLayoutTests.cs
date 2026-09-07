using System.Collections.Generic;
using System.Linq;
using Armagan.AUI.Core;
using NUnit.Framework;

namespace Armagan.AUI.Tests
{
    public class AuiLayoutTests
    {
        /// <summary>Deterministic: every character is 6 mm wide, a line is fontSize tall.</summary>
        private sealed class StubMeasurer : IAuiTextMeasurer
        {
            public void Measure(string text, float fontSize, int weight, float maxWidth, bool wrap, out float width, out float height)
            {
                width = (text ?? "").Length * 6f;
                height = fontSize;
                if (wrap && maxWidth > 0 && width > maxWidth)
                {
                    int lines = (int)System.Math.Ceiling(width / maxWidth);
                    width = maxWidth;
                    height = fontSize * lines;
                }
            }
        }

        private static AuiLayoutNode[] Solve(string markup, string sheet, out AuiMarkupDocument doc, out AuiDiagnosticBag bag, Dictionary<string, string[]> translations = null)
        {
            doc = AuiParser.Parse(markup, "V.aui");
            bag = new AuiDiagnosticBag("V.aui");
            bag.AddRange(doc.Diagnostics);
            var sheets = new List<AuiStyleSheet>();
            if (sheet != null) sheets.Add(AuiStyleParser.Parse(sheet, "V.auis", AuiStyleTier.Linked));
            foreach (AuiStyleBlock b in doc.Styles) sheets.Add(AuiStyleParser.Parse(b.Text, "V.aui", AuiStyleTier.Inline, b.Line, b.Column));
            var styles = new AuiStyleResolver(sheets).Resolve(doc, bag);
            var info = AuiAnalyzer.Analyze(doc, new AuiAnalysisContext(), bag);
            var ctx = new AuiLayoutContext { Measurer = new StubMeasurer(), BoundTextSample = "WWWWWWWWWW" };
            if (translations != null) ctx.Translations = k => translations.ContainsKey(k) ? translations[k] : null;
            return new AuiLayoutSolver(doc, styles, info, ctx, bag).Solve();
        }

        private static AuiLayoutNode ById(AuiLayoutNode[] l, AuiMarkupDocument doc, string id)
        {
            return l[doc.FindById(id).Index];
        }

        [Test]
        public void ColumnFlowWithPaddingAndGap()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var l = Solve("<view name=\"V\" width=\"200\" height=\"100\"><text id=\"a\">Hello</text><text id=\"b\">Hi</text></view>",
                "view { padding: 10; gap: 5; font-size: 10 } text { }", out doc, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items));
            var a = ById(l, doc, "a");
            var b = ById(l, doc, "b");
            Assert.AreEqual(10, a.Local.X); Assert.AreEqual(10, a.Local.Y);
            Assert.AreEqual(180, a.Local.W, 0.01);           // stretch across
            Assert.AreEqual(11, a.Local.H, 0.01);            // fontSize 10 + 1 slack
            Assert.AreEqual(10 + 11 + 5, b.Local.Y, 0.01);
            Assert.AreEqual(32, a.IntrinsicW, 0.01);         // 5 chars * 6 + 2
        }

        [Test]
        public void RowGrowSharesLeftover()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var l = Solve("<view name=\"V\" width=\"300\" height=\"50\"><panel id=\"r\" class=\"row\"><icon id=\"i\" name=\"x\" size=\"20\"/><spacer id=\"s\"/><icon id=\"j\" name=\"x\" size=\"20\"/></panel></view>",
                "view { padding: 0 } .row { flex-direction: row; gap: 10; align: center } spacer { grow: 1 }", out doc, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items));
            var s = ById(l, doc, "s");
            var j = ById(l, doc, "j");
            Assert.AreEqual(300 - 40 - 20, s.Local.W, 0.01);
            Assert.AreEqual(280, j.Local.X, 0.01);
            Assert.AreEqual(20, j.Local.W);
            // The view is a column, so stretch widens the row to 300; its height stays intrinsic (20).
            var r = ById(l, doc, "r");
            Assert.AreEqual(300, r.Local.W);
            Assert.AreEqual(20, r.Local.H);
            Assert.AreEqual(0, j.Local.Y, 0.01);
        }

        [Test]
        public void JustifyVariants()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var l = Solve("<view name=\"V\" width=\"100\" height=\"40\"><panel id=\"c\" style=\"justify: center\"><icon id=\"a\" name=\"x\" size=\"10\"/><icon id=\"b\" name=\"x\" size=\"10\"/></panel><panel id=\"e\" style=\"justify: end\"><icon id=\"d\" name=\"x\" size=\"10\"/></panel><panel id=\"sb\" style=\"flex-direction: row; justify: space-between\"><icon id=\"p\" name=\"x\" size=\"10\"/><icon id=\"q\" name=\"x\" size=\"10\"/></panel></view>",
                "view { padding: 0; gap: 0 } panel { height: 30%; gap: 0; padding: 0 }", out doc, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items));
            // each panel is 30% of 40 = 12 tall; centered pair of 10s in 12 -> starts at -4 (overflow clamps leftover to 0 with a warning)
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.LayoutOverflow));
            var d = ById(l, doc, "d");
            Assert.AreEqual(2, d.Local.Y, 0.01);                      // end: 12 - 10
            var q = ById(l, doc, "q");
            Assert.AreEqual(90, q.Local.X, 0.01);                     // space-between in 100
        }

        [Test]
        public void AnchorsAndPercent()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var l = Solve("<view name=\"V\" width=\"200\" height=\"100\"><panel id=\"f\" style=\"anchor: fill\"/><icon id=\"br\" name=\"x\" size=\"10\" style=\"anchor: bottom\"/><panel id=\"half\" style=\"width: 50%; height: 20\"/></view>",
                "view { padding: 10; gap: 0 } panel { padding: 0 }", out doc, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items));
            var f = ById(l, doc, "f");
            Assert.IsTrue(f.AnchorFill);
            Assert.AreEqual(10, f.Local.X); Assert.AreEqual(180, f.Local.W); Assert.AreEqual(80, f.Local.H);
            var br = ById(l, doc, "br");
            Assert.AreEqual(90 - 10, br.Local.Y, 0.01);
            Assert.AreEqual(10 + 85, br.Local.X, 0.01);
            var half = ById(l, doc, "half");
            Assert.AreEqual(90, half.Local.W, 0.01);
            Assert.AreEqual(10, half.Local.Y, 0.01);                  // first flow child
        }

        [Test]
        public void TextMeasuresWidestTranslation()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var tr = new Dictionary<string, string[]> { { "k", new[] { "Hi", "Merhaba dunya", "こんにちは" } } };
            var l = Solve("<view name=\"V\" width=\"200\" height=\"100\"><panel class=\"row\"><text id=\"t\" key=\"k\"/></panel></view>",
                "view { padding: 0; font-size: 10 } .row { flex-direction: row; align: start }", out doc, out bag, tr);
            var t = ById(l, doc, "t");
            Assert.AreEqual(13 * 6 + 2, t.IntrinsicW, 0.01);
            Assert.AreEqual(t.IntrinsicW, t.Local.W, 0.01);
        }

        [Test]
        public void BoundTextIsContentSized()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var l = Solve("<view name=\"V\" width=\"200\" height=\"100\"><text id=\"t\" text=\"{name}\"/><text id=\"k\" key=\"k\" args=\"{name}\"/></view>", "view { padding: 0 }", out doc, out bag,
                new Dictionary<string, string[]> { { "k", new[] { "Hello {name}" } } });
            Assert.IsTrue(ById(l, doc, "t").ContentSized);
            Assert.IsTrue(ById(l, doc, "k").ContentSized);
            Assert.AreEqual(10 * 6 + 2, ById(l, doc, "t").IntrinsicW, 0.01);
            Assert.AreEqual(("Hello " + "WWWWWW").Length * 6 + 2, ById(l, doc, "k").IntrinsicW, 0.01);
        }

        [Test]
        public void WrappedTextGetsHeightFromMaxWidth()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var l = Solve("<view name=\"V\" width=\"200\" height=\"100\"><text id=\"t\" wrap=\"true\" style=\"max-width: 60; font-size: 10\">abcdefghijklmnopqrst</text></view>", "view { padding: 0 }", out doc, out bag);
            var t = ById(l, doc, "t");
            Assert.AreEqual(60, t.IntrinsicW, 0.01);   // max-width clamps the slack too
            Assert.AreEqual(21, t.IntrinsicH, 0.01);   // 120/60 = 2 lines * 10 + 1
        }

        [Test]
        public void RepeatReservesInstancesAndPositions()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var l = Solve("<view name=\"V\" width=\"200\" height=\"200\"><repeat id=\"rows\" max=\"3\"><panel id=\"item\" style=\"height: 30\"/></repeat></view>",
                "view { padding: 0 } repeat { gap: 5; padding: 0 } panel { padding: 0 }", out doc, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items));
            var r = ById(l, doc, "rows");
            Assert.IsTrue(r.ContentSized);
            Assert.AreEqual(3 * 30 + 2 * 5, r.IntrinsicH, 0.01);
            Assert.AreEqual(3, r.Instances.Count);
            Assert.AreEqual(35, r.Instances[1].Y, 0.01);
            Assert.AreEqual(200, r.Instances[1].W, 0.01);      // column list stretches to width
            var item = ById(l, doc, "item");
            Assert.AreEqual(0, item.Local.Y, 0.01);
            Assert.AreEqual(200, item.Local.W, 0.01);
        }

        [Test]
        public void GridRepeatWraps()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var l = Solve("<view name=\"V\" width=\"100\" height=\"200\"><repeat id=\"g\" max=\"5\" class=\"grid\"><icon name=\"x\" size=\"40\"/></repeat></view>",
                "view { padding: 0 } .grid { flex-direction: row; wrap: true; gap: 10; padding: 0 }", out doc, out bag);
            var g = ById(l, doc, "g");
            Assert.AreEqual(5, g.Instances.Count);
            Assert.AreEqual(0, g.Instances[0].X); Assert.AreEqual(50, g.Instances[1].X);
            Assert.AreEqual(0, g.Instances[2].X); Assert.AreEqual(50, g.Instances[2].Y);
            Assert.AreEqual(100, g.Instances[4].Y);
        }

        [Test]
        public void PagesFillAndSelectOptionsStackBelow()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var l = Solve("<view name=\"V\" width=\"200\" height=\"200\"><pages id=\"pg\"><page id=\"a\"/><page id=\"b\"/></pages><select id=\"s\" bind=\"{q}\"><option id=\"o1\">Low</option><option id=\"o2\">High</option></select></view>",
                "view { padding: 0; gap: 0 } pages { grow: 1 } page { padding: 0 } select { height: 30 } option { min-height: 20; padding: 0 }", out doc, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items));
            var a = ById(l, doc, "a");
            Assert.IsTrue(a.AnchorFill);
            Assert.AreEqual(170, a.Local.H, 0.01);
            var o2 = ById(l, doc, "o2");
            Assert.AreEqual(50, o2.Local.Y, 0.01);
            Assert.AreEqual(200, o2.Local.W, 0.01);
        }

        [Test]
        public void DisplayNoneAndHiddenFreeTheirSpace()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var l = Solve("<view name=\"V\" width=\"200\" height=\"100\"><icon id=\"a\" name=\"x\" size=\"10\" style=\"display: none\"/><icon id=\"h\" name=\"x\" size=\"10\" hidden=\"\"/><icon id=\"b\" name=\"x\" size=\"10\"/></view>",
                "view { padding: 0; gap: 5 }", out doc, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items));
            var a = ById(l, doc, "a");
            Assert.IsTrue(a.Hidden);
            Assert.IsTrue(a.OutOfFlow);
            Assert.AreEqual(0, a.Local.W); Assert.AreEqual(0, a.IntrinsicH);
            Assert.IsTrue(ById(l, doc, "h").Hidden);
            Assert.AreEqual(0, ById(l, doc, "b").Local.Y, 0.01);      // first flow child: nothing hidden reserves space
        }

        [Test]
        public void AbsoluteEdgesAllowNegativesAndStretch()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            var l = Solve("<view name=\"V\" width=\"200\" height=\"100\">" +
                "<icon id=\"tl\" name=\"x\" size=\"10\" style=\"position: absolute; top: -4; left: -6\"/>" +
                "<icon id=\"br\" name=\"x\" size=\"10\" style=\"position: absolute; bottom: 2; right: 3\"/>" +
                "<panel id=\"st\" style=\"position: absolute; top: 5; bottom: 5; left: 20; right: 20\"/>" +
                "<icon id=\"flow\" name=\"x\" size=\"10\"/></view>",
                "view { padding: 10; gap: 0 } panel { padding: 0 }", out doc, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items));
            var tl = ById(l, doc, "tl");
            Assert.IsTrue(tl.OutOfFlow);
            Assert.AreEqual(10 - 6, tl.Local.X, 0.01); Assert.AreEqual(10 - 4, tl.Local.Y, 0.01);
            var br = ById(l, doc, "br");
            Assert.AreEqual(10 + 180 - 3 - 10, br.Local.X, 0.01); Assert.AreEqual(10 + 80 - 2 - 10, br.Local.Y, 0.01);
            var st = ById(l, doc, "st");
            Assert.AreEqual(30, st.Local.X, 0.01); Assert.AreEqual(140, st.Local.W, 0.01);
            Assert.AreEqual(15, st.Local.Y, 0.01); Assert.AreEqual(70, st.Local.H, 0.01);
            Assert.AreEqual(10, ById(l, doc, "flow").Local.Y, 0.01);  // absolutes reserve nothing
        }

        [Test]
        public void AbsoluteDiagnostics()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            Solve("<view name=\"V\" width=\"200\" height=\"100\">" +
                "<icon name=\"x\" size=\"10\" style=\"position: absolute\"/>" +
                "<icon name=\"x\" size=\"10\" style=\"position: absolute; top: 0; bottom: 0; height: 10\"/>" +
                "<icon name=\"x\" size=\"10\" style=\"top: 4\"/></view>",
                "view { padding: 0 }", out doc, out bag);
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.AbsoluteWithoutEdges));
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.EdgeConflict));
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.EdgeIgnored));
            Solve("<view name=\"V\" width=\"200\" height=\"100\" style=\"display: none\"/>", "view { padding: 0 }", out doc, out bag);
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.DisplayOnRoot));
        }

        [Test]
        public void NegativeSizesAreRejectedButEdgesAndMarginAreNot()
        {
            AuiMarkupDocument doc; AuiDiagnosticBag bag;
            Solve("<view name=\"V\" width=\"200\" height=\"100\"><icon id=\"a\" name=\"x\" size=\"10\" style=\"width: -5; padding: -1; margin: -2; left: -3; position: absolute\"/></view>",
                "view { padding: 0 }", out doc, out bag);
            Assert.AreEqual(2, bag.Items.Count(x => x.Code == AuiCodes.LayoutNegativeSize));
        }

        [Test]
        public void RepeatCountContextLimitsRows()
        {
            var doc = AuiParser.Parse("<view name=\"V\" width=\"200\" height=\"200\"><repeat id=\"rows\" max=\"3\"><panel id=\"item\" style=\"height: 30\"/></repeat></view>", "V.aui");
            var bag = new AuiDiagnosticBag("V.aui");
            var sheets = new List<AuiStyleSheet> { AuiStyleParser.Parse("view { padding: 0 } repeat { gap: 5; padding: 0 } panel { padding: 0 }", "V.auis", AuiStyleTier.Linked) };
            var styles = new AuiStyleResolver(sheets).Resolve(doc, bag);
            var info = AuiAnalyzer.Analyze(doc, new AuiAnalysisContext(), bag);
            var ctx = new AuiLayoutContext { Measurer = new StubMeasurer(), RepeatCount = r => 2 };
            var l = new AuiLayoutSolver(doc, styles, info, ctx, bag).Solve();
            var r = ById(l, doc, "rows");
            Assert.AreEqual(2, r.Instances.Count);
            Assert.AreEqual(2 * 30 + 5, r.IntrinsicH, 0.01);
        }

        [Test]
        public void ApproxMeasurerCountsCjkWide()
        {
            var m = new AuiApproxTextMeasurer();
            float w1, h1, w2, h2;
            m.Measure("abcd", 10, 400, 0, false, out w1, out h1);
            m.Measure("日本語", 10, 400, 0, false, out w2, out h2);
            Assert.AreEqual(22, w1, 0.01);
            Assert.AreEqual(30, w2, 0.01);
            Assert.AreEqual(12.5f, h1, 0.01);
        }
    }
}
