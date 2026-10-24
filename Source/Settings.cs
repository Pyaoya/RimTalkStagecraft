using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimTalkStagecraft
{
    // Every switch is read when it is needed, so a change takes effect at once, without a restart.
    public class Settings : ModSettings
    {
        public bool place = true;
        public bool targetTags = true;
        public bool conditions = true;
        public int conditionTalks = 3;
        public string conditionSkip = "";
        public bool conditionLines;
        public bool questTalk = true;
        public bool notes = true;
        public bool speakButton = true;
        public string literatureWorld = "";

        public override void ExposeData()
        {
            Scribe_Values.Look(ref place, "place", true);
            Scribe_Values.Look(ref targetTags, "targetTags", true);
            Scribe_Values.Look(ref conditions, "conditions", true);
            Scribe_Values.Look(ref conditionTalks, "conditionTalks", 3);
            Scribe_Values.Look(ref conditionSkip, "conditionSkip", "");
            Scribe_Values.Look(ref conditionLines, "conditionLines", false);
            Scribe_Values.Look(ref questTalk, "questTalk", true);
            Scribe_Values.Look(ref notes, "notes", true);
            Scribe_Values.Look(ref speakButton, "speakButton", true);
            Scribe_Values.Look(ref literatureWorld, "literatureWorld", "");
        }

        private HashSet<string> skip;
        private string skipFrom;

        // Condition def names the player does not want brought up again, written with commas or spaces between.
        internal bool Skips(string defName)
        {
            string from = conditionSkip ?? "";
            if (skip == null || skipFrom != from)
            {
                skip = new HashSet<string>();
                foreach (string s in from.Split(',', ' ', ';', '\n', '\r', '\t', '，', '、', '；'))
                    if (s.Trim().Length > 0) skip.Add(s.Trim());
                skipFrom = from;
            }
            return skip.Contains(defName);
        }

        internal void Draw(Rect inRect)
        {
            Listing_Standard l = new Listing_Standard();
            l.ColumnWidth = (inRect.width - 34f) / 2f;
            l.Begin(inRect);

            l.CheckboxLabeled(Tr("Stagecraft_SetPlace"), ref place, Tr("Stagecraft_SetPlaceTip"));
            // In a game: write the description of the map in view (also on the world map, per place).
            Map here = Current.ProgramState == ProgramState.Playing ? Find.CurrentMap : null;
            if (place && here != null && here.Parent != null && l.ButtonText(Tr("Stagecraft_SetPlaceEdit"))) PlaceNotes.Edit(here.Parent);
            l.CheckboxLabeled(Tr("Stagecraft_SetTargets"), ref targetTags, Tr("Stagecraft_SetTargetsTip"));
            l.GapLine();
            l.CheckboxLabeled(Tr("Stagecraft_SetConditions"), ref conditions, Tr("Stagecraft_SetConditionsTip"));
            l.Label("Stagecraft_SetConditionTalks".Translate(conditionTalks));
            conditionTalks = Mathf.Clamp(Mathf.RoundToInt(l.Slider(conditionTalks, 1f, 6f)), 1, 6);
            l.Label("Stagecraft_SetConditionSkip".Translate(), -1f, Tr("Stagecraft_SetConditionSkipTip"));
            conditionSkip = l.TextEntry(conditionSkip ?? "");
            l.CheckboxLabeled(Tr("Stagecraft_SetConditionLines"), ref conditionLines, Tr("Stagecraft_SetConditionLinesTip"));
            l.GapLine();
            l.CheckboxLabeled(Tr("Stagecraft_SetQuestTalk"), ref questTalk, Tr("Stagecraft_SetQuestTalkTip"));

            l.NewColumn();
            l.CheckboxLabeled(Tr("Stagecraft_SetNotes"), ref notes, Tr("Stagecraft_SetNotesTip"));
            l.CheckboxLabeled(Tr("Stagecraft_SetSpeak"), ref speakButton, Tr("Stagecraft_SetSpeakTip"));
            l.GapLine();
            l.Label("Stagecraft_SetLiterature".Translate(), -1f, Tr("Stagecraft_SetLiteratureTip"));
            literatureWorld = l.TextEntry(literatureWorld ?? "", 8);
            l.End();
        }

        private static string Tr(string key)
        {
            return key.Translate().ToString();
        }
    }
}
