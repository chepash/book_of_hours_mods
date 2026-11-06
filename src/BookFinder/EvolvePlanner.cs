using System.Collections.Generic;
using System.Linq;

namespace BookFinder;

/// <summary>
/// Whether a soul place can evolve a given soul right now, or why not. Evolving needs, together:
/// the place's Evolve aspect for a Wisdom that evolves this soul (evolve.&lt;wisdom&gt;.&lt;soul&gt;);
/// a skill committed to that Wisdom and still attuned to the soul (a.&lt;soul&gt;; spent after one use);
/// that skill fitting the place's Skill slot, which only takes cards with one of the place's
/// principles (Verb.Hints); the soul fitting the Soul slot the same way; and two identical cards
/// of one tier of the soul. The skill slot is the easy one to miss: a skill attuned and committed
/// right can still be shut out by its principles.
/// </summary>
public static class EvolvePlanner
{
    public static string Verdict(DeskEntry place, string soul, List<ItemEntry> items, out bool ready)
    {
        ready = false;
        string soulLabel = WorldIndex.LabelOf(soul);
        List<string> wisdoms = place.EvolveIds.Where(w => DeskIndex.SoulsEvolvedVia(w).Contains(soul)).ToList();
        if (wisdoms.Count == 0)
            return $"Doesn't evolve {soulLabel}";

        List<ItemEntry> attuned = items.Where(i => i.Aspects.AspectValue("skill") > 0
            && i.Aspects.AspectValue("wisdom.committed") > 0
            && i.Aspects.AspectValue("a." + soul) > 0).ToList();
        List<ItemEntry> committedHere = attuned.Where(s => wisdoms.Any(w => s.Aspects.AspectValue("w." + w) > 0)).ToList();
        // One short reason, not a list of every unsuitable skill (wrong Wisdom, or the Skill slot's
        // principles): the player only needs to know there's none now.
        List<ItemEntry> fitting = committedHere.Where(s => place.Principles.Any(p => s.Aspects.AspectValue(p) > 0)).ToList();
        if (fitting.Count == 0)
            return "No suitable skill right now";

        string needs = string.Join(", ", place.Principles.Select(WorldIndex.LabelOf));

        // Pairs per tier: xhea, xhea2, ... are different cards; a stack counts by its quantity.
        var tiers = items.Where(i => i.Aspects.AspectValue("ability") > 0 && Base(i.Stack.EntityId) == soul)
            .GroupBy(i => i.Stack.EntityId)
            .Select(g => (tier: g.First(), count: g.Sum(i => i.Stack.Quantity)))
            .ToList();
        var pairs = tiers.Where(t => t.count >= 2).ToList();
        if (pairs.Count == 0)
            return $"Need 2 identical {soulLabel} cards";
        if (!pairs.Any(t => place.Principles.Any(p => t.tier.Aspects.AspectValue(p) > 0)))
            return $"{soulLabel} doesn't fit here (needs {needs})";

        ready = true;
        return $"Ready: {fitting[0].Title} + 2× {pairs[0].tier.Title}";
    }

    /// <summary>A skill card that could drive this evolution, and whether it can here.</summary>
    public class SkillOption
    {
        public ItemEntry Skill;
        public bool Usable;
        public string Note;
    }

    /// <summary>
    /// The player's skills attuned to the soul (a.&lt;soul&gt;), for the Souls rows: usable here
    /// when committed to a Wisdom this place evolves the soul through and sharing a principle with
    /// the place's Skill slot; otherwise the reason (wrong Wisdom, or the slot's principles).
    /// Usable ones first. Empty when the place doesn't evolve this soul at all.
    /// </summary>
    public static List<SkillOption> SkillOptions(DeskEntry place, string soul, List<ItemEntry> items)
    {
        var options = new List<SkillOption>();
        List<string> wisdoms = place.EvolveIds.Where(w => DeskIndex.SoulsEvolvedVia(w).Contains(soul)).ToList();
        if (wisdoms.Count == 0)
            return options;
        string wisdomLabels = string.Join(" or ", wisdoms.Select(w => WorldIndex.LabelOf("w." + w)));
        string needs = string.Join(", ", place.Principles.Select(WorldIndex.LabelOf));
        var attuned = items.Where(i => i.Aspects.AspectValue("skill") > 0 && i.Aspects.AspectValue("a." + soul) > 0)
            .GroupBy(i => i.Stack.EntityId).Select(g => g.First());
        foreach (ItemEntry skill in attuned)
        {
            bool committed = skill.Aspects.AspectValue("wisdom.committed") > 0;
            bool rightWisdom = committed && wisdoms.Any(w => skill.Aspects.AspectValue("w." + w) > 0);
            bool fits = place.Principles.Any(p => skill.Aspects.AspectValue(p) > 0);
            string note = !committed ? $"Not committed yet: needs commitment to {wisdomLabels}"
                : !rightWisdom ? $"Committed to {CommittedWisdom(skill)}; here it's {wisdomLabels}"
                : !fits ? $"Right Wisdom, but the Skill slot needs {needs}"
                : $"Use this: committed to {CommittedWisdom(skill)}, fits the Skill slot";
            options.Add(new SkillOption { Skill = skill, Usable = rightWisdom && fits, Note = note });
        }
        return options.OrderByDescending(o => o.Usable).ThenBy(o => o.Skill.Title).ToList();
    }

    private static string Base(string elementId) => elementId.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');

    /// <summary>The Wisdom left on a committed skill (committing removes the other one).</summary>
    private static string CommittedWisdom(ItemEntry skill)
    {
        string wisdom = skill.Aspects.Where(a => a.Value > 0 && a.Key.StartsWith("w.")).Select(a => a.Key).FirstOrDefault();
        return wisdom == null ? "?" : WorldIndex.LabelOf(wisdom);
    }
}
