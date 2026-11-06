using System;
using System.Collections.Generic;
using System.Linq;
using SecretHistories.Core;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.UI;

namespace BookFinder;

/// <summary>One desk with the skill and the soul (if needed) its slots really take.</summary>
public class InviteOption
{
    public DeskEntry Desk;
    public ItemEntry Skill;
    /// <summary>Null when ink + skill reach the amount alone.</summary>
    public ItemEntry Soul;
}

/// <summary>How to write one visitor's invitation now, or why it can't be written.</summary>
public class InvitePlan
{
    public PersonEntry Person;
    public string Principle;
    public int Amount;
    /// <summary>"Written: post it", "On the way", "Here", "Needs a special item"... null when writable.</summary>
    public string Status;
    public ItemEntry WritingCase;
    public ItemEntry Ink;
    /// <summary>Weakest first; each with whether it reaches the amount without a soul.</summary>
    public List<ItemEntry> Skills = new List<ItemEntry>();
    public HashSet<ItemEntry> SkillsNeedingSoul = new HashSet<ItemEntry>();
    /// <summary>Souls that top up the strongest skill to the amount (empty when no skill needs one).</summary>
    public List<ItemEntry> Souls = new List<ItemEntry>();
    /// <summary>How much of the principle the soul must add with the strongest skill.</summary>
    public int SoulNeeds;
    public List<DeskEntry> Desks = new List<DeskEntry>();
    /// <summary>Per desk, a checked skill and soul; those needing no soul first.</summary>
    public List<InviteOption> Options = new List<InviteOption>();
    public DeskEntry PostOffice;
    public bool Ready => Status == null && WritingCase != null && Ink != null && Skills.Count > 0 && Desks.Count > 0;
}

/// <summary>
/// Invitations (DLC_HOL_correspondence_summoning.json): write.summon.&lt;visitor&gt; at a desk
/// (*.consider) needs the Writing Case with that visitor's address (mutation address.&lt;visitor&gt;,
/// added from a calling card), any ink, a thing, and one principle at N
/// (e.g. Agdistis: Sky 3) counted over the whole desk. Perinculate is refused. The ink is a liquid,
/// so the letter's fatiguing turns it into dregs: the cheapest ink that lets the sum reach N is
/// picked (a typewriter carries "ink" too and is no liquid, so it is never used up). The letter is
/// then posted at the Post Office (1 penny), and the visitor arrives one part of the day later.
/// Only visitors whose address is in the Writing Case are offered: those are visitors the player
/// has met and noted, so nothing unseen is named.
/// </summary>
public static class InviteIndex
{
    private static Dictionary<string, Recipe> _summons;

    private static Dictionary<string, Recipe> Summons()
    {
        if (_summons != null)
            return _summons;
        _summons = new Dictionary<string, Recipe>();
        foreach (Recipe recipe in Watchman.Get<Compendium>().GetEntitiesAsList<Recipe>())
        {
            if (recipe?.Id != null && recipe.Id.StartsWith("write.summon.", StringComparison.Ordinal))
                _summons[recipe.Id.Substring("write.summon.".Length)] = recipe;
        }
        return _summons;
    }

    public static List<InvitePlan> Plans(List<PersonEntry> people, List<ItemEntry> items, List<DeskEntry> desks)
    {
        var plans = new List<InvitePlan>();
        ItemEntry wc = items.FirstOrDefault(i => i.Stack?.EntityId == "wc");
        if (wc == null)
            return plans;
        AspectsDictionary wcAspects = wc.Stack.GetAspects(true);
        // Cheapest first: inks that are no liquid (typewriters) cost nothing, then by total principles.
        List<ItemEntry> inks = items.Where(i => i.Aspects.AspectValue("ink") > 0 && i.Stack.EntityId != "perinculate"
                                                && i.Token != null && i.Token.IsValid() && !InRunningRecipe(i))
            .OrderBy(i => i.Aspects.AspectValue("liquid") > 0)
            .ThenBy(i => i.Aspects.Where(a => WorldIndex.IsPrinciple(a.Key)).Sum(a => a.Value))
            .ToList();
        List<ItemEntry> skills = items.Where(i => i.Aspects.AspectValue("skill") > 0 && !InRunningRecipe(i)).ToList();
        // Souls count too (the desk's soul slot adds its principles to the sum): fresh ones only.
        List<ItemEntry> souls = items.Where(i => i.Aspects.AspectValue("ability") > 0 && i.Aspects.AspectValue("fatigued") <= 0
                                                 && !InRunningRecipe(i))
            .GroupBy(i => i.Stack.EntityId).Select(g => g.First()).ToList();
        HashSet<string> onTheWay = new HashSet<string>(Watchman.Get<HornedAxe>().GetRegisteredSituations()
            .Where(s => s?.VerbId != null && s.VerbId.StartsWith("visitor.", StringComparison.Ordinal))
            .Select(s => s.VerbId.Substring("visitor.".Length)));
        DeskEntry post = desks.FirstOrDefault(d => d.Situation?.VerbId != null && d.Situation.VerbId.StartsWith("village.postoffice", StringComparison.Ordinal));

        foreach (PersonEntry person in people.Where(p => p.Kind == PersonKind.Visitor))
        {
            string id = person.PersonId;
            if (!Summons().TryGetValue(id, out Recipe summon) || wcAspects.AspectValue("address." + id) <= 0)
                continue; // no invitation for them, or their address isn't noted yet
            var plan = new InvitePlan { Person = person, WritingCase = wc, PostOffice = post };
            foreach (KeyValuePair<string, string> req in summon.Reqs)
            {
                if (WorldIndex.IsPrinciple(req.Key) && int.TryParse(req.Value, out int n))
                {
                    plan.Principle = req.Key;
                    plan.Amount = n;
                }
            }
            if (person.Here)
                plan.Status = "Here now";
            else if (onTheWay.Contains(id))
                plan.Status = "Invitation posted: on the way";
            else if (items.Any(i => i.Stack?.EntityId == "letter.summon." + id))
                plan.Status = "Invitation written: post it";
            else if (plan.Principle == null)
                plan.Status = "Needs a special item";
            if (plan.Status != null)
            {
                plans.Add(plan);
                continue;
            }
            Choose(plan, inks, skills, souls, desks);
            plans.Add(plan);
        }
        return plans.OrderBy(p => p.Status != null).ThenByDescending(p => p.Ready).ThenBy(p => p.Person.Name).ToList();
    }

    /// <summary>
    /// The cheapest ink that works, then per free desk the weakest skill that reaches the amount with
    /// it - alone, or with a soul topping it up (the letter sums the principle over the whole desk).
    /// Each desk is checked on its own: its skill slot and its soul slot take only cards with one of
    /// the desk's principles, so a soul that fits one desk is refused by another.
    /// </summary>
    private static void Choose(InvitePlan plan, List<ItemEntry> inks, List<ItemEntry> skills, List<ItemEntry> souls, List<DeskEntry> desks)
    {
        string p = plan.Principle;
        List<DeskEntry> writingDesks = desks.Where(d => d.IsIdle && d.Situation?.VerbId != null
                && d.Situation.VerbId.EndsWith(".consider", StringComparison.Ordinal)
                && AnySlotTakes(d, plan.WritingCase)).ToList();
        foreach (ItemEntry ink in inks)
        {
            int inkGives = ink.Aspects.AspectValue(p);
            var options = new List<InviteOption>();
            foreach (DeskEntry d in writingDesks.Where(d => AnySlotTakes(d, ink, "skill")))
            {
                SphereSpec soulSlot = SlotCheck.Slot(d, "ability");
                InviteOption best = null;
                foreach (ItemEntry skill in skills.Where(s => s.Aspects.AspectValue(p) > 0 && SkillSlotTakes(d, s))
                             .OrderByDescending(s => inkGives + s.Aspects.AspectValue(p) >= plan.Amount) // no soul needed first
                             .ThenBy(s => s.Aspects.AspectValue(p))) // the weakest that suffices
                {
                    int gap = plan.Amount - inkGives - skill.Aspects.AspectValue(p);
                    if (gap <= 0)
                    {
                        best = new InviteOption { Desk = d, Skill = skill };
                        break;
                    }
                    // The weakest soul that closes the gap and that this desk's soul slot takes.
                    ItemEntry soul = soulSlot == null ? null : souls
                        .Where(s => s.Aspects.AspectValue(p) >= gap && SlotCheck.Takes(soulSlot, s))
                        .OrderBy(s => s.Aspects.AspectValue(p)).ThenBy(s => s.Aspects.AspectValue("rank")).FirstOrDefault();
                    if (soul != null)
                    {
                        best = new InviteOption { Desk = d, Skill = skill, Soul = soul };
                        break;
                    }
                }
                if (best != null)
                    options.Add(best);
            }
            if (options.Count == 0)
                continue;
            plan.Ink = ink;
            plan.Options = options.OrderBy(o => o.Soul != null).ThenBy(o => o.Skill.Aspects.AspectValue(p)).ToList();
            plan.Desks = plan.Options.Select(o => o.Desk).ToList();
            plan.Skills = plan.Options.Select(o => o.Skill).Distinct().ToList();
            foreach (InviteOption o in plan.Options.Where(o => o.Soul != null))
                plan.SkillsNeedingSoul.Add(o.Skill);
            plan.Souls = plan.Options.Where(o => o.Soul != null).Select(o => o.Soul).Distinct().ToList();
            return;
        }
        plan.Status = inks.Count == 0 ? "No ink" : "No skill reaches " + WorldIndex.LabelOf(plan.Principle) + " " + plan.Amount + " at a free desk";
    }

    private static bool AnySlotTakes(DeskEntry desk, ItemEntry item, string exceptEssential = null) =>
        desk.Situation.Verb.Slots != null && desk.Situation.Verb.Slots.Any(s =>
            (s.Essential == null || !s.Essential.ContainsKey("ability") && !s.Essential.ContainsKey("skill") && !s.Essential.ContainsKey("memory"))
            && s.CheckPayloadAllowedHere(item.Stack).MatchType == SlotMatchForAspectsType.Okay);

    private static bool SkillSlotTakes(DeskEntry desk, ItemEntry skill)
    {
        SphereSpec slot = desk.Situation.Verb.Slots?.FirstOrDefault(s => s.Essential != null && s.Essential.ContainsKey("skill"));
        return slot != null && slot.CheckPayloadAllowedHere(skill.Stack).MatchType == SlotMatchForAspectsType.Okay;
    }

    private static bool InRunningRecipe(ItemEntry item)
    {
        if (item.HostToken == null || !item.HostToken.IsValid() || !(item.HostToken.Payload is Situation host))
            return false;
        try
        {
            StateEnum state = host.StateIdentifier;
            return state != StateEnum.Unstarted && state != StateEnum.Inchoate && state != StateEnum.Complete;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
