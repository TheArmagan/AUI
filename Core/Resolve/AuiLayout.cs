using System.Collections.Generic;

namespace Armagan.AUI.Core
{
    /// <summary>Text metrics, supplied by the host: TMP in Unity, a character heuristic elsewhere.</summary>
    public interface IAuiTextMeasurer
    {
        /// <param name="maxWidth">Available width in mm, or 0 for unbounded (single line).</param>
        void Measure(string text, float fontSize, int weight, float maxWidth, bool wrap, out float width, out float height);
    }

    /// <summary>
    /// A character-count heuristic good enough for the language server and the unit tests.
    /// Latin glyphs are ~0.55 em wide in Inter at these sizes, CJK glyphs a full em.
    /// </summary>
    public sealed class AuiApproxTextMeasurer : IAuiTextMeasurer
    {
        public float LineHeightFactor = 1.25f;

        public void Measure(string text, float fontSize, int weight, float maxWidth, bool wrap, out float width, out float height)
        {
            float w = 0f;
            float widest = 0f;
            int lines = 1;
            float wordStart = 0f;
            foreach (char c in text ?? "")
            {
                if (c == '\n') { widest = System.Math.Max(widest, w); w = 0; lines++; continue; }
                float cw = c >= 0x2E80 ? fontSize : (c == ' ' ? fontSize * 0.28f : fontSize * 0.55f);
                if (weight >= 600) cw *= 1.06f;
                if (wrap && maxWidth > 0 && w + cw > maxWidth && w > 0)
                {
                    widest = System.Math.Max(widest, w);
                    w = c == ' ' ? 0 : cw;
                    lines++;
                    continue;
                }
                w += cw;
                if (c == ' ') wordStart = w;
            }
            widest = System.Math.Max(widest, w);
            width = widest;
            height = fontSize * LineHeightFactor * lines;
        }
    }

    public struct AuiRect
    {
        public float X, Y, W, H;

        public AuiRect(float x, float y, float w, float h)
        {
            X = x; Y = y; W = w; H = h;
        }

        public override string ToString()
        {
            return "(" + AuiNum.Fmt(X) + "," + AuiNum.Fmt(Y) + " " + AuiNum.Fmt(W) + "x" + AuiNum.Fmt(H) + ")";
        }
    }

    /// <summary>The solver's answer for one element.</summary>
    public sealed class AuiLayoutNode
    {
        public AuiNode Node;
        /// <summary>Rect relative to the PARENT's top-left corner, y down, mm.</summary>
        public AuiRect Local;
        /// <summary>Rect relative to the view's top-left corner.</summary>
        public AuiRect Absolute;
        /// <summary>Intrinsic size from the measure pass.</summary>
        public float IntrinsicW, IntrinsicH;
        /// <summary>Anchored to fill the parent: bake as stretch anchors instead of a fixed rect.</summary>
        public bool AnchorFill;
        /// <summary>Content varies at runtime; the builder adds a LayoutGroup + ContentSizeFitter on this subtree.</summary>
        public bool ContentSized;
        /// <summary>For repeat containers: offset of each baked instance relative to the repeat's content box.</summary>
        public List<AuiRect> Instances;
        /// <summary>Content box (padding removed), relative to the element's own top-left.</summary>
        public AuiRect Content;
    }

    /// <summary>What the solver may ask the host: translations for width measurement, sprite sizes.</summary>
    public sealed class AuiLayoutContext
    {
        public IAuiTextMeasurer Measurer = new AuiApproxTextMeasurer();
        /// <summary>All translations of a key, for measuring the widest; null = key unknown.</summary>
        public System.Func<string, IEnumerable<string>> Translations;
        /// <summary>Natural size of an image src in mm; false when unknown.</summary>
        public AuiImageSizeLookup ImageSize;
        /// <summary>Sample text used to size a bound text with no max-width.</summary>
        public string BoundTextSample = "Mmmmmmmmmmm日";
        /// <summary>Analyzed model of a nested view by name, for its declared width and height; null = unknown.</summary>
        public System.Func<string, AuiViewInfo> ChildInfo;
    }

    public delegate bool AuiImageSizeLookup(string src, out float w, out float h);
}
