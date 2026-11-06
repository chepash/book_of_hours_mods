using System.Collections.Generic;
using BepInEx;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.Spheres;
using SecretHistories.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SlotFit;

/// <summary>
/// While a situation window is open (a desk, Consider with a skill in it, a bed...), cards in the
/// souls, skills and memories trays that fit none of its open slots of their kind are darkened.
/// A slot's kind is its Essential aspect: "ability" for a Soul slot, "skill", "memory" or "lesson"
/// (all cards in the memories tray). The fit test is the game's own: Sphere.GetMatchForTokenPayload,
/// i.e. Essential, Required (any one) and Forbidden of the slot's spec. Only slots active in the
/// situation's current state count, so Consider's Effort/Lesson slots appear once a skill is in.
/// A foreign-language book adds its own "language" slot to the desk (tomes.json: Required
/// s.&lt;language&gt;, no Essential); it counts for the skills tray, so the language skill isn't darkened.
/// A tray is left alone when the window has no slot of its kind. Polled every 0.25 s: slots come and
/// go as cards are placed, and the game offers no single event for that.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class SlotFitPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.slotfit";
    public const string Name = "Slot Fit";
    public const string Version = "0.1.0";

    private const string OverlayName = "SlotFitOverlay";
    private const float Shade = 0.62f;

    /// <summary>Tray path and the Essential aspects that make a slot "for" that tray.</summary>
    private static readonly (string tray, string[] kinds)[] Trays =
    {
        ("~/hand.abilities", new[] { "ability" }),
        ("~/hand.skills", new[] { "skill" }),
        ("~/hand.memories", new[] { "memory", "lesson" }),
    };

    private float _nextPoll;
    private bool _anyDim;

    private void Awake()
    {
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void OnDestroy()
    {
        // Hot reload: remove overlays at once (their fade component goes with the old assembly).
        foreach (SlotDimFade fade in Resources.FindObjectsOfTypeAll<SlotDimFade>())
        {
            if (fade != null)
                Destroy(fade.gameObject);
        }
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextPoll)
            return;
        _nextPoll = Time.unscaledTime + 0.25f;
        if (!Watchman.Exists<HornedAxe>())
            return;
        HornedAxe axe = Watchman.Get<HornedAxe>();
        Situation open = OpenSituation(axe);
        List<Sphere> slots = open != null ? ActiveSlots(open) : null;
        if (slots == null && !_anyDim)
            return; // nothing open and nothing to undo
        bool anyDim = false;
        foreach ((string trayPath, string[] kinds) in Trays)
        {
            Sphere tray = axe.GetSphereByAbsolutePath(trayPath);
            if (tray == null || !tray.IsValid())
                continue;
            List<Sphere> forTray = slots?.FindAll(s => IsKind(s, kinds) || (trayPath == "~/hand.skills" && IsLanguageSlot(s)));
            anyDim |= Apply(tray, forTray != null && forTray.Count > 0 ? forTray : null);
        }
        _anyDim = anyDim;
    }

    private static Situation OpenSituation(HornedAxe axe)
    {
        foreach (Situation situation in axe.GetRegisteredSituations())
        {
            if (situation != null && situation.IsOpen)
                return situation;
        }
        return null;
    }

    private static List<Sphere> ActiveSlots(Situation situation)
    {
        return situation.GetSpheresActiveForCurrentState()
            .FindAll(s => s != null && s.SphereCategory == SphereCategory.Threshold && s.GoverningSphereSpec != null);
    }

    /// <summary>The slot a foreign-language book adds: id "language", takes only s.* skill cards.</summary>
    private static bool IsLanguageSlot(Sphere slot)
    {
        SphereSpec spec = slot.GoverningSphereSpec;
        if (spec.Id == "language")
            return true;
        if (spec.Essential.Count > 0 || spec.Required.Count == 0)
            return false;
        foreach (string key in spec.Required.Keys)
        {
            if (!key.StartsWith("s.", System.StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static bool IsKind(Sphere slot, string[] kinds)
    {
        foreach (string kind in kinds)
        {
            if (slot.GoverningSphereSpec.Essential.ContainsKey(kind))
                return true;
        }
        return false;
    }

    /// <summary>Darkens tray cards no slot accepts; with no slots, undoes it. True if any is dark.</summary>
    private static bool Apply(Sphere tray, List<Sphere> slots)
    {
        bool anyDim = false;
        foreach (Token token in tray.Tokens)
        {
            if (token == null || !(token.Payload is ElementStack stack))
                continue;
            bool dim = slots != null && !slots.Exists(s => s.GetMatchForTokenPayload(stack).MatchType == SlotMatchForAspectsType.Okay);
            anyDim |= dim;
            Transform overlay = token.transform.Find(OverlayName);
            if (dim && overlay == null)
                overlay = AddOverlay(token.transform);
            SlotDimFade fade = overlay != null ? overlay.GetComponent<SlotDimFade>() : null;
            if (fade != null)
                fade.Target = dim ? Shade : 0f;
            else if (overlay != null)
                Destroy(overlay.gameObject); // left by an older version of this plugin
        }
        return anyDim;
    }

    private static Transform AddOverlay(Transform token)
    {
        var go = new GameObject(OverlayName, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(token, false);
        rect.SetAsLastSibling();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        go.AddComponent<LayoutElement>().ignoreLayout = true;
        Image image = go.AddComponent<Image>();
        image.sprite = BohShared.GameStyle.Sprite("box-rnd-16px");
        image.type = Image.Type.Sliced;
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = false; // the card stays draggable and clickable
        go.AddComponent<SlotDimFade>().Image = image;
        return rect;
    }
}

/// <summary>Moves the overlay's alpha towards Target over about a third of a second; gone at zero.</summary>
public class SlotDimFade : MonoBehaviour
{
    private const float Duration = 0.35f;
    public Image Image;
    public float Target;

    private void Update()
    {
        if (Image == null)
            return;
        Color c = Image.color;
        c.a = Mathf.MoveTowards(c.a, Target, Time.unscaledDeltaTime / Duration * 0.62f);
        Image.color = c;
        if (Target <= 0f && c.a <= 0f)
            Destroy(gameObject);
    }
}
