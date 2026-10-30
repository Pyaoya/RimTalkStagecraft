using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimTalkStagecraft
{
    // RimTalk hears of a raid from its letter, when it arrives, and never of how it ended. Once the
    // attackers on a home map are down or gone, the model is told what happened: who won, how long
    // it lasted, how fierce it was. What the colonists make of it is left to the model.
    //
    // An attacker is any awake pawn of a hostile faction the game counts as an active threat,
    // however it came: mods' raids need not go through the game's raid incident or lord jobs. What
    // is left out is what never came to attack: lord jobs named for staying put or for visiting
    // (NotAttacking), so a mod's guards are left out by the same words. Better silent than wrong,
    // so nothing is said when there was no contact, or when the two sides have made peace.
    //
    // A raid always ends with the raiders gone, so what decides it is what they took with them.
    // It was lost if a colonist was carried off, loot was carried off, raiders walked away from a
    // base damaged as far as the game's own raiders call "satisfied", at least two colonists died
    // and they were half of those present, or colonists died and not one raider fell. Otherwise it
    // was won, at a cost if somebody died; if no raider fell either, it is told as undecided.
    //
    // How fierce is what the raid did to the colony, not how many came: three colonists with a mod's
    // weapons may mow down hundreds and not feel it, and the head count is told anyway. Very, if
    // someone died or was carried off, half of those present went down, or the base was wrecked.
    // Hard, if anyone went down, half were badly hurt, the base was visibly damaged, or the fighting
    // went on for hours and cost something. Otherwise one-sided.
    //
    // The length told is of the fighting, from the first harm on either side to the last: raiders
    // may walk or wait for hours and then die in seconds.
    //
    // Nothing is saved: a raid under way when a game is loaded is picked up again, and losses from
    // before the save are not known.
    internal static class Raids
    {
        internal const int SettleTicks = 2500;       // an hour with no attacker: pods still in the air and second waves are the same fight
        private const int TalkLife = 30000;          // half a day for somebody to be free to talk
        private const int ManyDead = 2;              // one death of two colonists is a loss, not a massacre
        private const float WreckedFraction = 0.25f; // LordJob_AssaultColony: 0.25 to 0.35 of the colony's health
        private const float WreckedAtLeast = 900f;   // and at least this much
        private const float DamagedFraction = 0.2f;  // of that: enough to be seen and talked about
        private const float HurtBy = 0.01f;          // of a colonist's health lost, against rounding
        private const float BadlyBy = 0.2f;          // of it: a solid hit or more, not a graze
        private const int LongFight = 5000;          // two hours of it

        private static readonly string[] NotAttacking =
            { "Defend", "Visit", "Trade", "PrisonBreak", "Rebellion", "Sleep", "SitePawns", "Nest", "Hive", "ManTurrets", "Structure" };

        private class Battle
        {
            public int firstContact = -1, lastContact, lastSeen, colonyHealth;
            public float damageBefore, harm;
            public readonly HashSet<Faction> factions = new HashSet<Faction>();
            public readonly HashSet<Pawn> enemies = new HashSet<Pawn>();
            public readonly HashSet<Pawn> ours = new HashSet<Pawn>();
            public readonly Dictionary<Pawn, string> stealing = new Dictionary<Pawn, string>();
            public readonly Dictionary<Pawn, float> healthBefore = new Dictionary<Pawn, float>();
            public readonly HashSet<Pawn> downBefore = new HashSet<Pawn>();
            public readonly HashSet<Pawn> hurt = new HashSet<Pawn>();
            public readonly HashSet<Pawn> badly = new HashSet<Pawn>();
            public readonly HashSet<Pawn> fell = new HashSet<Pawn>();
        }

        private static readonly Dictionary<Map, Battle> battles = new Dictionary<Map, Battle>();
        private static bool broken;

        // For the self-test.
        internal static string LastTopic;
        internal static bool Watching(Map map) { return battles.ContainsKey(map); }

        internal static void NewGame()
        {
            battles.Clear();
        }

        // Called every check.
        internal static void Check(int now)
        {
            if (broken) return;
            try
            {
                if (!StagecraftMod.Settings.raidTalk || !News.PoolReady) { battles.Clear(); return; }
                List<Map> maps = Find.Maps;
                for (int i = 0; i < maps.Count; i++)
                    if (maps[i].IsPlayerHome) Watch(maps[i], now);
                foreach (Map gone in battles.Keys.Where(m => !maps.Contains(m)).ToList()) battles.Remove(gone);   // a map that was given up
            }
            catch (Exception e)
            {
                broken = true;
                Log.Warning(StagecraftMod.Tag + "following raids failed and is off until the game restarts: " + e);
            }
        }

        private static void Watch(Map map, int now)
        {
            Battle b;
            battles.TryGetValue(map, out b);
            foreach (IAttackTarget target in map.attackTargetsCache.TargetsHostileToColony)
            {
                Pawn p = target.Thing as Pawn;
                if (p == null || !IsAttacker(p, target)) continue;
                if (b == null)
                {
                    b = new Battle
                    {
                        damageBefore = map.damageWatcher.DamageTakenEver,
                        colonyHealth = map.wealthWatcher.HealthTotal
                    };
                    battles[map] = b;
                }
                b.lastSeen = now;
                b.factions.Add(p.Faction);
                b.enemies.Add(p);
                // What a raider leaves the map carrying is destroyed with it. Only a job of stealing
                // counts: raiders of a siege, say, carry their own shells.
                Thing held = p.CurJobDef == JobDefOf.Steal && p.carryTracker != null ? p.carryTracker.CarriedThing : null;
                if (held != null) b.stealing[p] = held.LabelNoCount;
                else b.stealing.Remove(p);
            }
            if (b == null) return;
            List<Pawn> people = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < people.Count; i++) Follow(b, people[i]);
            NoteContact(b, map, now);
            if (now - b.lastSeen < SettleTicks) return;
            battles.Remove(map);
            string topic = Topic(b, map);
            if (topic == null) return;
            LastTopic = topic;
            News.TalkSoon(topic, map, TalkLife);
        }

        private static bool IsAttacker(Pawn p, IAttackTarget target)
        {
            if (p.Faction == null || p.Faction == Faction.OfPlayer || !p.Faction.HostileTo(Faction.OfPlayer)) return false;
            if (!GenHostility.IsActiveThreatToPlayer(target)) return false;
            Lord lord = p.GetLord();
            if (lord == null || lord.LordJob == null) return true;
            string job = lord.LordJob.GetType().Name;
            for (int i = 0; i < NotAttacking.Length; i++)
                if (job.IndexOf(NotAttacking[i], StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        // Being hurt is noted while it is seen: by the end they may be up again, or dead.
        private static void Follow(Battle b, Pawn p)
        {
            b.ours.Add(p);
            float health = p.health.summaryHealth.SummaryHealthPercent, before;
            if (!b.healthBefore.TryGetValue(p, out before))
            {
                b.healthBefore[p] = health;
                if (p.Downed) b.downBefore.Add(p);   // already in bed with something else
                return;
            }
            if (health < before - HurtBy) b.hurt.Add(p);
            if (health < before - BadlyBy) b.badly.Add(p);
            if (p.Downed && !b.downBefore.Contains(p)) b.fell.Add(p);
        }

        private static void NoteContact(Battle b, Map map, int now)
        {
            float harm = b.hurt.Count + b.fell.Count + b.enemies.Count(Fallen)
                + b.ours.Count(p => p.Dead || PawnUtility.IsKidnappedPawn(p))
                + map.damageWatcher.DamageTakenEver - b.damageBefore;
            if (harm == b.harm) return;
            b.harm = harm;
            if (b.firstContact < 0) b.firstContact = now;
            b.lastContact = now;
        }

        private static bool Fallen(Pawn p)
        {
            return p.Dead || p.Downed || p.IsPrisonerOfColony;
        }

        // The wording is in the Keyed file. Null: nothing that can be told without being wrong.
        private static string Topic(Battle b, Map map)
        {
            if (b.enemies.Count == 0 || b.ours.Count == 0 || map.mapPawns.FreeColonistsSpawnedCount == 0) return null;
            foreach (Faction f in b.factions)
                if (!f.HostileTo(Faction.OfPlayer)) return null;   // made peace, or were paid to go

            int down = 0, away = 0;
            List<string> loot = new List<string>();
            foreach (Pawn p in b.enemies)
            {
                if (Fallen(p)) { down++; continue; }
                if (p.Spawned || p.Destroyed) continue;   // changed sides, gone mad, or a summons that ran out
                away++;
                string what;
                if (b.stealing.TryGetValue(p, out what) && !loot.Contains(what)) loot.Add(what);
            }

            List<string> dead = new List<string>(), taken = new List<string>(), fell = new List<string>();
            int hurt = 0;
            foreach (Pawn p in b.ours)
            {
                if (p.Dead) { dead.Add(p.LabelShort); continue; }
                if (PawnUtility.IsKidnappedPawn(p)) { taken.Add(p.LabelShort); continue; }
                if (b.fell.Contains(p)) fell.Add(p.LabelShort);
                if (b.hurt.Contains(p) || b.fell.Contains(p)) hurt++;
            }

            float damage = map.damageWatcher.DamageTakenEver - b.damageBefore;
            float wreckedAt = Math.Max(b.colonyHealth * WreckedFraction, WreckedAtLeast);
            bool wrecked = damage >= wreckedAt, damaged = damage >= wreckedAt * DamagedFraction;
            if (down == 0 && dead.Count == 0 && taken.Count == 0 && hurt == 0 && !damaged) return null;   // they came and went without a fight

            List<string> why = new List<string>();
            if (taken.Count > 0) why.Add("Stagecraft_RaidWhyTaken".Translate(taken.ToCommaList()).ToString());
            if (loot.Count > 0) why.Add("Stagecraft_RaidWhyLooted".Translate(loot.ToCommaList()).ToString());
            if (away > 0 && wrecked) why.Add("Stagecraft_RaidWhyWrecked".Translate().ToString());
            if (dead.Count >= ManyDead && dead.Count * 2 >= b.ours.Count) why.Add("Stagecraft_RaidWhyManyDead".Translate().ToString());
            if (down == 0 && dead.Count > 0) why.Add("Stagecraft_RaidWhyUnscathed".Translate().ToString());

            int fought = Math.Min(b.lastContact, b.lastSeen) - b.firstContact;   // harm after the last raider was seen is a fire, or bleeding
            bool wornDown = fought >= LongFight && (hurt > 0 || damage > 0f);
            string fierce = dead.Count > 0 || taken.Count > 0 || wrecked || fell.Count * 2 >= b.ours.Count ? "Stagecraft_RaidFierceHigh"
                : fell.Count > 0 || b.badly.Count * 2 >= b.ours.Count || damaged || wornDown ? "Stagecraft_RaidFierceMid"
                : "Stagecraft_RaidFierceLow";
            int hours = fought / 2500;
            List<string> facts = new List<string>
            {
                hours > 0 ? "Stagecraft_RaidHours".Translate(hours).ToString() : "Stagecraft_RaidShort".Translate().ToString(),
                fierce.Translate().ToString(),
                "Stagecraft_RaidFoes".Translate(b.enemies.Count, down, away).ToString(),
                "Stagecraft_RaidOurs".Translate(b.ours.Count, hurt).ToString()
            };
            if (dead.Count > 0) facts.Add("Stagecraft_RaidOursDead".Translate(dead.ToCommaList()).ToString());
            if (fell.Count > 0) facts.Add("Stagecraft_RaidOursFell".Translate(fell.ToCommaList()).ToString());
            if (wrecked) facts.Add("Stagecraft_RaidBaseWrecked".Translate().ToString());
            else if (damaged) facts.Add("Stagecraft_RaidBaseDamaged".Translate().ToString());

            List<string> names = new List<string>();
            foreach (Faction f in b.factions) names.Add(f.Name.NullOrEmpty() ? f.def.label : f.Name);
            string foe = names.ToCommaList(), told = string.Join("; ", facts.ToArray());
            if (why.Count > 0) return "Stagecraft_RaidLost".Translate(foe, told, string.Join("; ", why.ToArray())).ToString();
            if (dead.Count > 0) return "Stagecraft_RaidCostly".Translate(foe, told).ToString();
            return (down > 0 ? "Stagecraft_RaidWon" : "Stagecraft_RaidUndecided").Translate(foe, told).ToString();
        }
    }
}
