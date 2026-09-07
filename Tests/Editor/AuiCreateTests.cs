using System.Linq;
using Armagan.AUI.Core;
using NUnit.Framework;

namespace Armagan.AUI.Tests
{
    public class AuiCreateTests
    {
        private static AuiCompileResult Compile(string markup, string sheet = "")
        {
            var opt = new AuiCompileOptions
            {
                Measurer = new AuiApproxTextMeasurer(),
                ReadFile = p => p.EndsWith("V.auis") ? sheet : null,
                ViewExists = n => n == "Child",
                ChildInfo = n => n == "Child" ? AuiCompiler.Compile("<view name=\"Child\" width=\"50\" height=\"20\"><prop name=\"label\"/><text text=\"{label}\"/></view>", "C:/tmp/Child.aui", new AuiCompileOptions { Measurer = new AuiApproxTextMeasurer() }).Info : null,
            };
            return AuiCompiler.Compile(markup, "C:/tmp/V.aui", opt);
        }

        [Test]
        public void TemplatesAreIndexedLastAndTakeNoSpace()
        {
            AuiCompileResult r = Compile(
                "<view name=\"V\" width=\"300\" height=\"200\"><template id=\"row\" max=\"3\"><button onclick=\"Pick\" class=\"chip\"><text text=\"{item.label}\"/></button></template><panel id=\"list\"/><text id=\"after\">x</text></view>",
                ".chip { padding: 2 }");
            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Diagnostics.Items));
            AuiRepeatInfo t = r.Info.Repeats.Single(x => x.Id == "row");
            Assert.IsTrue(t.IsTemplate);
            AuiNode tpl = r.Doc.FindById("row");
            int list = r.Map.Elem(r.Doc.FindById("list"), 0), after = r.Map.Elem(r.Doc.FindById("after"), 0);
            int tplElem = r.Map.Elem(tpl, 0);
            Assert.Greater(tplElem, after, "the template and its instances come after every other element");
            Assert.Greater(r.Map.Elem(t.Template, 0), tplElem);
            Assert.IsTrue(r.Layout[tpl.Index].Hidden);
            Assert.AreEqual(0, r.Layout[r.Doc.FindById("list").Index].Local.Y, 0.01, "the template reserves nothing");
            // the instances were measured even though the template is hidden
            AuiLayoutTables lt = AuiLayoutTables.Build(r);
            int inst0 = r.Map.Elem(t.Template, 0);
            Assert.Greater(lt.IntrinsicH[inst0], 0);
            Assert.AreNotEqual(0, lt.Flags[inst0] & AuiLayoutTables.LF_HIDDEN_START);
            Assert.AreNotEqual(0, lt.Flags[tplElem] & AuiLayoutTables.LF_HIDDEN_START);
            StringAssert.Contains("public const int T_ROW = 0;", r.GeneratedSource);
            StringAssert.Contains("public int CreateRow(int parent) { return CreateElement(0, parent, -1); }", r.GeneratedSource);
            StringAssert.Contains("public void SetRowLabel(int elem, string value)", r.GeneratedSource);
            StringAssert.Contains("int row = InstanceRow(0, elem);", r.GeneratedSource);
            Assert.IsFalse(r.GeneratedSource.Contains("RowCount"), "no Count property for a template");
            Assert.AreEqual("void Pick(int row)", r.Info.FindHandler("Pick").Signature);
        }

        [Test]
        public void PoolsAndSlotsBecomeTemplates()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"300\" height=\"200\" pool=\"text:4 panel:2\"><panel id=\"host\"/><slot id=\"cards\" views=\"Child\" max=\"2\"/></view>");
            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Diagnostics.Items));
            Assert.AreEqual(3, r.Info.Repeats.Count);
            AuiRepeatInfo text = r.Info.Repeats.Single(x => x.PoolTag == "text");
            Assert.AreEqual(4, text.Max);
            Assert.AreEqual("pool-text", text.Id);
            Assert.AreEqual("text", text.Template.Tag);
            AuiRepeatInfo slot = r.Info.Repeats.Single(x => x.SlotId == "cards");
            Assert.AreEqual("Child", slot.SlotView);
            Assert.AreEqual("Child", slot.Template.Tag);
            Assert.AreEqual(1, r.Info.NestedViews.Count);
            Assert.IsFalse(r.GeneratedSource.Contains("childChild;"), "no single field for a slot's parked copies");
            StringAssert.Contains("public AuiView CreateCardsChild(int parent) { return CreateView(", r.GeneratedSource);
            StringAssert.Contains("public int CreatePoolText(int parent)", r.GeneratedSource);
            Assert.IsFalse(r.Diagnostics.Items.Any(x => x.Code == AuiCodes.MissingRequiredAttribute), "the pooled text does not warn about missing content");
        }

        [Test]
        public void PoolRejectsUnpoolableTags()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"300\" height=\"200\" pool=\"button:2 text:99\"/>");
            Assert.AreEqual(2, r.Diagnostics.Items.Count(x => x.Code == AuiCodes.BadAttributeValue), string.Join("\n", r.Diagnostics.Items));
        }

        [Test]
        public void TemplateMustBeDirectChildAndCannotHoldModalsOrRepeats()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"300\" height=\"200\"><panel><template id=\"t\" max=\"2\"><panel><repeat id=\"r\" max=\"2\"><text>x</text></repeat></panel></template></panel></view>");
            Assert.IsTrue(r.Diagnostics.Items.Any(x => x.Code == AuiCodes.ElementNotAllowedHere && x.Message.Contains("direct child")));
            Assert.IsTrue(r.Diagnostics.Items.Any(x => x.Code == AuiCodes.ElementNotAllowedHere && x.Message.Contains("<repeat> cannot be inside")));
        }
    }
}
