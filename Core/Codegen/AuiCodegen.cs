using System.Collections.Generic;
using System.Text;

namespace Armagan.AUI.Core
{
    public sealed class AuiCodegenContext
    {
        /// <summary>Hash of the compiled IR, written into the header so the writer can skip unchanged output.</summary>
        public string Hash = "";
        /// <summary>Namespace for the generated class, or null for the global namespace.</summary>
        public string Namespace;
        /// <summary>Analyzed model of a nested view by name, for typed child properties; null = unknown.</summary>
        public System.Func<string, AuiViewInfo> ChildInfo;
    }

    /// <summary>
    /// Emits Foo.g.cs: the generated half of a view. It contains only what differs per view;
    /// every mechanism lives in Runtime/AuiView.cs. Deterministic: the same model gives the
    /// same text, byte for byte, so the source writer can compare and skip.
    ///
    /// Contract with the author's Foo.cs:
    ///   - every handler named in the markup must exist with the signature the analyzer derived
    ///     (a missing one is a plain C# error naming the method);
    ///   - bindings are properties (PlayerName = ...) and repeat rows are SetRowsName(row, v);
    ///   - string values are escaped once when set and stored escaped.
    /// </summary>
    public static class AuiCodegen
    {
        private static readonly HashSet<string> Reserved = new HashSet<string>
        {
            "Lang", "InputPanel", "Content", "ContentGroup", "ContentRect", "ParentView", "Show", "Hide", "Toggle",
            "IsVisible", "Escape", "Emit", "Start", "Update", "Name", "Enabled", "GameObject", "Transform",
        };

        public static string Emit(AuiViewInfo info, AuiIndexMap map, AuiCodegenContext ctx, AuiDiagnosticBag d)
        {
            var g = new Gen(info, map, ctx ?? new AuiCodegenContext(), d);
            return g.Run();
        }

        private sealed class Gen
        {
            private readonly AuiViewInfo _info;
            private readonly AuiIndexMap _map;
            private readonly AuiCodegenContext _ctx;
            private readonly AuiDiagnosticBag _d;
            private readonly StringBuilder _sb = new StringBuilder();
            private int _indent;

            // text nodes that need a render method: elem (row 0) -> node
            private readonly List<AuiNode> _renderTexts = new List<AuiNode>();
            private readonly List<AuiNode> _sliders = new List<AuiNode>();
            private readonly List<AuiNode> _inputs = new List<AuiNode>();

            public Gen(AuiViewInfo info, AuiIndexMap map, AuiCodegenContext ctx, AuiDiagnosticBag d)
            {
                _info = info;
                _map = map;
                _ctx = ctx;
                _d = d;
            }

            // ------------------------------------------------------------ writer helpers

            private void L(string s = "")
            {
                if (s.Length == 0) { _sb.Append('\n'); return; }
                for (int i = 0; i < _indent; i++) _sb.Append("    ");
                _sb.Append(s).Append('\n');
            }

            private void Open(string s)
            {
                L(s);
                L("{");
                _indent++;
            }

            private void Close()
            {
                _indent--;
                L("}");
            }

            private static string Lit(string s)
            {
                var sb = new StringBuilder("\"");
                foreach (char c in s)
                {
                    if (c == '"') sb.Append("\\\"");
                    else if (c == '\\') sb.Append("\\\\");
                    else if (c == '\n') sb.Append("\\n");
                    else if (c < 32 || c > 126) sb.Append("\\u").Append(((int)c).ToString("X4"));
                    else sb.Append(c);
                }
                return sb.Append('"').ToString();
            }

            private int E(AuiNode n)
            {
                return _map.Base[n.Index];
            }

            private int Stride(AuiNode n)
            {
                return _map.Stride[n.Index];
            }

            /// <summary>Element index expression for a node, in row `row` when it is inside a repeat.</summary>
            private string Ex(AuiNode n, string row)
            {
                int b = E(n);
                int s = Stride(n);
                return s > 0 && row != null ? b + " + " + row + " * " + s : b.ToString();
            }

            private AuiRepeatInfo RepeatOf(AuiNode n)
            {
                return _info.RepeatContaining(n);
            }

            private int RepeatIndex(AuiRepeatInfo r)
            {
                return _info.Repeats.IndexOf(r);
            }

            private int GroupIndex(AuiPageGroup g)
            {
                return _info.PageGroups.IndexOf(g);
            }

            private int SelectIndex(AuiNode n)
            {
                for (int i = 0; i < _info.Selects.Count; i++) if (_info.Selects[i].Node == n) return i;
                return -1;
            }

            private string ConstName(string prefix, string id)
            {
                return prefix + AuiNaming.Identifier(id).ToUpperInvariant();
            }

            // ------------------------------------------------------------ main

            public string Run()
            {
                ResolveUnknownTypes();
                CheckNames();
                CollectRenderTargets();

                L("// <auto-generated>");
                L("//     AUI " + _ctx.Hash);
                L("//     Generated from " + _info.Name + ".aui. Do not edit; write your logic in " + _info.Name + ".cs.");
                L("// </auto-generated>");
                L("using UdonSharp;");
                L("using UnityEngine;");
                L("using Armagan.AUI;");
                L();
                bool ns = !string.IsNullOrEmpty(_ctx.Namespace);
                if (ns) Open("namespace " + _ctx.Namespace);
                L("[UdonBehaviourSyncMode(BehaviourSyncMode.None)]");
                Open("public partial class " + _info.Name + " : AuiView");

                EmitConstants();
                EmitNestedFields();
                EmitBindings();
                EmitRepeats();
                EmitPages();
                EmitSelects();
                EmitRenderMethods();
                EmitRetranslate();
                EmitAfterStart();
                EmitInputResult();
                EmitDispatch();
                EmitChildEvents();

                Close();
                if (ns) Close();
                return _sb.ToString();
            }

            private void ResolveUnknownTypes()
            {
                foreach (AuiBinding b in _info.Bindings)
                {
                    if (b.Type != AuiBindingType.Unknown) continue;
                    foreach (AuiBindingUse u in b.Uses)
                    {
                        if (!u.Node.IsComponentTag || _ctx.ChildInfo == null) continue;
                        AuiViewInfo child = _ctx.ChildInfo(u.Node.Tag);
                        AuiBinding cb = child != null ? child.FindBinding(u.Attribute, null) : null;
                        if (cb != null && cb.Type != AuiBindingType.Unknown) { b.Type = cb.Type; break; }
                    }
                    if (b.Type == AuiBindingType.Unknown) b.Type = AuiBindingType.String;
                }
            }

            private void CheckNames()
            {
                foreach (AuiBinding b in _info.Bindings)
                {
                    if (Reserved.Contains(b.PropertyName))
                    {
                        AuiBindingUse u = b.Uses[0];
                        _d.Error(AuiCodes.BadBindingSyntax, "Binding name '" + b.Name + "' collides with an AuiView member; pick another name.", u.Node.Line, u.Node.Column, 1);
                    }
                }
            }

            private void CollectRenderTargets()
            {
                foreach (AuiNode n in _info.Doc.Nodes)
                {
                    // A text bound to {item.x} is written directly by SetRowsX; only view-level
                    // bindings and keyed texts with placeholders need a render method.
                    bool boundText = n.Get("text") != null && !(Stride(n) > 0 && AuiAnalyzer.IsItemBinding(n.Get("text")));
                    if (n.Tag == "text" && (boundText || (n.Get("key") != null && n.Get("args") != null))) _renderTexts.Add(n);
                    if (n.Tag == "slider") _sliders.Add(n);
                    if (n.Tag == "input") _inputs.Add(n);
                }
            }

            // ------------------------------------------------------------ sections

            private void EmitConstants()
            {
                L("// ---- element indices (row 0 for repeat rows) and signal ids of elements with an id");
                foreach (AuiNode n in _info.Doc.Nodes)
                {
                    if (n.Id == null || E(n) < 0) continue;
                    L("public const int " + ConstName("E_", n.Id) + " = " + E(n) + ";");
                    int sig = _map.SignalOf[E(n)];
                    if (sig >= 0) L("public const int " + ConstName("S_", n.Id) + " = " + sig + ";");
                }
                foreach (AuiRepeatInfo r in _info.Repeats)
                    L("public const int " + ConstName("", r.Id) + "_MAX = " + r.Max + ";");
                L();
            }

            private void EmitNestedFields()
            {
                if (_info.NestedViews.Count == 0) return;
                L("// ---- nested views (assigned by the builder)");
                foreach (AuiNestedView nv in _info.NestedViews)
                    L("public " + nv.ViewName + " " + nv.FieldName + ";");
                L();
            }

            private string DefaultOf(AuiBinding b)
            {
                AuiBindingType t = b.Type;
                switch (t)
                {
                    case AuiBindingType.Bool:
                        // An if= element is baked active, so its binding starts true to match.
                        foreach (AuiBindingUse u in b.Uses) if (u.Attribute == "if") return "true";
                        return "false";
                    case AuiBindingType.Float: return "0f";
                    case AuiBindingType.Int: return "0";
                    default: return "\"\"";
                }
            }

            private void EmitBindings()
            {
                if (_info.Bindings.Count == 0) return;
                L("// ---- bindings: one property each; the setter repaints every element that uses it");
                foreach (AuiBinding b in _info.Bindings)
                {
                    string f = AuiNaming.Field(b.Name);
                    string t = b.CsType;
                    L("private " + t + " " + f + " = " + DefaultOf(b) + ";");
                    Open("public " + t + " " + b.PropertyName);
                    L("get { return " + f + "; }");
                    Open("set");
                    L(f + " = " + (b.Type == AuiBindingType.String ? "Escape(value)" : "value") + ";");
                    foreach (AuiBindingUse u in b.Uses) EmitApplyUse(b, u, f, null);
                    Close();
                    Close();
                }
                L();
            }

            /// <summary>The statements that push a binding's value into one element use. row = null outside repeats.</summary>
            private void EmitApplyUse(AuiBinding b, AuiBindingUse u, string valueExpr, string row)
            {
                AuiNode n = u.Node;
                bool inRepeat = Stride(n) > 0;
                // A non-item binding used inside a template applies to every row.
                if (inRepeat && row == null)
                {
                    AuiRepeatInfo r = RepeatOf(n);
                    Open("for (int row = 0; row < " + r.Max + "; row++)");
                    EmitApplyUse(b, u, valueExpr, "row");
                    Close();
                    return;
                }
                string ex = Ex(n, row);
                if (n.IsComponentTag)
                {
                    AuiNestedView nv = null;
                    foreach (AuiNestedView x in _info.NestedViews) if (x.Node == n) nv = x;
                    if (nv != null) L("if (" + nv.FieldName + " != null) " + nv.FieldName + "." + AuiNaming.Pascal(u.Attribute) + " = " + valueExpr + ";");
                    return;
                }
                switch (u.Attribute)
                {
                    case "text":
                    case "args":
                        L(RenderName(n) + "(" + (row ?? "") + ");");
                        break;
                    case "if":
                        L("SetActiveElem(" + ex + ", " + valueExpr + ");");
                        break;
                    case "disabled":
                        L("SetEnabledElem(" + ex + ", !" + valueExpr + ");");
                        break;
                    case "selected":
                        L("SetSelected(elemSignal[" + ex + "], " + valueExpr + ");");
                        break;
                    case "bind":
                        switch (n.Tag)
                        {
                            case "toggle": L("SetToggle(" + ex + ", " + valueExpr + ");"); break;
                            case "slider":
                                L("if (elemSliders[" + ex + "] != null) elemSliders[" + ex + "].SetValueWithoutNotify(" + valueExpr + ");");
                                L(RenderName(n) + "(" + (row ?? "") + ");");
                                break;
                            case "select": L("SetSelect(" + SelectIndex(n) + ", " + valueExpr + ");"); break;
                            case "tabs":
                            {
                                AuiPageGroup grp = null;
                                foreach (AuiPageGroup pg in _info.PageGroups) if (pg.Tabs == n) grp = pg;
                                if (grp != null) L("SelectPage(" + GroupIndex(grp) + ", " + valueExpr + ", false);");
                                break;
                            }
                            case "input": L("RenderInput(" + ex + ", " + valueExpr + ");"); break;
                        }
                        break;
                }
            }

            private string RenderName(AuiNode n)
            {
                return "Render_" + E(n);
            }

            private void EmitRepeats()
            {
                foreach (AuiRepeatInfo r in _info.Repeats)
                {
                    int ri = RepeatIndex(r);
                    string P = r.PascalId;
                    L("// ---- repeat '" + r.Id + "'");
                    Open("public int " + P + "Count");
                    L("get { return GetRepeatCount(" + ri + "); }");
                    L("set { SetRepeatCount(" + ri + ", value); }");
                    Close();
                    foreach (AuiBinding b in r.ItemBindings)
                    {
                        // Values that feed a placeholder are kept so a language switch can re-render the row.
                        bool stored = b.Type == AuiBindingType.String && UsedByArgs(b);
                        if (stored) L("private string[] _" + AuiNaming.Identifier(r.Id) + "_" + b.Name + " = new string[" + r.Max + "];");
                        Open("public void Set" + P + b.PropertyName + "(int row, " + b.CsType + " value)");
                        L("if (row < 0 || row >= " + r.Max + ") return;");
                        string val = b.Type == AuiBindingType.String ? "Escape(value)" : "value";
                        if (stored)
                        {
                            L("_" + AuiNaming.Identifier(r.Id) + "_" + b.Name + "[row] = " + val + ";");
                            val = "_" + AuiNaming.Identifier(r.Id) + "_" + b.Name + "[row]";
                        }
                        else if (b.Type == AuiBindingType.String)
                        {
                            L("string v = Escape(value);");
                            val = "v";
                        }
                        foreach (AuiBindingUse u in b.Uses)
                        {
                            if (u.Attribute == "text")
                                L("if (elemText[" + Ex(u.Node, "row") + "] != null) elemText[" + Ex(u.Node, "row") + "].text = " + val + ";");
                            else EmitApplyUse(b, u, val, "row");
                        }
                        Close();
                    }
                    L();
                }
            }

            private bool UsedByArgs(AuiBinding b)
            {
                foreach (AuiBindingUse u in b.Uses) if (u.Attribute == "args") return true;
                return false;
            }

            private void EmitPages()
            {
                foreach (AuiPageGroup g in _info.PageGroups)
                {
                    int gi = GroupIndex(g);
                    string P = g.PascalId;
                    L("// ---- pages '" + g.Id + "'");
                    L("public const int " + ConstName("", g.Id) + "_COUNT = " + g.Pages.Count + ";");
                    for (int i = 0; i < g.Pages.Count; i++)
                        if (g.Pages[i].Id != null) L("public const int " + ConstName(ConstName("", g.Id) + "_", g.Pages[i].Id) + " = " + i + ";");
                    L("public int Get" + P + "Page() { return GetPage(" + gi + "); }");
                    L("public void Select" + P + "Page(int page) { SelectPage(" + gi + ", page, false); }");
                    L();
                }
                if (_info.PageGroups.Count == 0) return;
                Open("protected override void _OnPageChanged(int group, int page)");
                foreach (AuiPageGroup g in _info.PageGroups)
                {
                    int gi = GroupIndex(g);
                    Open("if (group == " + gi + ")");
                    if (g.Binding != null) L(AuiNaming.Field(g.Binding.Name) + " = page;");
                    string h = g.Tabs != null ? g.Tabs.Get("onchange") : null;
                    if (h == null) h = g.Node.Get("onchange");
                    if (h != null) L(h + "(page);");
                    L("return;");
                    Close();
                }
                Close();
                L();
            }

            private void EmitSelects()
            {
                if (_info.Selects.Count == 0) return;
                foreach (AuiSelectInfo s in _info.Selects)
                {
                    int si = SelectIndex(s.Node);
                    string P = s.Node.Id != null ? AuiNaming.Pascal(s.Node.Id) : "Select" + si;
                    L("// ---- select '" + (s.Node.Id ?? si.ToString()) + "'");
                    L("public void Set" + P + "OptionLabel(int index, string label) { SetSelectOptionLabel(" + si + ", index, label); }");
                    L("public void Set" + P + "OptionCount(int count) { SetSelectOptionCount(" + si + ", count); }");
                }
                Open("protected override void _OnSelectChanged(int select, int index)");
                foreach (AuiSelectInfo s in _info.Selects)
                {
                    int si = SelectIndex(s.Node);
                    Open("if (select == " + si + ")");
                    if (s.Binding != null) L(AuiNaming.Field(s.Binding.Name) + " = index;");
                    string h = s.Node.Get("onchange");
                    if (h != null) L(h + "(index);");
                    L("return;");
                    Close();
                }
                Close();
                L();
            }

            private void EmitRenderMethods()
            {
                if (_renderTexts.Count + _sliders.Count + _inputs.Count == 0) return;
                L("// ---- render: push current values into elements");
                foreach (AuiNode n in _renderTexts)
                {
                    bool inRow = Stride(n) > 0;
                    string ex = Ex(n, inRow ? "row" : null);
                    Open("private void " + RenderName(n) + "(" + (inRow ? "int row" : "") + ")");
                    L("if (elemText[" + ex + "] == null) return;");
                    string text = n.Get("text");
                    if (text != null)
                    {
                        AuiBinding b = BindingFor(n, text);
                        if (b != null) L("elemText[" + ex + "].text = " + ValueExpr(b, n) + ";");
                    }
                    else
                    {
                        // key + args
                        var args = new List<AuiBinding>();
                        foreach (AuiKeyUse ku in _info.Keys) if (ku.Node == n && ku.Attribute.Name == "key") args = ku.Args;
                        L("if (lang == null || elemKey[" + ex + "] < 0) return;");
                        if (args.Count == 0) L("elemText[" + ex + "].text = lang.Get(elemKey[" + ex + "]);");
                        else
                        {
                            int count = System.Math.Min(4, args.Count);
                            var sb = new StringBuilder("elemText[" + ex + "].text = Fill" + count + "(lang.Get(elemKey[" + ex + "])");
                            for (int i = 0; i < count; i++)
                                sb.Append(", ").Append(Lit("{" + args[i].Name + "}")).Append(", ").Append(ValueExpr(args[i], n));
                            sb.Append(");");
                            L(sb.ToString());
                            if (args.Count > 4)
                                _d.Warning(AuiCodes.PlaceholderMismatch, "Only the first four args are substituted.", n.Line, n.Column, 5);
                        }
                    }
                    Close();
                }
                foreach (AuiNode n in _sliders)
                {
                    bool inRow = Stride(n) > 0;
                    string ex = Ex(n, inRow ? "row" : null);
                    int decimals = 0;
                    int.TryParse(n.Get("decimals") ?? "0", out decimals);
                    Open("private void " + RenderName(n) + "(" + (inRow ? "int row" : "") + ")");
                    L("if (elemText[" + ex + "] == null || elemSliders[" + ex + "] == null || lang == null || elemKey[" + ex + "] < 0) return;");
                    L("elemText[" + ex + "].text = lang.Get(elemKey[" + ex + "]).Replace(\"{value}\", FormatNumber(elemSliders[" + ex + "].value, " + decimals + "));");
                    Close();
                }
                L();
            }

            private AuiBinding BindingFor(AuiNode n, string attrValue)
            {
                if (!AuiAnalyzer.IsBindingValue(attrValue)) return null;
                string name = AuiAnalyzer.BindingName(attrValue);
                bool item = AuiAnalyzer.IsItemBinding(attrValue);
                return _info.FindBinding(name, item ? RepeatOf(n) : null);
            }

            /// <summary>Expression that yields a binding's current value where a render method runs (row in scope inside repeats).</summary>
            private string ValueExpr(AuiBinding b, AuiNode at)
            {
                if (!b.IsItem) return AuiNaming.Field(b.Name);
                if (UsedByArgs(b)) return "_" + AuiNaming.Identifier(b.Repeat.Id) + "_" + b.Name + "[row]";
                // A text-only item binding is written straight into the label; nothing to re-render.
                return "elemText[" + Ex(at, "row") + "].text";
            }

            private void EmitRetranslate()
            {
                var lines = new List<string>();
                foreach (AuiNode n in _renderTexts)
                {
                    if (n.Get("key") == null) continue; // plain text bindings do not change with language
                    lines.Add(RenderCall(n));
                }
                foreach (AuiNode n in _sliders) lines.Add(RenderCall(n));
                foreach (AuiNode n in _inputs)
                {
                    AuiBinding b = BindingFor(n, n.Get("bind") ?? "");
                    if (Stride(n) > 0) continue; // inputs in rows keep their value in the label; placeholder re-render skipped
                    lines.Add("RenderInput(" + E(n) + ", " + (b != null ? AuiNaming.Field(b.Name) : "\"\"") + ");");
                }
                if (lines.Count == 0) return;
                Open("public override void _Retranslate()");
                L("base._Retranslate();");
                foreach (string s in lines) L(s);
                Close();
                L();
            }

            private string RenderCall(AuiNode n)
            {
                if (Stride(n) == 0) return RenderName(n) + "();";
                AuiRepeatInfo r = RepeatOf(n);
                return "for (int row = 0; row < " + r.Max + "; row++) " + RenderName(n) + "(row);";
            }

            private void EmitAfterStart()
            {
                var lines = new List<string>();
                foreach (AuiNode n in _renderTexts) if (n.Get("text") != null && Stride(n) == 0) lines.Add(RenderName(n) + "();");
                foreach (AuiNode n in _sliders)
                {
                    AuiBinding b = BindingFor(n, n.Get("bind") ?? "");
                    if (b != null && Stride(n) == 0) lines.Add("if (elemSliders[" + E(n) + "] != null) " + AuiNaming.Field(b.Name) + " = elemSliders[" + E(n) + "].value;");
                    lines.Add(RenderCall(n));
                }
                foreach (AuiNode n in _inputs)
                {
                    if (Stride(n) > 0) continue;
                    AuiBinding b = BindingFor(n, n.Get("bind") ?? "");
                    lines.Add("RenderInput(" + E(n) + ", " + (b != null ? AuiNaming.Field(b.Name) : "\"\"") + ");");
                }
                foreach (AuiNestedView nv in _info.NestedViews)
                {
                    AuiViewInfo child = _ctx.ChildInfo != null ? _ctx.ChildInfo(nv.ViewName) : null;
                    foreach (AuiAttribute a in nv.Node.Attributes)
                    {
                        if (AuiSchema.IsCommonAttribute(a.Name) || a.Name.StartsWith("on-")) continue;
                        if (AuiAnalyzer.IsBindingValue(a.Value))
                        {
                            AuiBinding b = BindingFor(nv.Node, a.Value);
                            if (b != null) lines.Add("if (" + nv.FieldName + " != null) " + nv.FieldName + "." + AuiNaming.Pascal(a.Name) + " = " + AuiNaming.Field(b.Name) + ";");
                            continue;
                        }
                        AuiBinding cb = child != null ? child.FindBinding(a.Name, null) : null;
                        string lit;
                        if (cb == null) { _d.Warning(AuiCodes.UnknownAttribute, "<" + nv.ViewName + "> has no binding '" + a.Name + "'.", a.Line, a.Column, a.Name.Length); continue; }
                        switch (cb.Type)
                        {
                            case AuiBindingType.Bool: lit = a.Value == "true" ? "true" : "false"; break;
                            case AuiBindingType.Float: lit = a.Value + "f"; break;
                            case AuiBindingType.Int: lit = a.Value; break;
                            default: lit = Lit(a.Value); break;
                        }
                        lines.Add("if (" + nv.FieldName + " != null) " + nv.FieldName + "." + cb.PropertyName + " = " + lit + ";");
                    }
                }
                if (lines.Count == 0) return;
                Open("protected override void _AfterStart()");
                foreach (string s in lines) L(s);
                Close();
                L();
            }

            private void EmitInputResult()
            {
                if (_inputs.Count == 0) return;
                Open("public override void _OnInputResult()");
                foreach (AuiNode n in _inputs)
                {
                    AuiBinding b = BindingFor(n, n.Get("bind") ?? "");
                    string h = n.Get("onsubmit");
                    bool inRow = Stride(n) > 0;
                    if (!inRow)
                    {
                        Open("if (inputResultId == " + E(n) + ")");
                        if (b != null) L(b.PropertyName + " = inputResult;");
                        else L("RenderInput(" + E(n) + ", Escape(inputResult));");
                        if (h != null) L(h + "(" + (b != null ? AuiNaming.Field(b.Name) : "Escape(inputResult)") + ");");
                        L("return;");
                        Close();
                    }
                    else
                    {
                        AuiRepeatInfo r = RepeatOf(n);
                        int b0 = E(n), st = Stride(n);
                        Open("if (inputResultId >= " + b0 + " && inputResultId < " + (b0 + st * r.Max) + " && (inputResultId - " + b0 + ") % " + st + " == 0)");
                        L("int row = (inputResultId - " + b0 + ") / " + st + ";");
                        L("string v = Escape(inputResult);");
                        L("RenderInput(inputResultId, v);");
                        if (h != null) L(h + "(row, v);");
                        L("return;");
                        Close();
                    }
                }
                Close();
                L();
            }

            private void EmitDispatch()
            {
                // Group handlers by kind.
                var click = new List<AuiHandler>();
                var value = new List<AuiHandler>();
                var pointer = new List<AuiHandler>();
                foreach (AuiHandler h in _info.Handlers)
                {
                    if (h.Node.IsComponentTag) continue;
                    switch (h.Event)
                    {
                        case "onclick": case "onselect": click.Add(h); break;
                        case "onchange":
                            if (h.Node.Tag == "toggle") click.Add(h);
                            else if (h.Node.Tag == "slider") value.Add(h);
                            break; // select/tabs go through their own overrides
                        case "onsubmit": break; // input result path
                        default: pointer.Add(h); break;
                    }
                }
                // Toggles without a handler still need their binding field refreshed on click.
                var toggleBindings = new List<AuiNode>();
                foreach (AuiNode n in _info.Doc.Nodes) if (n.Tag == "toggle" && n.Get("bind") != null) toggleBindings.Add(n);

                if (click.Count + value.Count + pointer.Count + toggleBindings.Count == 0) return;
                Open("protected override void _Dispatch(int id, int kind)");
                Open("if (kind == SIG_CLICK)");
                EmitClickDispatch(click, toggleBindings);
                L("return;");
                Close();
                if (value.Count > 0)
                {
                    Open("if (kind == SIG_VALUE)");
                    foreach (AuiHandler h in value) EmitValueCase(h);
                    L("return;");
                    Close();
                }
                if (pointer.Count > 0)
                {
                    foreach (AuiHandler h in pointer)
                    {
                        string sigKind = h.Event == "onhover" ? "SIG_ENTER" : h.Event == "onleave" ? "SIG_EXIT" : h.Event == "onpress" ? "SIG_DOWN" : "SIG_UP";
                        EmitSignalCase(h.Node, "kind == " + sigKind, h.Name + "(" + (h.HasRow ? "row" : "") + ")", null);
                    }
                }
                Close();
                L();
            }

            private void EmitClickDispatch(List<AuiHandler> click, List<AuiNode> toggleBindings)
            {
                var done = new HashSet<AuiNode>();
                foreach (AuiHandler h in click)
                {
                    AuiNode n = h.Event == "onselect" ? (h.Repeat != null ? h.Repeat.Template : null) : h.Node;
                    if (n == null) continue;
                    string call;
                    if (h.Node.Tag == "toggle")
                    {
                        AuiBinding b = BindingFor(h.Node, h.Node.Get("bind") ?? "");
                        string bind = b != null && !b.IsItem ? AuiNaming.Field(b.Name) + " = GetToggle(" + Ex(h.Node, Stride(h.Node) > 0 ? "row" : null) + "); " : "";
                        call = bind + h.Name + "(" + (h.HasRow ? "row, " : "") + "GetToggle(" + Ex(h.Node, Stride(h.Node) > 0 ? "row" : null) + "))";
                        done.Add(h.Node);
                    }
                    else call = h.Name + "(" + (h.HasRow ? "row" : "") + ")";
                    EmitSignalCase(n, null, call, null);
                }
                foreach (AuiNode n in toggleBindings)
                {
                    if (done.Contains(n) || Stride(n) > 0) continue;
                    AuiBinding b = BindingFor(n, n.Get("bind"));
                    if (b == null) continue;
                    EmitSignalCase(n, null, AuiNaming.Field(b.Name) + " = GetToggle(" + E(n) + ")", null);
                }
            }

            private void EmitValueCase(AuiHandler h)
            {
                AuiNode n = h.Node;
                AuiBinding b = BindingFor(n, n.Get("bind") ?? "");
                string ex = Ex(n, Stride(n) > 0 ? "row" : null);
                var sb = new StringBuilder();
                if (b != null && !b.IsItem) sb.Append(AuiNaming.Field(b.Name)).Append(" = elemSliders[").Append(ex).Append("].value; ");
                sb.Append(RenderName(n)).Append("(").Append(Stride(n) > 0 ? "row" : "").Append("); ");
                sb.Append(h.Name).Append("(").Append(h.HasRow ? "row, " : "").Append("elemSliders[").Append(ex).Append("].value)");
                EmitSignalCase(n, null, sb.ToString(), null);
            }

            /// <summary>Emit `if (id == S) { call; return; }`, or the row arithmetic when the node sits in a repeat.</summary>
            private void EmitSignalCase(AuiNode n, string extraCondition, string call, string unused)
            {
                int e = E(n);
                if (e < 0) return;
                int sig = _map.SignalOf[e];
                if (sig < 0)
                {
                    _d.Error(AuiCodes.HandlerNotIdentifier, "<" + n.Tag + "> has a handler but no signal; this is a compiler bug.", n.Line, n.Column, 1);
                    return;
                }
                string cond = extraCondition != null ? extraCondition + " && " : "";
                if (Stride(n) == 0)
                {
                    L("if (" + cond + "id == " + sig + ") { " + call + "; return; }");
                    return;
                }
                AuiRepeatInfo r = RepeatOf(n);
                int sigBase = _map.RepeatSignalBase[r.Node.Index];
                int sigStride = _map.RepeatSignalStride[r.Node.Index];
                int off = sig - sigBase;
                Open("if (" + cond + "id >= " + sigBase + " && id < " + (sigBase + sigStride * r.Max) + " && (id - " + sigBase + ") % " + sigStride + " == " + off + ")");
                L("int row = (id - " + sigBase + ") / " + sigStride + ";");
                L("if (row >= GetRepeatCount(" + RepeatIndex(r) + ")) return;");
                L(call + ";");
                L("return;");
                Close();
            }

            private void EmitChildEvents()
            {
                var lines = new List<string>();
                foreach (AuiNestedView nv in _info.NestedViews)
                    foreach (AuiAttribute a in nv.Node.Attributes)
                        if (a.Name.StartsWith("on-") && AuiSchema.IsIdentifier(a.Value))
                            lines.Add("if (child == " + nv.FieldName + " && eventName == " + Lit(a.Name.Substring(3)) + ") { " + a.Value + "(); return; }");
                if (lines.Count == 0) return;
                Open("public override void _OnChildEvent(AuiView child, string eventName)");
                foreach (string s in lines) L(s);
                Close();
                L();
            }
        }
    }
}
