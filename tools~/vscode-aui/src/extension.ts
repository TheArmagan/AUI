import * as path from "path";
import * as fs from "fs";
import * as vscode from "vscode";
import { LanguageClient, LanguageClientOptions, ServerOptions, TransportKind } from "vscode-languageclient/node";

let client: LanguageClient | undefined;

export function activate(context: vscode.ExtensionContext) {
    const config = vscode.workspace.getConfiguration("aui");
    const dotnet = config.get<string>("dotnetPath") || "dotnet";
    let serverPath = config.get<string>("serverPath") || "";
    if (!serverPath) {
        const bundled = path.join(context.extensionPath, "server", "aui-lsp.dll");
        serverPath = bundled;
    }
    if (!fs.existsSync(serverPath)) {
        vscode.window.showErrorMessage("AUI: language server not found at " + serverPath + ". Run 'npm run server' in tools~/vscode-aui or set aui.serverPath.");
        return;
    }

    const isDll = serverPath.toLowerCase().endsWith(".dll");
    const serverOptions: ServerOptions = {
        run: { command: isDll ? dotnet : serverPath, args: isDll ? [serverPath] : [], transport: TransportKind.stdio },
        debug: { command: isDll ? dotnet : serverPath, args: isDll ? [serverPath] : [], transport: TransportKind.stdio },
    };
    const clientOptions: LanguageClientOptions = {
        documentSelector: [
            { scheme: "file", language: "aui" },
            { scheme: "file", language: "auis" },
        ],
        synchronize: {
            fileEvents: [
                vscode.workspace.createFileSystemWatcher("**/Lang/*.json"),
                vscode.workspace.createFileSystemWatcher("**/*.auis"),
                vscode.workspace.createFileSystemWatcher("**/*.aui"),
            ],
        },
    };
    client = new LanguageClient("aui", "AUI Language Server", serverOptions, clientOptions);
    client.start();
    context.subscriptions.push({ dispose: () => client?.stop() });
}

export function deactivate(): Thenable<void> | undefined {
    return client?.stop();
}
