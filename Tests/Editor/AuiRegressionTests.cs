using System.Collections.Generic;
using System.Linq;
using Armagan.AUI.Core;
using NUnit.Framework;

namespace Armagan.AUI.Tests
{
    /// <summary>
    /// One test per shipped defect found in the September 2026 review, each named after the
    /// failing input rather than the fix, so a regression reads as what the author would see.
    /// </summary>
    public class AuiRegressionTests
    {
        private static AuiResolvedStyle[] Resolve(string markup, IEnumerable<AuiStyleSheet> sheets, out AuiDiagnosticBag bag)
        {
            var doc = AuiParser.Parse(markup, "V.aui");
            bag = new AuiDiagnosticBag("V.aui");
            bag.AddRange(doc.Diagnostics);
            foreach (AuiStyleSheet s in sheets) bag.AddRange(s.Diagnostics);
            return new AuiStyleResolver(new List<AuiStyleSheet>(sheets)).Resolve(doc, bag);
        }

        private static AuiStyleSheet Linked(string s)
        {
            return AuiStyleParser.Parse(s, "V.auis", AuiStyleTier.Linked);
        }

        private static AuiStyleSheet Theme(string s)
        {
            return AuiStyleParser.Parse(s, "Theme.auis", AuiStyleTier.Theme);
        }

        // ---------------------------------------------------------------- var()

        [Test]
        public void VarFallbackWithParensSurvivesADefinedVariable()
        {
            AuiDiagnosticBag bag;
            var rs = Resolve("<view name=\"V\"><text/></view>", new[] { Linked(":root { --accent: #E0A86A }\ntext { color: var(--accent, rgba(0,0,0,1)) }") }, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items));
            Assert.AreEqual("#E0A86A", rs[1].Get("color"));
        }

        [Test]
        public void VarFallbackWithParensWhenUndefined()
        {
            AuiDiagnosticBag bag;
            var rs = Resolve("<view name=\"V\"><text/></view>", new[] { Linked("text { color: var(--nope, rgba(0,0,0,1)) }") }, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items));
            Assert.AreEqual("rgba(0,0,0,1)", rs[1].Get("color"));
        }

        [Test]
        public void NestedVarInFallbackResolves()
        {
            AuiDiagnosticBag bag;
            var rs = Resolve("<view name=\"V\"><text/></view>", new[] { Linked(":root { --b: 7 }\ntext { gap: var(--a, var(--b)) }") }, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items));
            Assert.AreEqual("7", rs[1].Get("gap"));
        }

        [Test]
        public void RuleTableSubstituteMatchesResolver()
        {
            Assert.AreEqual("#111", AuiRuleTable.Substitute("var(--a, rgba(0,0,0,1))", new[] { "--a" }, new[] { "#111" }));
            Assert.AreEqual("rgba(0,0,0,1)", AuiRuleTable.Substitute("var(--z, rgba(0,0,0,1))", new[] { "--a" }, new[] { "#111" }));
            Assert.AreEqual("7", AuiRuleTable.Substitute("var(--a, var(--b))", new[] { "--b" }, new[] { "7" }));
            Assert.AreEqual(-1, AuiRuleTable.MatchingParen("var(--a", 3));
        }

        // ---------------------------------------------------------------- font shorthand

        [Test]
        public void FontShorthandBeatsAnInheritedLonghand()
        {
            AuiDiagnosticBag bag;
            var rs = Resolve("<view name=\"V\"><panel class=\"hint\"><button key=\"x\"/></panel></view>", new[]
            {
                Theme("button { font: 500 18 }"),
                Linked(".hint { font-size: 15 }"),
            }, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items));
            Assert.AreEqual(15f, rs[1].FontSize);
            Assert.AreEqual(18f, rs[2].FontSize, "the button's own shorthand must win over the panel's inherited font-size");
            Assert.AreEqual(500, rs[2].FontWeight);
        }

        [Test]
        public void MoreSpecificFontShorthandBeatsEarlierLonghand()
        {
            AuiDiagnosticBag bag;
            var rs = Resolve("<view name=\"V\"><text class=\"title\"/></view>", new[] { Linked("text { font-size: 14 } .title { font: 600 26 }") }, out bag);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items));
            Assert.AreEqual(26f, rs[1].FontSize);
            Assert.AreEqual(600, rs[1].FontWeight);
        }

        [Test]
        public void LonghandAfterShorthandStillWins()
        {
            AuiDiagnosticBag bag;
            var rs = Resolve("<view name=\"V\"><text class=\"big\"/></view>", new[] { Linked("text { font: 600 26 } .big { font-size: 40 }") }, out bag);
            Assert.AreEqual(40f, rs[1].FontSize);
            Assert.AreEqual(600, rs[1].FontWeight);
        }

        // ---------------------------------------------------------------- stylesheet parser

        [Test]
        public void VariableOutsideRootIsAnError()
        {
            var s = Linked(".card { --pad: 4; gap: 1 }");
            Assert.IsTrue(s.Diagnostics.Items.Any(x => x.Code == AuiCodes.StyleSyntax && x.Message.Contains(":root")));
            Assert.AreEqual(0, s.Variables.Count);
            Assert.AreEqual("1", s.Rules[0].Find("gap").Value);
        }

        [Test]
        public void LineCommentInsideAValueIsDropped()
        {
            var s = Linked("text { color: #fff // note\n padding: 2 }");
            Assert.AreEqual("#fff", s.Rules[0].Find("color").Value);
            Assert.AreEqual("2", s.Rules[0].Find("padding").Value);
        }

        [Test]
        public void UnbalancedParenDoesNotSwallowTheSheet()
        {
            var s = Linked("button { background: sprite(Foo #fff; gap: 4 }\n.card { padding: 2 }");
            Assert.IsTrue(s.Diagnostics.Items.Any(x => x.Code == AuiCodes.BadStyleValue && x.Message.Contains("(")));
            Assert.AreEqual(2, s.Rules.Count, "the rule after the bad value must still parse");
            Assert.AreEqual("2", s.Rules[1].Find("padding").Value);
            Assert.AreEqual("4", s.Rules[0].Find("gap").Value);
        }

        [Test]
        public void NegativeGrowIsRejected()
        {
            AuiDiagnosticBag bag;
            var rs = Resolve("<view name=\"V\"><panel class=\"x\"/></view>", new[] { Linked(".x { grow: -1 }") }, out bag);
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.BadStyleValue));
            Assert.IsNull(rs[1].Get("grow"));
        }

        // ---------------------------------------------------------------- markup parser

        [Test]
        public void SurrogateEntityIsAWarningNotAnException()
        {
            var doc = AuiParser.Parse("<view name=\"V\"><text>a&#xD800;b</text></view>", "V.aui");
            Assert.IsTrue(doc.Diagnostics.Items.Any(x => x.Code == AuiCodes.UnknownEntity));
        }

        // ---------------------------------------------------------------- analyzer

        private static AuiViewInfo Analyze(string markup, out AuiDiagnosticBag bag, AuiAnalysisContext ctx = null)
        {
            var doc = AuiParser.Parse(markup, "Demo.aui");
            bag = new AuiDiagnosticBag("Demo.aui");
            bag.AddRange(doc.Diagnostics);
            return AuiAnalyzer.Analyze(doc, ctx ?? new AuiAnalysisContext { FileStem = "Demo" }, bag);
        }

        [Test]
        public void SelectInsideARepeatIsRefused()
        {
            AuiDiagnosticBag bag;
            Analyze("<view name=\"Demo\"><repeat id=\"rows\" max=\"2\"><panel><select bind=\"{item.q}\"><option key=\"a\"/></select></panel></repeat></view>", out bag);
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.ElementNotAllowedHere && x.Message.Contains("select")), string.Join("\n", bag.Items));
        }

        [Test]
        public void HiddenOnAnIconedElementWarns()
        {
            AuiDiagnosticBag bag;
            Analyze("<view name=\"Demo\"><button id=\"next\" icon=\"chevron-right\" hidden=\"true\" onclick=\"Next\"/></view>", out bag,
                new AuiAnalysisContext { FileStem = "Demo", IconExists = n => true });
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.BadAttributeValue && x.Severity == AuiSeverity.Warning && x.Message.Contains("hidden")), string.Join("\n", bag.Items));
        }

        [Test]
        public void HiddenOnAPlainPanelIsFine()
        {
            AuiDiagnosticBag bag;
            Analyze("<view name=\"Demo\"><panel hidden=\"true\"><spacer/></panel></view>", out bag);
            Assert.IsFalse(bag.Items.Any(x => x.Code == AuiCodes.BadAttributeValue));
        }

        [Test]
        public void TextAttributeAndLiteralTogetherIsAnError()
        {
            AuiDiagnosticBag bag;
            Analyze("<view name=\"Demo\"><text text=\"{x}\">literal</text></view>", out bag);
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.TextAndKeyBoth));
        }

        [Test]
        public void LiteralNestedViewPropIsTypeChecked()
        {
            var childDoc = AuiParser.Parse("<view name=\"Child\"><prop name=\"ratio\" type=\"float\"/><prop name=\"on\" type=\"bool\"/><text text=\"{title}\"/></view>", "Child.aui");
            AuiViewInfo child = AuiAnalyzer.Analyze(childDoc, new AuiAnalysisContext { FileStem = "Child" }, new AuiDiagnosticBag("Child.aui"));
            AuiDiagnosticBag bag;
            Analyze("<view name=\"Demo\"><Child ratio=\"abc\" on=\"yes\" title=\"fine\"/></view>", out bag,
                new AuiAnalysisContext { FileStem = "Demo", ViewExists = n => n == "Child", ChildInfo = n => child });
            var bad = bag.Items.Where(x => x.Code == AuiCodes.BadAttributeValue).ToList();
            Assert.AreEqual(2, bad.Count, string.Join("\n", bag.Items));
        }

        // ---------------------------------------------------------------- <input type="url">

        [Test]
        public void UrlInputHandlerTakesAVrcUrl()
        {
            AuiDiagnosticBag bag;
            AuiViewInfo v = Analyze("<view name=\"Demo\"><input type=\"url\" bind=\"{link}\" onsubmit=\"LinkSubmitted\"/><input bind=\"{nick}\" onsubmit=\"NickSubmitted\"/></view>", out bag);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items));
            Assert.AreEqual("void LinkSubmitted(VRCUrl value)", v.FindHandler("LinkSubmitted").Signature);
            Assert.AreEqual("void NickSubmitted(string value)", v.FindHandler("NickSubmitted").Signature);
            Assert.AreEqual(AuiBindingType.String, v.FindBinding("link", null).Type);
        }

        [Test]
        public void UrlInputTypeIsValidatedAndMaxlengthWarns()
        {
            AuiDiagnosticBag bag;
            Analyze("<view name=\"Demo\"><input type=\"email\" bind=\"{a}\"/><input type=\"url\" maxlength=\"8\" bind=\"{b}\"/></view>", out bag);
            Assert.AreEqual(1, bag.Items.Count(x => x.Code == AuiCodes.BadAttributeValue && x.Severity == AuiSeverity.Error));
            Assert.AreEqual(1, bag.Items.Count(x => x.Code == AuiCodes.BadAttributeValue && x.Severity == AuiSeverity.Warning && x.Message.Contains("maxlength")));
        }

        [Test]
        public void UrlInputCodegenPassesTheUrlToTheHandler()
        {
            AuiDiagnosticBag bag; AuiIndexMap map;
            string src = AuiCodegenTests.Generate("<view name=\"Demo\"><input id=\"link\" type=\"url\" bind=\"{link}\" onsubmit=\"LinkSubmitted\"/><repeat id=\"rows\" max=\"2\"><input type=\"url\" onsubmit=\"RowLink\"/></repeat></view>", out bag, out map);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items));
            StringAssert.Contains("using VRC.SDKBase;", src);
            StringAssert.Contains("Link = inputResult;", src);
            StringAssert.Contains("LinkSubmitted(inputResultUrl);", src);
            StringAssert.Contains("RowLink(row, inputResultUrl);", src);
        }

        // ---------------------------------------------------------------- codegen

        [Test]
        public void IdsThatCollapseToOneIdentifierAreReported()
        {
            AuiDiagnosticBag bag; AuiIndexMap map;
            AuiCodegenTests.Generate("<view name=\"Demo\"><button id=\"save-all\" onclick=\"A\"/><button id=\"saveall\" onclick=\"B\"/></view>", out bag, out map);
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.DuplicateId && x.Message.Contains("E_SAVEALL")), string.Join("\n", bag.Items));
        }

        [Test]
        public void TwoNestedViewsWithoutIdsShareAFieldAndAreReported()
        {
            AuiDiagnosticBag bag; AuiIndexMap map;
            AuiCodegenTests.Generate("<view name=\"Demo\"><LanguagePicker/><LanguagePicker/></view>", out bag, out map);
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.DuplicateId && x.Message.Contains("childLanguagePicker")), string.Join("\n", bag.Items));
        }

        [Test]
        public void BindingAndRepeatCountCollide()
        {
            AuiDiagnosticBag bag; AuiIndexMap map;
            AuiCodegenTests.Generate("<view name=\"Demo\"><text text=\"{itemsCount}\"/><repeat id=\"items\" max=\"2\"><text text=\"{item.name}\"/></repeat></view>", out bag, out map);
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.DuplicateId && x.Message.Contains("ItemsCount")), string.Join("\n", bag.Items));
        }

        [Test]
        public void DistinctNamesDoNotCollide()
        {
            AuiDiagnosticBag bag; AuiIndexMap map;
            AuiCodegenTests.Generate(AuiCodegenTests.Demo, out bag, out map);
            Assert.IsFalse(bag.Items.Any(x => x.Code == AuiCodes.DuplicateId), string.Join("\n", bag.Items));
        }

        // ---------------------------------------------------------------- layout

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

        [Test]
        public void WrapWithoutMaxWidthWrapsAtTheAvailableWidth()
        {
            var doc = AuiParser.Parse("<view name=\"V\" width=\"100\" height=\"200\"><text id=\"t\" wrap=\"true\">twenty characters here</text></view>", "V.aui");
            var bag = new AuiDiagnosticBag("V.aui");
            var sheets = new List<AuiStyleSheet> { AuiStyleParser.Parse("view { padding: 0 } text { font-size: 10; padding: 0 }", "V.auis", AuiStyleTier.Linked) };
            var styles = new AuiStyleResolver(sheets).Resolve(doc, bag);
            var info = AuiAnalyzer.Analyze(doc, new AuiAnalysisContext(), bag);
            var l = new AuiLayoutSolver(doc, styles, info, new AuiLayoutContext { Measurer = new StubMeasurer(), BoundTextSample = "W" }, bag).Solve();
            AuiLayoutNode t = l[doc.FindById("t").Index];
            // 22 chars * 6 = 132 > 100: two lines of 10, plus the 1 mm slack; one line would be 11.
            Assert.AreEqual(21f, t.IntrinsicH, 0.01);
            Assert.LessOrEqual(t.IntrinsicW, 102f);
        }

        // ---------------------------------------------------------------- workspace

        [Test]
        public void DuplicateViewStemsAreRecorded()
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aui-dup-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "a"));
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "b"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(root, "a", "Card.aui"), "<view name=\"Card\"/>");
                System.IO.File.WriteAllText(System.IO.Path.Combine(root, "b", "Card.aui"), "<view name=\"Card\"/>");
                AuiWorkspace ws = AuiWorkspace.Scan(root, null);
                Assert.AreEqual(1, ws.DuplicateViews.Count);
                StringAssert.StartsWith("Card:", ws.DuplicateViews[0]);
            }
            finally
            {
                System.IO.Directory.Delete(root, true);
            }
        }
    }
}
