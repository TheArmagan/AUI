using UnityEngine;

namespace Armagan.AUI
{
    /// <summary>
    /// Project-wide AUI configuration. One instance lives in the consumer project
    /// (Assets/AUI/AuiSettings.asset, created by AUI/Create Settings); the editor finds it by type.
    /// Plain ScriptableObject, never read at runtime.
    /// </summary>
    public sealed class AuiSettings : ScriptableObject
    {
        [Tooltip("Folder with one <code>.json per language, e.g. Assets/AUI/lang.")]
        public string langFolder = "Assets/AUI/lang";

        [Tooltip("Language code used when a key is missing in the player's language.")]
        public string defaultLanguage = "en";

        [Tooltip("Extra folders searched for nested views by tag name (besides the view's own folder).")]
        public string[] componentFolders = new string[0];

        [Tooltip("Extra folders with Lucide-style SVG icons (white sources), searched before the package's own.")]
        public string[] iconFolders = new string[0];

        [Tooltip("Characters to bake into the fonts on top of everything found in the language files.")]
        public string extraCharacters = "";

        [Tooltip("Unicode ranges (hex, comma separated) always baked, for runtime strings such as player names.")]
        public string unicodeRanges = "0020-007E,00A0-024F,0370-03FF,0400-04FF,2000-206F,20A0-20CF,2190-21FF,3000-303F,FF00-FFEF,200B";

        [Tooltip("The baked font set; created by AUI/Bake Fonts.")]
        public AuiFontSet fontSet;

        [Tooltip("Sample text used to size a bound text that has no max-width.")]
        public string boundTextSample = "Mmmmmmmmmmmm";
    }
}
