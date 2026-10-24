using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimTalkStagecraft
{
    // RimTalk Literature has the model write books, journals, art and the like from prompts of
    // its own, which say nothing of where the story is set: in a medieval game a diary came out
    // full of patrol robots and satellite stations. Every such prompt gets the player's own short
    // description of the world added to its end (the setting; left empty, nothing is added).
    internal static class Literature
    {
        internal static string Fallback;   // used while the setting is empty (Interop.WorldFallback)

        internal static void Apply(Harmony harmony)
        {
            try
            {
                string[] writers =
                {
                    "synopsis.SynopsisPromptBuilder", "journal.llm.JournalFromSummaryRequest", "authoring.llm.BookFromSummaryRequest",
                    "art.ArtPromptBuilder", "art.llm.PersonaWeaponRequest", "tv.TvProgramPromptBuilder",
                    "events.IdeoDescriptionRewriter", "events.LetterTextRewriter", "events.QuestDescriptionRewriter",
                    "events.quests.AdvertisementQuestRequest", "events.quests.WarningQuestRequest",
                    "events.letters.AllyDiplomacyLetterRequest", "events.letters.FamilyLetterRequest"
                };
                int told = 0, found = 0;
                foreach (string name in writers)
                {
                    Type type = GenTypes.GetTypeInAnyAssembly("RimTalk_LiteratureExpansion." + name);
                    if (type == null) continue;
                    found++;
                    foreach (MethodInfo m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        if (m.Name != "BuildPrompt" || m.ReturnType != typeof(string)) continue;
                        harmony.Patch(m, null, new HarmonyMethod(typeof(Literature), nameof(InThisWorld)));
                        told++;
                    }
                }
                if (found > 0) Log.Message(StagecraftMod.Tag + "RimTalk Literature: " + told + " prompts can be told about this world.");
            }
            catch (Exception e)
            {
                Log.Warning(StagecraftMod.Tag + "RimTalk Literature was left as it is: " + e);
            }
        }

        // The prompts may be built on another thread: only plain strings are read here.
        private static void InThisWorld(ref string __result)
        {
            string own = StagecraftMod.Settings != null ? StagecraftMod.Settings.literatureWorld : null;
            string world = !own.NullOrEmpty() && own.Trim().Length > 0 ? own.Trim() : Fallback;
            if (__result.NullOrEmpty() || world.NullOrEmpty()) return;
            __result = __result + "\n\n" + world;
        }
    }
}
