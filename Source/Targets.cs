using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimTalkStagecraft
{
    // What a pawn is doing reaches the model as the game's own job report, "Tending to Mira":
    // a bare name. Unless the other one stands nearby and is listed, the model has to guess who
    // that is, and makes up a patient, a prisoner or a dog. Every pawn the job is aimed at gets
    // what it is to the colony after its name: colonist, slave, prisoner, guest, enemy, animal,
    // mechanoid. RimTalk does this itself, at length, when its context optimization is off;
    // then nothing is added here.
    internal static class Targets
    {
        private static string format, colonist, slave, prisoner, guest, enemy, stranger, animal, mech, insect, entity;

        internal static void Apply(Harmony harmony)
        {
            try
            {
                Type util = GenTypes.GetTypeInAnyAssembly("RimTalk.Util.PawnUtil");
                MethodInfo m = util == null ? null : util.GetMethod("GetPawnActivity", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Pawn), typeof(HashSet<Pawn>), typeof(bool) }, null);
                if (m == null || m.ReturnType != typeof(string))
                {
                    if (util != null) Log.Warning(StagecraftMod.Tag + "RimTalk has changed: the people a job is aimed at are left unmarked.");
                    return;
                }
                harmony.Patch(m, null, new HarmonyMethod(typeof(Targets), nameof(Mark)));
            }
            catch (Exception e)
            {
                Log.Warning(StagecraftMod.Tag + "the people a job is aimed at are left unmarked: " + e.Message);
            }
        }

        // Read on the main thread when a game starts: RimTalk may build its prompts on another.
        internal static void ReadWords()
        {
            format = W("Stagecraft_TagFormat");
            colonist = W("Stagecraft_TagColonist");
            slave = W("Stagecraft_TagSlave");
            prisoner = W("Stagecraft_TagPrisoner");
            guest = W("Stagecraft_TagGuest");
            enemy = W("Stagecraft_TagEnemy");
            stranger = W("Stagecraft_TagStranger");
            animal = W("Stagecraft_TagAnimal");
            mech = W("Stagecraft_TagMech");
            insect = W("Stagecraft_TagInsect");
            entity = W("Stagecraft_TagEntity");
        }

        private static string W(string key)
        {
            return key.CanTranslate() ? key.Translate().ToString() : null;
        }

        // __2: RimTalk's context optimization is on, so it left the names as they are.
        private static void Mark(Pawn __0, bool __2, ref string __result)
        {
            if (!__2 || __result.NullOrEmpty() || __0 == null || format == null) return;
            if (StagecraftMod.Settings == null || !StagecraftMod.Settings.targetTags) return;
            try
            {
                __result = Tagged(__0, __result);
            }
            catch (Exception)
            {
            }
        }

        private static string Tagged(Pawn pawn, string text)
        {
            List<Pawn> others = new List<Pawn>();
            Job job = pawn.CurJob;
            if (job != null)
            {
                Add(others, job.targetA, pawn);
                Add(others, job.targetB, pawn);
                Add(others, job.targetC, pawn);
                if (job.targetQueueA != null) foreach (LocalTargetInfo t in job.targetQueueA) Add(others, t, pawn);
                if (job.targetQueueB != null) foreach (LocalTargetInfo t in job.targetQueueB) Add(others, t, pawn);
            }
            Pawn foe = pawn.mindState != null ? pawn.mindState.enemyTarget as Pawn : null;
            if (foe != null && foe != pawn && !others.Contains(foe)) others.Add(foe);
            if (others.Count == 0) return text;
            // Longer names first, so that one name inside another is not marked in the wrong place.
            others.Sort((a, b) => (b.LabelShort ?? "").Length.CompareTo((a.LabelShort ?? "").Length));
            foreach (Pawn p in others)
            {
                string name = p.LabelShort, tag = Tag(p);
                if (name.NullOrEmpty() || tag == null) continue;
                int at = text.IndexOf(name, StringComparison.Ordinal);
                if (at < 0) continue;
                int end = at + name.Length;
                if (end < text.Length && (text[end] == '（' || text[end] == '(')) continue;
                text = text.Insert(end, string.Format(format, tag));
            }
            return text;
        }

        private static void Add(List<Pawn> list, LocalTargetInfo t, Pawn self)
        {
            Pawn p = t.Thing as Pawn;
            if (p != null && p != self && !list.Contains(p)) list.Add(p);
        }

        private static string Tag(Pawn p)
        {
            RaceProperties race = p.RaceProps;
            if (race == null) return null;
            Faction us = Faction.OfPlayer;
            if (race.Humanlike)
            {
                if (p.IsPrisonerOfColony) return prisoner;
                if (p.IsSlaveOfColony) return slave;
                if (p.Faction == us) return colonist;
                if (p.Faction == null) return stranger;
                if (p.HostileTo(us)) return enemy;
                return guest;
            }
            if (race.IsMechanoid) return mech;
            if (race.IsAnomalyEntity) return entity;
            if (race.Insect) return insect;
            if (race.Animal)
            {
                // An animal without a name is called by its kind already ("tending to a cow").
                if (p.Name == null || p.Name.Numerical) return null;
                return animal == null ? null : string.Format(animal, p.def.label);
            }
            return null;
        }
    }
}
