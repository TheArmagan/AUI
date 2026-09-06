using System.Collections.Generic;
using Armagan.AUI.Core;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// The one place editor-side AUI code writes to the console. Everything is prefixed
    /// "[AUI]" so a search in the console finds all of it, and a diagnostic with a position
    /// is printed as "path:line:col" so the console line is clickable.
    /// </summary>
    public static class AuiLog
    {
        public const string Prefix = "[AUI] ";

        public static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message)
        {
            Debug.LogError(Prefix + message);
        }

        public static void Report(IEnumerable<AuiDiagnostic> diagnostics)
        {
            if (diagnostics == null) return;
            foreach (AuiDiagnostic d in diagnostics)
            {
                string text = d.ToString();
                if (d.Severity == AuiSeverity.Error) Debug.LogError(text);
                else if (d.Severity == AuiSeverity.Warning) Debug.LogWarning(text);
                else Debug.Log(text);
            }
        }
    }
}
