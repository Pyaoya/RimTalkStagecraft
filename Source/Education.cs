using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimTalkStagecraft
{
    // Progression: Education lets the player name each class, but what a pawn is doing reaches the
    // model as the job's report alone: "teaching class", "attending class". The name of the class
    // is added after it, for the teacher and the students alike. Without that mod nothing is patched.
    internal static class Education
    {
        private static bool tried;
        private static Type classJob;
        private static FieldInfo studyGroup, className;
        private static string format;

        // Looked up once a game is up, whatever the load order was. The wording is read here, on
        // the main thread, because RimTalk may build its prompts on another.
        internal static void Register(Harmony harmony)
        {
            format = "Stagecraft_ClassName".CanTranslate() ? "Stagecraft_ClassName".Translate().ToString() : null;
            if (tried) return;
            tried = true;
            try
            {
                classJob = GenTypes.GetTypeInAnyAssembly("ProgressionEducation.LordJob_AttendClass");
                Type group = GenTypes.GetTypeInAnyAssembly("ProgressionEducation.StudyGroup");
                if (classJob == null || group == null) return;
                studyGroup = classJob.GetField("studyGroup");
                className = group.GetField("className");
                Type util = GenTypes.GetTypeInAnyAssembly("RimTalk.Util.PawnUtil");
                MethodInfo activity = util == null ? null : util.GetMethod("GetActivity", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Pawn) }, null);
                if (studyGroup == null || className == null || activity == null || activity.ReturnType != typeof(string))
                {
                    Log.Warning(StagecraftMod.Tag + "RimTalk or Progression: Education has changed: class names are left out.");
                    return;
                }
                harmony.Patch(activity, null, new HarmonyMethod(typeof(Education), nameof(Mark)));
                Log.Message(StagecraftMod.Tag + "RimTalk is told the name of the class a pawn is in.");
            }
            catch (Exception e)
            {
                Log.Warning(StagecraftMod.Tag + "class names are left out: " + e.Message);
            }
        }

        private static void Mark(Pawn __0, ref string __result)
        {
            if (__result.NullOrEmpty() || __0 == null || format == null) return;
            if (StagecraftMod.Settings == null || !StagecraftMod.Settings.classNames) return;
            try
            {
                Lord lord = __0.GetLord();
                if (lord == null || !classJob.IsInstanceOfType(lord.LordJob)) return;
                object group = studyGroup.GetValue(lord.LordJob);
                string name = group == null ? null : className.GetValue(group) as string;
                if (!name.NullOrEmpty() && !__result.Contains(name)) __result += string.Format(format, name);
            }
            catch (Exception)
            {
            }
        }
    }
}
