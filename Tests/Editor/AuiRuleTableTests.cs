using System.Collections.Generic;
using System.IO;
using System.Linq;
using Armagan.AUI.Core;
using NUnit.Framework;

namespace Armagan.AUI.Tests
{
    /// <summary>
    /// The runtime cascade (AuiRuleTable, mirrored in Udon) must agree with the compile-time
    /// cascade (AuiStyleResolver) for every element of every fixture: same normal-state values
    /// after the same encoding, same set of state rules.
    /// </summary>
    public class AuiRuleTableTests
    {
        private static string Theme()
        {
            string path = Path.Combine(TestPaths.PackageRoot, "Theme/Default.auis");
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }

        private static AuiCompileResult Compile(string markup, string sheet)
        {
            var opt = new AuiCompileOptions
            {
                Theme = AuiStyleParser.Parse(Theme(), "Default.auis", AuiStyleTier.Theme),
                Measurer = new AuiApproxTextMeasurer(),
                ReadFile = p => p.EndsWith("V.auis") ? sheet : null,
            };
            return AuiCompiler.Compile(markup, "C:/tmp/V.aui", opt);
        }

        private static readonly string[] Fixtures =
        {
            "<view name=\"V\" width=\"300\" height=\"200\"><panel class=\"row card\"><text class=\"title\">Hi</text><button id=\"b\" class=\"ghost\" icon=\"x\" style=\"scale: 1.2; color: #FF0000\"/><toggle bind=\"{on}\">T</toggle></panel><select bind=\"{q}\"><option>A</option><option>B</option></select></view>|.card { color: var(--muted); } .x { background: sprite(Rounded4) #123456; transition: all 0.2s in-quad }",
            "<view name=\"V\" width=\"300\" height=\"200\"><repeat id=\"r\" max=\"3\"><panel class=\"row\"><text text=\"{item.t}\" class=\"muted\"/><button icon=\"x\"/></panel><empty>none</empty></repeat><tabs><tab page=\"a\">A</tab></tabs><pages><page id=\"a\"><div class=\"chip\">x</div></page></pages></view>|.hot:hover { background: #F00; } .hot { padding: 4; display: none; position: absolute; top: -2; width: 50% }",
        };

        /// <summary>Encode the resolver's textual value the way the table does; null when it is not a runtime property.</summary>
        private static bool Expected(string prop, string v, out float[] val, out string refName)
        {
            return AuiRuleTable.Encode(prop, v, out val, out refName);
        }

        [Test]
        public void TableResolutionMatchesStyleResolver()
        {
            foreach (string f in Fixtures)
            {
                string[] parts = f.Split('|');
                AuiCompileResult r = Compile(parts[0], parts[1]);
                Assert.IsFalse(r.HasErrors, string.Join("\n", r.Diagnostics.Items));
                AuiRuleTable t = r.Rules;
                var resolved = new AuiRuleTable.Resolved[r.Map.ElementCount];
                var parentOf = new int[r.Map.ElementCount];
                for (int e = 0; e < r.Map.ElementCount; e++)
                {
                    AuiNode node = r.Map.NodeOf[e];
                    int row = r.Map.RowOf[e] < 0 ? 0 : r.Map.RowOf[e];
                    parentOf[e] = node != null && node.Parent != null ? r.Map.Elem(node.Parent, row) : -1;
                    resolved[e] = t.Resolve(t.ElemTag[e], t.ElemClass0[e], t.ElemClass1[e], t.ElemNode[e], parentOf[e] >= 0 ? resolved[parentOf[e]] : null);
                    if (node == null) continue;
                    AuiResolvedStyle s = r.Styles[node.Index];
                    foreach (KeyValuePair<string, string> kv in s.Values)
                    {
                        AuiPropDef def = AuiSchema.FindProp(kv.Key);
                        if (def == null || def.BakeOnly) continue;
                        int prop = AuiRuleTable.PropId(kv.Key);
                        if (prop < 0) continue;
                        float[] val; string refName;
                        if (!Expected(kv.Key, kv.Value, out val, out refName)) continue;
                        string where = "<" + node.Tag + (node.Id != null ? " id=" + node.Id : "") + "> elem " + e + " prop " + kv.Key;
                        Assert.IsNotNull(resolved[e].Value[prop], where + " missing in table resolution");
                        if (prop != AuiRuleTable.PROP_TRANSITION)
                            for (int i = 0; i < 4; i++) Assert.AreEqual(val[i], resolved[e].Value[prop][i], 0.0001, where + " component " + i);
                        if (prop == AuiRuleTable.PROP_BG && refName != null)
                            Assert.AreEqual(refName, t.RefNames[resolved[e].Ref[AuiRuleTable.PROP_SPRITE]], where + " sprite");
                    }
                    // Every state rule the resolver saw is one the table collected for the element.
                    int stateRulesExpected = 0;
                    foreach (KeyValuePair<string, Dictionary<string, string>> st in s.States) stateRulesExpected += st.Value.Count > 0 ? 1 : 0;
                    var statesInTable = new HashSet<int>();
                    foreach (int rule in resolved[e].StateRules) statesInTable.Add(t.RuleState[rule]);
                    Assert.GreaterOrEqual(statesInTable.Count, stateRulesExpected, "<" + node.Tag + "> states");
                }
            }
        }

        [Test]
        public void NoBakeOnlyDeclarationReachesTheTable()
        {
            AuiCompileResult r = Compile(Fixtures[0].Split('|')[0], Fixtures[0].Split('|')[1]);
            foreach (int p in r.Rules.DeclProp) Assert.IsTrue(p >= 0 && p < AuiRuleTable.PROP_COUNT);
            Assert.IsTrue(r.Rules.ClassNames.Contains("card"));
            Assert.IsTrue(r.Rules.ClassNames.Contains("x"), "classes defined only by the sheet are still baked");
            Assert.AreEqual(r.Rules.ClassNames.OrderBy(x => x, System.StringComparer.Ordinal).ToArray(), r.Rules.ClassNames);
        }

        [Test]
        public void ClassAttributeMixesLiteralsAndBindings()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"200\" height=\"100\"><panel id=\"p\" class=\"card {tone} {extra}\"/></view>", ".card { padding: 2 } .warm { color: #F00 }");
            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Diagnostics.Items));
            AuiNode p = r.Doc.FindById("p");
            Assert.AreEqual(new[] { "card" }, p.Classes);
            Assert.AreEqual(2, p.ClassBindings.Length);
            Assert.AreEqual(2, r.Rules.ClassSlots);
            AuiBinding tone = r.Info.FindBinding("tone", null);
            Assert.IsNotNull(tone);
            Assert.AreEqual(AuiBindingType.String, tone.Type);
            Assert.AreEqual(0, tone.Uses[0].Slot);
            Assert.AreEqual(1, r.Info.FindBinding("extra", null).Uses[0].Slot);
            StringAssert.Contains("SetClassSlot(1, 0, _tone);", r.GeneratedSource);
            StringAssert.Contains("SetClassSlot(1, 1, _extra);", r.GeneratedSource);
            StringAssert.Contains("public const int C_CARD = ", r.GeneratedSource);
            StringAssert.Contains("public const int C_WARM = ", r.GeneratedSource);
        }

        [Test]
        public void BakeOnlyFontWeightWarnsForRuntimeOnlyClasses()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"200\" height=\"100\"><text class=\"a\">x</text></view>", ".a { font-weight: 600 } .b { font: 600 20 } .c { font: 400 20 }");
            var codes = r.Diagnostics.Items.Where(x => x.Code == AuiCodes.BakeOnlyProperty).ToList();
            Assert.AreEqual(1, codes.Count, string.Join("\n", r.Diagnostics.Items));
            StringAssert.Contains("'.b'", codes[0].Message);
            // .b's size half is still a runtime declaration
            bool hasFontSize = false;
            for (int i = 0; i < r.Rules.DeclProp.Length; i++) if (r.Rules.DeclProp[i] == AuiRuleTable.PROP_FONT && System.Math.Abs(r.Rules.DeclValue[4 * i] - 20) < 0.001) hasFontSize = true;
            Assert.IsTrue(hasFontSize);
        }

        [Test]
        public void MaterialAndShaderBackgroundsBecomeMaterialDeclarations()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"200\" height=\"100\" materials=\"Extra\"><panel class=\"glow\"/><text style=\"color: shader(X)\">t</text></view>",
                ".glow { background: sprite(Rounded8) shader(UI/Default) #FF0000 } .glow:hover { background: material(Glow) }");
            Assert.IsTrue(r.Diagnostics.Items.Any(x => x.Code == AuiCodes.BadStyleValue && x.Message.Contains("only valid in background")), string.Join("\n", r.Diagnostics.Items));
            AuiRuleTable t = r.Rules;
            int shaderRef = System.Array.IndexOf(t.RefNames, "UI/Default");
            int materialRef = System.Array.IndexOf(t.RefNames, "Glow");
            int extraRef = System.Array.IndexOf(t.RefNames, "Extra");
            Assert.GreaterOrEqual(shaderRef, 0); Assert.GreaterOrEqual(materialRef, 0); Assert.GreaterOrEqual(extraRef, 0);
            Assert.AreEqual(AuiRuleTable.REF_SHADER, t.RefKind[shaderRef]);
            Assert.AreEqual(AuiRuleTable.REF_MATERIAL, t.RefKind[materialRef]);
            Assert.AreEqual(AuiRuleTable.REF_MATERIAL, t.RefKind[extraRef]);
            bool decl = false;
            for (int i = 0; i < t.DeclProp.Length; i++) if (t.DeclProp[i] == AuiRuleTable.PROP_MATERIAL && t.DeclRef[i] == shaderRef) decl = true;
            Assert.IsTrue(decl, "shader() yields a PROP_MATERIAL declaration");
            bool hoverDecl = false;
            for (int rule = 0; rule < t.RuleCount; rule++)
            {
                if (t.RuleState[rule] != AuiRuleTable.STATE_HOVER) continue;
                for (int k = t.RuleDeclStart[rule]; k < t.RuleDeclStart[rule] + t.RuleDeclCount[rule]; k++) if (t.DeclProp[k] == AuiRuleTable.PROP_MATERIAL && t.DeclRef[k] == materialRef) hoverDecl = true;
            }
            Assert.IsTrue(hoverDecl, "material() works in a state rule");
        }

        [Test]
        public void VariableDeclarationsKeepTheirTemplateAndReencode()
        {
            AuiCompileResult r = Compile("<view name=\"V\" width=\"200\" height=\"100\"><panel class=\"card\"/><text class=\"hint\">x</text></view>",
                ":root { --tint: #112233; --pad: 6 } .card { background: sprite(Rounded8) var(--tint); padding: var(--pad) 2; } .hint { color: var(--nope, #445566); opacity: 0.5 }");
            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Diagnostics.Items));
            AuiRuleTable t = r.Rules;
            Assert.AreEqual(t.DeclProp.Length, t.DeclTemplate.Length, "one template slot per declaration");
            Assert.AreEqual(t.VarNames.Length, t.VarValues.Length);
            Assert.AreEqual(t.VarNames.OrderBy(x => x, System.StringComparer.Ordinal).ToArray(), t.VarNames, "sorted");
            Assert.IsTrue(t.VarNames.Contains("--muted"), "theme variables are in the table");
            int tint = System.Array.IndexOf(t.VarNames, "--tint");
            Assert.GreaterOrEqual(tint, 0);
            Assert.AreEqual("#112233", t.VarValues[tint]);

            int bg = -1, pad = -1, color = -1, opacity = -1;
            for (int k = 0; k < t.DeclProp.Length; k++)
            {
                if (t.DeclProp[k] == AuiRuleTable.PROP_BG && t.DeclTemplate[k] == "sprite(Rounded8) var(--tint)") bg = k;
                if (t.DeclProp[k] == AuiRuleTable.PROP_PADDING && t.DeclTemplate[k] == "var(--pad) 2") pad = k;
                if (t.DeclProp[k] == AuiRuleTable.PROP_COLOR && t.DeclTemplate[k] == "var(--nope, #445566)") color = k;
                if (t.DeclProp[k] == AuiRuleTable.PROP_OPACITY && System.Math.Abs(t.DeclValue[4 * k] - 0.5f) < 0.001f) opacity = k;
            }
            Assert.GreaterOrEqual(bg, 0, "background keeps its template");
            Assert.GreaterOrEqual(pad, 0, "padding keeps its template");
            Assert.GreaterOrEqual(color, 0, "a fallback-only var() keeps its template");
            Assert.GreaterOrEqual(opacity, 0);
            Assert.IsNull(t.DeclTemplate[opacity], "a literal value has no template");
            Assert.AreEqual(AuiRuleTable.PROP_SPRITE, t.DeclProp[bg + 1]);
            Assert.IsNull(t.DeclTemplate[bg + 1], "the sprite half never comes from a variable");

            // Re-substituting with the baked values reproduces the baked encoding (what the runtime does on every change).
            float[] val; string refName;
            Assert.IsTrue(AuiRuleTable.Encode("background", AuiRuleTable.Substitute(t.DeclTemplate[bg], t.VarNames, t.VarValues), out val, out refName));
            for (int i = 0; i < 4; i++) Assert.AreEqual(t.DeclValue[4 * bg + i], val[i], 0.0001);
            Assert.AreEqual("Rounded8", refName);
            Assert.IsTrue(AuiRuleTable.Encode("padding", AuiRuleTable.Substitute(t.DeclTemplate[pad], t.VarNames, t.VarValues), out val, out refName));
            Assert.AreEqual(new[] { 6f, 2f, 6f, 2f }, val);
            Assert.AreEqual("#445566", AuiRuleTable.Substitute(t.DeclTemplate[color], t.VarNames, t.VarValues), "fallback when undefined");

            // A new value re-encodes.
            string[] values = (string[])t.VarValues.Clone();
            values[tint] = "#FF0000";
            Assert.IsTrue(AuiRuleTable.Encode("background", AuiRuleTable.Substitute(t.DeclTemplate[bg], t.VarNames, values), out val, out refName));
            Assert.AreEqual(1f, val[0], 0.0001); Assert.AreEqual(0f, val[1], 0.0001); Assert.AreEqual(1f, val[3], 0.0001);
        }

        [Test]
        public void TooManyClassesIsAnError()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 70; i++) sb.Append(".c").Append(i).Append(" { padding: 1 } ");
            AuiCompileResult r = Compile("<view name=\"V\" width=\"200\" height=\"100\"><panel class=\"c1\"/></view>", sb.ToString());
            Assert.IsTrue(r.Diagnostics.Items.Any(x => x.Code == AuiCodes.TooManyClasses));
        }

        [Test]
        public void EaseNamesMatchVrcTweenEase()
        {
            System.Type t = System.Type.GetType("VRC.SDK3.Components.VRCTweenEase, VRCSDK3");
            if (t == null) Assert.Inconclusive("VRCSDK3 not loadable here");
            string[] enumNames = System.Enum.GetNames(t);
            for (int i = 0; i < AuiRuleTable.EaseNames.Length; i++)
            {
                int idx = System.Array.IndexOf(enumNames, AuiRuleTable.EaseNames[i]);
                Assert.AreEqual(i, idx, AuiRuleTable.EaseNames[i] + " must be enum member " + i + " (VRCTweenEase order: " + string.Join(",", enumNames) + ")");
            }
        }
    }
}
