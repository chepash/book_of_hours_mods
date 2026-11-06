using System;
using System.Collections.Generic;
using System.Linq;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.Tokens.Payloads;
using SecretHistories.UI;

namespace BookFinder;

/// <summary>Our own grouping of workstations, by verb id.</summary>
public enum PlaceKind
{
    /// <summary>
    /// Reading desks: a slot accepts readables (e.g. "Papers" on Desk: Eva). Also the places
    /// that play the readables a desk can't: the two phonographs (records) and the projector
    /// (films); only three in the whole game, too few for a kind of their own.
    /// </summary>
    Desk,
    /// <summary>Beds (library.bed.*), e.g. guest beds for visitors.</summary>
    Bed,
    /// <summary>
    /// Places to evolve the soul: the verb carries an "Evolve via ..." aspect (e.illumination,
    /// e.horomachistry, ...). Only there two identical soul cards combine into a greater one,
    /// using a skill committed to that Wisdom and attuned to that soul.
    /// </summary>
    Soul,
    /// <summary>Everything else in the house: altars, telescope, hearths, kitchens, garden.</summary>
    House,
    /// <summary>village.*: Rectory, Smithy, Mrs and Mr Kille, Sweet Bones, Post Office.</summary>
    Village,
    /// <summary>world.* (moor, beachcombing, sea-caves, boathouse, ...) and lighthouse ("Behind the Scenes").</summary>
    Outdoors,
}

/// <summary>A workstation (desk, telescope, village building, walk, ...) the player can use.</summary>
public class DeskEntry
{
    public PlaceKind Kind;
    public Situation Situation;
    public Token Token;
    public string Name;
    public string Location;
    /// <summary>Principles the desk works with (Verb.Hints), e.g. edge, grail, moon, winter.</summary>
    public List<string> Principles;
    /// <summary>Slots beyond Soul/Skill/Memory, e.g. "Papers: Readable / Blank".</summary>
    public List<string> Extras;
    /// <summary>Wisdoms this place evolves souls through ("Illumination"), from its e.* aspects.</summary>
    public List<string> Evolves;
    /// <summary>The same Wisdoms as ids ("illumination").</summary>
    public List<string> EvolveIds;
    /// <summary>Base soul ids this place can evolve ("xmet", "xpho", ...; any tier), via its Wisdoms.</summary>
    public HashSet<string> EvolvableSouls;
    /// <summary>Read live, not at scan time: a desk starts or finishes while the window is open.</summary>
    public StateEnum State
    {
        get
        {
            try
            {
                return Situation != null ? Situation.StateIdentifier : StateEnum.Unstarted;
            }
            catch (System.Exception)
            {
                return StateEnum.Unstarted; // situation torn down since the scan
            }
        }
    }

    public bool IsIdle => State == StateEnum.Unstarted || State == StateEnum.Inchoate;
    public bool IsDone => State == StateEnum.Complete;
    public bool IsWorking => !IsIdle && !IsDone;

    public string StateLabel => IsIdle ? "Idle" : IsDone ? "Done" : "Working";

    /// <summary>"Working: Edge: Unearthed! · 0:42 left" while a recipe runs, else null.</summary>
    public string WorkDescription()
    {
        if (!IsWorking)
            return null;
        string recipe = null;
        float left = 0f;
        try
        {
            recipe = Situation.GetCurrentRecipe()?.Label;
            left = Situation.TimeRemaining;
        }
        catch (System.Exception)
        {
            // no current recipe
        }
        string text = "Working" + (string.IsNullOrEmpty(recipe) ? "" : ": " + recipe);
        if (left > 0f)
            text += $" · {(int)left / 60}:{(int)left % 60:00} left";
        return text;
    }
}

public class DeskFilter
{
    public readonly HashSet<string> Principles = new HashSet<string>();
    public bool IdleOnly;
    /// <summary>Null: every kind.</summary>
    public PlaceKind? Kind;
    /// <summary>Soul ids; the window keeps at most one. Only offered with Kind = Soul.</summary>
    public readonly HashSet<string> Souls = new HashSet<string>();

    public bool Matches(DeskEntry desk, string query)
    {
        if (!string.IsNullOrEmpty(query) && desk.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
            && desk.Location.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
            && !desk.Evolves.Any(w => w.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
            return false;
        if (Kind.HasValue && desk.Kind != Kind.Value)
            return false;
        if (IdleOnly && !desk.IsIdle)
            return false;
        if (Souls.Count > 0 && !desk.EvolvableSouls.Any(Souls.Contains))
            return false;
        return Principles.All(desk.Principles.Contains);
    }
}

/// <summary>
/// Workstations are Situations whose Verb has Category == Workstation (verbs/workstations_*.json).
/// Verb.Hints lists the principles it works with; Verb.Slots are its slots, the first three being
/// Soul / Skill / Memory (essential ability / skill / memory) and the rest desk-specific.
/// </summary>
public static class DeskIndex
{
    public static List<DeskEntry> Scan()
    {
        var desks = new List<DeskEntry>();
        foreach (Situation situation in Watchman.Get<HornedAxe>().GetRegisteredSituations())
        {
            Verb verb = situation.Verb;
            Token token = situation.Token;
            if (verb == null || verb.Category != VerbCategory.Workstation || token == null || !token.IsValid())
                continue;
            if (token.Sphere == null || token.Sphere.Shrouded || !WorldIndex.IsReachable(token.Sphere.SphereCategory))
                continue;
            TerrainFeature room = WorldIndex.FindRoom(token, out string fallbackLabel);
            if (room != null && (room.IsShrouded || room.IsSealed))
                continue;

            desks.Add(new DeskEntry
            {
                Kind = Classify(verb),
                Situation = situation,
                Token = token,
                Name = string.IsNullOrEmpty(verb.Label) ? verb.Id : verb.Label,
                Location = room != null ? room.Label : fallbackLabel ?? "",
                Principles = (verb.Hints ?? new List<string>()).Where(WorldIndex.IsPrinciple).ToList(),
                Extras = DescribeExtras(verb),
                Evolves = EvolveWisdoms(verb),
                EvolveIds = EvolveIdsOf(verb),
                EvolvableSouls = new HashSet<string>(EvolveIdsOf(verb).SelectMany(SoulsEvolvedVia)),
            });
        }
        return desks.OrderBy(d => d.Kind).ThenBy(d => d.Location).ThenBy(d => d.Name).ToList();
    }

    public static PlaceKind Classify(Verb verb)
    {
        string id = verb.Id;
        if (id.StartsWith("village.", StringComparison.Ordinal))
            return PlaceKind.Village;
        // Before Outdoors: Deeplight Corals (world.*) evolves souls too.
        if (EvolveWisdoms(verb).Count > 0)
            return PlaceKind.Soul;
        if (id.StartsWith("world.", StringComparison.Ordinal) || id == "lighthouse"
            || id.StartsWith("lighthouse.", StringComparison.Ordinal))
            return PlaceKind.Outdoors;
        if (id.StartsWith("library.bed.", StringComparison.Ordinal))
            return PlaceKind.Bed;
        bool readsBooks = (verb.Slots ?? new List<SphereSpec>()).Any(slot =>
            ReadsAny(slot.Required) || ReadsAny(slot.Essential));
        return readsBooks ? PlaceKind.Desk : PlaceKind.House;
    }

    private static readonly string[] ReadableAspects = { "readable", "record.phonograph", "film" };

    private static bool ReadsAny(SecretHistories.Core.AspectsDictionary aspects) =>
        aspects != null && ReadableAspects.Any(aspects.ContainsKey);

    private static Dictionary<string, HashSet<string>> _soulsByWisdom;

    /// <summary>
    /// Souls a Wisdom can evolve, from the game's recipes "evolve.&lt;wisdom&gt;.&lt;soul&gt;"
    /// (crafting_1_evolutions.json), e.g. evolve.illumination.xmet. Built once.
    /// </summary>
    public static HashSet<string> SoulsEvolvedVia(string wisdom)
    {
        BuildSoulsByWisdom();
        return _soulsByWisdom.TryGetValue(wisdom, out HashSet<string> found) ? found : new HashSet<string>();
    }

    /// <summary>Wisdoms that evolve a soul (xere: skolekosophy, bosk).</summary>
    public static List<string> WisdomsEvolving(string soul)
    {
        BuildSoulsByWisdom();
        return _soulsByWisdom.Where(p => p.Value.Contains(soul)).Select(p => p.Key).ToList();
    }

    /// <summary>Every soul some Wisdom can evolve.</summary>
    public static HashSet<string> AllEvolvableSouls()
    {
        BuildSoulsByWisdom();
        return new HashSet<string>(_soulsByWisdom.Values.SelectMany(s => s));
    }

    private static void BuildSoulsByWisdom()
    {
        if (_soulsByWisdom != null)
            return;
        _soulsByWisdom = new Dictionary<string, HashSet<string>>();
        foreach (Recipe recipe in Watchman.Get<Compendium>().GetEntitiesAsList<Recipe>())
        {
            string[] parts = (recipe.Id ?? "").Split('.');
            if (parts.Length != 3 || parts[0] != "evolve" || recipe.Reqs == null)
                continue;
            // The soul comes from the attunement requirement (a.xhea), not the id: ids have typos
            // (evolve.birdsong.xtris needs a.xtri), and all tiers (xhea, xhea2, xhea3) share it.
            string attunement = recipe.Reqs.Keys.FirstOrDefault(k => k.StartsWith("a.x", StringComparison.Ordinal));
            if (attunement == null)
                continue;
            if (!_soulsByWisdom.TryGetValue(parts[1], out HashSet<string> souls))
                _soulsByWisdom[parts[1]] = souls = new HashSet<string>();
            souls.Add(attunement.Substring(2));
        }
    }

    private static List<string> EvolveIdsOf(Verb verb) =>
        verb.Aspects == null
            ? new List<string>()
            : verb.Aspects.Keys.Where(k => k.StartsWith("e.", StringComparison.Ordinal)).Select(k => k.Substring(2)).ToList();

    /// <summary>e.illumination → "Illumination" (the label of the Wisdom w.illumination).</summary>
    private static List<string> EvolveWisdoms(Verb verb)
    {
        if (verb.Aspects == null)
            return new List<string>();
        return verb.Aspects.Keys
            .Where(k => k.StartsWith("e.", StringComparison.Ordinal))
            .Select(k => WorldIndex.LabelOf("w." + k.Substring(2)))
            .ToList();
    }

    private static readonly HashSet<string> CommonSlots = new HashSet<string> { "ability", "skill", "memory" };

    private static List<string> DescribeExtras(Verb verb)
    {
        var extras = new List<string>();
        foreach (SphereSpec slot in verb.Slots ?? new List<SphereSpec>())
        {
            // Soul / Skill / Memory are on every desk: only the essential aspect differs.
            if (slot.Essential != null && slot.Essential.Keys.Any(CommonSlots.Contains))
                continue;
            IEnumerable<string> accepts = (slot.Required ?? new SecretHistories.Core.AspectsDictionary()).Keys
                .Concat((slot.Essential ?? new SecretHistories.Core.AspectsDictionary()).Keys)
                .Select(WorldIndex.LabelOf)
                .Distinct();
            string list = string.Join(" / ", accepts);
            if (list.Length == 0)
                continue;
            string label = string.IsNullOrEmpty(slot.Label) || slot.Label == "+" ? "" : slot.Label + ": ";
            extras.Add(label + list);
        }
        return extras;
    }
}
