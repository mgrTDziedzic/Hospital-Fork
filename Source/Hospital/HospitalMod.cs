using HarmonyLib;
using UnityEngine;
using Verse;

namespace Hospital
{

	[StaticConstructorOnStartup]
	public class HospitalMod : Mod
	{
		public static Harmony harmonyInstance;
		public static Settings Settings;
		
		public HospitalMod(ModContentPack content) : base(content)
		{
			Settings = GetSettings<Settings>();

			harmonyInstance = new Harmony("Niejeden.HospitalFork");
            harmonyInstance.PatchAll();

            // Optional mods: one info line each when detected, nothing otherwise.
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                Compat.MultiDoctorCompat.Startup();
                Compat.PlasteelCompat.Startup();
                Compat.MedPodCompat.Startup();
            });
        }
        /// <summary>
        /// The (optional) GUI part to set your settings.
        /// </summary>
        /// <param name="inRect">A Unity Rect with the size of the settings window.</param>
        public override void DoSettingsWindowContents(Rect inRect)
        {
	        Settings.DoWindowContents(inRect);
        }

        /// <summary>
        /// Override SettingsCategory to show up in the list of settings.
        /// Using .Translate() is optional, but does allow for localisation.
        /// </summary>
        /// <returns>The (translated) mod name.</returns>
        public override string SettingsCategory()
        {
	        return "Hospital";
        }
	}
}