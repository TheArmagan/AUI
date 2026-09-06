namespace Armagan.AUI.Core
{
    /// <summary>
    /// Every diagnostic code AUI can emit, in one place. A code is stable for the life of the
    /// project: it appears in the console, in VS Code, and in the README table, so it is never
    /// renumbered or reused. Add at the end of a block; never fill a gap.
    ///
    /// Blocks: 0xx markup syntax, 1xx markup semantics, 2xx stylesheet, 3xx i18n, 4xx layout,
    /// 5xx build/codegen (editor only), 6xx fonts (editor only).
    /// </summary>
    public static class AuiCodes
    {
        // ---- markup syntax
        public const string UnexpectedCharacter = "AUI001";
        public const string UnclosedTag = "AUI002";
        public const string MismatchedCloseTag = "AUI003";
        public const string MultipleRoots = "AUI004";
        public const string AttributeNotQuoted = "AUI005";
        public const string DuplicateAttribute = "AUI006";
        public const string EmptyDocument = "AUI007";
        public const string BareAmpersand = "AUI008";
        public const string UnknownEntity = "AUI009";
        public const string TextOutsideRoot = "AUI010";
        public const string UnterminatedComment = "AUI011";
        public const string UnterminatedAttribute = "AUI012";

        // ---- markup semantics
        public const string UnknownTag = "AUI100";
        public const string UnknownAttribute = "AUI101";
        public const string DuplicateId = "AUI102";
        public const string ViewNameMismatch = "AUI103";
        public const string RootMustBeView = "AUI104";
        public const string RepeatTemplateCount = "AUI105";
        public const string TabWithoutPage = "AUI106";
        public const string NestedViewNotFound = "AUI107";
        public const string BindingTypeConflict = "AUI108";
        public const string HandlerNotIdentifier = "AUI109";
        public const string MissingRequiredAttribute = "AUI110";
        public const string BadAttributeValue = "AUI111";
        public const string ElementNotAllowedHere = "AUI112";
        public const string NestedViewCycle = "AUI113";
        public const string PageWithoutTabs = "AUI114";
        public const string TextAndKeyBoth = "AUI115";
        public const string BadBindingSyntax = "AUI116";
        public const string LiteralTextInTranslatedView = "AUI117";
        public const string ItemBindingOutsideRepeat = "AUI118";
        public const string UnknownIcon = "AUI119";
        public const string RepeatMaxOutOfRange = "AUI120";

        // ---- stylesheet
        public const string StyleSyntax = "AUI200";
        public const string UnknownStyleProperty = "AUI201";
        public const string BadStyleValue = "AUI202";
        public const string UndefinedVariable = "AUI203";
        public const string UnknownState = "AUI204";
        public const string BadSelector = "AUI205";
        public const string UnusedClass = "AUI206";
        public const string UnknownClass = "AUI207";
        public const string StylesheetNotFound = "AUI208";
        public const string BadTransition = "AUI209";
        public const string NonAnimatableTransition = "AUI210";

        // ---- i18n
        public const string MissingTranslation = "AUI300";
        public const string MissingDefaultTranslation = "AUI301";
        public const string LanguageFileSyntax = "AUI302";
        public const string UnknownKey = "AUI303";
        public const string PlaceholderNotBound = "AUI304";
        public const string NoLanguages = "AUI305";
        public const string DuplicateKey = "AUI306";
        public const string PlaceholderMismatch = "AUI307";

        // ---- layout
        public const string LayoutOverflow = "AUI400";
        public const string LayoutNegativeSize = "AUI401";

        // ---- build / codegen (editor only)
        public const string GeneratedClassMissing = "AUI500";
        public const string ProgramAssetMissing = "AUI501";
        public const string HandlerMissing = "AUI502";
        public const string PrefabBuildFailed = "AUI503";
        public const string ExportMissing = "AUI504";

        // ---- fonts (editor only)
        public const string FontSetStale = "AUI600";
        public const string GlyphMissing = "AUI601";
        public const string FontSourceMissing = "AUI602";
    }
}
