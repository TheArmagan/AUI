using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
                .WithServices(services => services.AddSingleton<Workspace>())
                .WithHandler<TextDocumentHandler>()
                .WithHandler<CompletionHandler>()
                .WithHandler<HoverHandler>()
                .WithHandler<DefinitionHandler>()
                .WithHandler<SymbolHandler>()
                .WithHandler<WatchedFilesHandler>()
                .OnInitialize((s, request, token) =>
                {
                    var ws = s.Services.GetRequiredService<Workspace>();
                    string root = request.RootUri != null ? request.RootUri.GetFileSystemPath() : request.RootPath;
                    ws.Initialize(root);
                    return Task.CompletedTask;
                })
                .OnStarted((s, token) =>
                {
                    var ws = s.Services.GetRequiredService<Workspace>();
                    ws.Attach(s);
                    ws.RecompileAll();
                    return Task.CompletedTask;
                }));
            await server.WaitForExit;
        }
    }
}
