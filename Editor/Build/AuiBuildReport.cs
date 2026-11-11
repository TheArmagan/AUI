using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Armagan.AUI.Editor
{
    /// <summary>
    /// Marks a static method to run after AUI has built views, so code that wires things into a
    /// view's prefab or its scene instance (and would otherwise have to be re-run by hand after every
    /// rebuild) runs by itself.
    ///
    /// Signature: <c>static void Method(AuiBuildReport report)</c>. With view names the hook runs only
    /// when one of those views was actually rebuilt; with none it runs after any build. Hooks run once
    /// per finished drain (queue empty, no generated source waiting for a reload), after the language
    /// table, children before parents having already been built. A hook that throws is logged and
    /// does not stop the others.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class AuiAfterBuildAttribute : Attribute
    {
        public readonly string[] ViewNames;
        /// <summary>Lower runs first. Hooks with the same order run in name order (type, then method).</summary>
        public int Order;

        public AuiAfterBuildAttribute(params string[] viewNames)
        {
            ViewNames = viewNames ?? new string[0];
        }
    }

    /// <summary>What a post-build hook is told: the views rebuilt since the last time hooks ran.</summary>
    public sealed class AuiBuildReport
    {
        /// <summary>View names (class names) whose prefab was rebuilt.</summary>
        public readonly IReadOnlyList<string> BuiltViews;
        /// <summary>The .aui asset paths of the same views, in build order.</summary>
        public readonly IReadOnlyList<string> BuiltPaths;

        public AuiBuildReport(List<string> views, List<string> paths)
        {
            BuiltViews = views;
            BuiltPaths = paths;
        }

        public bool IsBuilt(string viewName)
        {
            foreach (string v in BuiltViews) if (v == viewName) return true;
            return false;
        }

        /// <summary>True when any of <paramref name="viewNames"/> was rebuilt.</summary>
        public bool AnyBuilt(params string[] viewNames)
        {
            foreach (string n in viewNames) if (IsBuilt(n)) return true;
            return false;
        }
    }

    /// <summary>One view's line in a drain: what happened to it and where the time went.</summary>
    [Serializable]
    public sealed class AuiViewBuildRecord
    {
        public const string Built = "built", Skipped = "skipped", Waiting = "waiting", Failed = "failed", Errors = "errors", GaveUp = "gave up";

        public string path;
        public string view;
        public string status;
        public string reason;
        public bool forced;
        public int elements;
        public long loadMs, treeMs, tablesMs, finishMs, auditMs, saveMs, revertMs, totalMs;

        public string Phases()
        {
            return "load " + loadMs + ", tree " + treeMs + ", tables " + tablesMs + ", finish " + finishMs + ", audit " + auditMs + ", save " + saveMs + ", revert " + revertMs;
        }
    }

    /// <summary>One drain of the build queue, kept in SessionState so the Build Status window survives reloads.</summary>
    [Serializable]
    public sealed class AuiDrainRecord
    {
        public string time;
        public int views, built;
        public bool sourcesWritten;
        public long totalMs, scanMs, compileMs, usharpMs, buildMs, importMs, langMs, hooksMs;
        public int cached;
        public List<AuiViewBuildRecord> records = new List<AuiViewBuildRecord>();

        private const string Key = "AUI.LastDrain";

        public void Save()
        {
            SessionState.SetString(Key, JsonUtility.ToJson(this));
        }

        public static AuiDrainRecord Load()
        {
            string s = SessionState.GetString(Key, "");
            if (string.IsNullOrEmpty(s)) return null;
            try { return JsonUtility.FromJson<AuiDrainRecord>(s); }
            catch (Exception) { return null; }
        }
    }

    /// <summary>Finds and runs the [AuiAfterBuild] hooks.</summary>
    internal static class AuiBuildHooks
    {
        private static bool _running;

        public static bool Running
        {
            get { return _running; }
        }

        /// <summary>Runs every hook that cares about the report; returns how many ran.</summary>
        public static int Run(AuiBuildReport report)
        {
            if (_running) return 0;   // a hook that drains the queue itself must not re-enter
            var hooks = new List<KeyValuePair<MethodInfo, AuiAfterBuildAttribute>>();
            foreach (MethodInfo m in TypeCache.GetMethodsWithAttribute<AuiAfterBuildAttribute>())
            {
                var a = m.GetCustomAttribute<AuiAfterBuildAttribute>();
                if (a == null) continue;
                if (!m.IsStatic)
                {
                    AuiLog.Error("[AuiAfterBuild] " + m.DeclaringType + "." + m.Name + " must be static; skipped.");
                    continue;
                }
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length > 1 || (ps.Length == 1 && ps[0].ParameterType != typeof(AuiBuildReport)))
                {
                    AuiLog.Error("[AuiAfterBuild] " + m.DeclaringType + "." + m.Name + " must take no parameters or one AuiBuildReport; skipped.");
                    continue;
                }
                if (a.ViewNames.Length > 0 && !report.AnyBuilt(a.ViewNames)) continue;
                hooks.Add(new KeyValuePair<MethodInfo, AuiAfterBuildAttribute>(m, a));
            }
            hooks.Sort((x, y) =>
            {
                int c = x.Value.Order.CompareTo(y.Value.Order);
                if (c != 0) return c;
                c = string.CompareOrdinal(x.Key.DeclaringType != null ? x.Key.DeclaringType.FullName : "", y.Key.DeclaringType != null ? y.Key.DeclaringType.FullName : "");
                return c != 0 ? c : string.CompareOrdinal(x.Key.Name, y.Key.Name);
            });
            int ran = 0;
            _running = true;
            try
            {
                foreach (var h in hooks)
                {
                    string name = h.Key.DeclaringType + "." + h.Key.Name;
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        h.Key.Invoke(null, h.Key.GetParameters().Length == 1 ? new object[] { report } : null);
                        ran++;
                        AuiLog.Info("after-build hook " + name + ": " + clock.ElapsedMilliseconds + " ms");
                    }
                    catch (TargetInvocationException e)
                    {
                        AuiLog.Error("after-build hook " + name + " threw: " + (e.InnerException ?? e));
                    }
                    catch (Exception e)
                    {
                        AuiLog.Error("after-build hook " + name + " threw: " + e);
                    }
                }
            }
            finally
            {
                _running = false;
            }
            return ran;
        }
    }
}
