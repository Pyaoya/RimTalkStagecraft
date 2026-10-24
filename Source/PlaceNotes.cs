using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimTalkStagecraft
{
    // The player's own words for a place, used for the location line instead of the generated
    // one. They are kept beside the saves, not in them, one file per world:
    // places_<world>.tsv in RimTalkStagecraft under the save data folder, world object id <tab> text.
    internal static class PlaceNotes
    {
        private static readonly Dictionary<int, string> notes = new Dictionary<int, string>();
        private static int loadedFor = int.MinValue;

        private static string FilePath(int world)
        {
            return Path.Combine(GenFilePaths.SaveDataFolderPath, "RimTalkStagecraft", "places_" + world + ".tsv");
        }

        // RimTalk may build its prompts off the main thread, so every touch of the table is locked.
        private static void Ensure()
        {
            if (Find.World == null) return;
            int world = Find.World.info.persistentRandomValue;
            if (world == loadedFor) return;
            loadedFor = world;
            notes.Clear();
            try
            {
                string path = FilePath(world);
                if (!File.Exists(path)) return;
                foreach (string line in File.ReadAllLines(path))
                {
                    int tab = line.IndexOf('\t');
                    int id;
                    if (tab > 0 && int.TryParse(line.Substring(0, tab), out id) && line.Length > tab + 1) notes[id] = line.Substring(tab + 1);
                }
            }
            catch (Exception e)
            {
                Log.Warning(StagecraftMod.Tag + "the places' own descriptions could not be read: " + e.Message);
            }
        }

        internal static string Get(MapParent place)
        {
            if (place == null) return null;
            lock (notes)
            {
                Ensure();
                string text;
                return notes.TryGetValue(place.ID, out text) ? text : null;
            }
        }

        // Empty text takes the place back to the generated description.
        internal static void Set(MapParent place, string text)
        {
            if (place == null) return;
            text = (text ?? "").Replace("\r", "").Replace('\n', ' ').Replace('\t', ' ').Trim();
            lock (notes)
            {
                Ensure();
                if (text.Length == 0) notes.Remove(place.ID);
                else notes[place.ID] = text;
                try
                {
                    string path = FilePath(loadedFor);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllLines(path, notes.OrderBy(kv => kv.Key).Select(kv => kv.Key + "\t" + kv.Value).ToArray());
                }
                catch (Exception e)
                {
                    Log.Warning(StagecraftMod.Tag + "the places' own descriptions could not be written: " + e.Message);
                }
            }
        }

        internal static void Edit(MapParent place)
        {
            if (place == null) return;
            Find.WindowStack.Add(new Dialog_PlaceNote(place, place.HasMap ? Place.Auto(place.Map) : null));
        }

        internal static Command Button(MapParent place)
        {
            Command_Action command = new Command_Action();
            command.defaultLabel = "Stagecraft_PlaceEdit".Translate();
            command.defaultDesc = "Stagecraft_PlaceEditDesc".Translate();
            command.icon = ContentFinder<Texture2D>.Get("UI/Buttons/Rename", false) ?? BaseContent.BadTex;
            command.action = delegate { Edit(place); };
            return command;
        }
    }

    // A place selected on the world map gets a button to write its description.
    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.GetGizmos))]
    internal static class Patch_WorldObjectGizmos
    {
        private static void Postfix(WorldObject __instance, ref IEnumerable<Gizmo> __result)
        {
            MapParent place = __instance as MapParent;
            if (place == null || __result == null || StagecraftMod.Settings == null || !StagecraftMod.Settings.place) return;
            __result = __result.Concat(new Gizmo[] { PlaceNotes.Button(place) });
        }
    }

    internal class Dialog_PlaceNote : Window
    {
        private readonly MapParent place;
        private readonly string auto;
        private string text;

        public Dialog_PlaceNote(MapParent place, string auto)
        {
            this.place = place;
            this.auto = auto;
            text = PlaceNotes.Get(place) ?? auto ?? "";
            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(660f, 440f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = inRect.y;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 36f), "Stagecraft_PlaceEditTitle".Translate(place.LabelCap));
            y += 40f;
            Text.Font = GameFont.Small;
            string note = "Stagecraft_PlaceEditNote".Translate();
            float h = Text.CalcHeight(note, inRect.width);
            Widgets.Label(new Rect(inRect.x, y, inRect.width, h), note);
            y += h + 6f;
            string generated = "Stagecraft_PlaceEditAuto".Translate(auto.NullOrEmpty() ? "Stagecraft_PlaceEditNoMap".Translate().ToString() : auto).Resolve();
            generated = generated.Colorize(ColoredText.SubtleGrayColor);
            h = Text.CalcHeight(generated, inRect.width);
            Widgets.Label(new Rect(inRect.x, y, inRect.width, h), generated);
            y += h + 8f;

            float buttons = inRect.yMax - 36f;
            text = Widgets.TextArea(new Rect(inRect.x, y, inRect.width, buttons - y - 10f), text);

            float w = (inRect.width - 20f) / 3f;
            if (Widgets.ButtonText(new Rect(inRect.x, buttons, w, 36f), "Stagecraft_PlaceEditSave".Translate()))
            {
                // Left as generated: nothing of the player's to keep.
                bool same = auto != null && (text ?? "").Trim() == auto.Trim();
                PlaceNotes.Set(place, same ? null : text);
                Close();
            }
            if (Widgets.ButtonText(new Rect(inRect.x + w + 10f, buttons, w, 36f), "Stagecraft_PlaceEditReset".Translate()))
            {
                PlaceNotes.Set(place, null);
                Close();
            }
            if (Widgets.ButtonText(new Rect(inRect.x + 2f * (w + 10f), buttons, w, 36f), "CancelButton".Translate())) Close();
        }
    }
}
