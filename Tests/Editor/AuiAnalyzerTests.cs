using System.Linq;
using Armagan.AUI.Core;
using NUnit.Framework;

namespace Armagan.AUI.Tests
{
    public class AuiAnalyzerTests
    {
        private static AuiViewInfo Analyze(string markup, out AuiDiagnosticBag bag, AuiAnalysisContext ctx = null)
        {
            var doc = AuiParser.Parse(markup, "Demo.aui");
            bag = new AuiDiagnosticBag("Demo.aui");
            bag.AddRange(doc.Diagnostics);
            return AuiAnalyzer.Analyze(doc, ctx ?? new AuiAnalysisContext { FileStem = "Demo" }, bag);
        }

        private static string Errors(AuiDiagnosticBag b)
        {
            return string.Join("\n", b.Items.Where(x => x.Severity == AuiSeverity.Error));
        }

        private const string Demo =
            "<view name=\"Demo\" width=\"400\" height=\"300\">\n" +
            "  <text key=\"demo.title\" class=\"title\"/>\n" +
            "  <text key=\"demo.greeting\" args=\"{playerName}\"/>\n" +
            "  <button onclick=\"Greet\" key=\"demo.greet\" disabled=\"{busy}\"/>\n" +
            "  <repeat id=\"rows\" max=\"3\" onselect=\"RowClick\">\n" +
            "    <button onclick=\"RowBuy\"><text text=\"{item.name}\"/></button>\n" +
            "    <empty><text key=\"demo.empty\"/></empty>\n" +
            "  </repeat>\n" +
            "  <tabs bind=\"{page}\"><tab page=\"first\" key=\"demo.tab1\"/><tab page=\"second\" key=\"demo.tab2\"/></tabs>\n" +
            "  <pages id=\"main\" default=\"second\">\n" +
            "    <page id=\"first\"><slider bind=\"{volume}\" onchange=\"VolumeChanged\"/></page>\n" +
            "    <page id=\"second\"><toggle bind=\"{muted}\" onchange=\"MuteChanged\" key=\"demo.mute\"/><input bind=\"{nick}\" onsubmit=\"NickSubmitted\"/></page>\n" +
            "  </pages>\n" +
            "  <select bind=\"{quality}\" onchange=\"QualityChanged\"><option value=\"low\" key=\"q.low\"/><option value=\"high\" key=\"q.high\"/></select>\n" +
            "  <LanguagePicker id=\"lp\" title=\"{playerName}\" on-picked=\"Picked\"/>\n" +
            "</view>";

        [Test]
        public void DemoAnalyzesClean()
        {
            AuiDiagnosticBag bag;
            var v = Analyze(Demo, out bag, new AuiAnalysisContext { FileStem = "Demo", ViewExists = n => n == "LanguagePicker" });
            Assert.IsFalse(bag.HasErrors, Errors(bag));
            Assert.AreEqual("Demo", v.Name);
            Assert.AreEqual(400f, v.Width);
            Assert.AreEqual(300f, v.Height);
        }

        [Test]
        public void BindingsAreTypedAndDeduplicated()
        {
            AuiDiagnosticBag bag;
            var v = Analyze(Demo, out bag, new AuiAnalysisContext { ViewExists = n => true });
            var names = v.Bindings.Select(b => b.Name + ":" + b.CsType).ToList();
            CollectionAssert.AreEquivalent(new[] { "playerName:string", "busy:bool", "page:int", "volume:float", "muted:bool", "nick:string", "quality:int" }, names);
            Assert.AreEqual(2, v.Bindings.First(b => b.Name == "playerName").Uses.Count);
            Assert.AreEqual("PlayerName", v.Bindings.First(b => b.Name == "playerName").PropertyName);
        }

        [Test]
        public void HandlersHaveSignatures()
        {
            AuiDiagnosticBag bag;
            var v = Analyze(Demo, out bag, new AuiAnalysisContext { ViewExists = n => true });
            var sigs = v.Handlers.Select(h => h.Signature).ToList();
            CollectionAssert.Contains(sigs, "void Greet()");
            CollectionAssert.Contains(sigs, "void RowClick(int row)");
            CollectionAssert.Contains(sigs, "void RowBuy(int row)");
            CollectionAssert.Contains(sigs, "void VolumeChanged(float value)");
            CollectionAssert.Contains(sigs, "void MuteChanged(bool value)");
            CollectionAssert.Contains(sigs, "void NickSubmitted(string value)");
            CollectionAssert.Contains(sigs, "void QualityChanged(int value)");
            CollectionAssert.Contains(sigs, "void Picked()");
        }

        [Test]
        public void RepeatsPagesSelectsNested()
        {
            AuiDiagnosticBag bag;
            var v = Analyze(Demo, out bag, new AuiAnalysisContext { ViewExists = n => true });
            Assert.AreEqual(1, v.Repeats.Count);
            var r = v.Repeats[0];
            Assert.AreEqual("rows", r.Id);
            Assert.AreEqual(3, r.Max);
            Assert.AreEqual("button", r.Template.Tag);
            Assert.IsNotNull(r.Empty);
            Assert.AreEqual("name", r.ItemBindings[0].Name);
            Assert.IsTrue(r.ItemBindings[0].IsItem);
            Assert.AreEqual(1, r.Handlers.Count);

            Assert.AreEqual(1, v.PageGroups.Count);
            var g = v.PageGroups[0];
            Assert.AreEqual("main", g.Id);
            Assert.AreEqual(2, g.Pages.Count);
            Assert.AreEqual(1, g.DefaultIndex);
            Assert.IsNotNull(g.Tabs);
            Assert.AreEqual(2, g.TabNodes.Count);
            Assert.AreEqual("page", g.Binding.Name);

            Assert.AreEqual(1, v.Selects.Count);
            Assert.AreEqual(2, v.Selects[0].Options.Count);
            Assert.AreEqual(2, v.Selects[0].Max);
            Assert.AreEqual("quality", v.Selects[0].Binding.Name);

            Assert.AreEqual(1, v.NestedViews.Count);
            Assert.AreEqual("LanguagePicker", v.NestedViews[0].ViewName);
            Assert.AreEqual("childLanguagePickerLp", v.NestedViews[0].FieldName);

            var keys = v.Keys.Select(k => k.Key).ToList();
            CollectionAssert.AreEquivalent(new[] { "demo.title", "demo.greeting", "demo.greet", "demo.empty", "demo.tab1", "demo.tab2", "demo.mute", "q.low", "q.high" }, keys);
            Assert.AreEqual("playerName", v.Keys.First(k => k.Key == "demo.greeting").Args[0].Name);
        }

        [Test]
        public void ReportsNameMismatchAndUnknownTagAndAttribute()
        {
            AuiDiagnosticBag bag;
            Analyze("<view name=\"Other\"><pannel><text kye=\"x\"/></pannel></view>", out bag);
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.ViewNameMismatch));
            var tag = bag.Items.First(x => x.Code == AuiCodes.UnknownTag);
            StringAssert.Contains("panel", tag.Message);
            var attr = bag.Items.First(x => x.Code == AuiCodes.UnknownAttribute);
            StringAssert.Contains("key", attr.Message);
        }

        [Test]
        public void ReportsBindingConflicts()
        {
            AuiDiagnosticBag bag;
            Analyze("<view name=\"Demo\"><text text=\"{x}\"/><toggle bind=\"{x}\"/></view>", out bag);
            var e = bag.Items.First(x => x.Code == AuiCodes.BindingTypeConflict);
            StringAssert.Contains("bool", e.Message);
            StringAssert.Contains("string", e.Message);
        }

        [Test]
        public void ReportsItemBindingOutsideRepeatAndBadSyntax()
        {
            AuiDiagnosticBag bag;
            Analyze("<view name=\"Demo\"><text text=\"{item.x}\"/><text text=\"plain\"/><button onclick=\"do it\"/></view>", out bag);
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.ItemBindingOutsideRepeat));
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.BadBindingSyntax));
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.HandlerNotIdentifier));
        }

        [Test]
        public void ReportsStructureProblems()
        {
            AuiDiagnosticBag bag;
            Analyze("<view name=\"Demo\">" +
                    "<repeat id=\"r\" max=\"2\"><text/><text/></repeat>" +
                    "<repeat max=\"0\"><text/></repeat>" +
                    "<tabs><tab page=\"nope\"/></tabs>" +
                    "<pages><page id=\"a\"/><text/></pages>" +
                    "<option/>" +
                    "<panel id=\"dup\"/><panel id=\"dup\"/>" +
                    "<Missing/>" +
                    "</view>", out bag, new AuiAnalysisContext { FileStem = "Demo", ViewExists = n => false });
            var codes = bag.Items.Select(x => x.Code).ToList();
            CollectionAssert.Contains(codes, AuiCodes.RepeatTemplateCount);
            CollectionAssert.Contains(codes, AuiCodes.RepeatMaxOutOfRange);
            CollectionAssert.Contains(codes, AuiCodes.MissingRequiredAttribute);
            CollectionAssert.Contains(codes, AuiCodes.TabWithoutPage);
            CollectionAssert.Contains(codes, AuiCodes.ElementNotAllowedHere);
            CollectionAssert.Contains(codes, AuiCodes.DuplicateId);
            CollectionAssert.Contains(codes, AuiCodes.NestedViewNotFound);
        }

        [Test]
        public void TwoPageGroupsNeedFor()
        {
            AuiDiagnosticBag bag;
            var v = Analyze("<view name=\"Demo\"><tabs for=\"b\"><tab page=\"y\"/></tabs><pages id=\"a\"><page id=\"x\"/></pages><pages id=\"b\"><page id=\"y\"/></pages></view>", out bag);
            Assert.IsFalse(bag.HasErrors, Errors(bag));
            Assert.AreSame(v.PageGroups[1].Tabs, v.Doc.Root.Children[0]);
        }

        [Test]
        public void NamingHelpers()
        {
            Assert.AreEqual("PlayerName", AuiNaming.Pascal("playerName"));
            Assert.AreEqual("SolidFurniture", AuiNaming.Pascal("solid-furniture"));
            Assert.AreEqual("_playerName", AuiNaming.Field("PlayerName"));
            Assert.AreEqual("_1st", AuiNaming.Identifier("1st"));
            Assert.AreEqual("ab", AuiNaming.Identifier("a-b"));
        }
    }
}
