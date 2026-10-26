using Armagan.AUI.Core;
using NUnit.Framework;

namespace Armagan.AUI.Tests
{
    public class AuiRtlTests
    {
        [Test]
        public void RtlCodes()
        {
            Assert.IsTrue(AuiRtl.IsRtlCode("ar"));
            Assert.IsTrue(AuiRtl.IsRtlCode("ar-EG"));
            Assert.IsTrue(AuiRtl.IsRtlCode("fa"));
            Assert.IsFalse(AuiRtl.IsRtlCode("en"));
            Assert.IsFalse(AuiRtl.IsRtlCode(""));
        }

        [Test]
        public void ShapesJoiningForms()
        {
            // "salam": seen initial, lam medial... lam + alef is one ligature, final after the lam
            // joins it, then meem isolated after the right-joining alef.
            string s = AuiRtl.Shape("سلام");
            Assert.AreEqual("ﺳﻼﻡ", s);
        }

        [Test]
        public void IsolatedAndFinal()
        {
            // beh alone is isolated; beh beh is initial + final.
            Assert.AreEqual("ﺏ", AuiRtl.Shape("ب"));
            Assert.AreEqual("ﺑﺐ", AuiRtl.Shape("بب"));
        }

        [Test]
        public void ReversesLtrRunsKeepsPlaceholders()
        {
            // A Latin run inside Arabic is written backwards for TMP's right-to-left placement;
            // a placeholder stays whole and readable.
            string p = AuiRtl.Prepare("ب VIP {name}");
            Assert.AreEqual("ﺏ {name} PIV", p);
        }

        [Test]
        public void TagsAreNotReversed()
        {
            Assert.AreEqual("<s>V005</s>", AuiRtl.Prepare("<s>500V</s>"));
        }

        [Test]
        public void VisualForLeftToRightLabels()
        {
            // "arabi" (ain reh beh yeh): reh does not join forward, so beh starts a new word part
            // (initial, not medial). Shaped, then reversed into drawing order.
            string v = AuiRtl.ToVisual("عربي");
            Assert.AreEqual("ﻲﺑﺮﻋ", v);
        }
    }
}
