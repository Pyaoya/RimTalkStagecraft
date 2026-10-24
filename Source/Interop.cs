using System;
using RimWorld;
using Verse;

namespace RimTalkStagecraft
{
    // Kept for LoreLink, the mod these parts were taken out of: it tells this one about its own
    // news banners, where the director's notes are kept and its world brief, and has its story
    // beats' conversations acted out. Not a feature of this mod and not described to players;
    // it may change along with LoreLink.
    public static class Interop
    {
        // A condition to be talked about `talks(c)` times in all while it lasts (0: never, and not
        // as an ordinary condition either), its title read without `titlePrefix`, in the wording of
        // the Keyed entry `promptKey` ({0} title, {1} description, {2} how long ago it began).
        public static void FollowCondition(string defName, Func<GameCondition, int> talks, string titlePrefix, string promptKey)
        {
            News.FollowCondition(defName, talks, titlePrefix, promptKey);
        }

        // Keeps every director's note in directed_<world>.tsv, in this folder under the save data folder.
        public static void RecordNotesIn(string folder)
        {
            if (!folder.NullOrEmpty()) Director.RecordFolder = folder;
        }

        // The world brief for RimTalk Literature while the player's own setting is empty.
        public static void WorldFallback(string text)
        {
            Literature.Fallback = text;
        }

        public static bool TalkReady
        {
            get { return Director.Ready; }
        }

        // Has `from` begin a conversation with `to` (null: whoever is about) that follows `note`.
        // Call it again at every check until it answers true; `tag` goes into the record.
        public static bool Stage(Pawn from, Pawn to, string note, string tag, int now)
        {
            return Director.Stage(from, to, note, tag, now);
        }
    }
}
