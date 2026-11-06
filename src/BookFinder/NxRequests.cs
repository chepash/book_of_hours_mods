using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SecretHistories.Entities;
using SecretHistories.Spheres;
using SecretHistories.Tokens.Payloads;
using SecretHistories.UI;
using UnityEngine;

namespace BookFinder;

/// <summary>
/// What an open constellation window (a further-stories Nx node on the Tree screen) asks for:
/// "a text on Ragged Crossroads [8]" - any readable with that subject (r.raggedcrossroads) and
/// sophistication (soph) at least 8 - or one particular book (t.onthewhite).
/// </summary>
public class NxRequest
{
    public string Visitor;
    public string Subject;
    public int MinSoph;
    public string BookId;
    public string Label;

    public bool Fits(ItemEntry item) => BookId != null
        ? item.Stack?.EntityId == BookId
        : item.Book?.Subject == Subject && item.Book.Soph >= MinSoph;
}

public static class NxRequests
{
    private static readonly FieldInfo InternalSphereField = AccessTools.Field(typeof(Nx), "_internalSphere");
    private static readonly FieldInfo SelectedRecipeField = AccessTools.Field(typeof(Nx), "_selectedRecipe");
    private static List<Recipe> _nxRecipes;

    /// <summary>
    /// The node's story card (n.reverend.success.one...) sits in its internal sphere. The recipes that
    /// finish a step (nxh.*) require that card, the visitor and the text; the visitor picked in the
    /// window (its nxc.* recipe) narrows them to what that visitor just asked for. Only the open
    /// window is read, so nothing beyond the step the player is on shows.
    /// </summary>
    public static List<NxRequest> ForOpenWindow()
    {
        var requests = new List<NxRequest>();
        try
        {
            Nx nx = Watchman.Get<NxNodesSphere>()?.Tokens.Select(t => t.Payload as Nx).FirstOrDefault(n => n != null && n.IsOpen);
            if (nx == null)
                return requests;
            string core = ((InternalSphereField.GetValue(nx) as Sphere)?.Tokens.FirstOrDefault()?.Payload as ElementStack)?.EntityId;
            if (core == null)
                return requests;
            Recipe selected = SelectedRecipeField.GetValue(nx) as Recipe;
            string visitor = selected != null && selected.IsValid() ? VisitorOf(selected, core) : null;
            _nxRecipes ??= Watchman.Get<Compendium>().GetEntitiesAsList<Recipe>()
                .Where(r => r?.Id != null && r.ActionId == "nx" && r.Reqs != null).ToList();
            foreach (Recipe recipe in _nxRecipes.Where(r => r.Reqs.ContainsKey(core)))
            {
                string who = VisitorOf(recipe, core);
                if (who == null || (visitor != null && who != visitor))
                    continue;
                string name = WorldIndex.LabelOf(who);
                string subject = recipe.Reqs.Keys.FirstOrDefault(k => k.StartsWith("r.", StringComparison.Ordinal));
                string book = recipe.Reqs.Keys.FirstOrDefault(k => k.StartsWith("t.", StringComparison.Ordinal));
                if (subject != null)
                {
                    int.TryParse(recipe.Reqs.TryGetValue("soph", out string soph) ? soph : "0", out int min);
                    requests.Add(new NxRequest { Visitor = who, Subject = subject, MinSoph = min, Label = $"{name}: {WorldIndex.SubjectName(subject)} {min}+" });
                }
                else if (book != null)
                    requests.Add(new NxRequest { Visitor = who, BookId = book, Label = $"{name}: {WorldIndex.LabelOf(book)}" });
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Book Finder] Constellation requests: " + e.Message);
        }
        return requests;
    }

    /// <summary>Visitor ids (ehsan, douglas...) are the required keys without a dot.</summary>
    private static string VisitorOf(Recipe recipe, string core) => recipe.Reqs
        .Where(r => r.Key != core && !r.Key.Contains(".") && r.Key != "soph" && int.TryParse(r.Value, out int v) && v > 0)
        .Select(r => r.Key).FirstOrDefault();
}
