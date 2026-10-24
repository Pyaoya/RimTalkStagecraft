using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimTalkStagecraft
{
    // RimTalk learns of happenings from letters alone: one chance to talk about a letter in the
    // two game hours after it arrives, and its title listed for half a day. It never reads map
    // conditions, so a cold snap or an eclipse that lasts for days is forgotten once that chance
    // has gone. While a condition lasts it is brought up as a topic a few more times, spread over
    // its days (Follow). It can also be listed in the [Environment] block of every RimTalk prompt
    // (Lines), which is off unless the player turns it on: with it on, one condition was brought
    // up in passing seven times in two and a half days, on top of the talks Follow arranges.
    // A quest the player accepts is brought up the same way, once and at once (TalkSoon).
    internal static class News
    {
        private const int LetterWindow = 6000;   // the letter's own chance lasts 5000 ticks; wait that out first
        private const int MinGap = 7500;         // at least three game hours between two talks on one condition
        private const int Spacing = 90000;       // a day and a half of a condition's time for each talk

        private class Track
        {
            public int done;
            public int lastTalk = -MinGap;
            public object pending;       // the RimTalk request now waiting to be picked up
            public int pendingSince;
            public bool letterChecked;
        }

        private class Topic
        {
            public string prompt;
            public object request;
            public int until;
        }

        // A condition another mod has told about (Interop.FollowCondition): how often, its title's
        // marker to take off, its own wording.
        private class Spec
        {
            public Func<GameCondition, int> talks;
            public string prefix;
            public string promptKey;
        }

        private static readonly Dictionary<string, Spec> specs = new Dictionary<string, Spec>();
        private static readonly List<Topic> soon = new List<Topic>();
        private static readonly Dictionary<GameCondition, Track> tracks = new Dictionary<GameCondition, Track>();
        private static readonly HashSet<GameCondition> seen = new HashSet<GameCondition>();
        private static MethodInfo pullFromPool;   // RimTalk's own "let this colonist take the newest request"; not public
        private static bool tried;
        private static bool poolReady;
        private static MethodInfo poolAdd, poolActive, poolHistory;
        private static PropertyInfo reqStatus, reqCreated, reqRaw;
        private static object eventType;

        internal static void Register()
        {
            if (tried) return;
            tried = true;
            Type api = GenTypes.GetTypeInAnyAssembly("RimTalk.API.RimTalkPromptAPI");
            if (api == null)
            {
                Log.Warning(StagecraftMod.Tag + "RimTalk is not there; nothing is added to it.");
                return;
            }
            try
            {
                Type env = GenTypes.GetTypeInAnyAssembly("RimTalk.API.ContextCategories").GetNestedType("Environment");
                Type position = GenTypes.GetTypeInAnyAssembly("RimTalk.API.ContextHookRegistry").GetNestedType("InjectPosition");
                // The lines go right under "Weather:", so they show only while RimTalk's weather context is on.
                MethodInfo inject = api.GetMethod("InjectEnvironmentSection");
                object weather = env.GetField("Weather").GetValue(null), after = Enum.Parse(position, "After");
                inject.Invoke(null, new object[] { StagecraftMod.Id, "Place", weather, after, new Func<Map, string>(Place.Line), 50 });
                inject.Invoke(null, new object[] { StagecraftMod.Id, "Conditions", weather, after, new Func<Map, string>(Lines), 100 });
                Log.Message(StagecraftMod.Tag + "RimTalk is told what place each map is, and of the conditions now on it.");
            }
            catch (Exception e)
            {
                Log.Warning(StagecraftMod.Tag + "could not add to RimTalk's environment, the place line is left out: " + e);
            }
            try
            {
                Type pool = GenTypes.GetTypeInAnyAssembly("RimTalk.Data.TalkRequestPool");
                Type req = GenTypes.GetTypeInAnyAssembly("RimTalk.Data.TalkRequest");
                poolAdd = pool.GetMethod("Add");
                poolActive = pool.GetMethod("GetAllActive");
                poolHistory = pool.GetMethod("GetHistory");
                reqStatus = req.GetProperty("Status");
                reqCreated = req.GetProperty("CreatedTick");
                reqRaw = req.GetProperty("RawPrompt");
                eventType = Enum.Parse(GenTypes.GetTypeInAnyAssembly("RimTalk.Source.Data.TalkType"), "Event");
                poolReady = poolAdd != null && poolActive != null && poolHistory != null && reqStatus != null && reqCreated != null && reqRaw != null;
                Type ticker = GenTypes.GetTypeInAnyAssembly("RimTalk.Patch.TickManagerPatch");
                if (ticker != null) pullFromPool = ticker.GetMethod("TryGenerateTalkFromPool", BindingFlags.NonPublic | BindingFlags.Static);
                if (poolReady) Log.Message(StagecraftMod.Tag + "conditions come up in RimTalk a few times while they last; quests are talked about when accepted"
                    + (pullFromPool != null ? "." : ", at RimTalk's next turn."));
                else Log.Warning(StagecraftMod.Tag + "RimTalk has changed: conditions and accepted quests are not brought up.");
            }
            catch (Exception e)
            {
                Log.Warning(StagecraftMod.Tag + "could not reach RimTalk's requests, conditions and accepted quests are not brought up: " + e);
            }
        }

        // For the self-test.
        internal static bool PoolReady { get { return poolReady; } }
        internal static int WantedFor(GameCondition c) { return Wanted(c); }
        internal static string PromptFor(GameCondition c, int age) { return Prompt(c, age); }

        internal static bool TryTopic(string prompt, Map map)
        {
            if (!poolReady) return false;
            object request = Add(prompt, map);
            return request != null && Queued(request);
        }

        // What was being followed belongs to the game that was loaded before.
        internal static void NewGame()
        {
            soon.Clear();
            tracks.Clear();
        }

        internal static void FollowCondition(string defName, Func<GameCondition, int> talks, string titlePrefix, string promptKey)
        {
            if (defName.NullOrEmpty()) return;
            specs[defName] = new Spec { talks = talks, prefix = titlePrefix, promptKey = promptKey };
        }

        // How many times in all a condition is to be talked about, the letter's own chance counted.
        private static int Wanted(GameCondition c)
        {
            Spec s;
            if (specs.TryGetValue(c.def.defName, out s)) return s.talks == null ? 0 : s.talks(c);
            Settings set = StagecraftMod.Settings;
            if (!set.conditions || set.Skips(c.def.defName) || c.Label.NullOrEmpty()) return 0;
            return Math.Max(1, Math.Min(set.conditionTalks, 1 + c.Duration / Spacing));
        }

        // The condition's title, without the marker another mod's titles may start with.
        private static string Title(GameCondition c)
        {
            string title = (c.Label ?? "").Trim();
            Spec s;
            if (specs.TryGetValue(c.def.defName, out s) && !s.prefix.NullOrEmpty() && title.StartsWith(s.prefix, StringComparison.Ordinal))
                title = title.Substring(s.prefix.Length).Trim();
            return title;
        }

        private static string Text(GameCondition c)
        {
            return ColoredText.StripTags(c.Description ?? "").Trim();
        }

        // Every condition now on a map that is followed, one line each. The wording is
        // Stagecraft_ConditionLine in the Keyed file, {0} title and {1} description.
        private static string Lines(Map map)
        {
            try
            {
                if (map == null || !StagecraftMod.Settings.conditionLines) return null;
                HashSet<GameConditionDef> shown = new HashSet<GameConditionDef>();
                StringBuilder sb = new StringBuilder();
                foreach (List<GameCondition> list in new[] { map.gameConditionManager.ActiveConditions, Find.World.gameConditionManager.ActiveConditions })
                {
                    foreach (GameCondition c in list)
                    {
                        if (c == null || c.def == null || c.Permanent || c.Expired || Wanted(c) <= 0 || !shown.Add(c.def)) continue;
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append("Stagecraft_ConditionLine".Translate(Title(c), Text(c)).ToString());
                    }
                }
                return sb.Length > 0 ? sb.ToString() : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Called every check. A condition is to be talked about Wanted() times in all. The first is
        // the letter's own chance, made up for here if nobody took it; the rest are spread evenly
        // over the condition's time. Each is a request put into RimTalk's pool and kept from running
        // out there until somebody takes it. Nothing of this is saved: after a load, what was due
        // before counts as done.
        internal static void Follow(int now)
        {
            if (!poolReady) return;
            try
            {
                Map home = Find.AnyPlayerHomeMap;
                if (home == null) return;
                if (soon.Count > 0) Soon(home, now);
                seen.Clear();
                List<Map> maps = Find.Maps;
                for (int i = 0; i < maps.Count; i++)
                    if (maps[i].IsPlayerHome) FollowOn(maps[i], maps[i].gameConditionManager.ActiveConditions, now);
                // World-wide ones are talked about at home.
                FollowOn(home, Find.World.gameConditionManager.ActiveConditions, now);
                if (tracks.Count > 0)
                    foreach (GameCondition gone in tracks.Keys.Where(k => !seen.Contains(k)).ToList()) tracks.Remove(gone);
            }
            catch (Exception e)
            {
                poolReady = false;
                Log.Warning(StagecraftMod.Tag + "bringing conditions up in RimTalk failed and is off until the game restarts: " + e);
            }
        }

        private static void FollowOn(Map map, List<GameCondition> active, int now)
        {
            bool? free = null;
            for (int i = 0; i < active.Count; i++)
            {
                GameCondition c = active[i];
                if (c == null || c.def == null || c.Permanent || c.Expired || !seen.Add(c)) continue;
                int want = Wanted(c);
                if (want <= 0) continue;
                int age = c.TicksPassed;
                Track f;
                if (!tracks.TryGetValue(c, out f))
                {
                    f = new Track();
                    while (f.done < want && Due(c, f.done, want) < age - 500) f.done++;
                    tracks[c] = f;
                }
                if (f.pending != null)
                {
                    string status = reqStatus.GetValue(f.pending, null).ToString();
                    if (status == "Processed") { f.done++; f.lastTalk = now; f.pending = null; }
                    else if (status == "Expired" || (now - f.pendingSince > LetterWindow && !Queued(f.pending))) f.pending = null;
                    else
                    {
                        // RimTalk drops such a request after 5000 ticks, yet at the fastest speed it lets
                        // someone speak only every 10800: left alone, most would run out unheard. So it is
                        // made young again every check for as long as somebody is free to take it.
                        if (free == null) free = SomeoneFree(map);
                        if (free.Value) reqCreated.SetValue(f.pending, now, null);
                        continue;
                    }
                }
                if (f.done >= want || age < Due(c, f.done, want) || now - f.lastTalk < MinGap) continue;
                if (f.done == 0 && !f.letterChecked)
                {
                    f.letterChecked = true;
                    if (LetterTalked(c)) { f.done = 1; continue; }
                }
                if (free == null) free = SomeoneFree(map);
                if (!free.Value) continue;
                f.pending = Add(Prompt(c, age), map);
                f.pendingSince = now;
            }
        }

        // Age at which talk number i (from 0) falls due. The later ones are shifted by up to a
        // quarter of the spacing either way, fixed per condition, so they do not all fall on the same hour.
        private static int Due(GameCondition c, int i, int want)
        {
            int step = c.Duration / want;
            int shift = i == 0 ? 0 : (int)((Rand.ValueSeeded(Gen.HashCombineInt(c.uniqueID, i)) - 0.5f) * 0.5f * step);
            return Math.Max(LetterWindow + i * MinGap, step * i + shift);
        }

        // Nobody is asked to chat about it in their sleep or with raiders on the map.
        private static bool SomeoneFree(Map map)
        {
            if (GenHostility.AnyHostileActiveThreatToPlayer(map)) return false;
            List<Pawn> pawns = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < pawns.Count; i++)
                if (!pawns[i].Downed && !pawns[i].InMentalState && pawns[i].Awake()) return true;
            return false;
        }

        // Whether RimTalk already had someone talk about the letter that came with this condition.
        private static bool LetterTalked(GameCondition c)
        {
            try
            {
                string title = Title(c);
                if (title.Length == 0) return false;
                foreach (object r in (IEnumerable)poolHistory.Invoke(null, null))
                {
                    if (r == null || (int)reqCreated.GetValue(r, null) < c.startTick - 250) continue;
                    if (reqStatus.GetValue(r, null).ToString() != "Processed") continue;
                    string raw = reqRaw.GetValue(r, null) as string;
                    if (raw != null && raw.Contains(title)) return true;
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        private static bool Queued(object request)
        {
            foreach (object r in (IEnumerable)poolActive.Invoke(null, null))
                if (ReferenceEquals(r, request)) return true;
            return false;
        }

        // The wording is in the Keyed file: Stagecraft_ConditionTalk, or the key the condition was told
        // about with; {0} title, {1} description, {2} how long ago it began.
        private static string Prompt(GameCondition c, int age)
        {
            Spec s;
            string key = specs.TryGetValue(c.def.defName, out s) && !s.promptKey.NullOrEmpty() ? s.promptKey : "Stagecraft_ConditionTalk";
            if (!key.CanTranslate()) return "(Talk again about " + Title(c) + ")\n[" + Text(c) + "]";
            string ago = age < 60000
                ? "Stagecraft_AgoHours".Translate(Math.Max(1, age / 2500)).ToString()
                : "Stagecraft_AgoDays".Translate(age / 60000).ToString();
            return key.Translate(Title(c), Text(c), ago).ToString();
        }

        // Puts a topic into RimTalk's pool, for whichever colonist it next lets speak, and returns
        // the request so its fate can be read later.
        private static object Add(string prompt, Map map)
        {
            poolAdd.Invoke(null, new object[] { prompt, null, null, map.uniqueID, eventType });
            object last = null;
            foreach (object r in (IEnumerable)poolActive.Invoke(null, null)) last = r;
            return last;
        }

        // Something to be talked about once and soon. It goes into RimTalk's pool, is kept from
        // running out there for `life` ticks, and is offered to a free colonist at every check
        // rather than waiting for RimTalk's own turn, which at the fastest speed comes only every
        // 10800 ticks. RimTalk still refuses while it is busy or a talk is still being shown.
        internal static void TalkSoon(string prompt, Map map, int life)
        {
            if (!poolReady || map == null || prompt.NullOrEmpty()) return;
            soon.Add(new Topic { prompt = prompt, request = Add(prompt, map), until = Find.TickManager.TicksGame + life });
            if (SomeoneFree(map)) Offer(map);
        }

        private static void Soon(Map map, int now)
        {
            bool? free = null;
            for (int i = soon.Count - 1; i >= 0; i--)
            {
                Topic t = soon[i];
                string status = reqStatus.GetValue(t.request, null).ToString();
                if (status == "Processed" || now > t.until) { soon.RemoveAt(i); continue; }
                if (free == null) free = SomeoneFree(map);
                if (!free.Value) continue;
                if (status == "Expired" || !Queued(t.request)) t.request = Add(t.prompt, map);
                else reqCreated.SetValue(t.request, now, null);
            }
            if (soon.Count > 0 && free == true) Offer(map);
        }

        // Lets a free colonist take the newest request now, the way RimTalk itself would at its next turn.
        private static void Offer(Map map)
        {
            if (pullFromPool == null) return;
            try
            {
                int asked = 0;
                foreach (Pawn p in map.mapPawns.FreeColonistsSpawned.InRandomOrder())
                {
                    if (p.Downed || p.InMentalState || !p.Awake()) continue;
                    if ((bool)pullFromPool.Invoke(null, new object[] { p }) || ++asked >= 4) return;
                }
            }
            catch (Exception e)
            {
                // RimTalk's own turn still takes the request, only later.
                pullFromPool = null;
                Log.Warning(StagecraftMod.Tag + "offering a topic to RimTalk at once failed, it will wait for RimTalk's turn from now on: " + e);
            }
        }

        // The player took a quest on: the colonists hear of it and talk it over. The wording is
        // Stagecraft_QuestAccepted in the Keyed file, {0} the quest's name, {1} the start of its description.
        internal static void QuestAccepted(Quest quest)
        {
            try
            {
                if (!poolReady || !StagecraftMod.Settings.questTalk || quest == null || quest.hidden || quest.parent != null) return;
                string text = ColoredText.StripTags(quest.description.Resolve() ?? "").Replace("\r", "").Replace("\n", " ").Trim();
                while (text.Contains("  ")) text = text.Replace("  ", " ");
                if (text.Length > 200)
                {
                    int cut = text.LastIndexOfAny(new[] { '。', '！', '？', '.', '!', '?' }, 199);
                    text = cut >= 100 ? text.Substring(0, cut + 1) : text.Substring(0, 200) + "…";
                }
                TalkSoon("Stagecraft_QuestAccepted".Translate(quest.name, text).ToString(), Find.AnyPlayerHomeMap, 30000);
            }
            catch (Exception e)
            {
                Log.Warning(StagecraftMod.Tag + "could not tell RimTalk of the accepted quest: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.Accept))]
    internal static class Patch_QuestAccept
    {
        private static void Postfix(Quest __instance)
        {
            News.QuestAccepted(__instance);
        }
    }
}
