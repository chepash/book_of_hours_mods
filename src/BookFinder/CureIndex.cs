using System;
using System.Collections.Generic;
using System.Linq;
using SecretHistories.Entities;
using SecretHistories.UI;

namespace BookFinder;

/// <summary>How the game removes one contamination from a book.</summary>
public class Cure
{
    /// <summary>The book's aspect, e.g. contamination.witchworms.</summary>
    public string Contamination;
    /// <summary>What the skill must carry, e.g. effective.contamination.infestation.</summary>
    public string Effective;
    /// <summary>Principle the whole desk must add up to, e.g. winter.</summary>
    public string Principle;
    public int Amount;
}

/// <summary>
/// The cure recipes (0_consider_decontaminations.json), read from the compendium once:
/// remove.contamination.&lt;x&gt; at any library desk (library.*) needs a soul, the book with
/// contamination.&lt;x&gt;, a skill with effective.contamination.&lt;class&gt; (Infestation,
/// Theoplasma, Curse, Corruption) and one principle at 7, e.g. witchworms: infestation + Winter 7.
/// The skills of a class rarely carry that principle themselves, so the rest comes from the soul,
/// a memory and the desk's other slots.
/// </summary>
public static class CureIndex
{
    private const string RecipePrefix = "remove.contamination.";
    private static Dictionary<string, Cure> _cures;

    public static Cure For(string contamination)
    {
        if (_cures == null)
            _cures = Load();
        return _cures.TryGetValue(contamination, out Cure cure) ? cure : null;
    }

    private static Dictionary<string, Cure> Load()
    {
        var cures = new Dictionary<string, Cure>();
        foreach (Recipe recipe in Watchman.Get<Compendium>().GetEntitiesAsList<Recipe>())
        {
            if (recipe?.Id == null || !recipe.Id.StartsWith(RecipePrefix, StringComparison.Ordinal) || recipe.Reqs == null)
                continue;
            var cure = new Cure();
            foreach (KeyValuePair<string, string> req in recipe.Reqs)
            {
                if (req.Key.StartsWith("contamination.", StringComparison.Ordinal))
                    cure.Contamination = req.Key;
                else if (req.Key.StartsWith("effective.contamination.", StringComparison.Ordinal))
                    cure.Effective = req.Key;
                else if (WorldIndex.IsPrinciple(req.Key) && int.TryParse(req.Value, out int amount))
                {
                    cure.Principle = req.Key;
                    cure.Amount = amount;
                }
            }
            if (cure.Contamination != null && cure.Effective != null && cure.Principle != null)
                cures[cure.Contamination] = cure;
        }
        return cures;
    }

    /// <summary>Skill cards the player holds that work against this cure's class.</summary>
    public static List<ItemEntry> SkillsFor(Cure cure, IEnumerable<ItemEntry> items)
    {
        return items.Where(i => i.Aspects.AspectValue("skill") > 0 && i.Aspects.AspectValue(cure.Effective) > 0)
            .GroupBy(i => i.Stack.EntityId)
            .Select(g => g.First())
            .ToList();
    }
}
