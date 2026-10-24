using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalkStagecraft
{
    // Two things added to RimTalk's own "talk to" window, which lets the player write a line for
    // one pawn to say to another.
    //
    // A director's note: text typed there between // and // is not a line anybody speaks.
    // RimTalk would have the pawn say it word for word; instead the pawn is asked to begin a
    // conversation that follows the note, and the model writes every line of it.
    //
    // A speak button on pawns the player cannot order about (guests, traders, prisoners), who
    // have no right-click menu to open that window from: pick whom they talk to, then either
    // let the model write it or open the window to write their line or a note.
    //
    // Another mod can ask for a conversation the same way (Stage, through Interop): its note is
    // acted out by the people it names, without the player typing anything.
    internal static class Director
    {
        private static bool tried, ready;
        private static MethodInfo dispatch, eligible, generate, isPlayer, canTalk;
        private static ConstructorInfo newRequest, newWindow;
        private static object eventType, directMode;
        private static Texture2D icon;

        private class Waiting
        {
            public Pawn from, to;
            public string note;
            public int until;
        }

        private static readonly List<Waiting> waiting = new List<Waiting>();
        private const int WaitTicks = 5000;   // two game hours for RimTalk to be free and the pawn able to speak

        // Staged notes whose speaker has been sent over to the other: note -> when.
        private static readonly Dictionary<string, int> walking = new Dictionary<string, int>();
        private static readonly HashSet<string> taken = new HashSet<string>();   // RimTalk took it on arrival
        private static readonly HashSet<string> ours = new HashSet<string>();    // sent by the speak button itself
        private const int WalkTicks = 2500;   // an hour to get there before being sent again
        private const float NearCells = 8f;

        // Set only by LoreLink (Interop.RecordNotesIn): without it nothing is written down.
        internal static string RecordFolder;

        internal static bool Ready { get { return ready; } }

        internal static void Register(Harmony harmony)
        {
            if (tried) return;
            tried = true;
            try
            {
                Type service = GenTypes.GetTypeInAnyAssembly("RimTalk.Service.CustomDialogueService");
                Type talk = GenTypes.GetTypeInAnyAssembly("RimTalk.Service.TalkService");
                Type util = GenTypes.GetTypeInAnyAssembly("RimTalk.Util.PawnUtil");
                Type request = GenTypes.GetTypeInAnyAssembly("RimTalk.Data.TalkRequest");
                Type kinds = GenTypes.GetTypeInAnyAssembly("RimTalk.Source.Data.TalkType");
                Type window = GenTypes.GetTypeInAnyAssembly("RimTalk.UI.CustomDialogueWindow");
                Type mode = GenTypes.GetTypeInAnyAssembly("RimTalk.UI.DialogueMode");
                if (service == null || talk == null || util == null || request == null || kinds == null || window == null || mode == null)
                {
                    Log.Warning(StagecraftMod.Tag + "RimTalk's talk window was not found: director's notes and the speak button are left out.");
                    return;
                }

                Type[] line = { typeof(Pawn), typeof(Pawn), typeof(string), typeof(bool), typeof(string) };
                dispatch = service.GetMethod("DispatchDialogue", line);
                canTalk = service.GetMethod("CanTalk", new[] { typeof(Pawn), typeof(Pawn) });
                MethodInfo execute = service.GetMethod("ExecuteDialogue", line);
                eligible = util.GetMethod("IsTalkEligible", new[] { typeof(Pawn) });
                isPlayer = util.GetMethod("IsPlayer", new[] { typeof(Pawn) });
                generate = talk.GetMethod("GenerateTalk", new[] { request });
                newRequest = request.GetConstructor(new[] { typeof(string), typeof(Pawn), typeof(Pawn), kinds });
                newWindow = window.GetConstructor(new[] { typeof(Pawn), typeof(Pawn), mode });
                if (dispatch == null || execute == null || eligible == null || isPlayer == null || generate == null || newRequest == null || newWindow == null)
                {
                    Log.Warning(StagecraftMod.Tag + "RimTalk has changed: director's notes and the speak button are left out.");
                    return;
                }
                eventType = Enum.Parse(kinds, "Event");
                directMode = Enum.Parse(mode, "Direct");

                harmony.Patch(execute, new HarmonyMethod(typeof(Director), nameof(Note)));
                harmony.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.GetGizmos)), null, new HarmonyMethod(typeof(Director), nameof(Button)));
                ready = true;
                Log.Message(StagecraftMod.Tag + "director's notes and the speak button are in place.");
            }
            catch (Exception e)
            {
                Log.Warning(StagecraftMod.Tag + "director's notes and the speak button could not be set up: " + e.Message);
            }
        }

        // A game was loaded: nobody of the game before is still waiting to speak.
        internal static void NewGame()
        {
            waiting.Clear();
            walking.Clear();
            taken.Clear();
            ours.Clear();
        }

        // Text that opens with two slashes is a note; what is left once the slashes are taken off is the note itself.
        internal static bool IsNote(string text, out string note)
        {
            note = null;
            if (text == null) return false;
            string t = text.Trim();
            if (!t.StartsWith("//") && !t.StartsWith("／／")) return false;
            note = t.Trim('/', '／', ' ', '　');
            return note.Length > 0;
        }

        // RimTalk is about to have __0 say __2 to __1 word for word. A note is acted out instead.
        private static bool Note(Pawn __0, Pawn __1, string __2, bool __3)
        {
            try
            {
                string note;
                if (__3 || !IsNote(__2, out note)) return true;
                Pawn from = __0, to = __1;
                // A staged talk's speaker has walked up to the other: the caller goes on asking until RimTalk takes the talk.
                if (walking.Remove(note))
                {
                    if (from != null && from.Spawned && !from.Dead && Try(from, to, note)) taken.Add(note);
                    return false;
                }
                // Turned off: RimTalk's own way, the text is said as it stands. The speak button's own note still counts.
                if (!ours.Remove(note) && !StagecraftMod.Settings.notes) return true;
                // Typed to a pawn as the player's own voice: that pawn is the one who begins.
                if (from != null && (bool)isPlayer.Invoke(null, new object[] { from })) { from = to; to = null; }
                if (from == null) return true;
                Record(from, to, note);
                if (!Try(from, to, note))
                    waiting.Add(new Waiting { from = from, to = to, note = note, until = Find.TickManager.TicksGame + WaitTicks });
                return false;
            }
            catch (Exception e)
            {
                Log.Warning(StagecraftMod.Tag + "a director's note could not be acted on, RimTalk treats it as a spoken line: " + e.Message);
                return true;
            }
        }

        // True when there is nothing left to do: the conversation was asked for, or the speaker is gone.
        private static bool Try(Pawn from, Pawn to, string note)
        {
            if (from == null || !from.Spawned || from.Dead) return true;
            if (to != null && (!to.Spawned || to.Dead || to.Map != from.Map)) to = null;
            string prompt = to == null
                ? "Stagecraft_Direct".Translate(note, from.LabelShort).ToString()
                : "Stagecraft_DirectTo".Translate(note, from.LabelShort, to.LabelShort).ToString();
            object request = newRequest.Invoke(new object[] { prompt, from, to, eventType });
            return (bool)generate.Invoke(null, new object[] { request });
        }

        // A conversation another mod asks for, `from` beginning it with `to` (or with whoever is
        // about). Called at every check until it answers true: RimTalk has taken it. Standing
        // apart, the speaker is first sent over to the other, the way RimTalk does it for a line
        // the player writes. `tag` is written into the record's last column.
        internal static bool Stage(Pawn from, Pawn to, string note, string tag, int now)
        {
            if (!ready || from == null || note.NullOrEmpty()) return true;
            if (taken.Remove(note)) { Record(from, to, note, tag ?? ""); return true; }
            if (!Eligible(from) || (to != null && !Eligible(to))) return false;
            if (to != null && !Near(from, to))
            {
                int sent;
                if (walking.TryGetValue(note, out sent) && now - sent < WalkTicks) return false;
                walking[note] = now;
                dispatch.Invoke(null, new object[] { from, to, "//" + note + "//", false, null });
                return false;
            }
            if (!Try(from, to, note)) return false;
            walking.Remove(note);
            Record(from, to, note, tag ?? "");
            return true;
        }

        // Close enough to talk by RimTalk's own measure, so that it does not send the speaker walking.
        private static bool Near(Pawn a, Pawn b)
        {
            if (a.Map != b.Map) return false;
            if (canTalk != null)
            {
                try { return (bool)canTalk.Invoke(null, new object[] { a, b }); }
                catch (Exception) { canTalk = null; }
            }
            return a.Position.InHorDistOf(b.Position, NearCells) && GenSight.LineOfSight(a.Position, b.Position, a.Map);
        }

        // Called with every check. RimTalk takes one request at a time and a pawn may be asleep
        // or mid-sentence, so a note waits a while for its turn.
        internal static void Tick(int now)
        {
            if (!ready || waiting.Count == 0) return;
            for (int i = waiting.Count - 1; i >= 0; i--)
            {
                Waiting w = waiting[i];
                try
                {
                    if (now > w.until)
                    {
                        waiting.RemoveAt(i);
                        if (w.from != null && w.from.Spawned)
                            Messages.Message("Stagecraft_DirectDropped".Translate(w.from.LabelShort), w.from, MessageTypeDefOf.NeutralEvent, false);
                    }
                    else if (Try(w.from, w.to, w.note)) waiting.RemoveAt(i);
                }
                catch (Exception e)
                {
                    waiting.RemoveAt(i);
                    Log.Warning(StagecraftMod.Tag + "a waiting director's note was dropped: " + e.Message);
                }
            }
        }

        // For LoreLink only (Interop.RecordNotesIn): the notes are the player's own words about what
        // happened, so they are kept outside the save, when, who to whom, and the note. A staged one
        // is kept too, with its tag in a last column that the player's own lack. Not a feature of
        // this mod: with no folder set, nothing is kept.
        private static void Record(Pawn from, Pawn to, string note, string tag = null)
        {
            if (RecordFolder.NullOrEmpty()) return;
            try
            {
                string path = Path.Combine(GenFilePaths.SaveDataFolderPath, RecordFolder, "directed_" + Find.World.info.persistentRandomValue + ".tsv");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string date = GenDate.DateFullStringWithHourAt(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(from.Map.Tile));
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + Find.TickManager.TicksGame + "\t" + date + "\t"
                    + from.LabelShort + "\t" + (to != null ? to.LabelShort : "") + "\t" + note.Replace('\t', ' ').Replace('\n', ' ')
                    + (tag.NullOrEmpty() ? "" : "\t" + tag) + "\n");
            }
            catch (Exception)
            {
            }
        }

        private static bool Eligible(Pawn pawn)
        {
            try { return (bool)eligible.Invoke(null, new object[] { pawn }); }
            catch (Exception) { return false; }
        }

        private static void Button(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!ready || !StagecraftMod.Settings.speakButton || __instance == null || __result == null || !__instance.Spawned || __instance.Dead) return;
            if (__instance.IsColonistPlayerControlled || __instance.RaceProps == null || !__instance.RaceProps.Humanlike) return;
            if (Find.Selector == null || Find.Selector.SingleSelectedThing != __instance || !Eligible(__instance)) return;
            __result = __result.Concat(new Gizmo[] { Speak(__instance) });
        }

        private static Gizmo Speak(Pawn pawn)
        {
            if (icon == null) icon = ContentFinder<Texture2D>.Get("UI/ChatGizmo", false) ?? BaseContent.BadTex;
            Command_Action command = new Command_Action();
            command.defaultLabel = "Stagecraft_SpeakLabel".Translate();
            command.defaultDesc = "Stagecraft_SpeakDesc".Translate(pawn.LabelShort);
            command.icon = icon;
            command.action = delegate
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                options.Add(new FloatMenuOption("Stagecraft_SpeakFree".Translate(pawn.LabelShort), delegate
                {
                    Pick(pawn, delegate(Pawn to)
                    {
                        string note = "Stagecraft_SpeakFreeNote".Translate().ToString();
                        ours.Add(note);
                        dispatch.Invoke(null, new object[] { pawn, to, "//" + note + "//", false, null });
                        Messages.Message("Stagecraft_SpeakQueued".Translate(pawn.LabelShort, to.LabelShort), pawn, MessageTypeDefOf.SilentInput, false);
                    });
                }));
                options.Add(new FloatMenuOption("Stagecraft_SpeakWrite".Translate(pawn.LabelShort), delegate
                {
                    Pick(pawn, delegate(Pawn to) { Find.WindowStack.Add((Window)newWindow.Invoke(new object[] { pawn, to, directMode })); });
                }));
                Find.WindowStack.Add(new FloatMenu(options));
            };
            return command;
        }

        private static void Pick(Pawn from, Action<Pawn> then)
        {
            TargetingParameters who = new TargetingParameters();
            who.canTargetPawns = true;
            who.canTargetBuildings = false;
            who.canTargetItems = false;
            who.mapObjectTargetsMustBeAutoAttackable = false;
            who.validator = delegate(TargetInfo t)
            {
                Pawn p = t.Thing as Pawn;
                return p != null && p != from && p.Spawned && !p.Dead && Eligible(p);
            };
            Find.Targeter.BeginTargeting(who, delegate(LocalTargetInfo t)
            {
                Pawn p = t.Thing as Pawn;
                if (p != null && from != null && from.Spawned) then(p);
            });
        }
    }
}
