using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BohShared;
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
/// A situation window shows the same icons for the recipe it's about to start or is running,
/// at the left end of its footer (the Start button's bar, empty on the left): the game names it
/// only in the note text. Only
/// craftable recipes (what the player is making on purpose), never events or hint-only ones.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class OrderPreviewPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.orderpreview";
    public const string Name = "Order Preview"; // orders and crafts: shows any recipe's result
    public const string Version = "0.1.0";

    private static readonly FieldInfo RequirementsField = AccessTools.Field(typeof(AmbittableDisplayOpen), "requirementAspectsDisplay");
    private static readonly FieldInfo RecipeField = AccessTools.Field(typeof(AmbittableDisplayOpen), "_recipe");
    private static readonly FieldInfo PayloadField = AccessTools.Field(typeof(AureateWindow), "_payload");
    private Harmony _harmony;

    private void Awake()
    {
        _harmony = new Harmony(Guid);
        _harmony.PatchAll(typeof(PopulatePatch));
        _harmony.PatchAll(typeof(ShowPatch));
        _harmony.PatchAll(typeof(WindowPatch));
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
        Logger.LogInfo($"Filled {added} result icons in {lists} existing recipe lists");

        // Situation windows too: a recipe already running doesn't call DisplayButtonState again
        // until its state changes.
        foreach (AureateWindow window in Resources.FindObjectsOfTypeAll<AureateWindow>())
        {
            if (window != null && window.gameObject.scene.IsValid() && PayloadField.GetValue(window) is Situation situation)
                WindowPatch.Postfix(window, situation);
        }
    }

    private static int FillMissingIcons(AmbittablesDisplay display)
    {
        int added = 0;
        foreach (AmbittableDisplayOpen entry in display.GetComponentsInChildren<AmbittableDisplayOpen>(true))
        {
            Transform parent = IconParent(entry);
            if (parent.GetComponentInChildren<ProductIcon>(true) != null)
                continue;
            List<Element> products = ResolveProducts(RecipeField.GetValue(entry) as Recipe);
            for (int i = 0; i < products.Count; i++)
            {
                Icons.Add(parent, products[i], i);
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
        foreach (WindowProducts strip in Resources.FindObjectsOfTypeAll<WindowProducts>())
        {
            if (strip != null)
                Destroy(strip.gameObject);
        }
    }

    /// <summary>
    /// DisplayButtonState runs whenever the window's situation changes state or its slots change,
    /// so it sees every new prediction and the start of the recipe.
    /// </summary>
    [HarmonyPatch(typeof(AureateWindow), "DisplayButtonState")]
    private static class WindowPatch
    {
        internal static void Postfix(AureateWindow __instance, Situation situation)
        {
            Recipe recipe = situation?.GetCurrentRecipe();
            bool show = recipe != null && recipe.Craftable && !recipe.HintOnly
                && (situation.TimeRemaining > 0f || situation.StateIdentifier == SecretHistories.Enums.StateEnum.Unstarted);
            WindowProducts.Show(__instance, show ? recipe : null);
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
            for (int i = 0; i < products.Count; i++)
                Icons.Add(parent, products[i], i);
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

    /// <summary>
    /// The row's background (ButtonBg, parent of the requirements display). Icons are placed there
    /// outside the layout: inside the requirements grid they wrapped onto a new line when a recipe
    /// had three requirements and spilled over the next header.
    /// </summary>
    private static Transform IconParent(AmbittableDisplayOpen entry)
    {
        Transform requirements = (RequirementsField.GetValue(entry) as AureateAspectsDisplay)?.transform;
        return requirements != null && requirements.parent != null ? requirements.parent : entry.transform;
    }

    internal static class Icons
    {
        private const float Size = 30f;
        private const float Margin = 16f;
        /// <summary>Slightly below the row's middle: at the bottom corner the badge touched the border.</summary>
        private const float Drop = -9f;

        /// <summary>
        /// At the row's right end, vertically centred (a little low), on a dark badge so it doesn't
        /// read as one more requirement. Several results line up right to left.
        /// </summary>
        internal static void Add(Transform parent, Element product, int index, float size = Size, float margin = Margin, float drop = Drop)
        {
            var go = new GameObject("OrderProduct", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.SetAsLastSibling();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(size + 6f, size + 6f);
            rect.anchoredPosition = new Vector2(-margin - index * (size + 10f), drop);
            go.AddComponent<LayoutElement>().ignoreLayout = true;

            var badge = go.AddComponent<Image>();
            badge.sprite = GameStyle.Sprite("box-rnd-16px");
            badge.type = Image.Type.Sliced;
            badge.color = new Color(0f, 0f, 0f, 0.55f);

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.SetParent(rect, false);
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(3f, 3f);
            iconRect.offsetMax = new Vector2(-3f, -3f);
            var image = iconGo.AddComponent<Image>();
            image.sprite = ResourcesManager.GetAppropriateSpriteForElement(product);
            image.preserveAspect = true;
            image.raycastTarget = false;

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

/// <summary>
/// The result icons in a situation window: a holder at the left of the footer's button bar
/// (first tried in Content's bottom right, where it could cover the notes or slots), rebuilt only
/// when the recipe changes.
/// </summary>
public class WindowProducts : MonoBehaviour
{
    private const float IconSize = 32f;
    private const float LeftMargin = 24f;
    private string _recipeId;

    internal static void Show(AureateWindow window, Recipe recipe)
    {
        Transform host = window.transform.Find("Footer/ButtonsBar") ?? window.transform.Find("Footer");
        if (host == null)
            return;
        WindowProducts strip = host.GetComponentInChildren<WindowProducts>(true);
        if (recipe == null)
        {
            if (strip != null)
                strip.gameObject.SetActive(false);
            return;
        }
        if (strip == null)
        {
            var go = new GameObject("RecipeProducts", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(host, false);
            rect.SetAsLastSibling();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(LeftMargin, 0f);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            strip = go.AddComponent<WindowProducts>();
        }
        strip.gameObject.SetActive(true);
        if (strip._recipeId == recipe.Id)
            return;
        strip._recipeId = recipe.Id;
        foreach (Transform child in strip.transform)
            Destroy(child.gameObject);
        List<Element> products = OrderPreviewPlugin.ResolveProducts(recipe);
        // Icons.Add lines icons up right to left from the holder's right edge; size the holder
        // to fit them so they start at its left.
        ((RectTransform)strip.transform).sizeDelta = new Vector2(
            Mathf.Max(0, products.Count * (IconSize + 10f) - 4f), IconSize + 6f);
        for (int i = 0; i < products.Count; i++)
            OrderPreviewPlugin.Icons.Add(strip.transform, products[i], i, IconSize, 0f, 0f);
    }
}
