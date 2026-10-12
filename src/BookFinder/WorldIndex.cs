using System;
using System.Collections.Generic;
using System.Linq;
using SecretHistories.Abstract;
using SecretHistories.Assets.Scripts.Application.Tokens;
using SecretHistories.Core;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.Spheres;
using SecretHistories.Tokens.Payloads;
using SecretHistories.UI;

namespace BookFinder;

public enum ReadState
{
    Any,
    Unread,
    Mastered,
}

/// <summary>A card or item the player can reach: on a table or shelf, in hand, in a workstation.</summary>
public class ItemEntry
{
    public Token Token;
    public ElementStack Stack;
    public string Title;
    public string Location;
    /// <summary>Lies in a hidden panel the player has to click open (e.g. Watchman's Tower desk).</summary>
    public bool InSecretCompartment;
    /// <summary>Aspects of one card (not multiplied by the stack size), without the element's own id.</summary>
    public AspectsDictionary Aspects;
    /// <summary>Set for readable items (books, films, records, ...).</summary>
    public BookInfo Book;
}

public class BookInfo
{
    /// <summary>Principle id of the book's mystery ("knock"), or null if it has none.</summary>
    public string Mystery;
    /// <summary>Difficulty: the level of the mystery.* aspect.</summary>
    public int Level;
    public bool Mastered;
    /// <summary>Language aspect id ("w.latin"), or null for books in the player's own language.</summary>
    public string Language;
    /// <summary>What kind of readable: Book, Record, Film, Scroll, Tablet, Letter, Journal.</summary>
    public string Kind = "Book";
    /// <summary>
    /// Contaminations the book carries (contamination.keeperskin, .witchworms, ...). They spread to
    /// nearby books, so infected ones are worth keeping apart.
    /// </summary>
    public readonly List<string> Contaminations = new List<string>();
    /// <summary>
    /// What (re)reading gives, e.g. mem.impulse; only for mastered books, since mastering
    /// already showed it (the mastering recipe carries reading.&lt;principle&gt; too).
    /// </summary>
    public string Memory;
}

public class BookFilter
{
    public readonly HashSet<string> Mysteries = new HashSet<string>();
    public readonly HashSet<int> Levels = new HashSet<int>();
    public ReadState ReadState = ReadState.Any;
    /// <summary>Language ids; <see cref="WorldIndex.NoLanguage"/> stands for "no foreign language".</summary>
    public readonly HashSet<string> Languages = new HashSet<string>();
    /// <summary>Readable kinds (<see cref="BookInfo.Kind"/>); any of these.</summary>
    public readonly HashSet<string> Kinds = new HashSet<string>();
    /// <summary>
    /// Contamination ids, any of these. <see cref="AnyContamination"/> matches every infected book,
    /// <see cref="NoContamination"/> every clean one.
    /// </summary>
    public readonly HashSet<string> Contaminations = new HashSet<string>();
    public const string AnyContamination = "*";
    public const string NoContamination = "-";

    public bool Matches(ItemEntry item, string query)
    {
        BookInfo book = item.Book;
        if (book == null || !WorldIndex.TitleMatches(item, query))
            return false;
        if (Mysteries.Count > 0 && (book.Mystery == null || !Mysteries.Contains(book.Mystery)))
            return false;
        if (Levels.Count > 0 && !Levels.Contains(book.Level))
            return false;
        if (ReadState == ReadState.Unread && book.Mastered)
            return false;
        if (ReadState == ReadState.Mastered && !book.Mastered)
            return false;
        if (Languages.Count > 0 && !Languages.Contains(book.Language ?? WorldIndex.NoLanguage))
            return false;
        if (Kinds.Count > 0 && !Kinds.Contains(book.Kind))
            return false;
        if (Contaminations.Count > 0
            && !(Contaminations.Contains(AnyContamination) && book.Contaminations.Count > 0)
            && !(Contaminations.Contains(NoContamination) && book.Contaminations.Count == 0)
            && !book.Contaminations.Any(Contaminations.Contains))
            return false;

        return true;
    }
}

public class ItemFilter
{
    /// <summary>Minimum principle levels the item must have, e.g. moth 2 (6 moth passes too).</summary>
    public readonly Dictionary<string, int> Principles = new Dictionary<string, int>();
    /// <summary>Other aspects (tags) the item must all have ("tool", "metal").</summary>
    public readonly HashSet<string> Tags = new HashSet<string>();

    public bool Matches(ItemEntry item, string query)
    {
        if (!WorldIndex.TitleMatches(item, query))
            return false;
        foreach (KeyValuePair<string, int> required in Principles)
        {
            if (item.Aspects.AspectValue(required.Key) < required.Value)
                return false;
        }
        foreach (string tag in Tags)
        {
            if (item.Aspects.AspectValue(tag) <= 0)
                return false;
        }
        return true;
    }

    /// <summary>Strongest match first: sum of the selected principles.</summary>
    public int Score(ItemEntry item) => Principles.Keys.Sum(p => item.Aspects.AspectValue(p));
}

/// <summary>
/// Collects element stacks from every registered sphere the player can reach.
/// Book data lives in aspects: "readable" marks the item, "mystery.&lt;principle&gt;": N is the
/// mystery and its difficulty, "mastery.&lt;principle&gt;" is added when it's been read,
/// "w.&lt;language&gt;" is its language.
/// </summary>
public static class WorldIndex
{
    public const string NoLanguage = "";

    public static readonly string[] Principles =
    {
        "edge", "forge", "grail", "heart", "knock", "lantern", "moon", "moth", "nectar", "rose", "scale", "sky", "winter",
    };

    private static readonly HashSet<string> PrincipleSet = new HashSet<string>(Principles);
    private static readonly Dictionary<string, Element> ElementCache = new Dictionary<string, Element>();

    /// <summary>
    /// Rooms that aren't open yet are still populated with their items, and only the room itself
    /// is marked shrouded or sealed, so each item is checked against the room it's in.
    /// <paramref name="report"/> collects every token with the reason it was kept or skipped.
    /// </summary>
    public static List<ItemEntry> Scan(List<string> report = null)
    {
        ElementCache.Clear();
        var items = new List<ItemEntry>();
        var seen = new HashSet<Token>();
        foreach (Sphere sphere in Watchman.Get<HornedAxe>().GetSpheres())
        {
            if (sphere == null || sphere.Defunct)
                continue;
            foreach (Token token in sphere.Tokens)
            {
                if (token == null || !token.IsValid() || !seen.Add(token) || !(token.Payload is ElementStack stack))
                    continue;

                TerrainFeature room = FindRoom(token, out string fallbackLabel);
                string path = sphere.GetAbsolutePath().ToString();
                string skip = !IsReachable(sphere.SphereCategory) ? "unreachable sphere"
                    : path.Contains("offstage") ? "offstage"
                    : IsInThePost(sphere, path) ? "in the post"
                    : sphere.Shrouded ? "sphere shrouded"
                    : stack.IsShrouded ? "item shrouded"
                    : room != null && room.IsShrouded ? "room shrouded"
                    : room != null && room.IsSealed ? "room sealed"
                    : null;
                string title = stack.Label ?? stack.EntityId;
                string location = room != null ? room.Label : fallbackLabel ?? "";
                bool secret = IsInSecretCompartment(sphere);
                report?.Add($"{(skip == null ? "SHOW" : "SKIP " + skip),-20} | {title} | room={room?.EntityId ?? "-"} '{location}' | sphere={path} cat={sphere.SphereCategory} | secret={secret} | {Visibility(token, sphere)} | {HierarchyPath(sphere.transform)}");
                if (skip != null)
                    continue;

                var item = new ItemEntry
                {
                    Token = token,
                    Stack = stack,
                    Title = title,
                    Location = location,
                    InSecretCompartment = secret,
                    // Per card, not multiplied by stack size: two Shapt cards still give their
                    // aspects once per use, so 2 Forge, not 4.
                    Aspects = new AspectsDictionary(stack.GetAspectsDividedByQuantity(false)),
                };
                item.Book = ReadBook(item.Aspects, stack.Element);
                items.Add(item);
            }
        }
        return items;
    }

    /// <summary>
    /// Secret compartments are sliding panels (AbstractSecretCompartment) that contain their slot,
    /// e.g. RoomContentsDominion/SecretCompartmentBelowWindow/OpenedCompartment/ThingSlotSeeded.
    /// The game doesn't record whether the player has found one, so we can only flag the slot.
    /// </summary>
    private static bool IsInSecretCompartment(Sphere sphere)
    {
        return sphere.GetComponentInParent<AbstractSecretCompartment>(true) != null;
    }

    private static string HierarchyPath(UnityEngine.Transform t)
    {
        var names = new List<string>();
        for (int i = 0; i < 4 && t != null; i++, t = t.parent)
            names.Add(t.name);
        names.Reverse();
        return string.Join("/", names);
    }

    /// <summary>Debug: is the token actually drawn? Active state, effective CanvasGroup alpha, position.</summary>
    private static string Visibility(Token token, Sphere sphere)
    {
        float alpha = 1f;
        foreach (UnityEngine.CanvasGroup group in token.GetComponentsInParent<UnityEngine.CanvasGroup>())
        {
            alpha *= group.alpha;
            if (group.ignoreParentGroups)
                break;
        }
        UnityEngine.Vector3 p = token.transform.position;
        return $"tokenActive={token.gameObject.activeInHierarchy} sphereActive={sphere.gameObject.activeInHierarchy} " +
               $"alpha={alpha:0.##} scale={token.transform.lossyScale.x:0.###} pos=({p.x:0},{p.y:0},{p.z:0}) go={token.Sphere?.gameObject.name}";
    }

    /// <summary>
    /// Post orders wait in ~/purchases.* (e.g. purchases.europe) under the invisible
    /// OffstageSphereHolder until delivered. Hidden: what's coming should stay a surprise.
    /// </summary>
    private static bool IsInThePost(Sphere sphere, string path)
    {
        return path.StartsWith("~/purchases.", StringComparison.Ordinal)
            || sphere.transform.parent != null && sphere.transform.parent.name == "OffstageSphereHolder";
    }

    /// <summary>
    /// Places the player can take things from: tables, shelves and hand (World), workstation
    /// slots (Threshold), a running workstation (SituationStorage) and its uncollected results
    /// (Output). Dormant spheres hold undrawn decks, e.g. d.books.nocturnal_draw with books the
    /// player hasn't found yet; Commitment is the Tree of Wisdom.
    /// </summary>
    public static bool IsReachable(SphereCategory category)
    {
        return category == SphereCategory.World
            || category == SphereCategory.Threshold
            || category == SphereCategory.SituationStorage
            || category == SphereCategory.Output;
    }

    private static BookInfo ReadBook(AspectsDictionary aspects, Element element)
    {
        if (aspects.AspectValue("readable") <= 0)
            return null;
        var book = new BookInfo();
        foreach (KeyValuePair<string, int> aspect in aspects)
        {
            if (aspect.Value <= 0)
                continue;
            if (aspect.Key.StartsWith("mystery.", StringComparison.Ordinal))
            {
                book.Mystery = aspect.Key.Substring("mystery.".Length);
                book.Level = aspect.Value;
            }
            else if (aspect.Key.StartsWith("mastery.", StringComparison.Ordinal))
            {
                book.Mastered = true;
            }
            else if (aspect.Key.StartsWith("w.", StringComparison.Ordinal))
            {
                book.Language = aspect.Key;
            }
            else if (aspect.Key.StartsWith("contamination.", StringComparison.Ordinal))
            {
                book.Contaminations.Add(aspect.Key);
            }
        }
        book.Kind = ReadableKind(aspects);
        if (book.Mastered && book.Mystery != null)
            book.Memory = ReadingReward(element, book.Mystery);
        return book;
    }

    /// <summary>
    /// Readables are told apart by aspects: film (projector), record.phonograph, scroll, tablet,
    /// correspondence (letters), journal; everything else is a book (codex).
    /// </summary>
    private static string ReadableKind(AspectsDictionary aspects)
    {
        if (aspects.AspectValue("film") > 0) return "Film";
        if (aspects.AspectValue("record.phonograph") > 0) return "Record";
        if (aspects.AspectValue("scroll") > 0) return "Scroll";
        if (aspects.AspectValue("tablet") > 0) return "Tablet";
        if (aspects.AspectValue("correspondence") > 0) return "Letter";
        if (aspects.AspectValue("journal") > 0) return "Journal";
        return "Book";
    }

    /// <summary>
    /// The element's xtrigger "reading.&lt;principle&gt;" spawns what reading gives, e.g.
    /// "reading.knock": [{"id": "mem.impulse", "morpheffect": "spawn"}]. Always one entry.
    /// </summary>
    private static string ReadingReward(Element element, string mystery)
    {
        if (element?.XTriggers == null
            || !element.XTriggers.TryGetValue("reading." + mystery, out List<MorphDetails> morphs) || morphs == null)
            return null;
        return morphs.Select(m => m.Id).FirstOrDefault(id => !string.IsNullOrEmpty(id));
    }

    /// <summary>Room the token is in, found by walking sphere containers up to a room (TerrainFeature).</summary>
    public static TerrainFeature FindRoom(Token token, out string fallbackLabel)
    {
        fallbackLabel = null;
        Token current = token;
        for (int depth = 0; depth < 8 && current != null; depth++)
        {
            Sphere sphere = current.Sphere;
            if (sphere == null)
                break;
            fallbackLabel ??= sphere.GoverningSphereSpec?.Label;
            IHasSpheres container = sphere.GetContainer();
            if (container is TerrainFeature room)
                return room;
            if (!(container is IManifestable manifestable))
                break;
            Token parent = manifestable.GetToken();
            if (parent == null || parent == current)
                break;
            current = parent;
        }
        return token.GetComponentInParent<TerrainFeature>();
    }

    public static bool TitleMatches(ItemEntry item, string query)
    {
        return string.IsNullOrEmpty(query) || item.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsPrinciple(string aspectId) => PrincipleSet.Contains(aspectId);

    /// <summary>
    /// Bookkeeping aspects that aren't useful as item tags: book data (own filters on the Books
    /// tab), Tree of Wisdom branches (w.horomachistry, also w.&lt;language&gt;), "Acted: ...",
    /// "Attuned: ..." (a.x*), "Relevance: ...", "Committed" (wisdom.committed).
    /// </summary>
    private static readonly string[] NotTagPrefixes =
    {
        "mystery.", "mastery.", "w.", "acted.", "a.x", "relevance.", "wisdom.",
    };

    /// <summary>
    /// Aspects worth offering as tags: visible, labelled aspects that aren't principles.
    /// Book bookkeeping (mystery./mastery./w.) has its own filters on the Books tab.
    /// </summary>
    public static bool IsTag(string aspectId)
    {
        if (IsPrinciple(aspectId) || NotTagPrefixes.Any(p => aspectId.StartsWith(p, StringComparison.Ordinal)))
            return false;
        // Fatigue markers: zedge, zmoon, ... ("Edge [Fatigued]").
        if (aspectId.Length > 1 && aspectId[0] == 'z' && IsPrinciple(aspectId.Substring(1)))
            return false;
        Element element = ElementOf(aspectId);
        return element != null && element.IsAspect && !element.IsHidden && !string.IsNullOrEmpty(element.Label);
    }

    public static Element ElementOf(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        if (!ElementCache.TryGetValue(id, out Element element))
        {
            element = Watchman.Get<Compendium>().GetEntityById<Element>(id);
            ElementCache[id] = element;
        }
        return element;
    }

    public static string LabelOf(string elementId)
    {
        Element element = ElementOf(elementId);
        return element != null && !string.IsNullOrEmpty(element.Label) ? element.Label : elementId ?? "";
    }

    /// <summary>"Vak" for w.vak, whose label is "Written in Vak".</summary>
    public static string LanguageName(string languageId)
    {
        const string prefix = "Written in ";
        string label = LabelOf(languageId);
        return label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? label.Substring(prefix.Length) : label;
    }
}
