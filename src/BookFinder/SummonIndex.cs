using System;
using System.Collections.Generic;
using System.Linq;

namespace BookFinder;

/// <summary>How to summon Echidna now, or why it can't be done.</summary>
public class SummonPlan
{
    /// <summary>"Here now", "On the way"... null when the walk can start.</summary>
    public string Status;
    public DeskEntry Moor;
    /// <summary>The skill with technique.summon.echidna and the most Scale.</summary>
    public ItemEntry Skill;
    /// <summary>The weakest fresh soul the Moor's soul slot takes that closes the Scale gap.</summary>
    public ItemEntry Soul;
    public ItemEntry Milk;
    /// <summary>Skill + soul + Serpent-Milk (3) Scale.</summary>
    public int Scale;
    public bool Ready => Status == null && Moor != null && Skill != null && Soul != null && Milk != null && Scale >= SummonIndex.ScaleNeeded;
}

/// <summary>
/// Echidna is no letter: gathering_1_exceptional.json gather.echidna.moor runs at world.moor
/// ("A Walk on the Moor") with a soul ("ability"), a skill that learnt technique.summon.echidna
/// (Consider x.summon.echidna with Coil &amp; Chasm, Pentiments, Serpents &amp; Venoms or Wolf
/// Stories), Serpent-Milk in the recipe's preslot and Scale 7 in total. Serpent-Milk itself has
/// Scale 3. Without echidna.inspired she comes as "echidna" (leaves after 360 s), with it as
/// "echidna.persistent" (stays; Secretary Persistent candidate). Offered only once a skill has the
/// technique, so nothing unseen is named.
/// </summary>
public static class SummonIndex
{
    public const int ScaleNeeded = 7;
    public const string Technique = "technique.summon.echidna";

    public static SummonPlan Echidna(List<PersonEntry> people, List<ItemEntry> items, List<DeskEntry> desks)
    {
        List<ItemEntry> skills = items.Where(i => i.Aspects.AspectValue("skill") > 0 && i.Aspects.AspectValue(Technique) > 0
                                                  && !SlotCheck.InRunningRecipe(i))
            .OrderByDescending(i => i.Aspects.AspectValue("scale")).ToList();
        if (skills.Count == 0)
            return null; // technique not learnt: not offered
        var plan = new SummonPlan
        {
            Moor = desks.FirstOrDefault(d => d.Situation?.VerbId == "world.moor"),
            Skill = skills[0],
            Milk = items.FirstOrDefault(i => i.Stack?.EntityId == "serpentmilk" && !SlotCheck.InRunningRecipe(i)),
        };
        if (people.Any(p => p.Here && p.PersonId != null && p.PersonId.StartsWith("echidna", StringComparison.Ordinal)))
            plan.Status = "Here now";
        else if (items.Any(i => i.Stack?.EntityId != null && i.Stack.EntityId.StartsWith("echidna.enroute", StringComparison.Ordinal)))
            plan.Status = "On the way";
        else if (plan.Moor == null)
            plan.Status = "The Moor isn't reachable";
        else if (!plan.Moor.IsIdle)
            plan.Status = "The Moor is busy";
        if (plan.Moor != null)
        {
            int gap = ScaleNeeded - plan.Skill.Aspects.AspectValue("scale") - 3;
            var soulSlot = SlotCheck.Slot(plan.Moor, "ability");
            List<ItemEntry> souls = items.Where(i => i.Aspects.AspectValue("ability") > 0 && i.Aspects.AspectValue("fatigued") <= 0
                                                     && !SlotCheck.InRunningRecipe(i)
                                                     && (soulSlot == null || SlotCheck.Takes(soulSlot, i)))
                .GroupBy(i => i.Stack.EntityId).Select(g => g.First()).ToList();
            // The weakest soul that closes the gap; any soul when skill + milk already reach 7.
            plan.Soul = souls.Where(s => s.Aspects.AspectValue("scale") >= gap)
                .OrderBy(s => s.Aspects.AspectValue("scale")).ThenBy(s => s.Aspects.AspectValue("rank")).FirstOrDefault();
        }
        plan.Scale = plan.Skill.Aspects.AspectValue("scale") + (plan.Soul?.Aspects.AspectValue("scale") ?? 0) + 3;
        return plan;
    }
}
