// Drives the language server over stdio without VS Code: initialize, open a view with a typo,
// expect diagnostics, ask for completion and hover. `node smoke.js <workspaceRoot> <viewPath>`.
const { spawn } = require("child_process");
const path = require("path");
const fs = require("fs");

const root = path.resolve(process.argv[2] || ".");
const view = path.resolve(process.argv[3] || "");
const server = path.join(__dirname, "server", "aui-lsp.dll");
const proc = spawn("dotnet", [server], { stdio: ["pipe", "pipe", "inherit"] });

let buffer = Buffer.alloc(0);
let nextId = 1;
const pending = new Map();
const notifications = [];

function send(method, params, isNotification) {
    const msg = { jsonrpc: "2.0", method, params };
    if (!isNotification) { msg.id = nextId++; }
    const json = JSON.stringify(msg);
    proc.stdin.write("Content-Length: " + Buffer.byteLength(json) + "\r\n\r\n" + json);
    if (isNotification) return Promise.resolve();
    return new Promise((resolve) => pending.set(msg.id, resolve));
}

proc.stdout.on("data", (chunk) => {
    buffer = Buffer.concat([buffer, chunk]);
    for (;;) {
        const headerEnd = buffer.indexOf("\r\n\r\n");
        if (headerEnd < 0) return;
        const header = buffer.slice(0, headerEnd).toString();
        const len = parseInt(/Content-Length: (\d+)/.exec(header)[1], 10);
        if (buffer.length < headerEnd + 4 + len) return;
        const body = JSON.parse(buffer.slice(headerEnd + 4, headerEnd + 4 + len).toString());
        buffer = buffer.slice(headerEnd + 4 + len);
        if (body.id !== undefined && pending.has(body.id)) { pending.get(body.id)(body); pending.delete(body.id); }
        else if (body.method) notifications.push(body);
    }
});

function uri(p) { return "file:///" + p.replace(/\\/g, "/").replace(/^\//, ""); }
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

(async () => {
    await send("initialize", { processId: process.pid, rootUri: uri(root), capabilities: {} });
    await send("initialized", {}, true);
    const text = fs.readFileSync(view, "utf8").replace("<text ", "<txet ");
    await send("textDocument/didOpen", { textDocument: { uri: uri(view), languageId: "aui", version: 1, text } }, true);
    await sleep(1500);
    const diags = notifications.filter((n) => n.method === "textDocument/publishDiagnostics" && n.params.uri.toLowerCase().endsWith(path.basename(view).toLowerCase()));
    const last = diags[diags.length - 1];
    console.log("diagnostics:", last ? last.params.diagnostics.map((d) => d.code + " " + d.message).join(" | ") : "(none)");
    const lines = text.split("\n");
    const keyLine = lines.findIndex((l) => l.includes("key=\""));
    const keyCol = lines[keyLine].indexOf("key=\"") + 5;
    const comp = await send("textDocument/completion", { textDocument: { uri: uri(view) }, position: { line: keyLine, character: keyCol } });
    console.log("completion at key=\"|:", (comp.result.items || comp.result).slice(0, 5).map((i) => i.label).join(", "), "...");
    const hover = await send("textDocument/hover", { textDocument: { uri: uri(view) }, position: { line: keyLine, character: keyCol + 2 } });
    console.log("hover:", hover.result ? hover.result.contents.value.split("\n")[0] : "(none)");
    const def = await send("textDocument/definition", { textDocument: { uri: uri(view) }, position: { line: keyLine, character: keyCol + 2 } });
    console.log("definition:", Array.isArray(def.result) ? def.result.map((l) => path.basename(l.uri) + ":" + (l.range.start.line + 1)).join(", ") : "(none)");
    await send("shutdown", {});
    await send("exit", {}, true);
    proc.kill();
    process.exit(0);
})().catch((e) => { console.error(e); proc.kill(); process.exit(1); });
