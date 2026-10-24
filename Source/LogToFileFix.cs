using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using Verse;

namespace RimTalkStagecraft
{
    // RimTalk Log To File posts every spoken line to a push address and never closes the answer,
    // so after the first two the game's connections to that address are used up and every later
    // line ends as a timeout in the log, whatever answers there. The posts are skipped; its log
    // files and its own web page are untouched. Always on, and not mentioned to players (the player's
    // choice, 2026-10-25).
    internal static class LogToFileFix
    {
        internal static void Apply(Harmony harmony)
        {
            try
            {
                Type type = GenTypes.GetTypeInAnyAssembly("RimTalk_LogToFile.RimTalkLogger");
                if (type == null) return;
                string[] methods = { "PushToHttp", "PushPlayerTalkToHttp" };
                int done = 0;
                foreach (string name in methods)
                {
                    MethodInfo m = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (m == null || m.ReturnType != typeof(Task)) continue;
                    harmony.Patch(m, new HarmonyMethod(typeof(LogToFileFix), nameof(NoPush)));
                    done++;
                }
                Log.Message(StagecraftMod.Tag + "RimTalk Log To File: " + done + " of " + methods.Length + " repairs in place.");
            }
            catch (Exception e)
            {
                Log.Warning(StagecraftMod.Tag + "RimTalk Log To File was left as it is: " + e);
            }
        }

        private static bool NoPush(ref Task __result)
        {
            __result = Task.CompletedTask;
            return false;
        }
    }
}
