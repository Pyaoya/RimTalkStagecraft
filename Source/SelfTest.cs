using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimTalkStagecraft
{
    // A self-test, for development. Start the game with -quicktest -stagecraftselftest: once the
    // map is up it writes into the log what each part found of RimTalk, the location line for the
    // map (generated, then with a description of the player's, then generated again), a colonist's activity aimed at a colonist, a prisoner and a named dog (as RimTalk gives
    // it, with and without its context optimization), how often a cold snap is to be talked about,
    // whether a topic reaches RimTalk's pool, a class's name in an activity (with Progression:
    // Education) and the topics of a raid won, won at a cost and lost. Every line starts with "[RimTalk Stagecraft]
    // self-test". Without the switch nothing here runs.
    internal static class SelfTest
    {
        private const string T = "[RimTalk Stagecraft] self-test: ";
        private static bool done;

        internal static void Run()
        {
            if (done || !GenCommandLine.CommandLineArgPassed("stagecraftselftest")) return;
            done = true;
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                try { Go(); }
                catch (Exception e) { Log.Error(T + "FAILED with " + e); }
                Log.Message(T + "finished");
            });
        }

        private static void Go()
        {
            Map map = Find.AnyPlayerHomeMap ?? Find.Maps.FirstOrDefault();
            if (map == null) { Log.Error(T + "no map"); return; }
            Log.Message(T + "director's notes ready: " + Director.Ready + "; RimTalk's pool reached: " + News.PoolReady);
            Log.Message(T + "location: " + (Place.Line(map) ?? "(nothing)"));
            PlaceNotes.Set(map.Parent, "self-test description");
            Log.Message(T + "location with the player's own description: " + (Place.Line(map) ?? "(nothing)"));
            PlaceNotes.Set(map.Parent, null);
            Log.Message(T + "and back to the generated one: " + (Place.Line(map) ?? "(nothing)"));

            List<Pawn> people = map.mapPawns.FreeColonistsSpawned.ToList();
            if (people.Count < 2) { Log.Error(T + "needs two colonists, has " + people.Count); return; }
            Pawn a = people[0], b = people[1];
            IntVec3 near = CellFinder.StandableCellNear(a.Position, map, 5f);

            Pawn prisoner = PawnGenerator.GeneratePawn(PawnKindDefOf.Villager, null);
            GenSpawn.Spawn(prisoner, near, map);
            prisoner.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);

            Pawn dog = PawnGenerator.GeneratePawn(PawnKindDef.Named("Husky"), Faction.OfPlayer);
            dog.Name = new NameSingle("Biscuit");
            GenSpawn.Spawn(dog, near, map);

            MethodInfo activity = GenTypes.GetTypeInAnyAssembly("RimTalk.Util.PawnUtil").GetMethod("GetPawnActivity",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Pawn), typeof(HashSet<Pawn>), typeof(bool) }, null);
            foreach (Pawn target in new[] { b, prisoner, dog })
            {
                Job job = JobMaker.MakeJob(JobDefOf.FollowClose, target);
                job.followRadius = 4f;
                job.expiryInterval = 60000;
                a.jobs.StartJob(job, JobCondition.InterruptForced);
                string on = (string)activity.Invoke(null, new object[] { a, new HashSet<Pawn>(), true });
                string off = (string)activity.Invoke(null, new object[] { a, new HashSet<Pawn> { target }, false });
                Log.Message(T + "activity, optimization on: " + on + " | off: " + off);
            }

            GameCondition cold = GameConditionMaker.MakeCondition(GameConditionDefOf.ColdSnap, 4 * 60000);
            map.gameConditionManager.RegisterCondition(cold);
            Log.Message(T + "a four-day cold snap is talked about " + News.WantedFor(cold) + " time(s); the topic reads: " + News.PromptFor(cold, 3 * 60000));
            GameCondition eclipse = GameConditionMaker.MakeCondition(GameConditionDefOf.Eclipse, 30000);
            map.gameConditionManager.RegisterCondition(eclipse);
            Log.Message(T + "a half-day eclipse is talked about " + News.WantedFor(eclipse) + " time(s)");

            Log.Message(T + "a topic put into RimTalk's pool is waiting there: " + News.TryTopic("(self-test topic)", map));

            ClassName(map, a);
            Raid(map, people);
        }

        // With Progression: Education in the mod list: a pawn in a class is reported with its name.
        private static void ClassName(Map map, Pawn pawn)
        {
            Type job = GenTypes.GetTypeInAnyAssembly("ProgressionEducation.LordJob_AttendClass");
            Type group = GenTypes.GetTypeInAnyAssembly("ProgressionEducation.StudyGroup");
            if (job == null || group == null) { Log.Message(T + "class name: Progression: Education is not in the mod list"); return; }
            object study = Activator.CreateInstance(group, pawn, new List<Pawn>(), "self-test class", 1, 0, 1);
            Lord lord = LordMaker.MakeNewLord(Faction.OfPlayer, (LordJob)Activator.CreateInstance(job, study), map, new[] { pawn });
            MethodInfo activity = GenTypes.GetTypeInAnyAssembly("RimTalk.Util.PawnUtil").GetMethod("GetActivity",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Pawn) }, null);
            Log.Message(T + "class name, activity in a class: " + activity.Invoke(null, new object[] { pawn }));
            map.lordManager.RemoveLord(lord);
        }

        // Raids of two, decided by hand, with Check() called directly as a play check would and the
        // clock moved on by hand. A raid is told the same however it came: under the game's own lord
        // job, a mod's own, or none. No contact and guards who stay put are not told at all.
        private static void Raid(Map map, List<Pawn> people)
        {
            Faction foes = Find.FactionManager.RandomEnemyFaction(false, false, false);
            if (foes == null || people.Count < 3) { Log.Message(T + "raid: needs an enemy faction and three colonists"); return; }
            IntVec3 near = people[2].Position;
            int now = Find.TickManager.TicksGame;

            // How fierce goes by what it did to us, not by how many came or how long they stayed.
            Fight(map, foes, near, null, "of forty, nobody touched, should be low", ref now, null, 40);
            Fight(map, foes, near, null, "with everybody grazed, should be low", ref now, r => Bruise(people, 3f));
            Fight(map, foes, near, null, "for hours, nobody touched, should be low", ref now, null, 2, 3 * 2500);
            Fight(map, foes, near, null, "for hours, somebody grazed, should be mid", ref now, r => Bruise(people.GetRange(0, 1), 3f), 2, 3 * 2500);
            Fight(map, foes, near, null, "with everybody badly hurt, should be mid", ref now, r => Bruise(people, 16f));

            Fight(map, foes, near, new LordJob_AssaultColony(foes), "won under the game's lord job", ref now, null);
            Fight(map, foes, near, new ModsOwnRaid(), "won under a mod's lord job", ref now, null);
            Fight(map, foes, near, null, "won with no lord at all", ref now, null);
            Fight(map, foes, near, new LordJob_AssaultColony(foes), "won at a cost", ref now, r => people[0].Kill(null));
            Fight(map, foes, near, new LordJob_AssaultColony(foes), "lost, one carried off", ref now, r => foes.kidnapped.Kidnap(people[1], r[0]));

            // A second wave within the hour is the same fight.
            List<Pawn> first = Wave(map, foes, near, new LordJob_AssaultColony(foes));
            Raids.LastTopic = null;
            Raids.Check(now);
            foreach (Pawn p in first) p.Kill(null);
            Raids.Check(now += 2000);
            List<Pawn> second = Wave(map, foes, near, null);
            Raids.Check(now += 2000);
            Log.Message(T + "raid, second wave within the hour, no topic yet: " + (Raids.LastTopic == null));
            foreach (Pawn p in second) p.Kill(null);
            Raids.Check(now += Raids.SettleTicks);
            Log.Message(T + "raid, both waves told as one: " + (Raids.LastTopic ?? "(none)"));

            // They came and went without a fight.
            List<Pawn> passing = Wave(map, foes, near, null);
            Raids.LastTopic = null;
            Raids.Check(now);
            foreach (Pawn p in passing) p.ExitMap(false, Rot4.North);
            Raids.Check(now += Raids.SettleTicks);
            Log.Message(T + "raid, no contact, nothing told: " + (Raids.LastTopic == null));

            // Guards who stay where they are do not count as a raid at all.
            List<Pawn> guards = Wave(map, foes, near, new LordJob_DefendPoint(near));
            Raids.Check(now);
            Log.Message(T + "raid, guards staying put not watched: " + !Raids.Watching(map));
            foreach (Pawn p in guards) p.Destroy();
        }

        private static void Bruise(List<Pawn> who, float severity)
        {
            foreach (Pawn p in who)
            {
                Hediff bruise = HediffMaker.MakeHediff(HediffDef.Named("Bruise"), p, p.RaceProps.body.corePart);
                bruise.Severity = severity;
                p.health.AddHediff(bruise);
            }
        }

        // With dragsOn, the first raider falls at once and the others that much later.
        private static void Fight(Map map, Faction foes, IntVec3 near, LordJob job, string what, ref int now, Action<List<Pawn>> during, int count = 2, int dragsOn = 0)
        {
            List<Pawn> raiders = Wave(map, foes, near, job, count);
            Raids.LastTopic = null;
            Raids.Check(now);
            bool watched = Raids.Watching(map);
            if (during != null) during(raiders);
            if (dragsOn > 0)
            {
                raiders[0].Kill(null);
                Raids.Check(now += 250);
                Raids.Check(now += dragsOn);
            }
            foreach (Pawn p in raiders) if (!p.Dead) p.Kill(null);
            Raids.Check(now += Raids.SettleTicks);
            Log.Message(T + "raid " + what + ", watched: " + watched + "; topic: " + (Raids.LastTopic ?? "(none)"));
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)   // what one raid did must not decide the next
                foreach (Hediff h in new List<Hediff>(p.health.hediffSet.hediffs))
                    if (h.def.defName == "Bruise") p.health.RemoveHediff(h);
        }

        private static List<Pawn> Wave(Map map, Faction foes, IntVec3 near, LordJob job, int count = 2)
        {
            List<Pawn> raiders = new List<Pawn>();
            for (int i = 0; i < count; i++)
            {
                Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Villager, foes);
                GenSpawn.Spawn(p, CellFinder.StandableCellNear(near, map, 8f), map);
                raiders.Add(p);
            }
            if (job != null) LordMaker.MakeNewLord(foes, job, map, raiders);
            return raiders;
        }

        private class ModsOwnRaid : LordJob
        {
            public override StateGraph CreateGraph()
            {
                StateGraph graph = new StateGraph();
                graph.AddToil(new LordToil_AssaultColony());
                return graph;
            }
        }
    }
}
