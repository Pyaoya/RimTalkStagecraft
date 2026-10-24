using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimTalkStagecraft
{
    // A self-test, for development. Start the game with -quicktest -stagecraftselftest: once the
    // map is up it writes into the log what each part found of RimTalk, the location line for the
    // map (generated, then with a description of the player's, then generated again), a colonist's activity aimed at a colonist, a prisoner and a named dog (as RimTalk gives
    // it, with and without its context optimization), how often a cold snap is to be talked about
    // and whether a topic reaches RimTalk's pool. Every line starts with "[RimTalk Stagecraft]
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
        }
    }
}
