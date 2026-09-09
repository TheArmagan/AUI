using System.Collections.Generic;
using System.IO;
using System.Linq;
using Armagan.AUI.Core;
using NUnit.Framework;

namespace Armagan.AUI.Tests
{
    public class AuiCodegenTests
    {
        public const string Demo =
            "<view name=\"Demo\" width=\"400\" height=\"300\">\n" +
            "  <text id=\"title\" key=\"demo.title\" class=\"title\"/>\n" +
            "  <text id=\"greeting\" key=\"demo.greeting\" args=\"{playerName}\"/>\n" +
            "  <text text=\"{status}\" class=\"muted\"/>\n" +
            "  <button id=\"greet\" onclick=\"Greet\" key=\"demo.greet\" disabled=\"{busy}\" onhover=\"GreetHover\"/>\n" +
            "  <repeat id=\"rows\" max=\"3\" onselect=\"RowClick\">\n" +
            "    <panel class=\"row\"><text text=\"{item.name}\"/><button onclick=\"RowBuy\" key=\"demo.buy\"/><toggle bind=\"{item.fav}\" onchange=\"RowFav\"/></panel>\n" +
            "    <empty><text key=\"demo.empty\"/></empty>\n" +
            "  </repeat>\n" +
            "  <tabs bind=\"{page}\" onchange=\"PageChanged\"><tab page=\"first\" key=\"demo.tab1\"/><tab page=\"second\" key=\"demo.tab2\"/></tabs>\n" +
            "  <pages id=\"main\" default=\"second\">\n" +
            "    <page id=\"first\"><slider id=\"vol\" bind=\"{volume}\" onchange=\"VolumeChanged\" format-key=\"demo.volfmt\" decimals=\"1\"/></page>\n" +
            "    <page id=\"second\"><toggle id=\"mute\" bind=\"{muted}\" onchange=\"MuteChanged\" key=\"demo.mute\"/><input id=\"nick\" bind=\"{nick}\" onsubmit=\"NickSubmitted\" placeholder-key=\"demo.nickph\"/><panel if=\"{isVip}\"/></page>\n" +
            "  </pages>\n" +
            "  <select id=\"quality\" bind=\"{quality}\" onchange=\"QualityChanged\"><option value=\"low\" key=\"q.low\"/><option value=\"high\" key=\"q.high\"/></select>\n" +
            "  <LanguagePicker id=\"lp\" title=\"{playerName}\" compact=\"true\" on-picked=\"Picked\"/>\n" +
            "</view>";

        private static AuiViewInfo ChildInfo(string name)
        {
            if (name != "LanguagePicker") return null;
            var doc = AuiParser.Parse("<view name=\"LanguagePicker\"><text text=\"{title}\"/><panel if=\"{compact}\"/></view>", "LanguagePicker.aui");
            return AuiAnalyzer.Analyze(doc, new AuiAnalysisContext(), new AuiDiagnosticBag("LanguagePicker.aui"));
        }

        public static string Generate(string markup, out AuiDiagnosticBag bag, out AuiIndexMap map)
        {
            var doc = AuiParser.Parse(markup, "Demo.aui");
            bag = new AuiDiagnosticBag("Demo.aui");
            bag.AddRange(doc.Diagnostics);
            var sheets = new List<AuiStyleSheet> { AuiStyleParser.Parse(File.ReadAllText(Path.Combine(TestPaths.PackageRoot, "Theme/Default.auis")), "Default.auis", AuiStyleTier.Theme) };
            var styles = new AuiStyleResolver(sheets).Resolve(doc, bag);
            var info = AuiAnalyzer.Analyze(doc, new AuiAnalysisContext { FileStem = "Demo", ViewExists = n => n == "LanguagePicker" }, bag);
            map = AuiIndexMap.Build(doc, info, styles);
            return AuiCodegen.Emit(info, map, new AuiCodegenContext { Hash = "test", ChildInfo = ChildInfo }, bag);
        }

        [Test]
        public void IndexMapAssignsRowsAndSignals()
        {
            AuiDiagnosticBag bag; AuiIndexMap map;
            Generate(Demo, out bag, out map);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items.Where(x => x.Severity == AuiSeverity.Error)));
            // view0 title1 greeting2 status3 greet4 repeat5 then 3 rows x (panel,text,button,toggle)=4 -> 6..17, empty18 emptytext19, tabs20 ...
            Assert.AreEqual(5, map.Base[5]);
            Assert.AreEqual(6, map.Base[6]);       // row template panel, instance 0
            Assert.AreEqual(4, map.Stride[6]);
            Assert.AreEqual(3, map.Copies[6]);
            Assert.AreEqual(18, map.Base[10]);     // <empty>
            Assert.AreEqual(0, map.SignalOf[4]);   // greet button is the first signal (view/title/greeting/status have none)
            Assert.IsTrue(map.SignalOf[6] >= 0);   // row root has a signal because of onselect
            Assert.AreEqual(map.SignalOf[6] + 3, map.SignalOf[10]);  // next row root, 3 signals per row (root, button, toggle)
            Assert.AreEqual(3, map.RepeatSignalStride[5]);
            Assert.IsTrue(map.PopupElem.Count == 1 && map.ScrimElem.Count == 1);
            Assert.AreEqual(AuiIndexMap.KIND_SCRIM, map.KindOf[map.ScrimElem.Values.First()]);
        }

        [Test]
        public void OutputIsDeterministic()
        {
            AuiDiagnosticBag b1, b2; AuiIndexMap m1, m2;
            string a = Generate(Demo, out b1, out m1);
            string b = Generate(Demo, out b2, out m2);
            Assert.AreEqual(a, b);
        }

        [Test]
        public void OutputHasTheTypedSurface()
        {
            AuiDiagnosticBag bag; AuiIndexMap map;
            string src = Generate(Demo, out bag, out map);
            Assert.IsFalse(bag.HasErrors, string.Join("\n", bag.Items.Where(x => x.Severity == AuiSeverity.Error)));
            StringAssert.Contains("public partial class Demo : AuiView", src);
            StringAssert.Contains("public string PlayerName", src);
            StringAssert.Contains("public bool Busy", src);
            StringAssert.Contains("public float Volume", src);
            StringAssert.Contains("public int Page", src);
            StringAssert.Contains("public int Quality", src);
            StringAssert.Contains("public string Nick", src);
            StringAssert.Contains("public bool IsVip", src);
            StringAssert.Contains("public int RowsCount", src);
            StringAssert.Contains("public void SetRowsName(int row, string value)", src);
            StringAssert.Contains("public void SetRowsFav(int row, bool value)", src);
            StringAssert.Contains("public const int E_TITLE = 1;", src);
            StringAssert.Contains("public const int ROWS_MAX = 3;", src);
            StringAssert.Contains("public const int MAIN_SECOND = 1;", src);
            StringAssert.Contains("public LanguagePicker childLanguagePickerLp;", src);
            StringAssert.Contains("Greet();", src);
            StringAssert.Contains("RowBuy(row);", src);
            StringAssert.Contains("RowClick(row);", src);
            StringAssert.Contains("RowFav(row, GetToggle(", src);
            StringAssert.Contains("VolumeChanged(elemSliders[", src);
            StringAssert.Contains("MuteChanged(GetToggle(", src);
            StringAssert.Contains("NickSubmitted(_nick);", src);
            StringAssert.Contains("PageChanged(page);", src);
            StringAssert.Contains("QualityChanged(index);", src);
            StringAssert.Contains("Picked();", src);
            StringAssert.Contains("GreetHover();", src);
            StringAssert.Contains("childLanguagePickerLp.Title = _playerName;", src);
            StringAssert.Contains("childLanguagePickerLp.Compact = true;", src);
            StringAssert.Contains("Fill1(lang.Get(elemKey[2]), \"{playerName}\", _playerName)", src);
            StringAssert.DoesNotContain("partial void", src);
            StringAssert.DoesNotContain("[NonSerialized]", src);
        }

        [Test]
        public void GoldenFile()
        {
            AuiDiagnosticBag bag; AuiIndexMap map;
            string src = Generate(Demo, out bag, out map);
            string dir = Path.Combine(TestPaths.PackageRoot, "Tests/Editor/Golden");
            string path = Path.Combine(dir, "Demo.g.cs.txt");
            string actualPath = Path.Combine(dir, "Demo.g.cs.actual.txt");
            // AUI_UPDATE_GOLDEN=1 rewrites the golden in one run instead of delete, run, review, run.
            bool update = System.Environment.GetEnvironmentVariable("AUI_UPDATE_GOLDEN") == "1";
            if (!File.Exists(path) || update)
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, src);
                if (File.Exists(actualPath)) File.Delete(actualPath);
                Assert.Inconclusive("Golden file written; review it and re-run.");
            }
            string expected = File.ReadAllText(path).Replace("\r\n", "\n");
            string actual = src.Replace("\r\n", "\n");
            if (expected != actual)
            {
                // The actual output next to the golden, so the review is a file diff rather than a console dump.
                File.WriteAllText(actualPath, src);
                Assert.Fail("Generated code differs from Tests/Editor/Golden/Demo.g.cs.txt; the output was written to Demo.g.cs.actual.txt. Diff them, then delete the golden (or set AUI_UPDATE_GOLDEN=1) and re-run to accept.");
            }
            if (File.Exists(actualPath)) File.Delete(actualPath);
        }

        [Test]
        public void ReservedBindingNameIsReported()
        {
            AuiDiagnosticBag bag; AuiIndexMap map;
            Generate("<view name=\"Demo\"><text text=\"{content}\"/></view>", out bag, out map);
            Assert.IsTrue(bag.Items.Any(x => x.Code == AuiCodes.BadBindingSyntax && x.Message.Contains("collides")));
        }
    }
}
