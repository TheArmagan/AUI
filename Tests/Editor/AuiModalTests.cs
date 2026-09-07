using System.Collections.Generic;
using System.Linq;
using Armagan.AUI.Core;
using NUnit.Framework;

namespace Armagan.AUI.Tests
{
    public class AuiModalTests
    {
        private static AuiCompileResult Compile(string markup, string sheet)
        {
            var opt = new AuiCompileOptions
            {
                Measurer = new AuiApproxTextMeasurer(),
                ReadFile = p => p.EndsWith("V.auis") ? sheet : null,
            };
            return AuiCompiler.Compile(markup, "C:/tmp/V.aui", opt);
        }

        [Test]
        public void ModalGetsScrimElementBindingAndCode()
        {
            AuiCompileResult r = Compile(
                "<view name=\"V\" width=\"300\" height=\"200\"><button id=\"open\" onclick=\"Open\">Go</button><modal id=\"confirm\" enabled=\"{asking}\" onclose=\"Closed\" onopen=\"Opened\"><text>Sure?</text><button id=\"yes\" onclick=\"Yes\">Yes</button></modal></view>",
                "modal { anchor: center; padding: 10 } modal::backdrop { background: rgba(0,0,0,0.5) }");
            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Diagnostics.Items));
            Assert.AreEqual(1, r.Info.Modals.Count);
            AuiModalInfo m = r.Info.Modals[0];
            Assert.AreEqual("confirm", m.Id);
            Assert.IsNotNull(m.Binding);
            Assert.AreEqual(AuiBindingType.Bool, m.Binding.Type);
            Assert.IsTrue(m.Dismissable);
            AuiNode modal = r.Doc.FindById("confirm");
            int scrim = r.Map.ModalScrimElem[modal.Index];
            Assert.AreEqual(r.Map.Elem(modal, 0) - 1, scrim, "the scrim is baked right before the body");
            Assert.AreEqual(AuiIndexMap.KIND_MODAL_SCRIM, r.Map.KindOf[scrim]);
            Assert.GreaterOrEqual(r.Map.SignalOf[scrim], 0, "the scrim has a signal");
            Assert.IsNotNull(r.Styles[modal.Index].Backdrop);
            Assert.AreEqual("rgba(0,0,0,0.5)", r.Styles[modal.Index].Backdrop.Get("background"));
            Assert.IsTrue(r.Layout[modal.Index].OutOfFlow);
            AuiLayoutTables t = AuiLayoutTables.Build(r);
            Assert.AreNotEqual(0, t.Flags[r.Map.Elem(modal, 0)] & AuiLayoutTables.LF_ABSOLUTE);
            StringAssert.Contains("public const int M_CONFIRM = 0;", r.GeneratedSource);
            StringAssert.Contains("public void OpenConfirm() { SetModal(0, true, true); }", r.GeneratedSource);
            StringAssert.Contains("SetModal(0, _asking, false);", r.GeneratedSource);
            StringAssert.Contains("if (!open) Closed();", r.GeneratedSource);
            StringAssert.Contains("if (open) Opened();", r.GeneratedSource);
            StringAssert.Contains("_asking = open;", r.GeneratedSource);
            // the button before the modal is still the first flow child: the modal reserves nothing
            Assert.AreEqual(0, r.Layout[r.Doc.FindById("open").Index].Local.Y, 0.01);
        }

        [Test]
        public void BackdropRulesStayOutOfTheRuntimeTableAndTheBodyIsInTheRootSubtree()
        {
            AuiCompileResult r = Compile(
                "<view name=\"V\" width=\"300\" height=\"200\"><panel class=\"card\"/><modal id=\"m\"><text>Sure?</text></modal></view>",
                "modal { background: #112233; padding: 10 } modal::backdrop { background: rgba(0,0,0,0.5) }");
            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Diagnostics.Items));
            AuiRuleTable t = r.Rules;
            int modalTag = System.Array.IndexOf(t.TagNames, "modal");
            Assert.GreaterOrEqual(modalTag, 0);
            // Exactly one rule targets the modal tag: the body rule, not the backdrop one.
            int modalRules = 0;
            for (int rule = 0; rule < t.RuleCount; rule++) if (t.RuleTag[rule] == modalTag) modalRules++;
            Assert.AreEqual(1, modalRules, "modal::backdrop must not become a modal rule");
            int body = r.Map.Base[r.Info.Modals[0].Node.Index];
            AuiRuleTable.Resolved res = t.Resolve(t.ElemTag[body], t.ElemClass0[body], t.ElemClass1[body], t.ElemNode[body], null);
            Assert.AreEqual(0x11 / 255f, res.Value[AuiRuleTable.PROP_BG][0], 0.001, "the body keeps its own background");
            // The scrim (no node) sits before the body; the root's subtree must still reach the body.
            Assert.AreEqual(t.ElemTag.Length, t.ElemSubtreeEnd[0], "the root subtree covers every element");
        }

        [Test]
        public void ModalMustBeDirectChildOfView()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"300\" height=\"200\"><panel><modal id=\"m\"/></panel></view>", "");
            Assert.IsTrue(r.Diagnostics.Items.Any(x => x.Code == AuiCodes.ElementNotAllowedHere && x.Message.Contains("direct child")));
        }

        [Test]
        public void BackdropSelectorRules()
        {
            AuiStyleSheet ok = AuiStyleParser.Parse("modal::backdrop { background: #000 } modal.dim::backdrop { opacity: 0.8 }", "V.auis", AuiStyleTier.Linked);
            Assert.IsFalse(ok.Diagnostics.HasErrors, string.Join("\n", ok.Diagnostics.Items));
            Assert.AreEqual("backdrop", ok.Rules[0].Selectors[0].Pseudo);
            Assert.AreEqual("dim", ok.Rules[1].Selectors[0].Class);
            Assert.IsFalse(ok.Rules[0].Selectors[0].Matches("modal", new string[0]), "a pseudo rule never styles the element itself");
            AuiStyleSheet bad1 = AuiStyleParser.Parse("button::backdrop { opacity: 0.5 }", "V.auis", AuiStyleTier.Linked);
            Assert.IsTrue(bad1.Diagnostics.Items.Any(x => x.Code == AuiCodes.BadSelector));
            AuiStyleSheet bad2 = AuiStyleParser.Parse("modal::shadow { opacity: 0.5 }", "V.auis", AuiStyleTier.Linked);
            Assert.IsTrue(bad2.Diagnostics.Items.Any(x => x.Code == AuiCodes.BadSelector));
            AuiStyleSheet bad3 = AuiStyleParser.Parse("modal::backdrop:hover { opacity: 0.5 }", "V.auis", AuiStyleTier.Linked);
            Assert.IsTrue(bad3.Diagnostics.Items.Any(x => x.Code == AuiCodes.BadSelector));
        }

        [Test]
        public void LiteralEnabledOpensAtStart()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"300\" height=\"200\"><modal id=\"m\" enabled=\"true\" dismissable=\"false\"/></view>", "");
            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Diagnostics.Items));
            Assert.IsTrue(r.Info.Modals[0].StartOpen);
            Assert.IsFalse(r.Info.Modals[0].Dismissable);
            Assert.IsNull(r.Info.Modals[0].Binding);
        }
    }
}
