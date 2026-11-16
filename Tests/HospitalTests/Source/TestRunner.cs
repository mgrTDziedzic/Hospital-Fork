using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace HospitalTests
{
    public class TestResult
    {
        public string Name, Description, Message, Stack;
        public bool Passed;
        public long Millis;
    }

    public static class TestRunner
    {
        public static List<TestResult> LastResults = new List<TestResult>();

        /// <summary>Results file: GenFilePaths.SaveDataFolderPath (the game's data folder, e.g. %USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios).</summary>
        public static string ResultsPath => Path.Combine(GenFilePaths.SaveDataFolderPath, "HospitalTestResults.txt");

        public static List<MethodInfo> Discover()
        {
            return typeof(TestRunner).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .Where(m => m.GetCustomAttribute<HospitalTestAttribute>() != null)
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name).ToList();
        }

        public static List<TestResult> RunAll() => Run(Discover());

        public static List<TestResult> Run(IEnumerable<MethodInfo> tests)
        {
            LogCapture.Install();
            var results = new List<TestResult>();
            foreach (var m in tests) results.Add(RunOne(m));
            LastResults = results;
            Report(results);
            return results;
        }

        private static TestResult RunOne(MethodInfo m)
        {
            var attr = m.GetCustomAttribute<HospitalTestAttribute>();
            var r = new TestResult { Name = m.DeclaringType.Name + "." + m.Name, Description = attr?.Description };
            var ctx = new TestContext();
            var sw = Stopwatch.StartNew();
            LogCapture.Begin();
            try
            {
                var args = m.GetParameters().Length == 0 ? new object[0] : new object[] { ctx };
                m.Invoke(null, args);
                r.Passed = true;
            }
            catch (Exception e)
            {
                if (e is TargetInvocationException tie && tie.InnerException != null) e = tie.InnerException;
                r.Passed = false;
                r.Message = e is AssertException ? e.Message : e.GetType().Name + ": " + e.Message;
                r.Stack = e.StackTrace;
            }
            finally
            {
                ctx.RunCleanup();
            }
            var errors = LogCapture.End();
            if (errors.Count > 0)
            {
                string logged = "game logged " + errors.Count + " error(s) during test: " + errors[0];
                r.Message = r.Passed ? logged : r.Message + " | " + logged;
                r.Passed = false;
            }
            sw.Stop();
            r.Millis = sw.ElapsedMilliseconds;
            return r;
        }

        public static void Report(List<TestResult> results, string fatal = null)
        {
            int pass = results.Count(r => r.Passed), fail = results.Count - pass;
            var sb = new StringBuilder();
            sb.AppendLine("Hospital tests " + DateTime.Now.ToString("s"));
            if (fatal != null) sb.AppendLine("FAIL <setup>: " + fatal);
            foreach (var r in results)
            {
                sb.AppendLine((r.Passed ? "PASS " : "FAIL ") + r.Name + " (" + r.Millis + " ms)" + (r.Description != null ? " - " + r.Description : ""));
                if (!r.Passed)
                {
                    sb.AppendLine("  " + r.Message);
                    if (r.Stack != null) sb.AppendLine("  " + r.Stack.Replace("\n", "\n  "));
                }
            }
            if (fatal != null) fail++;
            sb.AppendLine("RESULT: " + pass + " passed, " + fail + " failed");
            try { File.WriteAllText(ResultsPath, sb.ToString()); }
            catch (Exception e) { Log.Error("[HospitalTests] cannot write results: " + e.Message); }

            string summary = "[HospitalTests] " + pass + " passed, " + fail + " failed. Results: " + ResultsPath;
            if (fail > 0) Log.Error(summary + "\n" + sb); else Log.Message(summary);
        }
    }

    /// <summary>Collects error logs while a test runs. Two sources (Unity log event + Harmony postfix on Log.Error), de-duplicated by text.</summary>
    public static class LogCapture
    {
        private static bool installed, capturing;
        private static readonly object gate = new object();
        private static readonly List<string> errors = new List<string>();

        public static void Install()
        {
            if (installed) return;
            installed = true;
            Application.logMessageReceivedThreaded += (msg, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Add(msg);
            };
            try
            {
                var target = AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) });
                var post = AccessTools.Method(typeof(LogCapture), nameof(Add));
                if (target != null) new Harmony("hospital.tests.logcapture").Patch(target, postfix: new HarmonyMethod(post));
            }
            catch (Exception e) { Log.Warning("[HospitalTests] Log.Error patch failed, using Unity log event only: " + e.Message); }
        }

        // Harmony binds the first parameter by name "text" (Log.Error(string text))
        public static void Add(string text)
        {
            lock (gate) { if (capturing && !errors.Contains(text)) errors.Add(text); }
        }

        public static void Begin() { lock (gate) { errors.Clear(); capturing = true; } }
        public static List<string> End() { lock (gate) { capturing = false; return new List<string>(errors); } }
    }
}
