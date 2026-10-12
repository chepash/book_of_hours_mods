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
    /// <summary>Reading desks: a slot accepts readables (e.g. "Papers" on Desk: Eva).</summary>
    Desk,
    /// <summary>Beds (library.bed.*), e.g. guest beds for visitors.</summary>
    Bed,
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
    public StateEnum State;

    public bool IsIdle => State == StateEnum.Unstarted || State == StateEnum.Inchoate;

    public string StateLabel => IsIdle ? "Idle"
        : State == StateEnum.Complete ? "Done"
        : "Working";
}

public class DeskFilter
{
    public readonly HashSet<string> Principles = new HashSet<string>();
    public bool IdleOnly;
    /// <summary>Null: every kind.</summary>
    public PlaceKind? Kind;

    public bool Matches(DeskEntry desk, string query)
    {
        if (!string.IsNullOrEmpty(query) && desk.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
            && desk.Location.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        if (Kind.HasValue && desk.Kind != Kind.Value)
            return false;
        if (IdleOnly && !desk.IsIdle)
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
                State = situation.StateIdentifier,
            });
        }
        return desks.OrderBy(d => d.Kind).ThenBy(d => d.Location).ThenBy(d => d.Name).ToList();
    }

    public static PlaceKind Classify(Verb verb)
    {
        string id = verb.Id;
        if (id.StartsWith("village.", StringComparison.Ordinal))
            return PlaceKind.Village;
        if (id.StartsWith("world.", StringComparison.Ordinal) || id == "lighthouse"
            || id.StartsWith("lighthouse.", StringComparison.Ordinal))
            return PlaceKind.Outdoors;
        if (id.StartsWith("library.bed.", StringComparison.Ordinal))
            return PlaceKind.Bed;
        bool readsBooks = (verb.Slots ?? new List<SphereSpec>()).Any(slot =>
            (slot.Required != null && slot.Required.ContainsKey("readable"))
            || (slot.Essential != null && slot.Essential.ContainsKey("readable")));
        return readsBooks ? PlaceKind.Desk : PlaceKind.House;
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
