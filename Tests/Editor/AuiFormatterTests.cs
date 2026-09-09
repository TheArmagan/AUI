using Armagan.AUI.Core;
using NUnit.Framework;

namespace Armagan.AUI.Tests
{
    public class AuiFormatterTests
    {
        [Test]
        public void ChildrenGoOnTheirOwnLines()
        {
            string src = "<view name=\"V\"><panel class=\"row\"><text key=\"a\"/><spacer/><icon name=\"x\"/></panel></view>";
            string expected =
                "<view name=\"V\">\n" +
                "  <panel class=\"row\">\n" +
                "    <text key=\"a\"/>\n" +
                "    <spacer/>\n" +
                "    <icon name=\"x\"/>\n" +
                "  </panel>\n" +
                "</view>\n";
            Assert.AreEqual(expected, AuiFormatter.FormatMarkup(src));
        }

        [Test]
        public void TextOnlyElementStaysInline()
        {
            string src = "<view name=\"V\">\n<text>Debug   build</text>\n<button key=\"k\">Buy <icon name=\"cart\"/></button></view>";
            string expected =
                "<view name=\"V\">\n" +
                "  <text>Debug build</text>\n" +
                "  <button key=\"k\">\n" +
                "    Buy\n" +
                "    <icon name=\"cart\"/>\n" +
                "  </button>\n" +
                "</view>\n";
            Assert.AreEqual(expected, AuiFormatter.FormatMarkup(src));
        }

        [Test]
        public void LongTagsWrapOneAttributePerLine()
        {
            string src = "<view name=\"V\"><AvtrPager id=\"pager\" label=\"{pagerLabel}\" page=\"{pagerPage}\" hasFirst=\"{hasFirst}\" hasPrev=\"{hasPrev}\" hasNext=\"{hasNext}\" on-first=\"PagerFirst\"/></view>";
            string expected =
                "<view name=\"V\">\n" +
                "  <AvtrPager id=\"pager\"\n" +
                "             label=\"{pagerLabel}\"\n" +
                "             page=\"{pagerPage}\"\n" +
                "             hasFirst=\"{hasFirst}\"\n" +
                "             hasPrev=\"{hasPrev}\"\n" +
                "             hasNext=\"{hasNext}\"\n" +
                "             on-first=\"PagerFirst\"/>\n" +
                "</view>\n";
            Assert.AreEqual(expected, AuiFormatter.FormatMarkup(src));
        }

        [Test]
        public void CommentsBlankLinesAndProlog()
        {
            string src =
                "<?xml version=\"1.0\"?>\n" +
                "<view name=\"V\">\n" +
                "      <!-- The hub: a sidebar,\n" +
                "           one page per child. -->\n" +
                "  <link href=\"S.auis\"/>\n\n\n\n" +
                "  <panel/>\n" +
                "</view>\n";
            string expected =
                "<?xml version=\"1.0\"?>\n" +
                "<view name=\"V\">\n" +
                "  <!-- The hub: a sidebar,\n" +
                "       one page per child. -->\n" +
                "  <link href=\"S.auis\"/>\n" +
                "\n" +
                "  <panel/>\n" +
                "</view>\n";
            Assert.AreEqual(expected, AuiFormatter.FormatMarkup(src));
        }

        [Test]
        public void StyleBlockIsExpanded()
        {
            string src =
                "<view name=\"V\">\n" +
                "  <text class=\"a\"/>\n" +
                "  <style>\n" +
                "    /* the theme lays tabs out in a row */\n" +
                "    view { background: none; padding: 0 }\n" +
                "    .a, .b:hover { font: 700 40; color: var(--muted) } // trailing\n" +
                "  </style>\n" +
                "</view>";
            string expected =
                "<view name=\"V\">\n" +
                "  <text class=\"a\"/>\n" +
                "  <style>\n" +
                "    /* the theme lays tabs out in a row */\n" +
                "    view {\n" +
                "      background: none;\n" +
                "      padding: 0;\n" +
                "    }\n" +
                "    .a, .b:hover {\n" +
                "      font: 700 40;\n" +
                "      color: var(--muted);\n" +
                "    }   // trailing\n" +
                "  </style>\n" +
                "</view>\n";
            Assert.AreEqual(expected, AuiFormatter.FormatMarkup(src));
        }

        [Test]
        public void SheetIsExpandedAndCommentsSurvive()
        {
            string src =
                "/* Shared by every view.\n" +
                "   Two lines. */\n\n" +
                ":root { --ok:   #7FC97F; --pad: 12 }\n" +
                "view { background: sprite(Rounded12) var(--surface); padding: var(--pad); }\n\n\n" +
                ".ok     { color: var(--ok);     icon-color: var(--ok); }\n" +
                "button:hover { scale: 1.03 }\n" +
                ".x {\n" +
                "  color: #fff // note\n" +
                "  gap: 2;\n" +
                "}\n";
            string expected =
                "/* Shared by every view.\n" +
                "   Two lines. */\n" +
                "\n" +
                ":root {\n" +
                "  --ok: #7FC97F;\n" +
                "  --pad: 12;\n" +
                "}\n" +
                "view {\n" +
                "  background: sprite(Rounded12) var(--surface);\n" +
                "  padding: var(--pad);\n" +
                "}\n" +
                "\n" +
                ".ok {\n" +
                "  color: var(--ok);\n" +
                "  icon-color: var(--ok);\n" +
                "}\n" +
                "button:hover {\n" +
                "  scale: 1.03;\n" +
                "}\n" +
                ".x {\n" +
                "  color: #fff;   // note\n" +
                "  gap: 2;\n" +
                "}\n";
            Assert.AreEqual(expected, AuiFormatter.FormatSheet(src));
        }

        [Test]
        public void FormattingIsIdempotent()
        {
            string once = AuiFormatter.FormatMarkup(AuiCodegenTests.Demo);
            Assert.IsNotNull(once);
            Assert.AreEqual(once, AuiFormatter.FormatMarkup(once));
            string sheet = AuiFormatter.FormatSheet(System.IO.File.ReadAllText(System.IO.Path.Combine(TestPaths.PackageRoot, "Theme/Default.auis")));
            Assert.IsNotNull(sheet);
            Assert.AreEqual(sheet, AuiFormatter.FormatSheet(sheet));
        }

        [Test]
        public void FormattedMarkupStillCompilesToTheSameTree()
        {
            string formatted = AuiFormatter.FormatMarkup(AuiCodegenTests.Demo);
            AuiMarkupDocument a = AuiParser.Parse(AuiCodegenTests.Demo, "Demo.aui");
            AuiMarkupDocument b = AuiParser.Parse(formatted, "Demo.aui");
            Assert.IsFalse(b.Diagnostics.HasErrors, string.Join("\n", b.Diagnostics.Items));
            Assert.AreEqual(a.Nodes.Count, b.Nodes.Count);
            for (int i = 0; i < a.Nodes.Count; i++)
            {
                Assert.AreEqual(a.Nodes[i].Tag, b.Nodes[i].Tag);
                Assert.AreEqual(a.Nodes[i].Text, b.Nodes[i].Text);
                Assert.AreEqual(a.Nodes[i].Attributes.Count, b.Nodes[i].Attributes.Count);
                for (int k = 0; k < a.Nodes[i].Attributes.Count; k++) Assert.AreEqual(a.Nodes[i].Attributes[k].Value, b.Nodes[i].Attributes[k].Value);
            }
        }

        [Test]
        public void BrokenInputIsLeftAlone()
        {
            Assert.IsNull(AuiFormatter.FormatMarkup("<view name=\"V\"><panel></view>"));
            Assert.IsNull(AuiFormatter.FormatSheet("button { gap: 1 "));
        }
    }
}
