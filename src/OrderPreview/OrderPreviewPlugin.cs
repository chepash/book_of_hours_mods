using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using SecretHistories.Assets.Scripts.Application.UI;
using SecretHistories.Core;
using SecretHistories.Entities;
using SecretHistories.UI;
using SecretHistories.UI.Aspects;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OrderPreview;

/// <summary>
/// Adds the result's icon to each discovered recipe in a desk's Craftable list: what an order
/// will deliver ("Order Armagnac (7/-)") or what a craft makes ("Craft: Eigengrau"). Clicking it
/// shows the item's details as if it were the item itself, and AltPeek shows them on Alt-hover,
/// since the icon is an IDetailable. Undiscovered (locked) recipes are a different display and
/// stay untouched, so nothing is spoiled.
/// Crafts name their result in Recipe.Effects. Orders don't: the product is found through the
/// game's own chain: recipe mutation orderplaced.X → its "provisioning" xtrigger mutates
/// contains.X → that aspect's "unwrapping" xtrigger spawns the product.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class OrderPreviewPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.orderpreview";
    public const string Name = "Order Preview"; // orders and crafts: shows any recipe's result
    public const string Version = "0.1.0";

    private static readonly FieldInfo RequirementsField = AccessTools.Field(typeof(AmbittableDisplayOpen), "requirementAspectsDisplay");
    private static readonly FieldInfo RecipeField = AccessTools.Field(typeof(AmbittableDisplayOpen), "_recipe");
    private Harmony _harmony;

    private void Awake()
    {
        _harmony = new Harmony(Guid);
        _harmony.PatchAll(typeof(PopulatePatch));
        _harmony.PatchAll(typeof(ShowPatch));
        StartCoroutine(FillExistingLists());
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    /// <summary>
    /// Lists built before this plugin (re)loaded aren't redrawn by the game until their recipes
    /// change, and a hot reload removes the old icons. So fill every existing list once.
    /// </summary>
    private System.Collections.IEnumerator FillExistingLists()
    {
        yield return null; // let the previous instance's OnDestroy clean up first
        int lists = 0, added = 0;
        foreach (AmbittablesDisplay display in Resources.FindObjectsOfTypeAll<AmbittablesDisplay>())
        {
            if (display == null || !display.gameObject.scene.IsValid())
                continue;
            lists++;
            added += FillMissingIcons(display);
        }
        // TEMP diagnostics
        int entries = 0, withRecipe = 0, withIcon = 0, resolvable = 0;
        var samples = new List<string>();
        foreach (AmbittableDisplayOpen entry in Resources.FindObjectsOfTypeAll<AmbittableDisplayOpen>())
        {
            if (!entry.gameObject.scene.IsValid()) continue;
            entries++;
            var recipe = RecipeField.GetValue(entry) as Recipe;
            if (recipe != null) withRecipe++;
            if (IconParent(entry).GetComponentInChildren<ProductIcon>(true) != null) withIcon++;
            if (ResolveProducts(recipe).Count > 0) resolvable++;
            if (samples.Count < 6) samples.Add($"{entry.name} recipe={recipe?.Id} effects={(recipe?.Effects == null ? "null" : string.Join(",", recipe.Effects))}");
        }
        Logger.LogInfo($"Filled {added} result icons in {lists} existing recipe lists; entries={entries} withRecipe={withRecipe} withIcon={withIcon} resolvable={resolvable}");
        foreach (string sample in samples) Logger.LogInfo("  " + sample);
    }

    private static int FillMissingIcons(AmbittablesDisplay display)
    {
        int added = 0;
        foreach (AmbittableDisplayOpen entry in display.GetComponentsInChildren<AmbittableDisplayOpen>(true))
        {
            Transform parent = IconParent(entry);
            if (parent.GetComponentInChildren<ProductIcon>(true) != null)
                continue;
            foreach (Element product in ResolveProducts(RecipeField.GetValue(entry) as Recipe))
            {
                Icons.Add(parent, product);
                added++;
            }
        }
        return added;
    }

    private void OnDestroy()
    {
        // ScriptEngine destroys the plugin on hot reload: remove the icons it added.
        _harmony?.UnpatchSelf();
        foreach (ProductIcon icon in Resources.FindObjectsOfTypeAll<ProductIcon>())
        {
            if (icon != null)
                Destroy(icon.gameObject);
        }
    }

    [HarmonyPatch(typeof(AmbittableDisplayOpen), nameof(AmbittableDisplayOpen.Populate))]
    private static class PopulatePatch
    {
        private static void Postfix(AmbittableDisplayOpen __instance, Recipe recipe)
        {
            Transform parent = IconParent(__instance);
            // Populate can run again on the same entry.
            foreach (ProductIcon old in parent.GetComponentsInChildren<ProductIcon>(true))
                Destroy(old.gameObject);

            List<Element> products = ResolveProducts(recipe);
            foreach (Element product in products)
                Icons.Add(parent, product);
            // TEMP diagnostics
            Debug.Log($"[OrderPreview] Populate {recipe?.Id} effects={(recipe?.Effects == null ? "null" : string.Join(",", recipe.Effects))} products={string.Join(",", products.Select(p => p.Id))} parent={parent.name}");
        }
    }

    /// <summary>
    /// TryShowAmbittables keeps existing entries when the set of recipes hasn't changed, so
    /// Populate doesn't run for entries made before this plugin (re)loaded. Fill those in.
    /// </summary>
    [HarmonyPatch(typeof(AmbittablesDisplay), nameof(AmbittablesDisplay.TryShowAmbittables))]
    private static class ShowPatch
    {
        private static void Postfix(AmbittablesDisplay __instance) => FillMissingIcons(__instance);

    }

    private static Transform IconParent(AmbittableDisplayOpen entry) =>
        (RequirementsField.GetValue(entry) as AureateAspectsDisplay)?.transform ?? entry.transform;

    private static class Icons
    {
        internal static void Add(Transform parent, Element product)
        {
            var go = new GameObject("OrderProduct", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.SetAsLastSibling();
            var image = go.AddComponent<Image>();
            image.sprite = ResourcesManager.GetAppropriateSpriteForElement(product);
            image.preserveAspect = true;
            var layout = go.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = 34f;
            layout.minHeight = layout.preferredHeight = 34f;
            go.AddComponent<ProductIcon>().Init(product, image.sprite);
        }
    }

    private const int MaxIcons = 3;

    /// <summary>An order's product, or else the items a recipe creates (Effects with a positive count).</summary>
    internal static List<Element> ResolveProducts(Recipe recipe)
    {
        var products = new List<Element>();
        if (recipe == null)
            return products;
        Element ordered = ResolveOrderedProduct(recipe);
        if (ordered != null)
        {
            products.Add(ordered);
            return products;
        }
        if (recipe.Effects == null)
            return products;
        Compendium compendium = Watchman.Get<Compendium>();
        foreach (KeyValuePair<string, string> effect in recipe.Effects)
        {
            // Values can be expressions ("-cost.pence"); only plain positive counts create things.
            if (!int.TryParse(effect.Value, out int count) || count <= 0)
                continue;
            Element element = compendium.GetEntityById<Element>(effect.Key);
            if (element == null || element.IsAspect || products.Contains(element))
                continue;
            products.Add(element);
            if (products.Count >= MaxIcons)
                break;
        }
        return products;
    }

    private static Element ResolveOrderedProduct(Recipe recipe)
    {
        if (recipe.Mutations == null)
            return null;
        Compendium compendium = Watchman.Get<Compendium>();
        foreach (MutationEffect mutation in recipe.Mutations)
        {
            if (mutation?.Mutate == null || !mutation.Mutate.StartsWith("orderplaced.") || mutation.Mutate.Contains("*"))
                continue;
            string contains = FirstTarget(compendium.GetEntityById<Element>(mutation.Mutate), "provisioning");
            string productId = FirstTarget(compendium.GetEntityById<Element>(contains), "unwrapping");
            Element product = compendium.GetEntityById<Element>(productId);
            if (product != null)
                return product;
        }
        return null;
    }

    private static string FirstTarget(Element element, string trigger)
    {
        if (element?.XTriggers == null || !element.XTriggers.TryGetValue(trigger, out List<MorphDetails> morphs) || morphs == null)
            return null;
        // Skip "^" (the triggering card itself, e.g. the mutate -1 that clears contains.X).
        return morphs.Select(m => m.Id).FirstOrDefault(id => !string.IsNullOrEmpty(id) && id != "^");
    }
}

/// <summary>
/// The product as a detailable: the game's details window (and AltPeek) can show it like an
/// actual card, with label, description, art and aspects.
/// </summary>
public class ProductIcon : MonoBehaviour, IDetailable, IPointerClickHandler
{
    private Element _element;
    private Sprite _sprite;

    public void Init(Element element, Sprite sprite)
    {
        _element = element;
        _sprite = sprite;
    }

    public string Label => _element.Label;
    public string Description => _element.Desc;
    public bool Unique => _element.Unique;
    public Sprite GetDetailSprite() => _sprite;

    public AspectAssociations GetAspectAssociations() =>
        new AspectAssociations { Present = new AspectsDictionary(_element.Aspects ?? new AspectsDictionary()) };

    public string GetIllumination(string key) => null;

    public void SetIllumination(string key, string value)
    {
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Handled here, so the click doesn't also select the recipe behind the icon.
        Watchman.Get<AureateNotifier>().ShowDetailable(this);
    }
}
