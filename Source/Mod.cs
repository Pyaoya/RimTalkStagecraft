using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimTalkStagecraft
{
    // A few things RimTalk's pawns are told, or can be made to do, on top of RimTalk itself:
    // where they are (Place), who the people their jobs are aimed at are (Targets), conditions
    // that last for days and quests just taken (News), how a raid ended (Raids), director's notes
    // and a speak button for pawns the player cannot order about (Director), the name of the class
    // a pawn is in with Progression: Education (Education), a world brief for RimTalk Literature's
    // writing (Literature), and a quiet repair to RimTalk Log To File (LogToFileFix). RimTalk is
    // reached by name throughout: when an update renames what one part uses, that part is left
    // out with a line in the log and the rest go on.
    // Nothing is written into saves, so the mod can be added or taken out at any time.
    public class StagecraftMod : Mod
    {
        internal const string Id = "pyaoyao.rimtalkstagecraft";
        internal const string Tag = "[RimTalk Stagecraft] ";
        internal static Settings Settings;
        internal static Harmony Patcher;

        public StagecraftMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<Settings>();
            Patcher = new Harmony(Id);
            Patcher.PatchAll();
            Literature.Apply(Patcher);
            Targets.Apply(Patcher);
            LogToFileFix.Apply(Patcher);
        }

        public override string SettingsCategory()
        {
            return "Stagecraft_SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.Draw(inRect);
        }
    }

    // RimTalk's own parts are looked up once a game is up, when every mod has finished loading.
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    internal static class Patch_GameFinalizeInit
    {
        private static void Postfix()
        {
            Targets.ReadWords();
            News.Register();
            News.NewGame();
            Director.Register(StagecraftMod.Patcher);
            Director.NewGame();
            Raids.NewGame();
            Education.Register(StagecraftMod.Patcher);
            SelfTest.Run();   // only with -stagecraftselftest on the command line
        }
    }

    // Every 250 ticks of play: nothing runs while the game is paused.
    [HarmonyPatch(typeof(Game), nameof(Game.UpdatePlay))]
    internal static class Patch_GameUpdatePlay
    {
        private const int Interval = 250;
        private static int next, last = -1;

        private static void Postfix()
        {
            try
            {
                if (Current.ProgramState != ProgramState.Playing || Find.TickManager == null || Find.World == null) return;
                int now = Find.TickManager.TicksGame;
                if (now < last) next = 0;   // an earlier save was loaded
                last = now;
                if (now < next) return;
                next = now + Interval;
                Raids.Check(now);
                News.Follow(now);
                Director.Tick(now);
            }
            catch (Exception e)
            {
                next = last + 2500;
                Log.Error(StagecraftMod.Tag + "check failed: " + e);
            }
        }
    }
}
