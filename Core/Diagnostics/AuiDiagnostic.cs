using System.Collections.Generic;
using System.Text;

namespace Armagan.AUI.Core
{
    public enum AuiSeverity
    {
        Error = 0,
        Warning = 1,
        Info = 2,
    }

    /// <summary>
    /// One message about one place in one file. Both hosts (the Unity importer and the
    /// language server) publish exactly this: same code, same text, same position, so a
    /// problem VS Code underlines is the line the Unity console prints.
    ///
    /// Lines and columns are 1-based, because that is what people read in an editor. A
    /// diagnostic without a position (file-level problems such as a missing default language)
    /// carries line 0.
    /// </summary>
    public sealed class AuiDiagnostic
    {
        public readonly AuiSeverity Severity;
        /// <summary>Stable code such as AUI001. Documented in README; never renumbered.</summary>
        public readonly string Code;
        public readonly string Message;
        public readonly string File;
        public readonly int Line;
        public readonly int Column;
        /// <summary>Length in characters of the underlined span; 0 = the whole line.</summary>
        public readonly int Length;

        public AuiDiagnostic(AuiSeverity severity, string code, string message, string file,
                             int line, int column, int length)
        {
            Severity = severity;
            Code = code;
            Message = message;
            File = file;
            Line = line;
            Column = column;
            Length = length;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("[AUI] ");
            if (!string.IsNullOrEmpty(File))
            {
                sb.Append(File);
                if (Line > 0)
                {
                    sb.Append(':').Append(Line);
                    if (Column > 0) sb.Append(':').Append(Column);
                }
                sb.Append(' ');
            }
            sb.Append(Severity == AuiSeverity.Error ? "error " :
                      Severity == AuiSeverity.Warning ? "warning " : "info ");
            sb.Append(Code).Append(": ").Append(Message);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Collects diagnostics during one compile of one file. Cheap to pass around; the
    /// importer turns it into console lines, the LSP into a publishDiagnostics payload.
    /// </summary>
    public sealed class AuiDiagnosticBag
    {
        public readonly List<AuiDiagnostic> Items = new List<AuiDiagnostic>();
        public string File;

        public AuiDiagnosticBag(string file)
        {
            File = file;
        }

        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Items.Count; i++)
                    if (Items[i].Severity == AuiSeverity.Error) return true;
                return false;
            }
        }

        public void Error(string code, string message, int line = 0, int column = 0, int length = 0)
        {
            Items.Add(new AuiDiagnostic(AuiSeverity.Error, code, message, File, line, column, length));
        }

        public void Warning(string code, string message, int line = 0, int column = 0, int length = 0)
        {
            Items.Add(new AuiDiagnostic(AuiSeverity.Warning, code, message, File, line, column, length));
        }

        public void Info(string code, string message, int line = 0, int column = 0, int length = 0)
        {
            Items.Add(new AuiDiagnostic(AuiSeverity.Info, code, message, File, line, column, length));
        }

        public void AddRange(AuiDiagnosticBag other)
        {
            if (other == null) return;
            Items.AddRange(other.Items);
        }
    }
}
