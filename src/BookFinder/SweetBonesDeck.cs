using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SecretHistories.Entities;
using SecretHistories.Infrastructure;
using SecretHistories.Spheres;
using SecretHistories.UI;
using UnityEngine;

namespace BookFinder;

/// <summary>
/// The Sweet Bones' "Seek Unusual Help" deck (sweetbones.employables: ten employable cards -
/// Engineer, Musician, Painter, Poet twice, Fugitive and Nun once). Dealer.Deal takes the draw pile's
/// first card; the pile is shuffled only when the deck is created or runs out (resetonexhaustion).
/// So the order is fixed in advance and the next helper is known. Reordering the pile's token list
/// (Sphere._allTokens, then marking its element cache dirty) changes who comes next; the save
/// keeps the pile in that order.
/// </summary>
public static class SweetBonesDeck
{
    public const string DeckId = "sweetbones.employables";
    private static readonly FieldInfo TokensField = AccessTools.Field(typeof(Sphere), "_allTokens");
    private static readonly FieldInfo DirtyField = AccessTools.Field(typeof(Sphere), "_elementsCacheDirty");
    public static Sphere Pile()
    {
        try
        {
            return Watchman.Get<DealersTable>()?.GetDrawPile(DeckId) as Sphere;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static DeckSpec Spec() => Watchman.Get<Compendium>().GetEntityById<DeckSpec>(DeckId);

    /// <summary>The pile top first: the card the next "Seek Unusual Help" will deal.</summary>
    public static List<Token> Cards()
    {
        Sphere pile = Pile();
        return pile == null ? new List<Token>() : pile.GetElementTokens().ToList();
    }

    /// <summary>
    /// Cards dealt since the last shuffle: the deck's spec minus what is still in the pile (there is
    /// no discard pile; a dealt card simply leaves it). Order follows the spec, not the draw order.
    /// </summary>
    public static List<string> Drawn()
    {
        var drawn = new List<string>();
        DeckSpec spec = Spec();
        if (spec == null)
            return drawn;
        var left = new Dictionary<string, int>();
        foreach (Token card in Cards())
            left[card.PayloadEntityId] = left.TryGetValue(card.PayloadEntityId, out int n) ? n + 1 : 1;
        List<string> unique = Watchman.Get<Stable>()?.Protag()?.UniqueElementsManifested ?? new List<string>();
        foreach (string id in spec.Spec)
        {
            if (unique.Contains(id))
                continue;
            if (left.TryGetValue(id, out int n) && n > 0)
                left[id] = n - 1;
            else
                drawn.Add(id);
        }
        return drawn;
    }

    /// <summary>
    /// Puts a fresh <paramref name="elementId"/> card into the pile at <paramref name="at"/>, as
    /// Dealer.Shuffle provisions them. The dealt card it stands for isn't touched (it's a helper in
    /// the village or already gone); the extra copy lasts until the deck runs out and reshuffles.
    /// </summary>
    public static void Insert(string elementId, int at)
    {
        Sphere pile = Pile();
        if (pile == null)
            return;
        pile.ProvisionElementToken(elementId, 1);
        int count = Cards().Count;
        Move(count - 1, Math.Max(0, Math.Min(at, count - 1)));
    }

    /// <summary>
    /// The assistant a deck card turns into when hired (employable.poet -> assistance.poet, its
    /// "recruiting" xtrigger): that card carries the aspects that matter.
    /// </summary>
    public static Element HiredAs(Element employable)
    {
        if (employable?.XTriggers == null || !employable.XTriggers.TryGetValue("recruiting", out List<MorphDetails> morphs) || morphs.Count == 0)
            return null;
        return Watchman.Get<Compendium>().GetEntityById<Element>(morphs[0].Id);
    }

    /// <summary>Moves the card at <paramref name="from"/> to <paramref name="to"/> (0 = next to come).</summary>
    public static void Move(int from, int to)
    {
        Sphere pile = Pile();
        if (pile == null || TokensField == null || from == to)
            return;
        var all = (List<Token>)TokensField.GetValue(pile);
        List<Token> cards = pile.GetElementTokens().ToList();
        if (from < 0 || from >= cards.Count || to < 0 || to >= cards.Count)
            return;
        Token moving = cards[from];
        cards.RemoveAt(from);
        cards.Insert(to, moving);
        // Rewrite the element tokens' slots in the full list in the new order; other tokens stay put.
        var slots = new List<int>();
        for (int i = 0; i < all.Count; i++)
        {
            if (cards.Contains(all[i]))
                slots.Add(i);
        }
        for (int k = 0; k < slots.Count && k < cards.Count; k++)
            all[slots[k]] = cards[k];
        DirtyField?.SetValue(pile, true);
    }
}
