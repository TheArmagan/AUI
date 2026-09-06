using System.Linq;
using Armagan.AUI.Core;
using NUnit.Framework;

namespace Armagan.AUI.Tests
{
    public class AuiParserTests
    {
        private static AuiMarkupDocument Parse(string s)
        {
            return AuiParser.Parse(s, "Test.aui");
        }

        private static string Codes(AuiMarkupDocument d)
        {
            return string.Join(",", d.Diagnostics.Items.Select(x => x.Code));
        }

        [Test]
        public void ParsesSimpleTree()
        {
            var d = Parse("<view name=\"Foo\" width=\"400\">\n  <panel class=\"row\">\n    <text key=\"a.b\"/>\n    <button onclick=\"Buy\">Buy</button>\n  </panel>\n</view>");
            Assert.IsFalse(d.Diagnostics.HasErrors, Codes(d));
            Assert.AreEqual("view", d.Root.Tag);
            Assert.AreEqual("Foo", d.ViewName);
            Assert.AreEqual(1, d.Root.Children.Count);
            var panel = d.Root.Children[0];
            Assert.AreEqual("panel", panel.Tag);
            Assert.AreEqual(2, panel.Children.Count);
            Assert.IsTrue(panel.Children[0].SelfClosing);
            Assert.AreEqual("a.b", panel.Children[0].Get("key"));
            Assert.AreEqual("Buy", panel.Children[1].Text);
            Assert.AreEqual(4, d.Nodes.Count);
            Assert.AreEqual(0, d.Root.Index);
            Assert.AreEqual(3, panel.Children[1].Index);
        }

        [Test]
        public void RecordsPositions()
        {
            var d = Parse("<view name=\"Foo\">\n  <text key=\"x\"/>\n</view>");
            var t = d.Root.Children[0];
            Assert.AreEqual(2, t.Line);
            Assert.AreEqual(3, t.Column);
            var key = t.FindAttribute("key");
            Assert.AreEqual(2, key.Line);
            Assert.AreEqual(9, key.Column);
            Assert.AreEqual(14, key.ValueColumn);
            Assert.AreEqual(2, t.EndLine);
            Assert.AreEqual(18, t.EndColumn);
            Assert.AreEqual(3, d.Root.EndLine);
        }

        [Test]
        public void DecodesEntities()
        {
            var d = Parse("<view name=\"F\"><text>a &lt;b&gt; &amp; &quot;c&quot; &#65;&#x42;</text></view>");
            Assert.IsFalse(d.Diagnostics.HasErrors, Codes(d));
            Assert.AreEqual("a <b> & \"c\" AB", d.Root.Children[0].Text);
        }

        [Test]
        public void CollapsesWhitespaceInText()
        {
            var d = Parse("<view name=\"F\"><text>\n   Hello\n\t world   \n</text></view>");
            Assert.AreEqual("Hello world", d.Root.Children[0].Text);
        }

        [Test]
        public void WhitespaceOnlyContentIsNoText()
        {
            var d = Parse("<view name=\"F\">\n  <panel>\n  </panel>\n</view>");
            Assert.IsNull(d.Root.Text);
            Assert.IsNull(d.Root.Children[0].Text);
        }

        [Test]
        public void LiftsStyleAndLink()
        {
            var d = Parse("<view name=\"F\">\n<link href=\"F.auis\"/>\n<style>\n.a { color: red; }\n</style>\n<text/>\n</view>");
            Assert.IsFalse(d.Diagnostics.HasErrors, Codes(d));
            Assert.AreEqual(1, d.Links.Count);
            Assert.AreEqual("F.auis", d.Links[0].Href);
            Assert.AreEqual(1, d.Styles.Count);
            StringAssert.Contains(".a { color: red; }", d.Styles[0].Text);
            Assert.AreEqual(3, d.Styles[0].Line);
            Assert.AreEqual(1, d.Root.Children.Count);
            Assert.AreEqual("text", d.Root.Children[0].Tag);
            Assert.AreEqual(2, d.Nodes.Count);
        }

        [Test]
        public void StyleContentIsRaw()
        {
            var d = Parse("<view name=\"F\"><style>a > b { x: 1 } /* <not a tag> */</style></view>");
            Assert.IsFalse(d.Diagnostics.HasErrors, Codes(d));
            Assert.AreEqual("a > b { x: 1 } /* <not a tag> */", d.Styles[0].Text);
        }

        [Test]
        public void SkipsCommentsAndProlog()
        {
            var d = Parse("<?xml version=\"1.0\"?>\n<!-- top -->\n<view name=\"F\"><!-- inner --><text/><!-- x --></view>\n<!-- tail -->");
            Assert.IsFalse(d.Diagnostics.HasErrors, Codes(d));
            Assert.AreEqual(1, d.Root.Children.Count);
        }

        [Test]
        public void ReportsUnclosedTagAtOpenPosition()
        {
            var d = Parse("<view name=\"F\">\n  <panel>\n    <text/>\n</view>");
            var e = d.Diagnostics.Items.First(x => x.Code == AuiCodes.UnclosedTag);
            Assert.AreEqual(2, e.Line);
            Assert.AreEqual(3, e.Column);
            // Recovery: the view still closed, panel still contains text.
            Assert.AreEqual("panel", d.Root.Children[0].Tag);
            Assert.AreEqual("text", d.Root.Children[0].Children[0].Tag);
            Assert.AreEqual(4, d.Root.EndLine);
        }

        [Test]
        public void ReportsMismatchedClose()
        {
            var d = Parse("<view name=\"F\"><panel></pnael></panel></view>");
            Assert.IsTrue(d.Diagnostics.Items.Any(x => x.Code == AuiCodes.MismatchedCloseTag));
            Assert.AreEqual(1, d.Root.Children.Count);
        }

        [Test]
        public void ReportsMultipleRoots()
        {
            var d = Parse("<view name=\"F\"/><view name=\"G\"/>");
            Assert.IsTrue(d.Diagnostics.Items.Any(x => x.Code == AuiCodes.MultipleRoots));
            Assert.AreEqual("F", d.ViewName);
        }

        [Test]
        public void ReportsEmptyDocument()
        {
            var d = Parse("   \n <!-- nothing -->  ");
            Assert.IsTrue(d.Diagnostics.Items.Any(x => x.Code == AuiCodes.EmptyDocument));
            Assert.IsNull(d.Root);
        }

        [Test]
        public void ReportsUnquotedAndDuplicateAttributes()
        {
            var d = Parse("<view name=F id=\"a\" id=\"b\"/>");
            Assert.IsTrue(d.Diagnostics.Items.Any(x => x.Code == AuiCodes.AttributeNotQuoted));
            Assert.IsTrue(d.Diagnostics.Items.Any(x => x.Code == AuiCodes.DuplicateAttribute));
            Assert.AreEqual("F", d.ViewName);
            Assert.AreEqual(3, d.Root.Attributes.Count);
        }

        [Test]
        public void RecoversFromUnterminatedAttributeAtNextTag()
        {
            var d = Parse("<view name=\"F>\n<text key=\"k\"/>\n</view>");
            Assert.IsTrue(d.Diagnostics.Items.Any(x => x.Code == AuiCodes.UnterminatedAttribute));
            Assert.IsNotNull(d.Root);
        }

        [Test]
        public void BareAmpersandIsWarningNotError()
        {
            var d = Parse("<view name=\"F\"><text>fish & chips</text></view>");
            Assert.IsFalse(d.Diagnostics.HasErrors, Codes(d));
            Assert.IsTrue(d.Diagnostics.Items.Any(x => x.Code == AuiCodes.BareAmpersand));
            Assert.AreEqual("fish & chips", d.Root.Children[0].Text);
        }

        [Test]
        public void SingleQuotedAttributes()
        {
            var d = Parse("<view name='F'><text text='{playerName}'/></view>");
            Assert.IsFalse(d.Diagnostics.HasErrors, Codes(d));
            Assert.AreEqual("{playerName}", d.Root.Children[0].Get("text"));
        }

        [Test]
        public void ComponentTagsAreDetected()
        {
            var d = Parse("<view name=\"F\"><LanguagePicker/><panel/></view>");
            Assert.IsTrue(d.Root.Children[0].IsComponentTag);
            Assert.IsFalse(d.Root.Children[1].IsComponentTag);
        }

        [Test]
        public void NodeAtFindsInnermost()
        {
            var d = Parse("<view name=\"F\">\n  <panel>\n    <text key=\"k\"/>\n  </panel>\n</view>");
            Assert.AreEqual("text", d.NodeAt(3, 10).Tag);
            Assert.AreEqual("panel", d.NodeAt(2, 5).Tag);
            Assert.AreEqual("view", d.NodeAt(1, 2).Tag);
        }

        [Test]
        public void ClassesSplit()
        {
            var d = Parse("<view name=\"F\"><panel class=\" a  b\tc \"/></view>");
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, d.Root.Children[0].Classes);
        }

        [Test]
        public void MixedContentKeepsTextAndChildren()
        {
            var d = Parse("<view name=\"F\"><button>Buy <icon name=\"cart\"/> now</button></view>");
            var b = d.Root.Children[0];
            Assert.AreEqual("Buy now", b.Text);
            Assert.AreEqual(1, b.Children.Count);
        }

        [Test]
        public void CrlfLineNumbers()
        {
            var d = Parse("<view name=\"F\">\r\n  <text/>\r\n</view>");
            Assert.AreEqual(2, d.Root.Children[0].Line);
            Assert.AreEqual(3, d.Root.Children[0].Column);
        }
    }
}
