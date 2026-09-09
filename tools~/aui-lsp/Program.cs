using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;

namespace Armagan.AUI.Lsp
{
    /// <summary>
    /// The AUI language server: stdio JSON-RPC, the same Core compiler the Unity importer runs,
    /// so a squiggle in VS Code is the line the Unity console would print.
    /// </summary>
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var server = await LanguageServer.From(options => options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                // Handler exceptions and warnings go to the client (VS Code's "AUI Language Server" output channel).
                .ConfigureLogging(x => x.AddLanguageProtocolLogging().SetMinimumLevel(LogLevel.Warning))
                .WithServices(services => services.AddSingleton<Workspace>())
                .WithHandler<TextDocumentHandler>()
                .WithHandler<CompletionHandler>()
                .WithHandler<HoverHandler>()
                .WithHandler<DefinitionHandler>()
                .WithHandler<ReferencesHandler>()
                .WithHandler<PrepareRenameHandler>()
                .WithHandler<RenameHandler>()
                .WithHandler<CodeActionHandler>()
                .WithHandler<DocumentColorHandler>()
                .WithHandler<ColorPresentationHandler>()
                .WithHandler<InlayHintsHandler>()
                .WithHandler<DocumentLinkHandler>()
                .WithHandler<SymbolHandler>()
                .WithHandler<WorkspaceSymbolsHandler>()
                .WithHandler<WatchedFilesHandler>()
                .WithHandler<SemanticTokensHandler>()
                .WithHandler<FormattingHandler>()
                .OnRequest<GeneratedParams, GeneratedResult>("aui/generated", p => Generated.Handle(Ws, p))
                .OnInitialize((s, request, token) =>
                {
                    var ws = s.Services.GetRequiredService<Workspace>();
                    Ws = ws;
                    string root = null;
                    if (request.RootUri != null) root = request.RootUri.GetFileSystemPath();
                    else if (request.WorkspaceFolders != null && request.WorkspaceFolders.Any()) root = request.WorkspaceFolders.First().Uri.GetFileSystemPath();
                    else root = request.RootPath;
                    ws.Initialize(root);
                    return Task.CompletedTask;
                })
                .OnStarted((s, token) =>
                {
                    var ws = s.Services.GetRequiredService<Workspace>();
                    ws.Attach(s);
                    try { ws.RecompileAll(); }
                    catch (Exception e) { Console.Error.WriteLine("[aui-lsp] initial compile failed: " + e); }
                    return Task.CompletedTask;
                }));
            await server.WaitForExit;
        }

        private static Workspace Ws;
    }
}
