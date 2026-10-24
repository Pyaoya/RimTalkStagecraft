using System;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimTalkStagecraft
{
    // RimTalk tells the model a pawn's room and role but never what the map is: nobody knows
    // whether they stand in their own colony, another faction's town, a quest site or old ruins.
    // This gives every RimTalk prompt one line for it in the [Environment] block: the place's
    // name, what kind of place it is and whose, how its owners stand with the player's faction,
    // the quest that leads there, and the land around it. The wording is all in the Keyed file
    // (Stagecraft_Place and Stagecraft_Place...). Where the player has written a place's
    // description themselves (PlaceNotes), that is told instead.
    internal static class Place
    {
        // Empire (Refactored): the settlements under the player's rule. Their people at home are no strangers.
        private const string SubjectFaction = "PColony";

        internal static string Line(Map map)
        {
            try
            {
                if (map == null || !StagecraftMod.Settings.place) return null;
                string place = Describe(map, 0, true);
                return place.NullOrEmpty() ? null : T("Stagecraft_Place", place);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // The generated description alone, for the player to start from.
        internal static string Auto(Map map)
        {
            try { return map == null ? null : Describe(map, 0, false); }
            catch (Exception) { return null; }
        }

        private static string T(string key, params NamedArgument[] args)
        {
            return key.Translate(args).ToString();
        }

        private static string Describe(Map map, int depth, bool own)
        {
            MapParent parent = map.Parent;
            if (parent == null) return null;
            if (own)
            {
                string written = PlaceNotes.Get(parent);
                if (!written.NullOrEmpty()) return written;
            }

            // A map inside a map (a cave under the colony and the like): its own name, then the map it is entered from.
            if (map.IsPocketMap)
            {
                string inner = map.generatorDef != null && !map.generatorDef.label.NullOrEmpty() ? map.generatorDef.label : parent.Label;
                Map source = map.PocketMapParent != null ? map.PocketMapParent.sourceMap : null;
                string outer = source != null && source != map && depth < 3 ? Describe(source, depth + 1, own) : null;
                return outer.NullOrEmpty() ? inner : T("Stagecraft_PlacePocket", inner, outer);
            }

            string us = Faction.OfPlayer.Name;
            string name = parent.Label;
            Faction owner = parent.Faction;
            StringBuilder sb = new StringBuilder();
            if (map.IsPlayerHome)
            {
                sb.Append(T("Stagecraft_PlaceHome", name, us));
                Faction subjects = SubjectsHere(map);
                if (subjects != null) sb.Append(T("Stagecraft_PlaceHomeSubjects", subjects.Name, us));
            }
            else if (owner != null && owner.IsPlayer) sb.Append(T("Stagecraft_PlaceOwn", name, us, parent.def.label));
            else if (owner != null && owner.def.defName == SubjectFaction) sb.Append(T("Stagecraft_PlaceSubject", name, owner.Name, us));
            else if (parent is Settlement && owner != null) sb.Append(T("Stagecraft_PlaceSettlement", name, owner.Name, Stance(owner, us)));
            else
            {
                // Quest sites, ruins, camps, anything a mod adds: its name, its kind if the name does not say, who holds it.
                sb.Append(name);
                string kind = parent.def.label;
                if (!(parent is Site) && !kind.NullOrEmpty() && kind != name) sb.Append(T("Stagecraft_PlaceKind", kind));
                if (owner != null && !owner.Hidden) sb.Append(T("Stagecraft_PlaceHeldBy", owner.Name, Stance(owner, us)));
            }
            if (!map.IsPlayerHome)
            {
                Quest quest = QuestFor(parent);
                if (quest != null) sb.Append(quest.name == name ? T("Stagecraft_PlaceQuestSameName") : T("Stagecraft_PlaceQuest", quest.name));
            }

            // The named stretch of the world map it lies in, and the kind of landmark on its tile
            // (lake, valley, ruins...). Landmark names are generated nonsense, so only the kind is told.
            Tile tile = map.TileInfo;
            if (tile != null)
            {
                if (tile.feature != null && !tile.feature.name.NullOrEmpty()) sb.Append(T("Stagecraft_PlaceFeature", tile.feature.name));
                Landmark mark = tile.Landmark;
                if (mark != null && mark.def != null && !mark.def.label.NullOrEmpty()) sb.Append(T("Stagecraft_PlaceLandmark", mark.def.label));
            }
            return sb.ToString();
        }

        private static string Stance(Faction owner, string us)
        {
            switch (owner.PlayerRelationKind)
            {
                case FactionRelationKind.Hostile: return T("Stagecraft_PlaceStanceHostile", us);
                case FactionRelationKind.Ally: return T("Stagecraft_PlaceStanceAlly", us);
                default: return T("Stagecraft_PlaceStanceNeutral", us);
            }
        }

        // People of the player's subject settlements now on this map, if any.
        private static Faction SubjectsHere(Map map)
        {
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                if (p.Faction != null && p.Faction.def.defName == SubjectFaction && p.RaceProps.Humanlike) return p.Faction;
            return null;
        }

        private static Quest QuestFor(MapParent parent)
        {
            foreach (Quest q in Find.QuestManager.QuestsListForReading)
            {
                if (q.State != QuestState.Ongoing || q.hidden) continue;
                foreach (GlobalTargetInfo t in q.QuestLookTargets)
                    if (t.HasWorldObject && t.WorldObject == parent) return q;
            }
            return null;
        }
    }
}
