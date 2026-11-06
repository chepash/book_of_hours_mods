using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using SecretHistories.Abstract;
using SecretHistories.Core;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.Spheres;
using SecretHistories.Tokens.Payloads;
using SecretHistories.UI;
using UnityEngine;
using UnityEngine.UI;

namespace AutoTalk;

/// <summary>
/// A toggle button right of the speed controls. While it's on, a visitor who came for an Affair
/// (Incident) is talked to about it, given a fitting book and the talk is collected, all on its own.
///
/// How the game does it:
/// - incident.setup pulls the current Incident and links to visitor.for.&lt;key&gt; in ~/arrivalverbs;
///   that recipe greedily takes a visitor with a matching interest from ~/visitors.offstage and
///   mutates "v.&lt;key&gt;" on them (never sent for this Affair again).
/// - Talk verb slot "talk" takes the visitor; the visitor element (_visitor) opens slot "subject"
///   for the Incident. talk.visitor.intro.&lt;v&gt;.&lt;key&gt; (craftable) links to
///   talk.visitor.show.slot.specific: 60 s with slot "v" (Book).
/// - At the end talk.visitor.incident.success.&lt;v&gt;.&lt;key&gt;.mystery.&lt;p&gt; wants
///   mystery.p &gt;= interest.p, marks "acted.&lt;v&gt;.&lt;key&gt;.&lt;p&gt;" on the Incident and "tx.&lt;v&gt;"
///   on the book; intercept.tx (book already consulted) and cantread.&lt;lang&gt; (u.lang missing) fail.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class AutoTalkPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.autotalk";
    public const string Name = "Auto Talk";
    public const string Version = "0.1.0";

    internal static BepInEx.Logging.ManualLogSource Log;

    private ConfigEntry<bool> _enabled;
    private ConfigEntry<string> _noBook;

    private AutoTalkButton _button;
    private AutoTalkButton _affairsButton;
    private bool _affairsBusy;
    private float _nextAttach;
    private float _nextArrivalPoll;
    private float _nextScan;
    private float _nextStep;

    /// <summary>Visitor id -> Affair key that brought them (seen in a visitor.for.&lt;key&gt; situation).</summary>
    private readonly Dictionary<string, string> _arrivals = new Dictionary<string, string>();
    /// <summary>"visitor|key" pairs already tried this session (done, failed or no book).</summary>
    private readonly HashSet<string> _handled = new HashSet<string>();
    /// <summary>Set on toggle-on: also consider visitors already in the house (see FindPair).</summary>
    private bool _includePresent;

    private Job _job;

    private void Awake()
    {
        Log = Logger;
        _enabled = Config.Bind("General", "Enabled", false, "Automatic Affair talks are on (the talk button right of the speed controls).");
        _noBook = Config.Bind("General", "Skip", "opera.wings:olympe,opera.wings:arun,opera.apollo:morgen,intrusion:zachary,revolution:azita",
            "Affair:visitor pairs that are talked to but never given a book (comma separated). Default: the "
            + "Invisible Opera must not get a book from Olympe or Arun (closes Morgen's Exploits); Morgen must not act on "
            + "'Apollo and Marsyas' (her steps there give Stone Remembered); Zachary on the Endless Guest and Azita on "
            + "the Threshold Revolution get steps that would break the Institute council (Revelation; Autarchy + Fear Chandler).");
        _includePresent = _enabled.Value;
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        if (_button == null && now >= _nextAttach)
        {
            _nextAttach = now + 1f;
            _button = AutoTalkButton.Create("AutoTalkButton", ResourcesManager.GetSpriteForVerbLarge("talk"), null, 1.15f, Toggle);
            _button?.Paint(_enabled.Value);
            if (_button != null)
            {
                _affairsButton = AutoTalkButton.Create("CurrentAffairsButton", AffairsSprite(),
                    (RectTransform)_button.transform, 0.8f, TriggerCurrentAffairs);
                _affairsButton?.Paint(!_affairsBusy);
            }
        }

        try
        {
            if (now >= _nextArrivalPoll)
            {
                _nextArrivalPoll = now + 0.5f;
                PollArrivals();
            }
            if (!_enabled.Value)
                return;
            if (_job != null)
            {
                if (now >= _nextStep)
                {
                    _nextStep = now + 0.3f;
                    StepJob(now);
                }
            }
            else if (now >= _nextScan)
            {
                _nextScan = now + 5f;
                TryStartJob(now);
            }
        }
        catch (Exception e)
        {
            Logger.LogError($"AutoTalk: {e}");
            _job = null;
            _nextScan = now + 10f;
        }
    }

    private void OnDestroy()
    {
        if (_button != null)
            Destroy(_button.gameObject);
        if (_affairsButton != null)
            Destroy(_affairsButton.gameObject);
    }

    private void Toggle()
    {
        _enabled.Value = !_enabled.Value;
        _button?.Paint(_enabled.Value);
        if (_enabled.Value)
        {
            _includePresent = true;
            _nextScan = 0f;
        }
        Logger.LogInfo(_enabled.Value ? "AutoTalk on" : "AutoTalk off");
    }

    // ---------- arrivals ----------

    /// <summary>
    /// Watches the visitor.for.&lt;key&gt; situations: the visitor sits in their slot during the
    /// short warmup, so the visitor -> Affair link is read from there. Cheap: situations only.
    /// </summary>
    private void PollArrivals()
    {
        HornedAxe axe = Watchman.Get<HornedAxe>();
        if (axe == null)
            return;
        bool busy = false;
        foreach (Situation situation in axe.GetRegisteredSituations())
        {
            if (IsAffairsRun(situation))
                busy = true;
            if (_enabled.Value && _job == null && situation.VerbId == "talk" && situation.StateIdentifier == StateEnum.Complete
                && OutputHasIncident(situation))
            {
                // An Affair talk the mod didn't start (or lost on reload) is finished: collect it
                // so the Incident goes back to its place and the book to its shelf.
                Logger.LogInfo("AutoTalk: collected a finished Affair talk");
                CollectAndClose(situation);
                _nextScan = Time.unscaledTime + 1f;
                continue;
            }
            string key = ArrivalKey(situation);
            if (key == null)
                continue;
            foreach (Sphere sphere in situation.GetSpheres())
            {
                if (sphere == null || sphere.Defunct)
                    continue;
                foreach (Token token in sphere.Tokens)
                {
                    if (token?.Payload is ElementStack stack && stack.Element.Aspects.AspectValue("visitor") > 0)
                    {
                        if (!_arrivals.TryGetValue(stack.EntityId, out string old) || old != key)
                        {
                            _arrivals[stack.EntityId] = key;
                            _nextScan = 0f;
                            Logger.LogInfo($"AutoTalk: {stack.EntityId} is coming for {key}");
                        }
                    }
                }
            }
            // The arrival waits for a Collect with the visitor (and the Incident) in its output.
            if (_enabled.Value && situation.StateIdentifier == StateEnum.Complete)
            {
                Logger.LogInfo($"AutoTalk: collected the arrival for {key}");
                situation.Conclude();
                _nextScan = Time.unscaledTime + 1f;
            }
        }
        if (busy != _affairsBusy)
        {
            _affairsBusy = busy;
            _affairsButton?.Paint(!busy); // dim while Current Affairs or its arrival is running
        }
    }

    // ---------- Current Affairs on demand ----------

    /// <summary>
    /// incident.setup ("Current Affairs") or what it links to (incident.new, visitor.for.*,
    /// incident.retire...): one is running, so another must not be started.
    /// </summary>
    private static bool IsAffairsRun(Situation situation)
    {
        if (situation.VerbId != "incident.base")
            return false;
        StateEnum state = situation.StateIdentifier;
        return state != StateEnum.Complete || situation.GetSpheresByCategory(SphereCategory.Output).Any(s => s.Tokens.Count > 0);
    }

    private static Sprite AffairsSprite()
    {
        foreach (Sprite sprite in new[] { ResourcesManager.GetSpriteForAspect("incident"), ResourcesManager.GetSpriteForAspect("visitor") })
            if (sprite != null && !ResourcesManager.SpriteIsPlaceholder(sprite))
                return sprite;
        return ResourcesManager.GetSpriteForVerbLarge("talk");
    }

    /// <summary>
    /// What a season change does: spawn incident.setup (verb incident.base) in ~/arrivalverbs. After
    /// its 60 s warmup it sends one visitor for the current Affair, draws a new Affair, or retires
    /// one with no visitors left.
    /// </summary>
    private void TriggerCurrentAffairs()
    {
        HornedAxe axe = Watchman.Get<HornedAxe>();
        if (axe == null)
            return;
        if (axe.GetRegisteredSituations().Any(IsAffairsRun))
        {
            Logger.LogInfo("AutoTalk: Current Affairs is already running");
            return;
        }
        Sphere arrivals = axe.GetSphereByAbsolutePath(new SecretHistories.Fucine.FucinePath("~/arrivalverbs"));
        if (arrivals == null || !arrivals.IsValid())
        {
            Logger.LogWarning("AutoTalk: no ~/arrivalverbs sphere");
            return;
        }
        var command = new SecretHistories.Commands.SituationCreationCommand("incident.base").WithRecipeAboutToActivate("incident.setup");
        new SecretHistories.Commands.TokenCreationCommand(command, new TokenLocation(Vector3.zero, arrivals))
            .Execute(new Context(Context.ActionSource.JustSpawned), arrivals);
        _affairsBusy = true;
        _affairsButton?.Paint(false);
        Logger.LogInfo("AutoTalk: Current Affairs started by hand");
    }

    /// <summary>
    /// Puts a token into a slot the way a player's drag does. Token.StartDrag first calls
    /// RequestHomeLocationFromCurrentSphere, and on Collect the token goes back to that home (its
    /// shelf). Without it the book fell back to the default sphere, the hand.
    /// </summary>
    private static bool MoveLikeDrag(Token token, Sphere to)
    {
        token.RequestHomeLocationFromCurrentSphere();
        return to.TryAcceptToken(token, new Context(Context.ActionSource.PlayerDrag));
    }

    /// <summary>Collect, then close the talk window if it's open (the visitor has left by then).</summary>
    private static void CollectAndClose(Situation talk)
    {
        talk.Conclude();
        if (talk.IsOpen)
            talk.Close();
    }

    private static bool OutputHasIncident(Situation situation)
    {
        foreach (Sphere sphere in situation.GetSpheresByCategory(SphereCategory.Output))
            foreach (Token token in sphere.Tokens)
                if (token?.Payload is ElementStack stack && stack.Element.Aspects.AspectValue("incident") > 0)
                    return true;
        return false;
    }

    private static string ArrivalKey(Situation situation)
    {
        const string prefix = "visitor.for.";
        string id = situation.VerbId;
        if (id == null || !id.StartsWith(prefix, StringComparison.Ordinal))
        {
            try { id = situation.CurrentRecipeId; }
            catch { return null; }
        }
        return id != null && id.StartsWith(prefix, StringComparison.Ordinal) ? id.Substring(prefix.Length) : null;
    }

    // ---------- finding a pair ----------

    private sealed class Job
    {
        public Situation Talk;
        public Token Visitor;
        public Token Incident;
        public string VisitorId;
        public string Key;
        public Token Book;
        public bool NoBook;
        public int Phase; // 0 visitor placed, 1 incident placed, 2 started, 3 book placed
        public float Deadline;
        public string Pair => VisitorId + "|" + Key;
    }

    private void TryStartJob(float now)
    {
        if (AdoptRunningTalk())
            return;
        Situation talk = FindIdleTalk();
        if (talk == null)
            return;

        List<Token> visitors = new List<Token>();
        Dictionary<string, Token> incidents = new Dictionary<string, Token>();
        CollectTokens(visitors, incidents);
        bool includePresent = _includePresent;
        _includePresent = false;

        foreach (Token visitorToken in visitors)
        {
            var visitor = (ElementStack)visitorToken.Payload;
            string vid = visitor.EntityId;
            string key = PickAffair(vid, visitor, incidents, includePresent);
            if (key == null)
                continue;
            Token incident = incidents[key];
            bool noBook = IsSkipped(key, vid);
            Token book = noBook ? null : PickBook(vid, key, visitor);
            if (book == null && !noBook)
            {
                _handled.Add(vid + "|" + key);
                Logger.LogInfo($"AutoTalk: no fitting book for {vid} on {key}; left to you");
                continue;
            }

            Sphere talkSlot = ThresholdById(talk, "talk");
            if (talkSlot == null || !MoveLikeDrag(visitorToken, talkSlot))
            {
                Logger.LogInfo($"AutoTalk: Talk didn't take {vid}");
                _handled.Add(vid + "|" + key);
                continue;
            }
            _job = new Job { Talk = talk, Visitor = visitorToken, Incident = incident, VisitorId = vid, Key = key, Book = book, NoBook = noBook, Deadline = now + 10f };
            Logger.LogInfo($"AutoTalk: talking to {vid} about {key} " + (noBook ? "without a book (Skip list)" : $"with {((ElementStack)book.Payload).EntityId}"));
            return;
        }
    }

    /// <summary>
    /// An Affair talk already waiting for a book (started by hand or before a reload): take it over
    /// from the book step. Visitor and Incident sit in the situation's storage by then.
    /// </summary>
    private bool AdoptRunningTalk()
    {
        foreach (Situation situation in Watchman.Get<HornedAxe>().GetRegisteredSituations())
        {
            if (situation.VerbId != "talk" || situation.StateIdentifier != StateEnum.Ongoing)
                continue;
            Sphere slot = ThresholdById(situation, "v");
            if (slot == null || slot.Tokens.Count > 0)
                continue;
            Token visitor = null, incident = null;
            foreach (Sphere sphere in situation.GetSpheres())
                foreach (Token token in sphere.Tokens)
                    if (token?.Payload is ElementStack stack)
                    {
                        if (stack.Element.Aspects.AspectValue("visitor") > 0) visitor = token;
                        else if (stack.Element.Aspects.AspectValue("incident") > 0) incident = token;
                    }
            if (visitor == null || incident == null)
                continue;
            string vid = ((ElementStack)visitor.Payload).EntityId;
            string key = ((ElementStack)incident.Payload).EntityId.Substring("incident.".Length);
            string pair = vid + "|" + key;
            if (_handled.Contains(pair))
                continue;
            if (IsSkipped(key, vid))
            {
                _handled.Add(pair);
                continue;
            }
            _job = new Job { Talk = situation, Visitor = visitor, Incident = incident, VisitorId = vid, Key = key, Phase = 2 };
            Logger.LogInfo($"AutoTalk: took over the running talk with {vid} about {key}");
            return true;
        }
        return false;
    }

    private static Situation FindIdleTalk()
    {
        foreach (Situation situation in Watchman.Get<HornedAxe>().GetRegisteredSituations())
        {
            if (situation.VerbId != "talk" || situation.StateIdentifier != StateEnum.Unstarted)
                continue;
            if (situation.GetSpheresByCategory(SphereCategory.Threshold).Any(s => s.Tokens.Count > 0))
                continue; // the player is using it
            return situation;
        }
        return null;
    }

    /// <summary>Free visitors in the house and live Incidents (one pass over the spheres, every few seconds).</summary>
    private static void CollectTokens(List<Token> visitors, Dictionary<string, Token> incidents)
    {
        foreach (Sphere sphere in Watchman.Get<HornedAxe>().GetSpheres())
        {
            if (sphere == null || sphere.Defunct || sphere.SphereCategory != SphereCategory.World || sphere.Shrouded)
                continue;
            string path = null;
            foreach (Token token in sphere.Tokens)
            {
                if (token == null || !token.IsValid() || !(token.Payload is ElementStack stack) || stack.Defunct)
                    continue;
                AspectsDictionary aspects = stack.Element.Aspects;
                bool isVisitor = aspects.AspectValue("visitor") > 0;
                bool isIncident = aspects.AspectValue("incident") > 0;
                if (!isVisitor && !isIncident)
                    continue;
                path ??= sphere.GetAbsolutePath().ToString();
                if (path.Contains("offstage") || !token.CanBeDragged())
                    continue;
                if (isVisitor)
                {
                    visitors.Add(token);
                }
                else if (stack.EntityId.StartsWith("incident.", StringComparison.Ordinal)
                    && !stack.Mutations.ContainsKey("finished"))
                {
                    incidents[stack.EntityId.Substring("incident.".Length)] = token;
                }
            }
        }
    }

    /// <summary>
    /// The Affair this visitor came for: the one seen in their visitor.for situation. Right after
    /// toggling on (or a reload) arrivals may be unknown; then a visitor with exactly one open
    /// Affair they were sent for and haven't acted on is taken.
    /// </summary>
    private string PickAffair(string vid, ElementStack visitor, Dictionary<string, Token> incidents, bool includePresent)
    {
        var candidates = new List<string>();
        Dictionary<string, int> mutations = visitor.Mutations;
        foreach (KeyValuePair<string, Token> pair in incidents)
        {
            string key = pair.Key;
            if (!mutations.TryGetValue("v." + key, out int sent) || sent <= 0)
                continue;
            if (_handled.Contains(vid + "|" + key))
                continue;
            var incident = (ElementStack)pair.Value.Payload;
            string acted = "acted." + vid + ".";
            if (incident.Mutations.Keys.Any(k => k.StartsWith(acted, StringComparison.Ordinal)))
                continue;
            if (!HasSuccessRecipe(vid, key))
                continue;
            candidates.Add(key);
        }
        if (_arrivals.TryGetValue(vid, out string arrived))
            return candidates.Contains(arrived) ? arrived : null;
        return includePresent && candidates.Count == 1 ? candidates[0] : null;
    }

    private bool IsSkipped(string key, string vid)
    {
        foreach (string item in _noBook.Value.Split(','))
        {
            string[] parts = item.Trim().Split(':');
            if (parts.Length == 2 && parts[0].Trim() == key && parts[1].Trim() == vid)
                return true;
        }
        return false;
    }

    private static readonly string[] Principles =
    {
        "edge", "forge", "grail", "heart", "knock", "lantern", "moon", "moth", "nectar", "rose", "scale", "sky", "winter",
    };

    private static string SuccessRecipe(string vid, string key, string principle) =>
        $"talk.visitor.incident.success.{vid}.{key}.mystery.{principle}";

    private static bool HasSuccessRecipe(string vid, string key)
    {
        Compendium compendium = Watchman.Get<Compendium>();
        return Principles.Any(p => compendium.EntityExists<Recipe>(SuccessRecipe(vid, key, p)));
    }

    /// <summary>
    /// A book on a library shelf the visitor accepts: mystery of a principle this Affair's success
    /// recipe takes, at least the visitor's interest; not consulted by them yet (tx.&lt;v&gt;); in a
    /// language they read; not contaminated. Read books first, then the lowest level that's enough.
    /// </summary>
    private static Token PickBook(string vid, string key, ElementStack visitor)
    {
        Compendium compendium = Watchman.Get<Compendium>();
        AspectsDictionary visitorAspects = visitor.GetAspects();
        var wanted = new Dictionary<string, int>();
        foreach (string p in Principles)
        {
            if (compendium.EntityExists<Recipe>(SuccessRecipe(vid, key, p)))
                wanted[p] = Math.Max(1, visitorAspects.AspectValue("interest." + p));
        }
        if (wanted.Count == 0)
            return null;

        Token best = null;
        (int, int) bestRank = (int.MaxValue, int.MaxValue);
        foreach (Sphere sphere in Watchman.Get<HornedAxe>().GetSpheres())
        {
            if (sphere == null || sphere.Defunct || sphere.SphereCategory != SphereCategory.World || sphere.Shrouded)
                continue;
            string path = sphere.GetAbsolutePath().ToString();
            if (!path.StartsWith("~/library", StringComparison.Ordinal) || path.IndexOf("christmas", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (IsClosedRoom(sphere))
                continue;
            foreach (Token token in sphere.Tokens)
            {
                if (token == null || !token.IsValid() || !(token.Payload is ElementStack stack) || stack.Defunct || stack.IsShrouded)
                    continue;
                if (stack.Element.Aspects.AspectValue("readable") <= 0)
                    continue; // cheap check before GetAspects
                AspectsDictionary aspects = stack.GetAspects();
                if (aspects.AspectValue("form.order") > 0 || aspects.AspectValue("tx." + vid) > 0)
                    continue;
                int level = 0;
                foreach (KeyValuePair<string, int> want in wanted)
                {
                    int mystery = aspects.AspectValue("mystery." + want.Key);
                    if (mystery >= want.Value)
                        level = mystery;
                }
                if (level == 0)
                    continue;
                bool bad = false, mastered = false;
                foreach (KeyValuePair<string, int> aspect in aspects)
                {
                    if (aspect.Value <= 0)
                        continue;
                    if (aspect.Key.StartsWith("contamination.", StringComparison.Ordinal))
                        bad = true;
                    else if (aspect.Key.StartsWith("w.", StringComparison.Ordinal)
                        && visitorAspects.AspectValue("u." + aspect.Key.Substring(2)) <= 0)
                        bad = true;
                    else if (aspect.Key.StartsWith("mastery.", StringComparison.Ordinal))
                        mastered = true;
                }
                if (bad || !token.CanBeDragged())
                    continue;
                (int, int) rank = (mastered ? 0 : 1, level);
                if (rank.CompareTo(bestRank) < 0)
                {
                    bestRank = rank;
                    best = token;
                }
            }
        }
        return best;
    }

    private static bool IsClosedRoom(Sphere sphere)
    {
        for (int depth = 0; depth < 8 && sphere != null; depth++)
        {
            IHasSpheres container = sphere.GetContainer();
            if (container is TerrainFeature room)
                return room.IsShrouded || room.IsSealed;
            sphere = (container as ITokenPayload)?.GetToken()?.Sphere;
        }
        return false;
    }

    // ---------- the talk itself ----------

    private static Sphere ThresholdById(Situation situation, string id)
    {
        foreach (Sphere sphere in situation.GetSpheres())
            if (sphere != null && !sphere.Defunct && sphere.SphereCategory == SphereCategory.Threshold && sphere.GoverningSphereSpec?.Id == id)
                return sphere;
        return null;
    }

    private void StepJob(float now)
    {
        Job job = _job;
        Situation talk = job.Talk;
        StateEnum state = talk.StateIdentifier;
        switch (job.Phase)
        {
            case 0: // visitor in; wait for their "About?" slot
            {
                Sphere subject = ThresholdById(talk, "subject");
                if (subject != null && MoveLikeDrag(job.Incident, subject))
                {
                    job.Phase = 1;
                    job.Deadline = now + 10f;
                }
                else if (now > job.Deadline)
                {
                    Abort("the Incident didn't go into the talk");
                }
                return;
            }
            case 1: // both in; start the intro recipe
            {
                if (state == StateEnum.Unstarted)
                {
                    talk.UpdateRecipePrediction();
                    string expected = $"talk.visitor.intro.{job.VisitorId}.{job.Key}";
                    if (talk.CurrentRecipeId != expected)
                    {
                        if (now > job.Deadline)
                            Abort($"the talk would be '{talk.CurrentRecipeId}', not '{expected}'");
                        return;
                    }
                    talk.TryStart();
                    if (now > job.Deadline)
                        Abort("the talk didn't start");
                    return;
                }
                // Talk only (Skip list): nothing to give, just collect at the end.
                job.Phase = job.NoBook ? 3 : 2;
                return;
            }
            case 2: // wait for the Book slot of talk.visitor.show.slot.specific (game time: no deadline, may be paused)
            {
                if (state == StateEnum.Unstarted)
                {
                    Finish("the talk was stopped");
                    return;
                }
                if (state == StateEnum.Complete)
                {
                    Finish("ended before a book could be given");
                    return;
                }
                Sphere slot = ThresholdById(talk, "v");
                // Not CurrentRecipeId: while running it already shows the predicted alt (failure/success).
                if (slot == null || state != StateEnum.Ongoing)
                    return;
                if (slot.Tokens.Count > 0)
                {
                    job.Phase = 3; // the player put something in already
                    return;
                }
                if (job.Book == null || !job.Book.IsValid() || !job.Book.CanBeDragged())
                    job.Book = PickBook(job.VisitorId, job.Key, (ElementStack)job.Visitor.Payload);
                if (job.Book != null && MoveLikeDrag(job.Book, slot))
                {
                    job.Phase = 3;
                    Logger.LogInfo($"AutoTalk: gave {((ElementStack)job.Book.Payload).EntityId} to {job.VisitorId}");
                }
                else
                {
                    Finish("the book didn't go into the slot");
                }
                return;
            }
            case 3: // reading; collect when done (game time, so no deadline)
            {
                if (state == StateEnum.Unstarted)
                {
                    Finish("the talk was stopped");
                    return;
                }
                if (state == StateEnum.Complete)
                {
                    CollectAndClose(talk);
                    Finish("done");
                }
                return;
            }
        }
    }

    /// <summary>Before the talk started: hand everything back.</summary>
    private void Abort(string why)
    {
        if (_job.Talk.StateIdentifier == StateEnum.Unstarted)
            _job.Talk.DumpUnstartedBusiness();
        Finish(why);
    }

    private void Finish(string how)
    {
        Logger.LogInfo($"AutoTalk: {_job.VisitorId} on {_job.Key}: {how}");
        _handled.Add(_job.Pair);
        _arrivals.Remove(_job.VisitorId);
        _job = null;
        _nextScan = Time.unscaledTime + 1f;
    }
}

/// <summary>The toggle: the Talk verb icon alone, right of the speed buttons; bright when on, grey when off.</summary>
internal class AutoTalkButton : MonoBehaviour
{
    private static readonly Color OnColor = Color.white;
    /// <summary>Off: greyed out and faint, like a disabled control.</summary>
    private static readonly Color OffColor = new Color(0.55f, 0.55f, 0.55f, 0.45f);

    private Image _frame;
    private Image _icon;

    /// <summary>
    /// An icon button placed right of <paramref name="after"/>, or right of the speed controls'
    /// "faster" button when null. <paramref name="scale"/> is relative to that button's height.
    /// </summary>
    public static AutoTalkButton Create(string name, Sprite sprite, RectTransform after, float scale, Action onClick)
    {
        RectTransform anchorRect = after;
        float baseHeight;
        if (anchorRect == null)
        {
            SimplerSpeedControlUI ui = FindObjectOfType<SimplerSpeedControlUI>();
            if (ui == null)
                return null;
            if (!(HarmonyLib.AccessTools.Field(typeof(SimplerSpeedControlUI), "increaseSpeedButton")?.GetValue(ui) is Button anchor) || anchor == null)
                return null;
            anchorRect = (RectTransform)anchor.transform;
            baseHeight = Mathf.Max(anchorRect.rect.height, 24f);
        }
        else
        {
            baseHeight = anchorRect.sizeDelta.y / 1.15f;
        }
        float size = baseHeight * scale;

        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(anchorRect.parent, false);
        rect.anchorMin = anchorRect.anchorMin;
        rect.anchorMax = anchorRect.anchorMax;
        rect.pivot = anchorRect.pivot;
        rect.sizeDelta = new Vector2(size, size);
        float width = after != null ? after.sizeDelta.x : anchorRect.rect.width;
        rect.anchoredPosition = anchorRect.anchoredPosition + new Vector2(width * 0.5f + size * 0.5f + (after != null ? 4f : 8f), 0f);
        go.AddComponent<LayoutElement>().ignoreLayout = true;

        var button = go.AddComponent<AutoTalkButton>();
        button._frame = go.AddComponent<Image>();
        // Invisible hit area only: the speed button's sprite tinted gold looked like a stray rectangle.
        button._frame.color = new Color(0f, 0f, 0f, 0f);

        var iconGo = new GameObject("Icon", typeof(RectTransform));
        var iconRect = (RectTransform)iconGo.transform;
        iconRect.SetParent(rect, false);
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;
        button._icon = iconGo.AddComponent<Image>();
        button._icon.sprite = sprite;
        button._icon.preserveAspect = true;
        button._icon.raycastTarget = false;

        var uiButton = go.AddComponent<Button>();
        uiButton.targetGraphic = button._frame;
        uiButton.transition = Selectable.Transition.None;
        uiButton.onClick.AddListener(() => onClick());
        AutoTalkPlugin.Log.LogInfo($"AutoTalk: {name} placed");
        return button;
    }

    public void Paint(bool on)
    {
        if (_icon != null)
            _icon.color = on ? OnColor : OffColor;
    }
}
