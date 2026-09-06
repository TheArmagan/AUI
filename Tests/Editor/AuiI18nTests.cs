using System.Linq;
using Armagan.AUI.Core;
using NUnit.Framework;

namespace Armagan.AUI.Tests
{
    public class AuiJsonTests
    {
        [Test]
        public void FlattensNestedObjects()
        {
            var r = AuiJson.Flatten("{\n  \"_name\": \"English\",\n  \"settings\": { \"title\": \"Settings\", \"audio\": { \"mute\": \"Mute\" } },\n  \"n\": 3, \"b\": true\n}", "en.json");
            Assert.IsFalse(r.Diagnostics.HasErrors, string.Join("\n", r.Diagnostics.Items));
            Assert.AreEqual("Settings", r.Values["settings.title"]);
            Assert.AreEqual("Mute", r.Values["settings.audio.mute"]);
            Assert.AreEqual("3", r.Values["n"]);
            Assert.AreEqual("true", r.Values["b"]);
            CollectionAssert.AreEqual(new[] { "_name", "settings.title", "settings.audio.mute", "n", "b" }, r.Order);
            Assert.AreEqual(3, r.Positions["settings.title"].Line);
            Assert.AreEqual(26, r.Positions["settings.title"].Column);
        }

        [Test]
        public void EscapesAndUnicode()
        {
            var r = AuiJson.Flatten("{ \"a\": \"line\\nbreak \\\"q\\\" \\u00e7 \\/\" }", "x.json");
            Assert.AreEqual("line\nbreak \"q\" ç /", r.Values["a"]);
        }

        [Test]
        public void ToleratesCommentsAndTrailingComma()
        {
            var r = AuiJson.Flatten("{ // note\n \"a\": \"1\", /* x */ \"b\": \"2\", }", "x.json");
            Assert.IsFalse(r.Diagnostics.HasErrors, string.Join("\n", r.Diagnostics.Items));
            Assert.AreEqual(2, r.Values.Count);
        }

        [Test]
        public void ReportsArraysNullDuplicatesAndSyntax()
        {
            var r = AuiJson.Flatten("{ \"a\": [1], \"b\": null, \"c\": \"x\", \"c\": \"y\", \"d\" \"z\", \"e.f\": \"1\" }", "x.json");
            var codes = r.Diagnostics.Items.Select(x => x.Code).ToList();
            Assert.AreEqual(4, codes.Count(c => c == AuiCodes.LanguageFileSyntax), string.Join("\n", r.Diagnostics.Items));
            Assert.AreEqual(1, codes.Count(c => c == AuiCodes.DuplicateKey));
            Assert.AreEqual("x", r.Values["c"]);
        }

        [Test]
        public void UnterminatedStringDoesNotThrow()
        {
            var r = AuiJson.Flatten("{ \"a\": \"oops", "x.json");
            Assert.IsTrue(r.Diagnostics.HasErrors);
        }
    }

    public class AuiLanguageTableTests
    {
        private static AuiLanguageTable Table()
        {
            var en = AuiLanguageFile.Load("en", "{ \"_name\": \"English\", \"t\": \"Title\", \"g\": \"Hello {name}\", \"only\": \"x\", \"fmt\": \"{value} m\" }", "en.json");
            var tr = AuiLanguageFile.Load("tr", "{ \"_name\": \"Türkçe\", \"t\": \"Başlık\", \"g\": \"Merhaba {name}\" }", "tr.json");
            var ja = AuiLanguageFile.Load("ja", "{ \"_name\": \"日本語\", \"t\": \"タイトル\", \"g\": \"{name}さん、こんにちは\", \"extra\": \"{count}\" }", "ja.json");
            return AuiLanguageTable.Build(new[] { tr, ja, en }, "en");
        }

        [Test]
        public void BuildsSortedKeysAndLanguages()
        {
            var t = Table();
            CollectionAssert.AreEqual(new[] { "en", "ja", "tr" }, t.Codes());
            CollectionAssert.AreEqual(new[] { "English", "日本語", "Türkçe" }, t.Names());
            CollectionAssert.AreEqual(new[] { "extra", "fmt", "g", "only", "t" }, t.Keys);
            Assert.AreEqual(0, t.DefaultLanguage);
            Assert.AreEqual(2, t.IndexOfKey("g"));
            Assert.AreEqual(-1, t.IndexOfKey("_name"));
        }

        [Test]
        public void FlatAndFallback()
        {
            var t = Table();
            string[] flat = t.Flat();
            Assert.AreEqual(15, flat.Length);
            Assert.AreEqual("Başlık", flat[t.IndexOfLanguage("tr") * t.KeyCount + t.IndexOfKey("t")]);
            Assert.AreEqual("", flat[t.IndexOfLanguage("tr") * t.KeyCount + t.IndexOfKey("only")]);
            Assert.AreEqual("x", t.GetWithFallback(t.IndexOfLanguage("tr"), "only"));
            Assert.AreEqual(3, t.AllTranslations("g").Count());
            Assert.AreEqual(0, t.AllTranslations("nope").Count());
        }

        [Test]
        public void PlaceholdersAndCharacters()
        {
            CollectionAssert.AreEqual(new[] { "name", "count" }, AuiLanguageTable.Placeholders("{name} has {count} and {name} again {bad-one}"));
            var chars = Table().AllCharacters();
            Assert.IsTrue(chars.Contains('ş'));
            Assert.IsTrue(chars.Contains('本'));
        }

        [Test]
        public void CheckReportsUnknownMissingAndPlaceholders()
        {
            var t = Table();
            var doc = AuiParser.Parse("<view name=\"V\"><text key=\"t\"/><text key=\"only\"/><text key=\"nope\"/><text key=\"g\"/><text key=\"extra\" args=\"{count} {other}\"/><slider bind=\"{v}\" format-key=\"fmt\"/></view>", "V.aui");
            var bag = new AuiDiagnosticBag("V.aui");
            var info = AuiAnalyzer.Analyze(doc, new AuiAnalysisContext(), bag);
            t.Check(info, bag);
            var items = bag.Items;
            Assert.IsTrue(items.Any(x => x.Code == AuiCodes.UnknownKey && x.Message.Contains("nope")));
            Assert.IsTrue(items.Any(x => x.Code == AuiCodes.MissingTranslation && x.Message.Contains("'only'") && x.Message.Contains("ja") && x.Message.Contains("tr")));
            Assert.IsTrue(items.Any(x => x.Code == AuiCodes.PlaceholderMismatch && x.Severity == AuiSeverity.Error && x.Message.Contains("{name}")));
            Assert.IsTrue(items.Any(x => x.Code == AuiCodes.MissingDefaultTranslation && x.Message.Contains("extra")));
            Assert.IsTrue(items.Any(x => x.Code == AuiCodes.PlaceholderMismatch && x.Severity == AuiSeverity.Warning && x.Message.Contains("{other}")));
            Assert.IsFalse(items.Any(x => x.Message.Contains("{value}")));
        }
    }
}
