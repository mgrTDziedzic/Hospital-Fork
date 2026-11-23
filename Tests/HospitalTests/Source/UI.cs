using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace HospitalTests
{
    public class Window_TestResults : Window
    {
        private readonly List<TestResult> results;
        private Vector2 scroll;
        private float viewHeight;

        public Window_TestResults(List<TestResult> results)
        {
            this.results = results;
            doCloseX = true;
            draggable = true;
            resizeable = true;
            absorbInputAroundWindow = false;
        }

        public override Vector2 InitialSize => new Vector2(800f, 600f);

        public override void DoWindowContents(Rect inRect)
        {
            int pass = results.Count(r => r.Passed), skipped = results.Count(r => r.Skipped);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 32f), "Hospital tests: " + pass + "/" + (results.Count - skipped) + " passed" + (skipped > 0 ? ", " + skipped + " skipped" : ""));
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(inRect.width - 120f, 34f, 120f, 28f), "Run all again"))
            {
                Close();
                TestEntryPoints.RunAllAndShow();
                return;
            }
            Widgets.Label(new Rect(0, 36f, inRect.width - 130f, 24f), "Results file: " + TestRunner.ResultsPath);

            var outRect = new Rect(0, 70f, inRect.width, inRect.height - 70f);
            var view = new Rect(0, 0, outRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(outRect, ref scroll, view);
            float y = 0;
            foreach (var r in results)
            {
                string text = (r.Skipped ? "SKIP  " : r.Passed ? "PASS  " : "FAIL  ") + r.Name + "  (" + r.Millis + " ms)"
                              + (r.Description != null ? "\n" + r.Description : "")
                              + (r.Skipped ? "\nrequires " + r.SkipReason : r.Passed ? "" : "\n" + r.Message);
                float h = Text.CalcHeight(text, view.width - 8f) + 6f;
                var row = new Rect(0, y, view.width, h);
                Widgets.DrawBoxSolid(row, r.Skipped ? new Color(0.3f, 0.3f, 0.3f, 0.6f) : r.Passed ? new Color(0.1f, 0.35f, 0.1f, 0.6f) : new Color(0.45f, 0.1f, 0.1f, 0.6f));
                Widgets.Label(new Rect(4f, y + 3f, view.width - 8f, h), text);
                y += h + 2f;
            }
            viewHeight = y;
            Widgets.EndScrollView();
        }
    }

    public static class TestEntryPoints
    {
        public static void RunAllAndShow()
        {
            Find.WindowStack.Add(new Window_TestResults(TestRunner.RunAll()));
        }

        [DebugAction("Hospital Tests", "Run all", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunAll() { RunAllAndShow(); }

        [DebugActionYielder]
        public static IEnumerable<DebugActionNode> RunSingle()
        {
            foreach (MethodInfo m in TestRunner.Discover())
            {
                MethodInfo method = m;
                yield return new DebugActionNode("Run: " + method.DeclaringType.Name + "." + method.Name, DebugActionType.Action,
                    () => Find.WindowStack.Add(new Window_TestResults(TestRunner.Run(new[] { method }))));
            }
        }
    }

    // Scrollable text with a "Copy to clipboard" button (colour tags stripped in the copy).
    public class Window_TextSample : Window
    {
        private readonly string title;
        private readonly string text;
        private Vector2 scroll;

        public Window_TextSample(string title, string text)
        {
            this.title = title;
            this.text = text;
            doCloseX = true;
            draggable = true;
            resizeable = true;
            absorbInputAroundWindow = false;
        }

        public override Vector2 InitialSize => new Vector2(900f, 650f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width - 200f, 32f), title);
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(inRect.width - 180f, 2f, 160f, 28f), "Copy to clipboard"))
            {
                GUIUtility.systemCopyBuffer = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
                Messages.Message("Copied to clipboard", MessageTypeDefOf.SilentInput, false);
            }
            var outRect = new Rect(0, 38f, inRect.width, inRect.height - 38f);
            float h = Text.CalcHeight(text, outRect.width - 16f);
            var view = new Rect(0, 0, outRect.width - 16f, h);
            Widgets.BeginScrollView(outRect, ref scroll, view);
            Widgets.Label(new Rect(0, 0, view.width, h), text);
            Widgets.EndScrollView();
        }
    }
}
