using System.Reflection;
using HarmonyLib;


namespace NoBlockDetach
{
    public static class IngressPoint
    {
        public static void Main()
        {
            NoBlockDetachMod.harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    public class NoBlockDetachMod : ModBase
    {
        const string HarmonyID = "flsoz.ttmm.noblockdetach.mod";
        internal static Harmony harmony = new Harmony(HarmonyID);

        public override bool HasEarlyInit()
        {
            return true;
        }

        internal static bool Inited = false;
        public override void EarlyInit()
        {
            if (!Inited)
            {
                string logLevelMod = CommandLineReader.GetArgument("+log_level_NoBlockDetach");
                string logLevelGeneral = CommandLineReader.GetArgument("+log_level");
                string logLevel = logLevelGeneral;
                if (logLevelMod != null)
                {
                    logLevel = logLevelMod;
                }
                if (logLevel != null)
                {
                    string lower = logLevel.ToLower();
                    if (lower == "trace" || lower == "debug")
                    {
                        Patches.Debug = true;
                    }
                    else
                    {
                        Patches.Debug = false;
                    }
                }
                else
                {
                    Patches.Debug = false;
                }

                Inited = true;
            }
        }

        public override void DeInit()
        {
            harmony.UnpatchAll(HarmonyID);
        }

        public override void Init()
        {
            IngressPoint.Main();
        }
    }
}
