using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Armagan.AUI.Core;
using NUnit.Framework;
using UnityEngine;

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

        // ---- runtime: a bare AuiView with two stacked modals, driven as plain C#

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static void Set(AuiView v, string field, object value)
        {
            FieldInfo f = typeof(AuiView).GetField(field, Private);
            Assert.IsNotNull(f, field);
            f.SetValue(v, value);
        }

        private static T Get<T>(AuiView v, string field)
        {
            return (T)typeof(AuiView).GetField(field, Private).GetValue(v);
        }

        /// <summary>Elements: 0 root, 1 and 2 modal 0's scrim and body, 3 and 4 modal 1's. Tweens on, the real client's branch.</summary>
        private static AuiView StackedModals(GameObject root)
        {
            const int n = 5;
            var v = root.AddComponent<AuiView>();
            v.elemObjects = new GameObject[n];
            v.elemRects = new RectTransform[n];
            v.elemGroups = new CanvasGroup[n];
            v.elemBasePos = new Vector2[n];
            v.elemSignal = new int[n];
            v.elemSubtreeEnd = new int[n];
            for (int e = 0; e < n; e++)
            {
                v.elemSignal[e] = -1;
                v.elemSubtreeEnd[e] = e + 1;
                if (e == 0) continue;
                var go = new GameObject("e" + e, typeof(RectTransform));
                go.transform.SetParent(root.transform, false);
                v.elemObjects[e] = go;
                v.elemRects[e] = (RectTransform)go.transform;
                v.elemGroups[e] = go.AddComponent<CanvasGroup>();
            }
            v.modalElem = new[] { 2, 4 };
            v.modalScrimElem = new[] { 1, 3 };
            v.modalKind = new[] { AuiView.TRANS_FADE_SCALE, AuiView.TRANS_FADE_SCALE };
            v.modalDismissable = new[] { true, true };
            v.modalStartOpen = new[] { false, false };
            Set(v, "_started", true);
            Set(v, "_tweensCached", true);
            Set(v, "_tweensOk", true);
            Set(v, "_openSelect", -1);
            Set(v, "_hover", new bool[0]);
            Set(v, "_press", new bool[0]);
            typeof(AuiView).GetMethod("_AuiModalsInit", Private).Invoke(v, null);
            return v;
        }

        [Test]
        public void ClosingTheUpperModalStopsTakingClicksBeforeItsSnap()
        {
            var root = new GameObject("modals", typeof(RectTransform));
            try
            {
                AuiView v = StackedModals(root);
                v.SetModal(0, true, false);
                v.SetModal(1, true, false);
                Assert.IsTrue(v.elemGroups[4].blocksRaycasts, "an open body takes clicks");

                v.SetModal(1, false, false);

                // No snap has run: this is the 0.18 s of the exit fade, and all of the time after it
                // if the snap never lands. An alpha 0 body with raycasts on still eats every click.
                Assert.IsFalse(v.elemGroups[4].blocksRaycasts, "the closing body must not cover the modal below it");
                Assert.IsFalse(v.elemGroups[4].interactable, "nor may its invisible buttons fire");
                Assert.IsFalse(v.elemGroups[3].blocksRaycasts, "nor its backdrop");
                Assert.IsTrue(v.elemGroups[2].blocksRaycasts, "the modal below keeps its clicks");
                Assert.AreEqual(0, v.TopModal());
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void AnEarlySnapLeavesTheClosedModalAloneAndADueOneDeactivatesIt()
        {
            var root = new GameObject("modals", typeof(RectTransform));
            try
            {
                AuiView v = StackedModals(root);
                v.SetModal(0, true, false);
                v.SetModal(1, true, false);
                v.SetModal(1, false, false);

                float[] due = Get<float[]>(v, "_modalSnapAt");
                due[0] = Time.time - 1f;
                due[1] = Time.time + 10f;
                v._ModalSnap();
                Assert.IsTrue(v.elemObjects[4].activeSelf, "a snap ahead of the due time must not cut the exit short");

                due[1] = Time.time - 1f;
                v._ModalSnap();
                Assert.IsFalse(v.elemObjects[4].activeSelf, "closed body");
                Assert.IsFalse(v.elemObjects[3].activeSelf, "closed backdrop");
                Assert.IsTrue(v.elemObjects[2].activeSelf, "the open modal stays");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
