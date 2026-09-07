using System.Collections.Generic;
using System.Linq;
using Armagan.AUI.Core;
using NUnit.Framework;

namespace Armagan.AUI.Tests
{
    public class AuiComponentTests
    {
        private static AuiCompileResult Compile(string markup, string sheet = "", System.Func<string, AuiViewInfo> childInfo = null)
        {
            var opt = new AuiCompileOptions
            {
                Measurer = new AuiApproxTextMeasurer(),
                ReadFile = p => p.EndsWith("V.auis") ? sheet : null,
                ViewExists = n => childInfo != null && childInfo(n) != null,
                ChildInfo = childInfo,
            };
            return AuiCompiler.Compile(markup, "C:/tmp/V.aui", opt);
        }

        [Test]
        public void InlineComponentExpandsWithPropSubstitution()
        {
            AuiCompileResult r = Compile(
                "<view name=\"V\" width=\"300\" height=\"200\">" +
                "<component name=\"Card\"><prop name=\"title\"/><prop name=\"tone\" default=\"muted\"/><prop name=\"icon\" type=\"string\" default=\"zap\"/>" +
                "<panel class=\"card {tone}\"><text id=\"label\">{title}</text><button id=\"go\" icon=\"{icon}\" onclick=\"Go\"/></panel></component>" +
                "<Card id=\"a\" title=\"First\"/><Card id=\"b\" title=\"Second\" tone=\"title\" class=\"row\" style=\"gap: 3\"/><Card title=\"{bound}\"/>" +
                "</view>",
                ".card { padding: 4 } .muted { color: #888 } .title { font: 600 26 } .row { gap: 1 }");
            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Diagnostics.Items));
            Assert.AreEqual(0, r.Doc.Nodes.Count(n => n.Tag == "component" || n.Tag == "prop"), "declarations are lifted");
            Assert.AreEqual(3, r.Doc.Root.Children.Count);
            AuiNode a = r.Doc.FindById("a");
            Assert.AreEqual("panel", a.Tag);
            Assert.AreEqual("card muted", a.Get("class"));
            Assert.AreEqual("First", r.Doc.FindById("a_label").Text);
            Assert.AreEqual("zap", r.Doc.FindById("a_go").Get("icon"));
            AuiNode b = r.Doc.FindById("b");
            Assert.AreEqual("card title row", b.Get("class"));
            Assert.AreEqual("gap: 3", b.Get("style"));
            Assert.AreEqual("Second", r.Doc.FindById("b_label").Text);
            // a {binding} passed through a prop reaches the clone as a host binding
            AuiBinding bound = r.Info.FindBinding("bound", null);
            Assert.IsNotNull(bound);
            Assert.AreEqual(AuiBindingType.String, bound.Type);
            // indices are contiguous after expansion
            for (int i = 0; i < r.Doc.Nodes.Count; i++) Assert.AreEqual(i, r.Doc.Nodes[i].Index);
            Assert.IsTrue(r.Doc.Nodes.Any(n => n.ExpandedFrom != null));
            // the handler is the host's: one use per clone, all with the same signature
            Assert.AreEqual(3, r.Info.Handlers.Count(h => h.Name == "Go"));
            Assert.IsTrue(r.Info.Handlers.Where(h => h.Name == "Go").All(h => h.Signature == "void Go()"));
        }

        [Test]
        public void UnknownPropAndChildrenAreErrors()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"300\" height=\"200\"><component name=\"Chip\"><prop name=\"label\"/><text>{label}</text></component><Chip labl=\"x\"><text>no</text></Chip></view>");
            Assert.IsTrue(r.Diagnostics.Items.Any(x => x.Code == AuiCodes.UnknownComponentProp && x.Message.Contains("Did you mean 'label'")), string.Join("\n", r.Diagnostics.Items));
            Assert.IsTrue(r.Diagnostics.Items.Any(x => x.Code == AuiCodes.ElementNotAllowedHere));
        }

        [Test]
        public void ComponentsCanUseComponents()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"300\" height=\"200\"><component name=\"Inner\"><prop name=\"t\"/><text>{t}</text></component><component name=\"Outer\"><prop name=\"t\"/><panel><Inner t=\"{t}\"/><Inner t=\"fixed\"/></panel></component><Outer id=\"o\" t=\"hello\"/></view>");
            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Diagnostics.Items));
            AuiNode o = r.Doc.FindById("o");
            Assert.AreEqual(2, o.Children.Count);
            Assert.AreEqual("hello", o.Children[0].Text);
            Assert.AreEqual("fixed", o.Children[1].Text);
        }

        [Test]
        public void DeclaredPropsBecomeTypedBindingsWithDefaults()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"300\" height=\"200\"><prop name=\"title\" default=\"Hi\"/><prop name=\"count\" type=\"int\" default=\"3\"/><prop name=\"ratio\" type=\"float\" default=\"0.5\"/><prop name=\"on\" type=\"bool\" default=\"true\"/><event name=\"picked\" type=\"int\"/><event name=\"done\"/><text text=\"{title}\"/></view>");
            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Diagnostics.Items));
            Assert.AreEqual(AuiBindingType.Int, r.Info.FindBinding("count", null).Type);
            Assert.IsTrue(r.Info.FindBinding("count", null).IsProp);
            Assert.AreEqual(2, r.Info.Events.Count);
            Assert.AreEqual("int", r.Info.FindEvent("picked").Type);
            Assert.IsNull(r.Info.FindEvent("done").Type);
            StringAssert.Contains("private string _title = \"Hi\";", r.GeneratedSource);
            StringAssert.Contains("private int _count = 3;", r.GeneratedSource);
            StringAssert.Contains("private float _ratio = 0.5f;", r.GeneratedSource);
            StringAssert.Contains("private bool _on = true;", r.GeneratedSource);
            StringAssert.Contains("public int Count", r.GeneratedSource);
        }

        [Test]
        public void HostValidatesPropsAndTypesEventHandlers()
        {
            AuiViewInfo child = Compile("<view name=\"Child\" width=\"100\" height=\"50\"><prop name=\"title\"/><prop name=\"count\" type=\"int\"/><event name=\"picked\" type=\"int\"/><event name=\"done\"/></view>").Info;
            System.Func<string, AuiViewInfo> lookup = n => n == "Child" ? child : null;
            AuiCompileResult r = Compile("<view name=\"V\" width=\"300\" height=\"200\"><Child id=\"c\" title=\"x\" count=\"{n}\" on-picked=\"Picked\" on-done=\"Done\" on-other=\"Other\" bogus=\"1\"/></view>", "", lookup);
            Assert.IsTrue(r.Diagnostics.Items.Any(x => x.Code == AuiCodes.UnknownComponentProp && x.Message.Contains("'bogus'")), string.Join("\n", r.Diagnostics.Items));
            Assert.IsTrue(r.Diagnostics.Items.Any(x => x.Code == AuiCodes.UnknownComponentProp && x.Message.Contains("'other'")));
            AuiHandler picked = r.Info.FindHandler("Picked");
            Assert.AreEqual("int", picked.ParamType);
            Assert.AreEqual("void Picked(int value)", picked.Signature);
            Assert.IsNull(r.Info.FindHandler("Done").ParamType);
            Assert.AreEqual(AuiBindingType.Int, r.Info.FindBinding("n", null).Type, "a bound prop takes the child's declared type");
            StringAssert.Contains("eventName == \"picked\") { Picked(child.emitI); return; }", r.GeneratedSource);
            StringAssert.Contains("eventName == \"done\") { Done(); return; }", r.GeneratedSource);
        }
    }
}
