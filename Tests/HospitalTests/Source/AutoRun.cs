using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;

namespace HospitalTests
{
    /// <summary>
    /// Command line mode: -hospitaltest=SaveName. Never active without the argument.
    /// Flow: startup finished -> load save -> (GameComponent) wait some frames after the map is up -> run tests -> write results -> quit.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AutoRun
    {
        public const string Arg = "hospitaltest";
        public static bool Active;
        public static string SaveName;

        static AutoRun()
        {
            SaveName = ReadArg();
            if (string.IsNullOrEmpty(SaveName)) return;
            Active = true;
            Log.Message("[HospitalTests] auto mode, save: " + SaveName);
            LongEventHandler.ExecuteWhenFinished(Begin);
        }

        private static string ReadArg()
        {
            if (GenCommandLine.TryGetCommandLineArg(Arg, out string v) && !string.IsNullOrEmpty(v)) return v;
            // fallback: manual parse of -hospitaltest=Name
            foreach (string a in Environment.GetCommandLineArgs())
            {
                string prefix = "-" + Arg + "=";
                if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return a.Substring(prefix.Length).Trim('"');
            }
            return null;
        }

        private static void Begin()
        {
            try
            {
                if (!File.Exists(GenFilePaths.FilePathForSavedGame(SaveName)))
                {
                    Fail("save not found: " + GenFilePaths.FilePathForSavedGame(SaveName));
                    return;
                }
                GameDataSaveLoader.LoadGame(SaveName);
            }
            catch (Exception e)
            {
                Fail("loading save failed: " + e);
            }
        }

        public static void Fail(string reason)
        {
            TestRunner.Report(new List<TestResult>(), reason);
            Quit();
        }

        public static void Quit()
        {
            Log.Message("[HospitalTests] quitting");
            Application.Quit();
        }
    }

    /// <summary>Added to the Game automatically (GameComponent subclass with a Game ctor). Does nothing unless AutoRun.Active.</summary>
    public class AutoRunGameComponent : GameComponent
    {
        private const int FramesAfterMapReady = 120;
        private int frames;
        private bool done;

        public AutoRunGameComponent(Game game) { }

        public override void GameComponentUpdate()
        {
            if (!AutoRun.Active || done) return;
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null || Find.TickManager == null) return;
            if (LongEventHandler.AnyEventNowOrWaiting) return;
            if (++frames < FramesAfterMapReady) return;
            done = true;
            try
            {
                TestRunner.RunAll();
            }
            catch (Exception e)
            {
                AutoRun.Fail("test run crashed: " + e);
                return;
            }
            AutoRun.Quit();
        }
    }
}
