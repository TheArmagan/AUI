using System.Threading;
using System.Threading.Tasks;
using Armagan.AUI.Core;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Armagan.AUI.Lsp
{
    /// <summary>
    /// "Format Document" for .aui and .auis: one whole-document edit from <see cref="AuiFormatter"/>.
    /// A document with syntax errors comes back untouched (the formatter returns null), so
    /// formatting never moves a mistake around. Indentation is the package's own (two spaces,
    /// LF), not the editor's tab settings: every file in a project should look the same.
    /// </summary>
    public sealed class FormattingHandler : DocumentFormattingHandlerBase
    {
        private readonly Workspace _ws;

        public FormattingHandler(Workspace ws)
        {
            _ws = ws;
        }

        protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(DocumentFormattingCapability capability, ClientCapabilities clientCapabilities)
        {
            return new DocumentFormattingRegistrationOptions { DocumentSelector = Sel.Both };
        }

        public override Task<TextEditContainer> Handle(DocumentFormattingParams request, CancellationToken token)
        {
            string path = Workspace.PathOf(request.TextDocument.Uri);
            string text = _ws.Read(path);
            if (text == null) return Task.FromResult(new TextEditContainer());
            string formatted = Sel.IsSheet(path) ? AuiFormatter.FormatSheet(text) : AuiFormatter.FormatMarkup(text);
            if (formatted == null) return Task.FromResult(new TextEditContainer());
            string normalized = text.Replace("\r\n", "\n");
            if (formatted == normalized) return Task.FromResult(new TextEditContainer());
            Position end = Cursor.PositionOf(text, text.Length);
            var edit = new TextEdit { Range = new Range(new Position(0, 0), end), NewText = formatted };
            return Task.FromResult(new TextEditContainer(edit));
        }
    }
}
