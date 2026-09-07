using System.Linq;
using Armagan.AUI.Core;
using NUnit.Framework;

namespace Armagan.AUI.Tests
{
    public class AuiUtilityTests
    {
        private static AuiCompileResult Compile(string markup, string sheet = "")
        {
            var opt = new AuiCompileOptions
            {
                Measurer = new AuiApproxTextMeasurer(),
                ReadFile = p => p.EndsWith("V.auis") ? sheet : null,
            };
            return AuiCompiler.Compile(markup, "C:/tmp/V.aui", opt);
        }

        [Test]
        public void UtilitySheetParsesAndCoversTheDocumentedSet()
        {
            AuiStyleSheet s = AuiUtilityClasses.Sheet();
            Assert.IsFalse(s.Diagnostics.HasErrors, string.Join("\n", s.Diagnostics.Items));
            var names = s.ClassNames().ToList();
            foreach (string must in new[] { "*flex-row", "*items-center", "*justify-between", "*gap-8", "*p-4", "*px-12", "*mt-2", "*w-full", "*h-auto", "*hidden", "*absolute", "*inset-0", "*rounded-8", "*opacity-50", "*text-center", "*text-lg", "*font-bold", "*bg-card", "*fg-muted" })
                Assert.IsTrue(names.Contains(must), must);
            Assert.AreEqual(AuiStyleTier.Utility, s.Tier);
            Assert.Greater(names.Count, 200);
        }

        [Test]
        public void StarClassesApplyAndAreBakedOnlyWhenReferenced()
        {
            // .card sets no padding: a view sheet would beat the utility tier (see ViewSheetOverridesUtilities).
            AuiCompileResult r = Compile("<view name=\"V\" width=\"300\" height=\"200\" utilities=\"gap-16\"><panel id=\"p\" class=\"card *p-8 *flex-row\"><text id=\"t\" class=\"*fg-muted\">x</text></panel></view>", ".card { color: #123456 }");
            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Diagnostics.Items));
            AuiResolvedStyle p = r.Styles[r.Doc.FindById("p").Index];
            Assert.AreEqual(8, p.Padding.Top, 0.001);
            Assert.IsTrue(p.IsRow);
            Assert.AreEqual(new[] { "*fg-muted", "*flex-row", "*gap-16", "*p-8" }, r.Rules.StarNames);
            Assert.AreEqual(new[] { "card" }, r.Rules.ClassNames);
            Assert.IsFalse(r.Rules.StarNames.Contains("*p-4"), "unreferenced utilities are not in the table");
            int rules = r.Rules.RuleCount;
            Assert.Less(rules, 40, "only referenced star rules plus theme rules: " + rules);
            Assert.IsFalse(r.GeneratedSource.Contains("C_P_8"), "no constant for a star class");
            StringAssert.Contains("C_CARD", r.GeneratedSource);
            // the runtime reference resolver honours the star mask
            int pe = r.Map.Elem(r.Doc.FindById("p"), 0);
            var stars = new int[AuiRuleTable.STAR_WORDS];
            for (int w = 0; w < AuiRuleTable.STAR_WORDS; w++) stars[w] = r.Rules.ElemStar[pe * AuiRuleTable.STAR_WORDS + w];
            AuiRuleTable.Resolved res = r.Rules.Resolve(r.Rules.ElemTag[pe], r.Rules.ElemClass0[pe], r.Rules.ElemClass1[pe], stars, r.Rules.ElemNode[pe], null);
            Assert.AreEqual(8, res.Value[AuiRuleTable.PROP_PADDING][0], 0.001);
            Assert.AreEqual(1, res.Value[AuiRuleTable.PROP_FLEX_DIRECTION][0], 0.001);
            AuiRuleTable.Resolved without = r.Rules.Resolve(r.Rules.ElemTag[pe], r.Rules.ElemClass0[pe], r.Rules.ElemClass1[pe], new int[AuiRuleTable.STAR_WORDS], r.Rules.ElemNode[pe], null);
            Assert.IsNull(without.Value[AuiRuleTable.PROP_PADDING], "without the star mask nothing sets the padding");
            Assert.IsNotNull(without.Value[AuiRuleTable.PROP_COLOR], "the regular class still applies");
        }

        [Test]
        public void ViewSheetOverridesUtilities()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"300\" height=\"200\"><panel id=\"p\" class=\"*p-8 tight\"/></view>", ".tight { padding: 1 }");
            Assert.AreEqual(1, r.Styles[r.Doc.FindById("p").Index].Padding.Top, 0.001);
        }

        [Test]
        public void StyleValueBindingsAreTypedAndGenerated()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"300\" height=\"200\"><panel id=\"p\" style=\"width: {w}; opacity: {o}; color: {c}; offset: {dx} {dy}; padding: {pv} {ph}; justify: {j}\"/></view>");
            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Diagnostics.Items));
            Assert.AreEqual(AuiBindingType.String, r.Info.FindBinding("w", null).Type);
            Assert.AreEqual(AuiBindingType.Float, r.Info.FindBinding("o", null).Type);
            Assert.AreEqual(AuiBindingType.Color, r.Info.FindBinding("c", null).Type);
            Assert.AreEqual(AuiBindingType.Float, r.Info.FindBinding("dy", null).Type);
            Assert.AreEqual(1, r.Info.FindBinding("dy", null).Uses[0].StyleIndex);
            Assert.AreEqual(AuiBindingType.String, r.Info.FindBinding("ph", null).Type);
            Assert.AreEqual(AuiBindingType.String, r.Info.FindBinding("j", null).Type);
            AuiResolvedStyle p = r.Styles[r.Doc.FindById("p").Index];
            Assert.IsFalse(p.Width.IsSet, "the bake uses the default for a bound property");
            Assert.AreEqual("{w}", p.Bound["width"]);
            StringAssert.Contains("SetStyleBindingS(1, " + AuiRuleTable.PROP_WIDTH + ", 0, _w);", r.GeneratedSource);
            StringAssert.Contains("SetStyleBindingF(1, " + AuiRuleTable.PROP_OPACITY + ", 0, _o);", r.GeneratedSource);
            StringAssert.Contains("SetStyleBindingC(1, " + AuiRuleTable.PROP_COLOR + ", 0, _c);", r.GeneratedSource);
            StringAssert.Contains("SetStyleBindingF(1, " + AuiRuleTable.PROP_OFFSET + ", 1, _dy);", r.GeneratedSource);
            StringAssert.Contains("SetStyleBindingS(1, " + AuiRuleTable.PROP_PADDING + ", 1, _ph);", r.GeneratedSource);
            StringAssert.Contains("public Color C", r.GeneratedSource);
            StringAssert.Contains("Escape(EnsureStartedS(value))", r.GeneratedSource);
            StringAssert.Contains("EnsureStartedC(value)", r.GeneratedSource);
        }

        [Test]
        public void MixedAndForbiddenBoundValuesAreErrors()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"300\" height=\"200\"><panel class=\"x\" style=\"width: {w}mm; transition: {t}\"/></view>", ".x { width: {w} }");
            var codes = r.Diagnostics.Items.Where(x => x.Code == AuiCodes.BadBoundValue).ToList();
            Assert.AreEqual(3, codes.Count, string.Join("\n", r.Diagnostics.Items));
        }

        [Test]
        public void IconAttributeBindings()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"300\" height=\"200\"><button id=\"b\" icon=\"{ic}\"/><icon id=\"i\" name=\"{ic}\"/></view>");
            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Diagnostics.Items));
            Assert.AreEqual(AuiBindingType.String, r.Info.FindBinding("ic", null).Type);
            StringAssert.Contains("SetIcon(1, _ic);", r.GeneratedSource);
            StringAssert.Contains("SetIcon(2, _ic);", r.GeneratedSource);
        }
    }
}
