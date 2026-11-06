using System;
using System.Collections.Generic;
using System.Linq;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.UI;

namespace BookFinder;

/// <summary>A crafting recipe that makes an ink: skill + soul + one principle, at a library workstation.</summary>
public class InkRecipe
{
    public Recipe Recipe;
    /// <summary>The ink element made, e.g. yewgall.ink.</summary>
    public string Ink;
    /// <summary>The skill the recipe needs, e.g. s.leaves.thorns.</summary>
    public string Skill;
    public string Principle;
    public int Amount;
    /// <summary>Other requirements (materials like leathy or ichor.vitreous), if any.</summary>
    public List<string> Materials = new List<string>();
}

/// <summary>How to brew one ink now: where, with which skill card and which souls.</summary>
public class InkPlan
{
    public InkRecipe Recipe;
    public ItemEntry Skill;
    public int SkillGives;
    public List<DeskEntry> Desks = new List<DeskEntry>();
    /// <summary>One card per soul kind that fits a listed desk and reaches the amount with the skill.</summary>
    public List<ItemEntry> Souls = new List<ItemEntry>();
    /// <summary>Per desk, the souls its own soul slot takes (strongest first): slots differ by desk.</summary>
    public Dictionary<DeskEntry, List<ItemEntry>> SoulsByDesk = new Dictionary<DeskEntry, List<ItemEntry>>();
    public bool Unlocked;
    /// <summary>A desk already running this very recipe, or null.</summary>
    public DeskEntry BrewingAt;
    public bool Ready => BrewingAt == null && Desks.Count > 0 && Souls.Count > 0 && Recipe.Materials.Count == 0;
}

/// <summary>
/// Ink recipes are ordinary craft recipes (craft.&lt;skill&gt;_&lt;ink&gt;_&lt;principle&gt;, actionid library.*):
/// reqs ability + s.&lt;skill&gt; + &lt;principle&gt;: N, Prentice ones with no material at all. What may be
/// shown follows the game's own rule (UnstartedState.TryPopulateRecipesPredictionWithAmbittables,
/// AmbittableDisplayLocked): a recipe executable with the cards on the desk is shown in full, one
/// that isn't shows as "undiscovered" unless the character has unlocked it before
/// (Character.IsAmbittableRecipeUnlocked). So a plan is listed when its recipe is unlocked, or
/// when the player's own skill and a soul can do it right now at a reachable desk - exactly what
/// the game would reveal once those cards are put down.
/// </summary>
public static class InkIndex
{
    private static List<InkRecipe> _recipes;

    public static List<InkRecipe> Recipes()
    {
        if (_recipes != null)
            return _recipes;
        _recipes = new List<InkRecipe>();
        foreach (Recipe recipe in Watchman.Get<Compendium>().GetEntitiesAsList<Recipe>())
        {
            if (recipe?.Id == null || !recipe.Ambittable || recipe.HintOnly || recipe.Effects == null || recipe.Reqs == null)
                continue;
            string ink = recipe.Effects.Keys.FirstOrDefault(id => (WorldIndex.ElementOf(id)?.Aspects?.AspectValue("ink") ?? 0) > 0);
            if (ink == null)
                continue;
            var item = new InkRecipe { Recipe = recipe, Ink = ink };
            foreach (KeyValuePair<string, string> req in recipe.Reqs)
            {
                if (req.Key == "ability")
                    continue;
                if (req.Key.StartsWith("s.", StringComparison.Ordinal))
                    item.Skill = req.Key;
                else if (WorldIndex.IsPrinciple(req.Key) && int.TryParse(req.Value, out int amount))
                {
                    item.Principle = req.Key;
                    item.Amount = amount;
                }
                else if (int.TryParse(req.Value, out int n) && n > 0)
                    item.Materials.Add(req.Key);
            }
            if (item.Skill != null && item.Principle != null)
                _recipes.Add(item);
        }
        return _recipes;
    }

    public static List<InkPlan> Plans(List<ItemEntry> items, List<DeskEntry> desks)
    {
        Character protag = null;
        try
        {
            protag = Watchman.Get<Stable>().Protag();
        }
        catch (Exception)
        {
            // no character yet
        }
        List<ItemEntry> souls = items.Where(i => i.Aspects.AspectValue("ability") > 0 && i.Aspects.AspectValue("fatigued") <= 0
                                                  && !InRunningRecipe(i) && i.Token != null && i.Token.IsValid()).ToList();
        var plans = new List<InkPlan>();
        foreach (InkRecipe recipe in Recipes())
        {
            // Already brewing: the recipe runs in a desk (its skill card sits in that desk now).
            DeskEntry brewing = desks.FirstOrDefault(d => d.IsWorking && CurrentRecipeId(d) == recipe.Recipe.Id);
            if (brewing != null)
            {
                ItemEntry busySkill = items.FirstOrDefault(i => i.Stack?.EntityId == recipe.Skill);
                if (busySkill != null)
                    plans.Add(new InkPlan { Recipe = recipe, Skill = busySkill, SkillGives = busySkill.Aspects.AspectValue(recipe.Principle), BrewingAt = brewing });
                continue;
            }
            ItemEntry skill = items.FirstOrDefault(i => i.Stack?.EntityId == recipe.Skill && !InRunningRecipe(i));
            if (skill == null)
                continue; // not the player's skill, or it's busy in a desk: nothing to show
            var plan = new InkPlan
            {
                Recipe = recipe,
                Skill = skill,
                SkillGives = skill.Aspects.AspectValue(recipe.Principle),
                Unlocked = protag != null && protag.IsAmbittableRecipeUnlocked(recipe.Recipe.Id),
            };
            var soulKinds = new HashSet<string>();
            foreach (DeskEntry desk in desks)
            {
                if (!desk.IsIdle || desk.Situation?.Verb == null || !desk.Situation.Verb.Id.StartsWith("library.", StringComparison.Ordinal))
                    continue;
                SphereSpec skillSlot = Slot(desk, "skill");
                SphereSpec soulSlot = Slot(desk, "ability");
                if (skillSlot == null || soulSlot == null || !Fits(skillSlot, skill))
                    continue;
                List<ItemEntry> fitting = souls.Where(s => Fits(soulSlot, s)
                    && plan.SkillGives + s.Aspects.AspectValue(recipe.Principle) >= recipe.Amount).ToList();
                if (fitting.Count == 0)
                    continue;
                plan.Desks.Add(desk);
                plan.SoulsByDesk[desk] = fitting.OrderByDescending(s => s.Aspects.AspectValue(recipe.Principle))
                    .GroupBy(s => s.Stack.EntityId).Select(g => g.First()).ToList();
                foreach (ItemEntry soul in fitting.OrderByDescending(s => s.Aspects.AspectValue(recipe.Principle)))
                {
                    if (soulKinds.Add(soul.Stack.EntityId))
                        plan.Souls.Add(soul);
                }
            }
            if (plan.Ready || plan.Unlocked)
                plans.Add(plan);
        }
        return plans.OrderBy(p => p.BrewingAt != null).ThenByDescending(p => p.Ready)
            .ThenBy(p => WorldIndex.LabelOf(p.Recipe.Ink), StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Skill.Title)
            .ToList();
    }

    /// <summary>
    /// The card sits in a desk whose recipe is already running. A card only laid into an idle
    /// desk's slot is still free: the player is putting the brew together. So is one waiting in
    /// a finished desk's results.
    /// </summary>
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

    private static string CurrentRecipeId(DeskEntry desk)
    {
        try
        {
            return desk.Situation?.GetCurrentRecipe()?.Id;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static SphereSpec Slot(DeskEntry desk, string essential) =>
        desk.Situation.Verb.Slots?.FirstOrDefault(s => s.Essential != null && s.Essential.ContainsKey(essential));

    private static bool Fits(SphereSpec slot, ItemEntry item) =>
        slot.CheckPayloadAllowedHere(item.Stack).MatchType == SlotMatchForAspectsType.Okay;
}
