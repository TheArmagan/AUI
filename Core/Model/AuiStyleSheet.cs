using System.Collections.Generic;

namespace Armagan.AUI.Core
{
    /// <summary>
    /// Where a sheet came from, which decides the cascade order. Higher wins.
    /// </summary>
    public enum AuiStyleTier
    {
        Theme = 0,     // the package default theme
        Utility = 1,   // the generated *utility classes (AuiUtilityClasses)
        Linked = 2,    // <link href> or the sibling Foo.auis
        Inline = 3,    // <style> blocks in the view
        Attribute = 4  // style="" on an element
    }

    /// <summary>tag | .class | tag.class | tag:state | .class:state | :root | *</summary>
    public sealed class AuiSelector
    {
        public string Tag;    // null = any
        public string Class;  // null = any
        public string State;  // null = normal
        public bool IsRoot;   // :root, only for variables
        /// <summary>"backdrop" for modal::backdrop: styles the dimming scrim, not the element.</summary>
        public string Pseudo;

        /// <summary>tag 1, class 2, tag+class 3; +10 with a state. Later rules break ties.</summary>
        public int Specificity
        {
            get
            {
                int s = 0;
                if (Tag != null) s += 1;
                if (Class != null) s += 2;
                if (State != null) s += 10;
                return s;
            }
        }

        public bool Matches(string tag, string[] classes)
        {
            if (IsRoot || Pseudo != null) return false;
            return MatchesElement(tag, classes);
        }

        /// <summary>Tag and class test alone, used for pseudo-element rules against their host element.</summary>
        public bool MatchesElement(string tag, string[] classes)
        {
            if (Tag != null && Tag != tag && !(Tag == "panel" && tag == "div") && !(Tag == "div" && tag == "panel")) return false;
            if (Class != null)
            {
                bool has = false;
                for (int i = 0; i < classes.Length; i++) if (classes[i] == Class) { has = true; break; }
                if (!has) return false;
            }
            return true;
        }

        public override string ToString()
        {
            if (IsRoot) return ":root";
            string s = Tag ?? (Class == null ? "*" : "");
            if (Class != null) s += "." + Class;
            if (Pseudo != null) s += "::" + Pseudo;
            if (State != null) s += ":" + State;
            return s;
        }
    }

    public sealed class AuiDeclaration
    {
        public string Property;
        /// <summary>Raw text after the colon, trimmed, var() not yet substituted.</summary>
        public string Value;
        public int Line;
        public int Column;
        public int ValueColumn;
    }

    public sealed class AuiStyleRule
    {
        public readonly List<AuiSelector> Selectors = new List<AuiSelector>();
        public readonly List<AuiDeclaration> Declarations = new List<AuiDeclaration>();
        public int Line;
        public int Column;
        /// <summary>Source order across the whole cascade; assigned by the resolver.</summary>
        public int Order;
        public AuiStyleSheet Sheet;

        public AuiDeclaration Find(string property)
        {
            for (int i = 0; i < Declarations.Count; i++)
                if (Declarations[i].Property == property) return Declarations[i];
            return null;
        }
    }

    public sealed class AuiVariable
    {
        public string Name;   // "--accent"
        public string Value;
        public int Line;
        public int Column;
    }

    public sealed class AuiStyleSheet
    {
        public string File;
        public AuiStyleTier Tier;
        public readonly List<AuiStyleRule> Rules = new List<AuiStyleRule>();
        public readonly List<AuiVariable> Variables = new List<AuiVariable>();
        public readonly AuiDiagnosticBag Diagnostics;

        public AuiStyleSheet(string file, AuiStyleTier tier)
        {
            File = file;
            Tier = tier;
            Diagnostics = new AuiDiagnosticBag(file);
        }

        /// <summary>Every class name mentioned by any selector, for completion and unused-class checks.</summary>
        public IEnumerable<string> ClassNames()
        {
            var seen = new HashSet<string>();
            foreach (AuiStyleRule r in Rules)
                foreach (AuiSelector s in r.Selectors)
                    if (s.Class != null && seen.Add(s.Class)) yield return s.Class;
        }
    }
}
