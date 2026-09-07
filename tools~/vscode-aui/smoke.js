// Drives the language server over stdio without VS Code and checks every capability:
// diagnostics, completion (with replace ranges), hover, definition, references, rename,
// code actions, colours, inlay hints, document links, symbols, the generated code request.
//   node smoke.js [workspaceRoot] [viewPath]
// Defaults to the package repository and its ControlsDemo sample. Exit code 1 on any failure.
const { spawn } = require("child_process");
const path = require("path");
const fs = require("fs");

const root = path.resolve(process.argv[2] || path.join(__dirname, "..", ".."));
const view = path.resolve(process.argv[3] || path.join(root, "Samples~", "Demo", "Views", "Gallery", "ControlsDemo.aui"));
const dir = path.dirname(view);
const server = path.join(__dirname, "server", "aui-lsp.dll");
const proc = spawn("dotnet", [server], { stdio: ["pipe", "pipe", "inherit"] });

let buffer = Buffer.alloc(0);
let nextId = 1;
const pending = new Map();
const notifications = [];
let failures = 0;

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
        else if (body.method === "client/registerCapability") { proc.stdin.write(frame({ jsonrpc: "2.0", id: body.id, result: null })); }
        else if (body.method === "window/logMessage") { if (body.params.type <= 2) console.error("server log: " + body.params.message.split("\n").slice(0, 3).join(" / ")); }
        else if (body.method) notifications.push(body);
    }
});

function frame(obj) { const json = JSON.stringify(obj); return "Content-Length: " + Buffer.byteLength(json) + "\r\n\r\n" + json; }
function uri(p) { return "file:///" + p.replace(/\\/g, "/").replace(/^\//, ""); }
// The same file can arrive as file:///c%3A/... or file:///c:/...; compare decoded and case-folded.
function sameUri(a, b) { return decodeURIComponent(a).toLowerCase() === decodeURIComponent(b).toLowerCase(); }
function fileOf(u) { return decodeURIComponent(u.replace(/^file:\/\/\//, "")).replace(/\//g, path.sep); }
const locs = (r) => (Array.isArray(r.result) ? r.result : r.result ? [r.result] : []);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
async function waitFor(pred, ms) {
    const until = Date.now() + (ms || 4000);
    while (Date.now() < until) { const v = pred(); if (v) return v; await sleep(50); }
    return null;
}
function diagsFor(file) {
    const list = notifications.filter((n) => n.method === "textDocument/publishDiagnostics" && sameUri(n.params.uri, uri(file)));
    return list.length ? list[list.length - 1].params.diagnostics : null;
}
function check(name, ok, detail) {
    console.log((ok ? "PASS " : "FAIL ") + name + (detail !== undefined ? "  " + String(detail).slice(0, 160) : ""));
    if (!ok) failures++;
}
function pos(text, needle, delta) {
    const i = text.indexOf(needle);
    if (i < 0) throw new Error("not in text: " + needle);
    const at = i + (delta || 0);
    const before = text.slice(0, at);
    const line = before.split("\n").length - 1;
    const character = at - (before.lastIndexOf("\n") + 1);
    return { line, character };
}
const td = (file) => ({ textDocument: { uri: uri(file) } });
const items = (comp) => (comp.result && (comp.result.items || comp.result)) || [];

(async () => {
    await send("initialize", { processId: process.pid, rootUri: uri(root), capabilities: { textDocument: { rename: { prepareSupport: true }, semanticTokens: { requests: { full: true, range: true }, tokenTypes: [], tokenModifiers: [], formats: ["relative"] } }, workspace: { workspaceEdit: { documentChanges: true, resourceOperations: ["create"] } } } });
    await send("initialized", {}, true);

    // ---- 1. a typo in the view is an AUI100 with a quick fix
    const original = fs.readFileSync(view, "utf8");
    const text = original.replace("<text ", "<txet ");
    await send("textDocument/didOpen", { textDocument: { uri: uri(view), languageId: "aui", version: 1, text } }, true);
    let diags = await waitFor(() => { const d = diagsFor(view); return d && d.some((x) => x.code === "AUI100") ? d : null; });
    check("diagnostics: AUI100 for <txet>", !!diags, diags && diags.map((d) => d.code + " " + d.message).join(" | "));
    if (diags) {
        const d = diags.find((x) => x.code === "AUI100");
        const ca = await send("textDocument/codeAction", { ...td(view), range: d.range, context: { diagnostics: [d] } });
        const fix = (ca.result || []).find((a) => a.title === "Change to 'text'");
        const edit = fix && Object.values(fix.edit.changes)[0][0];
        check("code action: Change to 'text' replaces txet", !!edit && edit.newText === "text" && text.split("\n")[edit.range.start.line].slice(edit.range.start.character, edit.range.end.character) === "txet", fix && fix.title);
    }

    // ---- 2. completion with replace ranges
    const keyPos = pos(text, 'key="controls.volume"', 5);
    let comp = await send("textDocument/completion", { ...td(view), position: keyPos });
    let list = items(comp);
    check("completion: keys at key=\"|", list.some((i) => i.label === "controls.volume") && list.every((i) => i.textEdit), list.slice(0, 4).map((i) => i.label).join(", "));

    const classPos = pos(text, 'class="setting"', 7);
    comp = await send("textDocument/completion", { ...td(view), position: classPos });
    list = items(comp);
    const cls = list.find((i) => i.label === "setting");
    check("completion: classes with sheet name", !!cls && /auis|style/.test(cls.detail || ""), cls && cls.detail);

    const bindPos = pos(text, 'bind="{volume}"', 7);
    comp = await send("textDocument/completion", { ...td(view), position: bindPos });
    list = items(comp);
    const b = list.find((i) => i.label === "{volume}");
    check("completion: {binding} replaces from the brace", !!b && b.textEdit.range.start.character === bindPos.character - 1, b && JSON.stringify(b.textEdit.range));

    const onPos = pos(text, 'onchange="VolumeChanged"', 10);
    comp = await send("textDocument/completion", { ...td(view), position: onPos });
    list = items(comp);
    check("completion: handlers from the view and its C#", list.some((i) => i.label === "VolumeChanged"), list.map((i) => i.label).slice(0, 6).join(", "));

    const attrPos = pos(text, 'bind="{volume}"', 0);
    comp = await send("textDocument/completion", { ...td(view), position: attrPos });
    list = items(comp);
    check("completion: attribute names skip ones already on the tag", list.some((i) => i.label === "step") && !list.some((i) => i.label === "id"), list.map((i) => i.label).join(", "));

    const closePos = pos(text, "</view>", 2);
    comp = await send("textDocument/completion", { ...td(view), position: closePos });
    list = items(comp);
    check("completion: </ offers the open tag", list.length >= 1 && list[0].label === "view", list.map((i) => i.label).join(", "));

    // ---- 3. a scratch stylesheet: selector states, properties, values, colours, symbols
    const sheet = path.join(dir, "Scratch.auis");
    const sheetText = ".card {\n  background: sprite(Rounded8) #291E17;\n  color: var(--text);\n  pading: 4;\n  gap: 2\n  opacity: 0.5;\n}\nbutton:ho";
    await send("textDocument/didOpen", { textDocument: { uri: uri(sheet), languageId: "auis", version: 1, text: sheetText } }, true);
    diags = await waitFor(() => { const d = diagsFor(sheet); return d && d.length ? d : null; });
    check("diagnostics: a sheet no view links still reports AUI201", !!diags && diags.some((d) => d.code === "AUI201"), diags && diags.map((d) => d.code + " " + d.message).join(" | "));
    if (diags) {
        const d = diags.find((x) => x.code === "AUI201");
        const ca = await send("textDocument/codeAction", { ...td(sheet), range: d.range, context: { diagnostics: [d] } });
        check("code action: Change to 'padding'", (ca.result || []).some((a) => a.title === "Change to 'padding'"), (ca.result || []).map((a) => a.title).join(" | "));
        const semi = diags.find((x) => x.code === "AUI200" && /Missing ';'/.test(x.message));
        const ca2 = semi && await send("textDocument/codeAction", { ...td(sheet), range: semi.range, context: { diagnostics: [semi] } });
        check("code action: Insert ';'", !!ca2 && (ca2.result || []).some((a) => a.title === "Insert ';'"), ca2 && (ca2.result || []).map((a) => a.title).join(" | "));
    }
    comp = await send("textDocument/completion", { ...td(sheet), position: pos(sheetText, "button:ho", 9) });
    list = items(comp);
    const hov = list.find((i) => i.label === ":hover");
    check("completion: :state replaces from the colon", !!hov && hov.textEdit.range.start.character === 6, hov && JSON.stringify(hov.textEdit.range));
    comp = await send("textDocument/completion", { ...td(sheet), position: pos(sheetText, "var(--text)", 6) });
    list = items(comp);
    const v = list.find((i) => i.label === "var(--text)");
    check("completion: var(--x) replaces the whole var( token", !!v && v.textEdit.range.start.character === 9, v && JSON.stringify(v.textEdit.range));
    const colors = await send("textDocument/documentColor", td(sheet));
    check("colours: one swatch for #291E17", (colors.result || []).length === 1, JSON.stringify((colors.result || [])[0]));
    const pres = await send("textDocument/colorPresentation", { ...td(sheet), color: { red: 1, green: 0, blue: 0, alpha: 1 }, range: (colors.result || [{}])[0].range });
    check("colours: presentations hex and rgba", (pres.result || []).length === 2 && pres.result[0].label === "#FF0000", (pres.result || []).map((p) => p.label).join(", "));
    const ssym = await send("textDocument/documentSymbol", td(sheet));
    check("symbols: sheet rules", (ssym.result || []).some((s) => s.name === ".card"), (ssym.result || []).map((s) => s.name).join(", "));
    // semantic tokens: the sheet's first token is the .card class; the view's <style> body and style="" values get tokens too
    const sem = await send("textDocument/semanticTokens/full", td(sheet));
    const semData = (sem.result && sem.result.data) || [];
    check("semantic tokens: sheet starts with a class token", semData.length >= 5 && semData[0] === 0 && semData[1] === 0 && semData[2] === 5, semData.slice(0, 10).join(","));
    const vsem = await send("textDocument/semanticTokens/full", td(view));
    check("semantic tokens: view style values", !!vsem.result && vsem.result.data.length > 0, vsem.result && vsem.result.data.length + " ints");
    const vh = await send("textDocument/hover", { ...td(sheet), position: pos(sheetText, "var(--text)", 6) });
    check("hover: variable value", !!vh.result && /--text/.test(vh.result.contents.value), vh.result && vh.result.contents.value.split("\n")[0]);
    await send("textDocument/didClose", { textDocument: { uri: uri(sheet) } }, true);
    const cleared = await waitFor(() => { const d = diagsFor(sheet); return d && d.length === 0 ? true : null; });
    check("diagnostics: closing a scratch sheet clears them", !!cleared);

    // ---- 4. hover, definition, references, rename on the view
    let h = await send("textDocument/hover", { ...td(view), position: pos(text, 'bind="{volume}"', 8) });
    check("hover: binding type", !!h.result && /float/.test(h.result.contents.value), h.result && h.result.contents.value.split("\n")[0]);
    h = await send("textDocument/hover", { ...td(view), position: pos(text, 'onchange="VolumeChanged"', 12) });
    check("hover: handler signature", !!h.result && /VolumeChanged\(float value\)/.test(h.result.contents.value), h.result && h.result.contents.value.split("\n")[1]);
    h = await send("textDocument/hover", { ...td(view), position: keyPos });
    check("hover: key with every language", !!h.result && /`en`/.test(h.result.contents.value), h.result && h.result.contents.value.split("\n")[0]);

    const cs = path.join(dir, path.basename(view, ".aui") + ".cs");
    if (fs.existsSync(cs)) {
        const def = await send("textDocument/definition", { ...td(view), position: pos(text, 'onchange="VolumeChanged"', 12) });
        check("definition: handler to its C# method", locs(def).some((l) => l.uri.toLowerCase().endsWith(".cs")), JSON.stringify(def.result));
    } else console.log("skip definition: handler (no " + path.basename(cs) + ")");
    const keyDef = await send("textDocument/definition", { ...td(view), position: keyPos });
    const projectLang = dir.split(path.sep).slice(0, -2).join(path.sep).toLowerCase();
    check("definition: key to every language file, in the project's folder", locs(keyDef).length >= 2 && locs(keyDef).every((l) => fileOf(l.uri).toLowerCase().startsWith(projectLang)), locs(keyDef).map((l) => fileOf(l.uri) + ":" + (l.range.start.line + 1)).join(", "));
    let def = await send("textDocument/definition", { ...td(view), position: classPos });
    check("definition: class to its rule", locs(def).length >= 1 && /\.auis$/i.test(locs(def)[0].uri), JSON.stringify(def.result));

    const refs = await send("textDocument/references", { ...td(view), position: classPos, context: { includeDeclaration: true } });
    check("references: class across views and sheets", locs(refs).length >= 2 && locs(refs).some((l) => /\.auis$/i.test(l.uri)), locs(refs).length + " locations");

    const prep = await send("textDocument/prepareRename", { ...td(view), position: pos(text, 'bind="{volume}"', 8) });
    check("rename: prepare gives the binding name", !!prep.result && prep.result.placeholder === "volume", JSON.stringify(prep.result));
    const ren = await send("textDocument/rename", { ...td(view), position: pos(text, 'bind="{volume}"', 8), newName: "loudness" });
    const renEdits = ren.result && ren.result.changes && Object.values(ren.result.changes)[0];
    check("rename: binding edits inside the view", !!renEdits && renEdits.length >= 1 && renEdits[0].newText === "loudness", renEdits && renEdits.length + " edits");
    const badRen = await send("textDocument/rename", { ...td(view), position: keyPos, newName: "other.volume" });
    const shown = await waitFor(() => notifications.find((n) => n.method === "window/showMessage" && /nesting/.test(n.params.message)), 1500);
    check("rename: a key keeps its nesting (message, no edits)", !badRen.error && Object.keys((badRen.result && badRen.result.changes) || {}).length === 0 && !!shown, shown && shown.params.message);
    const keyRen = await send("textDocument/rename", { ...td(view), position: keyPos, newName: "controls.loudness" });
    const keyFiles = keyRen.result && keyRen.result.changes ? Object.keys(keyRen.result.changes) : [];
    check("rename: a key edits the view and every language file", keyFiles.length >= 3 && keyFiles.some((u) => /\.json$/i.test(u)), keyFiles.map((u) => path.basename(u)).join(", "));

    // ---- 5. inlay hints, links, generated code, workspace symbols
    const hints = await send("textDocument/inlayHint", { ...td(view), range: { start: { line: 0, character: 0 }, end: { line: 999, character: 0 } } });
    check("inlay hints: default-language text after key=", Array.isArray(hints.result) && hints.result.length >= 1, Array.isArray(hints.result) && hints.result.slice(0, 3).map((x) => x.label).join(" | "));
    const scratch = path.join(dir, "Scratch.aui");
    const scratchText = '<view name="Scratch" width="100" height="100">\n  <link href="' + path.basename(view, ".aui") + '.auis"/>\n  <text key="controls.nope"/>\n  <Missing/>\n</view>\n';
    await send("textDocument/didOpen", { textDocument: { uri: uri(scratch), languageId: "aui", version: 1, text: scratchText } }, true);
    diags = await waitFor(() => { const d = diagsFor(scratch); return d && d.some((x) => x.code === "AUI303") ? d : null; }) || diagsFor(scratch);
    check("diagnostics: unknown key and missing nested view", !!diags && diags.some((d) => d.code === "AUI107") && diags.some((d) => d.code === "AUI303"), diags && diags.map((d) => d.code + " " + d.message).join(" | "));
    if (diags && diags.some((d) => d.code === "AUI303") && diags.some((d) => d.code === "AUI107")) {
        const d = diags.find((x) => x.code === "AUI303");
        const ca = await send("textDocument/codeAction", { ...td(scratch), range: d.range, context: { diagnostics: [d] } });
        const create = (ca.result || []).find((a) => /^Create 'controls.nope' in /.test(a.title));
        const edit = create && Object.values(create.edit.changes)[0][0];
        check("code action: create the key in the default language", !!edit && /"nope": ""/.test(edit.newText), create && create.title + " -> " + JSON.stringify(edit && edit.newText));
        check("code action: reveals the new value afterwards", !!create && create.command && create.command.command === "aui.reveal", create && JSON.stringify(create.command && create.command.arguments));
        const nv = diags.find((x) => x.code === "AUI107");
        const ca2 = await send("textDocument/codeAction", { ...td(scratch), range: nv.range, context: { diagnostics: [nv] } });
        const mk = (ca2.result || []).find((a) => a.title === "Create Missing.aui next to this view");
        check("code action: create the missing nested view file", !!mk && mk.edit.documentChanges && mk.edit.documentChanges.length === 2, mk && mk.title);
    }
    const links = await send("textDocument/documentLink", td(scratch));
    check("document links: <link href> resolves", Array.isArray(links.result) && links.result.length === 1, JSON.stringify(links.result));
    await send("textDocument/didClose", { textDocument: { uri: uri(scratch) } }, true);

    const gen = await send("aui/generated", { uri: uri(view) });
    check("generated code request", !!gen.result && /class ControlsDemo/.test(gen.result.text), gen.result && gen.result.text.split("\n").find((l) => /class /.test(l)));
    const wsym = await send("workspace/symbol", { query: "controls" });
    check("workspace symbols: keys and views", Array.isArray(wsym.result) && wsym.result.some((s) => s.name === "controls.volume") && wsym.result.some((s) => s.name === "ControlsDemo"), Array.isArray(wsym.result) && wsym.result.length + " symbols");
    const dsym = await send("textDocument/documentSymbol", td(view));
    check("symbols: view outline with details", Array.isArray(dsym.result) && dsym.result[0].children.some((c) => c.detail), Array.isArray(dsym.result) && dsym.result[0].children.map((c) => c.name + (c.detail ? " (" + c.detail + ")" : "")).slice(0, 3).join(", "));

    // ---- 6. a language file edited live: a syntax error appears on the JSON, then clears
    const langDef = locs(keyDef).map((l) => fileOf(l.uri)).find((p) => /en\.json$/i.test(p));
    if (langDef && fs.existsSync(langDef)) {
        const jsonText = fs.readFileSync(langDef, "utf8");
        await send("textDocument/didOpen", { textDocument: { uri: uri(langDef), languageId: "json", version: 1, text: jsonText + "\n}" } }, true);
        const bad = await waitFor(() => { const d = diagsFor(langDef); return d && d.some((x) => x.code === "AUI302") ? d : null; });
        check("diagnostics: live edit of en.json reports AUI302 on the JSON", !!bad, bad && bad.map((d) => d.message).join(" | "));
        await send("textDocument/didChange", { textDocument: { uri: uri(langDef), version: 2 }, contentChanges: [{ text: jsonText }] }, true);
        const good = await waitFor(() => { const d = diagsFor(langDef); return d && !d.some((x) => x.code === "AUI302") ? true : null; });
        check("diagnostics: the JSON error clears after the fix", !!good);
        const jh = await send("textDocument/hover", { ...td(langDef), position: pos(jsonText, '"volume"', 2) });
        check("hover: a key from inside the JSON", !!jh.result && /volume/.test(jh.result.contents.value), jh.result && jh.result.contents.value.split("\n")[0]);
        const jr = await send("textDocument/references", { ...td(langDef), position: pos(jsonText, '"volume"', 2), context: { includeDeclaration: true } });
        check("references: from a JSON key to the views", locs(jr).some((l) => /\.aui$/i.test(l.uri)), locs(jr).length + " locations");
        await send("textDocument/didClose", { textDocument: { uri: uri(langDef) } }, true);
    } else console.log("skip live JSON edit (no en.json found)");

    await send("shutdown", {});
    await send("exit", {}, true);
    proc.kill();
    console.log(failures === 0 ? "all checks passed" : failures + " check(s) failed");
    process.exit(failures === 0 ? 0 : 1);
})().catch((e) => { console.error(e); proc.kill(); process.exit(1); });
