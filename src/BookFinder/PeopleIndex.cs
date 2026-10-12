using System;
using System.Collections.Generic;
using System.Linq;
using SecretHistories.Core;
using SecretHistories.Entities;
using SecretHistories.Spheres;
using SecretHistories.Tokens.Payloads;
using SecretHistories.UI;

namespace BookFinder;

public enum PersonKind
{
    Visitor,
    Assistant,
}

/// <summary>A visitor or village assistant the player has already met.</summary>
public class PersonEntry
{
    public PersonKind Kind;
    public Element Element;
    /// <summary>The card, when it currently exists (assistants vanish after helping).</summary>
    public Token Token;
    public ElementStack Stack;
    public string Name;
    /// <summary>Element id without the "embarking_" prefix of a departing visitor's card.</summary>
    public string PersonId;
    /// <summary>On stage: at the door, in the house, in hand; not offstage or gone.</summary>
    public bool Here;
    public string Location;
    public AspectsDictionary Aspects;

    /// <summary>
    /// What the person brings to the table: a visitor's interests (interest.&lt;principle&gt;),
    /// an assistant's own principle aspects.
    /// </summary>
    public IEnumerable<KeyValuePair<string, int>> Principles => Kind == PersonKind.Visitor
        ? Aspects.Where(a => a.Value > 0 && a.Key.StartsWith("interest.", StringComparison.Ordinal))
            .Select(a => new KeyValuePair<string, int>(a.Key.Substring("interest.".Length), a.Value))
        : Aspects.Where(a => a.Value > 0 && WorldIndex.IsPrinciple(a.Key));

    public int PrincipleValue(string principle) =>
        Kind == PersonKind.Visitor ? Aspects.AspectValue("interest." + principle) : Aspects.AspectValue(principle);

    /// <summary>Languages a visitor understands (u.latin -> "Latin").</summary>
    public IEnumerable<string> Languages =>
        Aspects.Where(a => a.Value > 0 && a.Key.StartsWith("u.", StringComparison.Ordinal))
            .Select(a => WorldIndex.LanguageName("w." + a.Key.Substring(2)))
            .OrderBy(n => n);
}

public class PeopleFilter
{
    public readonly HashSet<string> Principles = new HashSet<string>();
    /// <summary>Null: everyone.</summary>
    public PersonKind? Kind;
    public bool HereOnly;

    public bool Matches(PersonEntry person, string query)
    {
        if (!string.IsNullOrEmpty(query) && person.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        if (Kind.HasValue && person.Kind != Kind.Value)
            return false;
        if (HereOnly && !person.Here)
            return false;
        return Principles.All(p => person.PrincipleValue(p) > 0);
    }
}

/// <summary>
/// Visitors: every visitor card exists from the start, waiting in ~/visitors.offstage, and the
/// game keeps no "met" record. So each visitor is marked with an illumination (a string note
/// the game saves with the card) the first time it's seen on stage; cards already carrying
/// mutations (interests, topics, gratitude added during visits) count as met too.
/// Assistants (aspect "assistance", e.g. assistance.rector) are unique cards created when
/// hired and removed after helping; the game records every unique element that has existed
/// in Character.UniqueElementsManifested, which is exactly the "met" list.
/// </summary>
public static class PeopleIndex
{
    public const string SeenKey = "shirofaii.boh.seen";

    /// <summary>People met so far; also marks visitors currently on stage as met.</summary>
    public static List<PersonEntry> Scan()
    {
        var byPerson = new Dictionary<string, PersonEntry>();
        var seen = new HashSet<Token>();
        foreach (Sphere sphere in Watchman.Get<HornedAxe>().GetSpheres())
        {
            if (sphere == null || sphere.Defunct)
                continue;
            string path = null;
            foreach (Token token in sphere.Tokens)
            {
                if (token == null || !token.IsValid() || !seen.Add(token) || !(token.Payload is ElementStack stack))
                    continue;
                // Cheap reject on the element's static aspects first: this runs every few seconds
                // over every token in the world, and GetAspects builds a new dictionary each call.
                AspectsDictionary definition = stack.Element?.Aspects;
                PersonKind kind;
                if (definition == null)
                    continue;
                if (definition.AspectValue("visitor") > 0 || definition.AspectValue("visitor.embarking") > 0)
                    kind = PersonKind.Visitor;
                else if (definition.AspectValue("assistance") > 0)
                    kind = PersonKind.Assistant;
                else
                    continue;
                AspectsDictionary aspects = stack.GetAspects(false);
                path ??= sphere.GetAbsolutePath().ToString();

                TerrainFeature room = WorldIndex.FindRoom(token, out string fallbackLabel);
                bool here = WorldIndex.IsReachable(sphere.SphereCategory) && !path.Contains("offstage")
                    && !sphere.Shrouded && !(room != null && (room.IsShrouded || room.IsSealed));
                if (kind == PersonKind.Visitor)
                {
                    if (here && stack.GetIllumination(SeenKey) != "1")
                        stack.SetIllumination(SeenKey, "1");
                    if (!here && stack.GetIllumination(SeenKey) != "1" && stack.Mutations.Count == 0)
                        continue;
                }
                else if (!here)
                {
                    continue; // An offstage assistant card is not one the player has hired.
                }

                string personId = stack.EntityId.StartsWith("embarking_", StringComparison.Ordinal)
                    ? stack.EntityId.Substring("embarking_".Length)
                    : stack.EntityId;
                var entry = new PersonEntry
                {
                    Kind = kind,
                    Element = stack.Element,
                    Token = token,
                    Stack = stack,
                    Name = stack.Label ?? personId,
                    PersonId = personId,
                    Here = here,
                    Location = here ? (room != null ? room.Label : fallbackLabel ?? "") : "",
                    Aspects = new AspectsDictionary(aspects),
                };
                // One row per person; prefer the card that is on stage.
                if (!byPerson.TryGetValue(personId, out PersonEntry existing) || (here && !existing.Here))
                    byPerson[personId] = entry;
            }
        }

        // Assistants who helped before and are gone now: known from the unique-element history.
        Character protag = Watchman.Exists<Stable>() ? Watchman.Get<Stable>().Protag() : null;
        if (protag != null)
        {
            foreach (string id in protag.UniqueElementsManifested)
            {
                if (byPerson.ContainsKey(id))
                    continue;
                Element element = WorldIndex.ElementOf(id);
                if (element == null || element.Aspects == null || element.Aspects.AspectValue("assistance") <= 0)
                    continue;
                byPerson[id] = new PersonEntry
                {
                    Kind = PersonKind.Assistant,
                    Element = element,
                    Name = string.IsNullOrEmpty(element.Label) ? id : element.Label,
                    PersonId = id,
                    Location = "",
                    Aspects = new AspectsDictionary(element.Aspects),
                };
            }
        }

        return byPerson.Values.OrderByDescending(p => p.Here).ThenBy(p => p.Kind).ThenBy(p => p.Name).ToList();
    }
}
