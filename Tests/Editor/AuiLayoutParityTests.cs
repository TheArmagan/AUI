using System.Collections.Generic;
using Armagan.AUI.Core;
using NUnit.Framework;
using UnityEngine;

namespace Armagan.AUI.Tests
{
    /// <summary>
    /// The Udon solver (Runtime/AuiView.Layout.cs) must produce the rectangles the Core solver
    /// produced for the same tree. Every fixture is compiled by Core, its layout tables are
    /// built by AuiLayoutTables, an AuiView proxy is filled with them and solved over the
    /// tables only, and every element's local rect is compared.
    /// </summary>
    public class AuiLayoutParityTests
    {
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

        private static readonly string[] Fixtures =
        {
            "<view name=\"V\" width=\"200\" height=\"100\"><text id=\"a\">Hello</text><text id=\"b\">Hi</text></view>|view { padding: 10; gap: 5; font-size: 10 } text { }",
            "<view name=\"V\" width=\"300\" height=\"50\"><panel id=\"r\" class=\"row\"><icon id=\"i\" name=\"x\" size=\"20\"/><spacer id=\"s\"/><icon id=\"j\" name=\"x\" size=\"20\"/></panel></view>|view { padding: 0 } .row { flex-direction: row; gap: 10; align: center } spacer { grow: 1 }",
            "<view name=\"V\" width=\"100\" height=\"40\"><panel id=\"c\" style=\"justify: center\"><icon id=\"a\" name=\"x\" size=\"10\"/><icon id=\"b\" name=\"x\" size=\"10\"/></panel><panel id=\"e\" style=\"justify: end\"><icon id=\"d\" name=\"x\" size=\"10\"/></panel><panel id=\"sb\" style=\"flex-direction: row; justify: space-between\"><icon id=\"p\" name=\"x\" size=\"10\"/><icon id=\"q\" name=\"x\" size=\"10\"/></panel></view>|view { padding: 0; gap: 0 } panel { height: 30%; gap: 0; padding: 0 }",
            "<view name=\"V\" width=\"200\" height=\"100\"><panel id=\"f\" style=\"anchor: fill\"/><icon id=\"br\" name=\"x\" size=\"10\" style=\"anchor: bottom\"/><panel id=\"half\" style=\"width: 50%; height: 20\"/></view>|view { padding: 10; gap: 0 } panel { padding: 0 }",
            "<view name=\"V\" width=\"200\" height=\"100\"><text id=\"t\" text=\"{nm}\"/><text id=\"k\" key=\"k\" args=\"{nm}\"/></view>|view { padding: 0 }",
            "<view name=\"V\" width=\"200\" height=\"100\"><text id=\"t\" wrap=\"true\" style=\"max-width: 60; font-size: 10\">abcdefghijklmnopqrst</text></view>|view { padding: 0 }",
            "<view name=\"V\" width=\"200\" height=\"200\"><repeat id=\"rows\" max=\"3\"><panel id=\"item\" style=\"height: 30\"><text text=\"{item.label}\"/></panel><empty><text>none</text></empty></repeat></view>|view { padding: 0 } repeat { gap: 5; padding: 0 } panel { padding: 0 }",
            "<view name=\"V\" width=\"100\" height=\"200\"><repeat id=\"g\" max=\"5\" class=\"grid\"><icon name=\"x\" size=\"40\"/></repeat></view>|view { padding: 0 } .grid { flex-direction: row; wrap: true; gap: 10; padding: 0 }",
            "<view name=\"V\" width=\"200\" height=\"200\"><pages id=\"pg\"><page id=\"a\"><text>A</text></page><page id=\"b\"/></pages><select id=\"s\" bind=\"{q}\"><option id=\"o1\">Low</option><option id=\"o2\">High</option></select></view>|view { padding: 0; gap: 0 } pages { grow: 1 } page { padding: 0 } select { height: 30 } option { min-height: 20; padding: 0 }",
            "<view name=\"V\" width=\"200\" height=\"100\"><icon id=\"a\" name=\"x\" size=\"10\" style=\"display: none\"/><icon id=\"h\" name=\"x\" size=\"10\" hidden=\"\"/><icon id=\"b\" name=\"x\" size=\"10\"/></view>|view { padding: 0; gap: 5 }",
            "<view name=\"V\" width=\"200\" height=\"100\"><icon id=\"tl\" name=\"x\" size=\"10\" style=\"position: absolute; top: -4; left: -6\"/><icon id=\"br\" name=\"x\" size=\"10\" style=\"position: absolute; bottom: 2; right: 3\"/><panel id=\"st\" style=\"position: absolute; top: 5; bottom: 5; left: 20; right: 20\"/><icon id=\"flow\" name=\"x\" size=\"10\"/></view>|view { padding: 10; gap: 0 } panel { padding: 0 }",
            "<view name=\"V\" width=\"240\" height=\"120\"><panel class=\"row\"><button id=\"b1\" icon=\"zap\">Go</button><button id=\"b2\" icon=\"zap\"/><toggle id=\"t1\" bind=\"{on}\">Solid</toggle><spacer/><text id=\"long\" text=\"{v}\"/></panel><panel class=\"wrap\"><icon name=\"x\" size=\"30\"/><icon name=\"x\" size=\"30\"/><icon name=\"x\" size=\"30\"/><icon name=\"x\" size=\"30\"/></panel></view>|view { padding: 8; gap: 6 } .row { flex-direction: row; gap: 8; align: center } .wrap { flex-direction: row; wrap: true; gap: 4 } button { padding: 4 8; gap: 6 } toggle { padding: 4 8 }",
            "<view name=\"V\" width=\"200\" height=\"120\"><panel class=\"row\"><text id=\"a\" style=\"margin: 2 4\">abc</text><panel id=\"p\" style=\"grow: 2; margin: 1\"/><panel id=\"q\" style=\"grow: 1; max-width: 20\"/></panel></view>|view { padding: 0 } .row { flex-direction: row; gap: 3; align: end; padding: 5 }",
            // size= against a sheet that sizes icons, which is what the default theme does (icon { width: 18 }).
            // The runtime tables used to carry only the sheet's 18, so a 40 mm icon was solved back to 18 in the world.
            "<view name=\"V\" width=\"200\" height=\"120\"><panel class=\"row\"><icon id=\"big\" name=\"x\" size=\"40\"/><icon id=\"dflt\" name=\"x\"/><icon id=\"small\" name=\"x\" size=\"9\"/></panel></view>|view { padding: 0 } .row { flex-direction: row; gap: 4; align: center; padding: 0 } icon { width: 18; height: 18 }",
        };

        private static AuiCompileResult Compile(string fixture, int[] counts)
        {
            string[] parts = fixture.Split('|');
            var opt = new AuiCompileOptions
            {
                Measurer = new StubMeasurer(),
                BoundTextSample = "WWWWWWWWWW",
                ReadFile = p => p.EndsWith("V.auis") ? parts[1] : null,
            };
            // Compile does not expose RepeatCount, so run the pipeline by hand where a count is wanted.
            AuiCompileResult r = AuiCompiler.Compile(parts[0], "C:/tmp/V.aui", opt);
            if (counts != null)
            {
                var ctx = new AuiLayoutContext { Measurer = new StubMeasurer(), BoundTextSample = "WWWWWWWWWW", RepeatCount = ri => counts[r.Info.Repeats.IndexOf(ri)] };
                r.Layout = new AuiLayoutSolver(r.Doc, r.Styles, r.Info, ctx, new AuiDiagnosticBag("V.aui")).Solve();
            }
            return r;
        }

        private static AuiView Proxy(AuiCompileResult r, AuiLayoutTables t)
        {
            var go = new GameObject("parity", typeof(RectTransform));
            go.hideFlags = HideFlags.HideAndDontSave;
            var v = go.AddComponent<AuiView>();
            int n = t.N;
            v.elemObjects = new GameObject[n];
            v.elemText = new TMPro.TextMeshProUGUI[n];
            v.elemRects = new RectTransform[n];
            v.elemBasePos = new Vector2[n];
            v.elemSignal = new int[n];
            v.layoutEnabled = true;
            v.layLangCount = t.LangCount; v.layRootW = t.RootW; v.layRootH = t.RootH;
            v.layParent = t.Parent; v.layFirstChild = t.FirstChild; v.layNextSibling = t.NextSibling; v.layKind = t.Kind; v.layFlags = t.Flags;
            v.layJustify = t.Justify; v.layAlign = t.Align; v.layAnchor = t.Anchor; v.layRepeat = t.Repeat; v.laySizeKind = t.SizeKind; v.layEdgeKind = t.EdgeKind;
            v.layGrow = t.Grow; v.layGap = t.Gap; v.layPad = t.Pad; v.layMargin = t.Margin; v.laySizeVal = t.SizeVal; v.layEdgeVal = t.EdgeVal;
            v.layIntrinsicW = t.IntrinsicW; v.layIntrinsicH = t.IntrinsicH; v.layLangW = t.LangW; v.layLangH = t.LangH; v.layContentW = t.ContentW; v.layContentH = t.ContentH;
            var first = new List<int>(); var stride = new List<int>(); var max = new List<int>();
            foreach (AuiRepeatInfo rep in r.Info.Repeats)
            {
                first.Add(rep.Template != null ? r.Map.Elem(rep.Template, 0) : 0);
                stride.Add(rep.Template != null ? r.Map.Stride[rep.Template.Index] : 1);
                max.Add(rep.Max);
            }
            v.repeatFirst = first.ToArray(); v.repeatStride = stride.ToArray(); v.repeatMax = max.ToArray();
            v.selectElem = new int[0];
            return v;
        }

        private static void AssertParity(string fixture, int[] counts)
        {
            AuiCompileResult r = Compile(fixture, counts);
            Assert.IsFalse(r.HasErrors, fixture + "\n" + string.Join("\n", r.Diagnostics.Items));
            AuiLayoutTables t = AuiLayoutTables.Build(r);
            AuiView v = Proxy(r, t);
            try
            {
                v._AuiSolveTablesOnly(counts);
                int checkedCount = 0;
                for (int e = 0; e < t.N; e++)
                {
                    AuiNode node = r.Map.NodeOf[e];
                    if (node == null || t.Kind[e] == AuiLayoutTables.LK_IGNORE) continue;
                    if (node.Parent == null) continue;
                    int row = r.Map.RowOf[e];
                    AuiLayoutNode ln = r.Layout[node.Index];
                    if (ln.Hidden) { Assert.AreEqual(0, v.LayoutW(e), 0.01, Where(fixture, node, e, "hidden W")); continue; }
                    // Rows beyond the count are inactive at runtime and not placed.
                    if (counts != null && row >= 0 && InactiveRow(r, node, row, counts)) { Assert.IsFalse(v.LayoutPlaced(e), Where(fixture, node, e, "placed but inactive")); continue; }
                    AuiRect expect = ln.Local;
                    if (row > 0 && node.Parent.Tag == "repeat")
                    {
                        AuiLayoutNode rl = r.Layout[node.Parent.Index];
                        AuiRect r0 = rl.Instances[0], ri = rl.Instances[row];
                        expect = new AuiRect(expect.X + ri.X - r0.X, expect.Y + ri.Y - r0.Y, ri.W, ri.H);
                    }
                    Assert.IsTrue(v.LayoutPlaced(e), Where(fixture, node, e, "not placed"));
                    Assert.AreEqual(expect.X, v.LayoutX(e), 0.01, Where(fixture, node, e, "X"));
                    Assert.AreEqual(expect.Y, v.LayoutY(e), 0.01, Where(fixture, node, e, "Y"));
                    Assert.AreEqual(expect.W, v.LayoutW(e), 0.01, Where(fixture, node, e, "W"));
                    Assert.AreEqual(expect.H, v.LayoutH(e), 0.01, Where(fixture, node, e, "H"));
                    checkedCount++;
                }
                Assert.Greater(checkedCount, 0);
            }
            finally
            {
                Object.DestroyImmediate(v.gameObject);
            }
        }

        private static bool InactiveRow(AuiCompileResult r, AuiNode node, int row, int[] counts)
        {
            AuiRepeatInfo rep = r.Info.RepeatContaining(node);
            if (rep == null) return false;
            int count = counts[r.Info.Repeats.IndexOf(rep)];
            if (count == 0 && row == 0) return false;   // an empty list still lays the template out at row 0 (both solvers)
            return row >= count;
        }

        private static string Where(string fixture, AuiNode node, int e, string what)
        {
            return what + " of <" + node.Tag + (node.Id != null ? " id=" + node.Id : "") + "> elem " + e + "\n" + fixture.Split('|')[0];
        }

        [Test]
        public void UdonSolverMatchesCoreOnEveryFixture()
        {
            foreach (string f in Fixtures) AssertParity(f, null);
        }

        [Test]
        public void UdonSolverMatchesCoreAtRuntimeRepeatCounts()
        {
            AssertParity(Fixtures[6], new[] { 2 });
            AssertParity(Fixtures[6], new[] { 0 });
            AssertParity(Fixtures[7], new[] { 3 });
        }

        /// <summary>Parity alone would pass with both solvers wrong in the same way, which is how this went unseen: assert the numbers.</summary>
        [Test]
        public void IconSizeBeatsASheetThatSizesIcons()
        {
            AuiCompileResult r = Compile(Fixtures[Fixtures.Length - 1], null);
            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Diagnostics.Items));
            var byId = new Dictionary<string, AuiNode>();
            foreach (AuiNode n in r.Doc.Nodes) if (n.Id != null) byId[n.Id] = n;
            Assert.AreEqual(40f, r.Layout[byId["big"].Index].Local.W, 0.01, "size=40 against icon { width: 18 }");
            Assert.AreEqual(40f, r.Layout[byId["big"].Index].Local.H, 0.01);
            Assert.AreEqual(18f, r.Layout[byId["dflt"].Index].Local.W, 0.01, "no size=: the sheet decides");
            Assert.AreEqual(9f, r.Layout[byId["small"].Index].Local.W, 0.01, "size= can also shrink");

            AuiLayoutTables t = AuiLayoutTables.Build(r);
            int e = r.Map.Elem(byId["big"], 0);
            Assert.AreEqual(40f, t.SizeVal[6 * e + AuiLayoutTables.SZ_W], 0.01, "the runtime tables carry it too");
        }

        [Test]
        public void IconSizeDoesNotOverrideAnInlineWidth()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"200\" height=\"100\"><icon id=\"i\" name=\"x\" size=\"40\" style=\"width: 12; max-width: 60\"/></view>|view { padding: 0 } icon { width: 18; height: 18 }", null);
            AuiNode icon = null;
            foreach (AuiNode n in r.Doc.Nodes) if (n.Id == "i") icon = n;
            Assert.AreEqual(12f, r.Layout[icon.Index].Local.W, 0.01, "an inline width is more specific than size=");
            Assert.AreEqual(40f, r.Layout[icon.Index].Local.H, 0.01, "max-width must not be mistaken for width, nor block height");
        }

        [Test]
        public void TablesHaveConsistentTree()
        {
            foreach (string f in Fixtures)
            {
                AuiCompileResult r = Compile(f, null);
                AuiLayoutTables t = AuiLayoutTables.Build(r);
                for (int e = 0; e < t.N; e++)
                {
                    Assert.Less(t.Parent[e], e, "parent must precede child (elem " + e + ")");
                    for (int c = t.FirstChild[e]; c >= 0; c = t.NextSibling[c]) Assert.AreEqual(e, t.Parent[c]);
                }
                Assert.AreEqual(-1, t.Parent[0]);
            }
        }
    }
}
