using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SecretHistories.Core;
using SecretHistories.Entities;
using SecretHistories.UI;
using UnityEngine;

namespace BookFinder;

/// <summary>One line of the Cookbook: a kitchen recipe, or a brew (Recipe is null then).</summary>
public class CookEntry
{
    public string Id;
    public string Label;
    public Recipe Recipe;
    public List<string> Results;
    public List<string> Ingredients;
    public bool OnlyThese;
}

/// <summary>
/// Kitchen recipes (DLC_HOL_cooking.json: cook.*, all inheriting _cook at library.kitchen.*, a soul
/// plus one to three ingredients by card id or aspect) and which of them the player has cooked.
/// The game keeps no history of executed recipes, so the mod records it: a Harmony prefix on
/// Situation.ExecuteCurrentRecipe (BookFinderPlugin) adds every cook.* it sees to
/// BepInEx\config\shirofaii.boh.bookfinder.cooked.txt. Older cooking is taken from Player.log /
/// Player-prev.log ("recipe 'cook.x' has executed", only the last two sessions) and, on the first
/// run, from a seed of dishes made for earlier salons.
/// </summary>
public static class CookBook
{
    private const string Prefix = "cook.";
    /// <summary>Brews are recorded as "brew:&lt;packet id&gt;": the recipe (brew.kitchen) is the same for all.</summary>
    public const string BrewPrefix = "brew:";

    /// <summary>Cooked before this mod recorded anything: the salon dishes of the first year.</summary>
    private static readonly string[] Seed =
    {
        "cook.egg", "cook.mackerel.intoxicating", "cook.pumpkin.dripping", "cook.blue.crown",
        "cook.knife.veg.marrow.dark", "cook.bowls.kitchen.dough.egg", "cook.dough.cake.honey", "cook.egg.bread", "cook.dough", "cook.fruit.bowl.pears",
        "brew:packet.coffee.dawnlion", "brew:packet.cocoa",
    };

    private static readonly Regex Executed = new Regex(@"recipe '(cook\.[^']+)' has executed", RegexOptions.Compiled);

    private static List<Recipe> _all;
    private static HashSet<string> _cooked;

    private static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, BookFinderPlugin.Guid + ".cooked.txt");

    /// <summary>Every kitchen recipe, read from the compendium once.</summary>
    public static List<Recipe> All() => _all ??= Watchman.Get<Compendium>().GetEntitiesAsList<Recipe>()
        .Where(r => r?.Id != null && r.Id.StartsWith(Prefix, StringComparison.Ordinal) && r.Reqs != null && r.Effects != null)
        .ToList();

    public static bool IsCooked(string recipeId) => Cooked().Contains(recipeId);

    /// <summary>Ids of the recipes cooked so far: the file, plus what the current logs show.</summary>
    public static HashSet<string> Cooked()
    {
        if (_cooked != null)
            return _cooked;
        _cooked = new HashSet<string>();
        try
        {
            if (File.Exists(FilePath))
                _cooked.UnionWith(File.ReadAllLines(FilePath).Select(l => l.Trim())
                    .Where(l => l.StartsWith(Prefix, StringComparison.Ordinal) || l.StartsWith(BrewPrefix, StringComparison.Ordinal)));
            else
                _cooked.UnionWith(Seed);
            foreach (string log in new[] { "Player.log", "Player-prev.log" })
            {
                string path = Path.Combine(Application.persistentDataPath, log);
                if (!File.Exists(path))
                    continue;
                // The game keeps the log open for writing: read it shared.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                foreach (Match m in Executed.Matches(reader.ReadToEnd()))
                    _cooked.Add(m.Groups[1].Value);
            }
            Save();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Book Finder] Can't read the cooked recipes: " + e.Message);
        }
        return _cooked;
    }

    /// <summary>Called for every executed recipe; keeps the kitchen ones (cook.*) and brews (brew:&lt;card&gt;).</summary>
    public static void Record(string recipeId)
    {
        if (recipeId == null || !(recipeId.StartsWith(Prefix, StringComparison.Ordinal) || recipeId.StartsWith(BrewPrefix, StringComparison.Ordinal))
            || !Cooked().Add(recipeId))
            return;
        Save();
    }

    /// <summary>
    /// Brewing (crafting_1_simplemanipulations.json: brew.kitchen / brew.fireplace) takes any
    /// "brewable" card plus a soul; the card's own "dist" xtrigger decides what it becomes (a packet of
    /// Dawnlion Coffee -> a pot). So the brewed card is what's worth remembering, read from the
    /// situation's slots just before the recipe runs. Hatching eggs (also brewable) are left out.
    /// </summary>
    public static void RecordBrew(Situation situation)
    {
        string id = situation?.GetCurrentRecipe()?.Id;
        if (id != "brew.kitchen" && id != "brew.fireplace")
            return;
        foreach (SecretHistories.Spheres.Sphere sphere in situation.GetInteriorSpheres())
        {
            foreach (Token token in sphere.GetElementTokens())
            {
                if (!(token.Payload is ElementStack stack))
                    continue;
                AspectsDictionary aspects = stack.GetAspects(true);
                if (aspects.AspectValue("brewable") > 0 && aspects.AspectValue("egg") <= 0)
                    Record(BrewPrefix + stack.EntityId);
            }
        }
    }

    /// <summary>What a brewable card becomes (its "dist" xtrigger), or null.</summary>
    public static string BrewResult(string cardId)
    {
        Element element = WorldIndex.ElementOf(cardId);
        if (element?.XTriggers == null || !element.XTriggers.TryGetValue("dist", out List<MorphDetails> morphs) || morphs == null)
            return null;
        return morphs.Select(m => m.Id).FirstOrDefault(m => !string.IsNullOrEmpty(m));
    }

    /// <summary>Everything cooked before, as entries for the Cookbook: kitchen recipes and brews.</summary>
    public static List<CookEntry> Entries()
    {
        var entries = All().Where(r => IsCooked(r.Id)).Select(r => new CookEntry
        {
            Id = r.Id, Label = r.Label, Recipe = r, Results = Results(r), Ingredients = Ingredients(r), OnlyThese = OnlyThese(r),
        }).ToList();
        foreach (string id in Cooked().Where(c => c.StartsWith(BrewPrefix, StringComparison.Ordinal)))
        {
            string card = id.Substring(BrewPrefix.Length);
            string result = BrewResult(card);
            if (result == null)
                continue;
            entries.Add(new CookEntry
            {
                Id = id, Label = "Brew: " + WorldIndex.LabelOf(card), Results = new List<string> { result },
                Ingredients = new List<string> { card },
            });
        }
        return entries;
    }

    private static void Save()
    {
        try
        {
            File.WriteAllLines(FilePath, _cooked.OrderBy(id => id, StringComparer.Ordinal));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Book Finder] Can't save the cooked recipes: " + e.Message);
        }
    }

    /// <summary>What the recipe makes (its positive effects), the dish first.</summary>
    public static List<string> Results(Recipe recipe) => recipe.Effects
        .Where(e => int.TryParse(e.Value, out int n) && n > 0)
        .Select(e => e.Key)
        .ToList();

    /// <summary>Positive requirements except the soul: ingredients and tools, by card id or aspect.</summary>
    public static List<string> Ingredients(Recipe recipe) => recipe.Reqs
        .Where(r => r.Key != "ability" && int.TryParse(r.Value, out int n) && n > 0)
        .Select(r => r.Key)
        .ToList();

    /// <summary>"ingredient": -2 and the like: nothing else edible may go in.</summary>
    public static bool OnlyThese(Recipe recipe) =>
        recipe.Reqs.TryGetValue("ingredient", out string v) && int.TryParse(v, out int n) && n < 0;

    /// <summary>
    /// A kitchen recipe that makes this card (e.g. dough.cake from Mix Cake Batter), the cooked one
    /// first; null if none does. Only exact card ids: an aspect (egg) has many sources.
    /// </summary>
    public static Recipe MakerOf(string cardId) => All()
        .Where(r => r.Effects.TryGetValue(cardId, out string v) && int.TryParse(v, out int n) && n > 0)
        .OrderByDescending(r => IsCooked(r.Id))
        .FirstOrDefault();
}
