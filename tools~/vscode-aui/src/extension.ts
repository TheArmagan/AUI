import * as path from "path";
import * as fs from "fs";
import * as vscode from "vscode";
import { LanguageClient, LanguageClientOptions, ServerOptions, TransportKind } from "vscode-languageclient/node";

let client: LanguageClient | undefined;
let output: vscode.OutputChannel;
const GENERATED_SCHEME = "aui-generated";

export async function activate(context: vscode.ExtensionContext) {
    output = vscode.window.createOutputChannel("AUI Language Server");
    context.subscriptions.push(output);

    const generated = new GeneratedProvider();
    context.subscriptions.push(vscode.workspace.registerTextDocumentContentProvider(GENERATED_SCHEME, generated));
    context.subscriptions.push(vscode.commands.registerCommand("aui.restartServer", async () => {
        await stopClient();
        await startClient(context);
    }));
    context.subscriptions.push(vscode.commands.registerCommand("aui.showGenerated", showGenerated));
    context.subscriptions.push(vscode.commands.registerCommand("aui.reveal", reveal));
    context.subscriptions.push(vscode.workspace.onDidChangeTextDocument((e) => {
        autoCloseTag(e);
        generated.documentChanged(e.document);
    }));
    context.subscriptions.push(vscode.workspace.onDidChangeConfiguration(async (e) => {
        if (e.affectsConfiguration("aui.serverPath") || e.affectsConfiguration("aui.dotnetPath")) {
            const pick = await vscode.window.showInformationMessage("AUI: the server settings changed. Restart the language server?", "Restart");
            if (pick === "Restart") await vscode.commands.executeCommand("aui.restartServer");
        }
    }));
    await startClient(context);
}

export function deactivate(): Thenable<void> | undefined {
    return stopClient();
}

// ---------------------------------------------------------------- client lifecycle

async function startClient(context: vscode.ExtensionContext) {
    const config = vscode.workspace.getConfiguration("aui");
    const dotnet = config.get<string>("dotnetPath") || "dotnet";
    let serverPath = config.get<string>("serverPath") || "";
    if (!serverPath) serverPath = path.join(context.extensionPath, "server", "aui-lsp.dll");
    if (!fs.existsSync(serverPath)) {
        const pick = await vscode.window.showErrorMessage(
            "AUI: language server not found at " + serverPath + ". Run 'npm run server' in tools~/vscode-aui or set aui.serverPath.",
            "Open Settings");
        if (pick === "Open Settings") vscode.commands.executeCommand("workbench.action.openSettings", "aui.serverPath");
        return;
    }

    const isDll = serverPath.toLowerCase().endsWith(".dll");
    const command = isDll ? dotnet : serverPath;
    const args = isDll ? [serverPath] : [];
    const serverOptions: ServerOptions = {
        run: { command, args, transport: TransportKind.stdio },
        debug: { command, args, transport: TransportKind.stdio },
    };
    const watchers = [
        vscode.workspace.createFileSystemWatcher("**/{lang,Lang}/*.json"),
        vscode.workspace.createFileSystemWatcher("**/*.auis"),
        vscode.workspace.createFileSystemWatcher("**/*.aui"),
        vscode.workspace.createFileSystemWatcher("**/*.svg"),
    ];
    context.subscriptions.push(...watchers);
    const clientOptions: LanguageClientOptions = {
        documentSelector: [
            { scheme: "file", language: "aui" },
            { scheme: "file", language: "auis" },
            // Language files: live diagnostics while translating, hover and references on keys.
            { scheme: "file", pattern: "**/{lang,Lang}/*.json" },
        ],
        synchronize: { fileEvents: watchers },
        outputChannel: output,
        middleware: {
            provideInlayHints: (document, range, token, next) => {
                if (!vscode.workspace.getConfiguration("aui").get<boolean>("inlayHints.translations", true)) return [];
                return next(document, range, token);
            },
        },
    };
    client = new LanguageClient("aui", "AUI Language Server", serverOptions, clientOptions);
    try {
        await client.start();
    } catch (e) {
        vscode.window.showErrorMessage("AUI: the language server failed to start: " + String(e));
    }
}

async function stopClient() {
    const c = client;
    client = undefined;
    if (c) await c.stop();
}

// ---------------------------------------------------------------- auto-closing tags

const TAG = /<(\/?)([A-Za-z_][\w.-]*)(?:"[^"]*"|'[^']*'|[^'">])*?(\/?)>|<!--[\s\S]*?-->/g;

/** The innermost element still open at the end of the text. */
function unclosedTag(text: string): string | undefined {
    const stack: string[] = [];
    for (const m of text.matchAll(TAG)) {
        if (m[0].startsWith("<!--")) continue;
        const name = m[2];
        if (m[1] === "/") {
            const i = stack.lastIndexOf(name);
            if (i >= 0) stack.splice(i);
        } else if (m[3] !== "/" && name !== "link") stack.push(name);
    }
    return stack.length ? stack[stack.length - 1] : undefined;
}

function insideComment(before: string): boolean {
    const open = before.lastIndexOf("<!--");
    return open >= 0 && before.lastIndexOf("-->") < open;
}

/**
 * Typing ">" after "<panel class=..." inserts "</panel>" after the caret; typing "/" after "<"
 * completes the innermost open tag. Same behaviour as the HTML extension, decided from the text.
 */
function autoCloseTag(e: vscode.TextDocumentChangeEvent) {
    if (e.document.languageId !== "aui" || e.contentChanges.length !== 1) return;
    if (!vscode.workspace.getConfiguration("aui").get<boolean>("autoClosingTags", true)) return;
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document !== e.document) return;
    const change = e.contentChanges[0];
    if (change.text !== ">" && change.text !== "/") return;
    const version = e.document.version;
    const pos = change.range.start.translate(0, 1);
    setTimeout(() => {
        if (editor.document.version !== version) return;
        const doc = editor.document;
        const before = doc.getText(new vscode.Range(new vscode.Position(0, 0), pos));
        if (insideComment(before)) return;
        if (change.text === ">") {
            if (before.endsWith("/>")) return;
            const m = /<([A-Za-z_][\w.-]*)(?:(?:[^'"/>]|"[^"]*"|'[^']*')*?)>$/.exec(before);
            if (!m || m[1] === "link") return;
            // <style> is raw content: the same rule applies, the closing tag is what ends it.
            const closing = "</" + m[1] + ">";
            const after = doc.getText(doc.validateRange(new vscode.Range(pos, pos.translate(0, closing.length))));
            if (after.startsWith(closing)) return;
            editor.insertSnippet(new vscode.SnippetString("$0" + closing), pos, { undoStopBefore: false, undoStopAfter: false });
        } else {
            if (!before.endsWith("</")) return;
            const tag = unclosedTag(before.slice(0, -2));
            if (!tag) return;
            const after = doc.getText(doc.validateRange(new vscode.Range(pos, pos.translate(0, tag.length + 1))));
            if (after.startsWith(tag)) return;
            editor.insertSnippet(new vscode.SnippetString(tag + ">$0"), pos, { undoStopBefore: false, undoStopAfter: false });
        }
    }, 40);
}

// ---------------------------------------------------------------- generated code preview

/** aui-generated:/Name.g.cs?<source uri> shows the C# the server generates for a view, refreshed as you type. */
class GeneratedProvider implements vscode.TextDocumentContentProvider {
    private readonly emitter = new vscode.EventEmitter<vscode.Uri>();
    readonly onDidChange = this.emitter.event;
    private readonly timers = new Map<string, NodeJS.Timeout>();

    static uriFor(source: vscode.Uri): vscode.Uri {
        const stem = path.basename(source.fsPath, ".aui");
        return vscode.Uri.from({ scheme: GENERATED_SCHEME, path: "/" + stem + ".g.cs", query: source.toString() });
    }

    async provideTextDocumentContent(uri: vscode.Uri): Promise<string> {
        if (!client) return "// AUI language server is not running.";
        try {
            const result = await client.sendRequest<{ text: string; hasErrors: boolean }>("aui/generated", { uri: uri.query });
            return result.text;
        } catch (e) {
            return "// " + String(e);
        }
    }

    documentChanged(doc: vscode.TextDocument) {
        if (doc.languageId !== "aui") return;
        const target = GeneratedProvider.uriFor(doc.uri);
        const key = target.toString();
        if (!vscode.workspace.textDocuments.some((d) => d.uri.toString() === key)) return;
        const old = this.timers.get(key);
        if (old) clearTimeout(old);
        this.timers.set(key, setTimeout(() => { this.timers.delete(key); this.emitter.fire(target); }, 400));
    }
}

async function showGenerated() {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document.languageId !== "aui") {
        vscode.window.showInformationMessage("AUI: open a .aui view first.");
        return;
    }
    const uri = GeneratedProvider.uriFor(editor.document.uri);
    const doc = await vscode.workspace.openTextDocument(uri);
    await vscode.languages.setTextDocumentLanguage(doc, "csharp");
    await vscode.window.showTextDocument(doc, { viewColumn: vscode.ViewColumn.Beside, preview: true, preserveFocus: true });
}

// ---------------------------------------------------------------- reveal (after a quick fix created something elsewhere)

async function reveal(uriString: string, line: number, character: number) {
    const uri = vscode.Uri.parse(uriString);
    const doc = await vscode.workspace.openTextDocument(uri);
    const editor = await vscode.window.showTextDocument(doc, { preview: false });
    const pos = doc.validatePosition(new vscode.Position(line, character));
    editor.selection = new vscode.Selection(pos, pos);
    editor.revealRange(new vscode.Range(pos, pos), vscode.TextEditorRevealType.InCenterIfOutsideViewport);
}
