using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BohShared;
using HarmonyLib;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.Infrastructure;
using SecretHistories.Spheres;
using SecretHistories.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CopyBreaks;

/// <summary>
/// "Conversations" in a salon's window: copies the whole conversation so far - every note the salon
/// has written (its notes sphere keeps one token per note: the opening, each exchange between
/// guests, the ending), each as its title and text - in order, for pasting into the player's
/// notes. The game's own copy (SalonWindow.CopyVisibleNoteText) takes only the page on screen.
/// Notes are recorded as they appear (see Transcripts), since the window keeps only the last 9.
/// Sits next to the Start button (SalonWindow.startButton); shown once the salon is over.
/// </summary>
internal static class SalonCopyButton
{
    private const string ButtonName = "CopySalonButton";
    private static readonly Color Red = CopyButtonFx.Red;
    private static readonly FieldInfo StartButtonField = AccessTools.Field(typeof(SalonWindow), "startButton");
    private static readonly FieldInfo PayloadField = AccessTools.Field(typeof(SalonWindow), "_payload");

    [HarmonyPatch(typeof(SalonWindow))]
    internal static class WindowPatch
    {
        [HarmonyPostfix, HarmonyPatch("DisplayButtonState")]
        internal static void AfterButtons(SalonWindow __instance, Situation situation) => Refresh(__instance, situation);

        [HarmonyPostfix, HarmonyPatch(nameof(SalonWindow.SituationSphereContentsUpdated))]
        internal static void AfterContents(SalonWindow __instance, Situation s) => Refresh(__instance, s);
    }

    internal static void Refresh(SalonWindow window, Situation situation)
    {
        if (window == null || situation == null || !(StartButtonField?.GetValue(window) is Button start) || start == null)
            return;
        Transform bar = start.transform.parent;
        Transform existing = bar.Find(ButtonName);
        // Recorded all along (the window keeps only 9 notes), but offered only once the salon is
        // over, like "Book text" after the reading.
        string text = ConversationText(situation);
        if (situation.StateIdentifier != StateEnum.Complete)
            text = null;
        if (text == null)
        {
            if (existing != null)
                existing.gameObject.SetActive(false);
            return;
        }
        GameObject button = existing != null ? existing.gameObject : Create(bar, start);
        button.SetActive(true);
        button.GetComponent<CopyTarget>().Text = text;
    }

    /// <summary>
    /// Everything said in each salon, by situation id, kept in a file
    /// (BepInEx\cache\salon-transcripts\&lt;id&gt;.txt) so a hot reload mid-salon loses nothing.
    /// The salon verbs keep only their last notes ("maxnotes": 9; Situation.ReceiveNote retires the
    /// oldest). While the window holds fewer than that, it holds the whole conversation since the
    /// salon began, so the transcript simply becomes what's shown (this also starts a new salon in
    /// the same room afresh: its situation id stays the same). Once the window is full, the notes
    /// shown are lined up against the end of the transcript and only the new ones are appended.
    /// </summary>
    private static readonly Dictionary<string, List<(string title, string text)>> Transcripts =
        new Dictionary<string, List<(string, string)>>();

    private static string TranscriptPath(string situationId)
    {
        string dir = System.IO.Path.Combine(BepInEx.Paths.CachePath, "salon-transcripts");
        System.IO.Directory.CreateDirectory(dir);
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            situationId = situationId.Replace(c, '_');
        return System.IO.Path.Combine(dir, situationId + ".txt");
    }

    private const char RecordSep = '\u001E', FieldSep = '\u001F';

    private static List<(string, string)> Load(string situationId)
    {
        var list = new List<(string, string)>();
        try
        {
            string path = TranscriptPath(situationId);
            if (System.IO.File.Exists(path))
            {
                foreach (string record in System.IO.File.ReadAllText(path).Split(new[] { RecordSep }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] f = record.Split(FieldSep);
                    list.Add((f[0], f.Length > 1 ? f[1] : ""));
                }
            }
        }
        catch (System.Exception)
        {
            // unreadable file: start over
        }
        return list;
    }

    private static void Save(string situationId, List<(string title, string text)> transcript)
    {
        try
        {
            System.IO.File.WriteAllText(TranscriptPath(situationId),
                string.Join(RecordSep.ToString(), transcript.Select(n => n.title + FieldSep + n.text)));
        }
        catch (System.Exception)
        {
            // not fatal: the window's own notes still copy
        }
    }

    /// <summary>A finished transcript, kept as &lt;id&gt;_&lt;time&gt;.txt before a new salon replaces it.</summary>
    private static void Archive(string situationId, List<(string title, string text)> transcript)
    {
        try
        {
            string path = TranscriptPath(situationId);
            System.IO.File.WriteAllText(path.Substring(0, path.Length - 4) + System.DateTime.Now.ToString("_yyyyMMdd-HHmmss") + ".txt",
                string.Join(RecordSep.ToString(), transcript.Select(n => n.title + FieldSep + n.text)));
        }
        catch (System.Exception)
        {
            // not fatal
        }
    }

    /// <summary>All notes of the salon, oldest first; null before it has really begun.</summary>
    private static string ConversationText(Situation situation)
    {
        Sphere notes = situation.GetSpheresByCategory(SphereCategory.Notes).FirstOrDefault();
        if (notes == null)
            return null;
        List<(string title, string text)> current = notes.Tokens
            .Where(t => t != null && t.Payload != null)
            .Select(t => (title: Clean(t.Payload.MetafictionalLabel), text: Clean(t.Payload.MetafictionalDescription)))
            .Where(n => n.title.Length > 0 || n.text.Length > 0)
            .ToList();
        if (!Transcripts.TryGetValue(situation.Id, out var transcript))
            Transcripts[situation.Id] = transcript = Load(situation.Id);
        int max = situation.Verb != null && situation.Verb.MaxNotes > 0 ? situation.Verb.MaxNotes : 9;
        bool changed;
        if (current.Count < 2)
        {
            // Results collected: the window is back to the room's own description. That is no new
            // salon, and replacing the transcript with it once wiped a whole conversation.
            changed = false;
        }
        else if (current.Count < max)
        {
            changed = !transcript.SequenceEqual(current);
            // A different salon in the same room (not this one growing): keep the old one aside.
            if (changed && transcript.Count >= 2 && !current.Take(transcript.Count).SequenceEqual(transcript)
                && !transcript.Take(current.Count).SequenceEqual(current))
                Archive(situation.Id, transcript);
            transcript.Clear();
            transcript.AddRange(current);
        }
        else
        {
            // Longest tail of the transcript that is a head of what's shown.
            int overlap = 0;
            for (int k = System.Math.Min(transcript.Count, current.Count); k > 0; k--)
            {
                if (transcript.Skip(transcript.Count - k).SequenceEqual(current.Take(k)))
                {
                    overlap = k;
                    break;
                }
            }
            changed = overlap < current.Count;
            transcript.AddRange(current.Skip(overlap));
        }
        if (changed)
            Save(situation.Id, transcript);
        var parts = transcript.ToList();
        if (parts.Count < 2)
            return null;
        var sb = new StringBuilder();
        sb.Append(Clean(situation.Label));
        foreach ((string title, string text) in parts)
        {
            sb.Append("\n\n");
            if (title.Length > 0)
                sb.Append(title).Append("\n\n");
            sb.Append(text);
        }
        return sb.ToString().Trim();
    }

    private static string Clean(string s) =>
        string.IsNullOrEmpty(s) ? "" : Regex.Replace(s, "<sprite name=([a-z.]+)>", "$1").Replace("<i>", "").Replace("</i>", "")
            .Replace("<b>", "").Replace("</b>", "").Trim();

    private static GameObject Create(Transform bar, Button start)
    {
        Image bg = GameStyle.Panel(bar, ButtonName, "slots_bg", CopyButtonFx.BgNormal);
        RectTransform rect = bg.rectTransform;
        RectTransform startRect = (RectTransform)start.transform;
        // Left of the Start button, same height and vertical place.
        rect.anchorMin = rect.anchorMax = startRect.anchorMin;
        rect.pivot = new Vector2(1f, startRect.pivot.y);
        rect.sizeDelta = new Vector2(140f, 32f);
        rect.anchoredPosition = startRect.anchoredPosition + new Vector2(-startRect.rect.width * startRect.pivot.x - 12f, 0f);
        bg.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        // A red, thicker border: the gold one looked like part of the game's own window and got missed.
        Image outline = GameStyle.Panel(rect, "Outline", "information_goldborder", Red, 0.8f);
        GameStyle.Stretch(outline.rectTransform, -2, -2, -2, -2);
        outline.raycastTarget = false;
        TextMeshProUGUI label = GameStyle.Text(rect, "Label", "Conversations", 16f, GameStyle.Cream, FontStyles.SmallCaps,
            TextAlignmentOptions.Center);
        GameStyle.Stretch(label.rectTransform);
        label.raycastTarget = false;

        var target = bg.gameObject.AddComponent<CopyTarget>();
        var fx = bg.gameObject.AddComponent<CopyButtonFx>();
        fx.OutlineNormal = Red;
        fx.OutlineLit = CopyButtonFx.RedLit;
        fx.Init(bg, outline, label);
        Button button = bg.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() =>
        {
            ClipboardTextCopier.CopyTextToClipboard(target.Text);
            fx.Flash();
        });
        return bg.gameObject;
    }

    /// <summary>Salon windows already open when the plugin (re)loads.</summary>
    internal static System.Collections.IEnumerator FillOpenWindows()
    {
        yield return null;
        foreach (SalonWindow window in Resources.FindObjectsOfTypeAll<SalonWindow>())
        {
            if (window != null && window.gameObject.scene.IsValid() && PayloadField?.GetValue(window) is Situation situation)
                Refresh(window, situation);
        }
    }
}
