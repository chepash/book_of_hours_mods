using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using SecretHistories.Core;
using SecretHistories.Entities;
using SecretHistories.Spheres;
using SecretHistories.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LessonFit;

/// <summary>
/// While a Lesson card is shown in the detail window (top right, after clicking the card), skills
/// it can't upgrade are darkened in the skills tray. Consider takes a Lesson for a skill when they
/// share a principle ("from this Skill, or from another which shares a Power aspect"), and a skill
/// at level 9 can't go higher (u.skill.max.hint). The detail window keeps what it shows in
/// _currentDetailable and clears it in HideAll, so a cheap poll of that field is enough.
/// Darkening is the same faded overlay as in TreeDim, under its own name.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class LessonFitPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.lessonfit";
    public const string Name = "Lesson Fit";
    public const string Version = "0.1.0";

    private const string OverlayName = "LessonFitOverlay";
    private const string SkillsTray = "~/hand.skills";
    private const float Shade = 0.62f;
    private const int MaxSkillLevel = 9;

    private static readonly string[] Principles =
    {
        "lantern", "forge", "edge", "winter", "heart", "grail", "moth",
        "knock", "sky", "moon", "nectar", "scale", "rose",
    };

    private static readonly FieldInfo WindowField = AccessTools.Field(typeof(AureateNotifier), "masterDetailWindow");
    private static readonly FieldInfo DetailableField = AccessTools.Field(typeof(AureateMasterDetailWindow), "_currentDetailable");

    private AureateMasterDetailWindow _window;
    private ElementStack _lesson;
    private float _nextPoll;
    private float _nextRefresh;

    private void Awake()
    {
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void OnDestroy()
    {
        // Hot reload: remove overlays at once (their fade component goes with the old assembly).
        foreach (LessonDimFade fade in Resources.FindObjectsOfTypeAll<LessonDimFade>())
        {
            if (fade != null)
                Destroy(fade.gameObject);
        }
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextPoll)
            return;
        _nextPoll = Time.unscaledTime + 0.1f;

        ElementStack lesson = FocusedLesson();
        if (lesson != _lesson)
        {
            _lesson = lesson;
            Apply(lesson);
            _nextRefresh = Time.unscaledTime + 0.5f;
        }
        else if (lesson != null && Time.unscaledTime >= _nextRefresh)
        {
            // Skills can arrive in the tray or level up while the lesson stays in focus.
            Apply(lesson);
            _nextRefresh = Time.unscaledTime + 0.5f;
        }
    }

    /// <summary>The Lesson stack the detail window shows now, or null.</summary>
    private ElementStack FocusedLesson()
    {
        if (_window == null)
        {
            if (WindowField == null || !Watchman.Exists<AureateNotifier>())
                return null;
            _window = WindowField.GetValue(Watchman.Get<AureateNotifier>()) as AureateMasterDetailWindow;
            if (_window == null)
                return null;
        }
        if (!(DetailableField?.GetValue(_window) is ElementStack stack) || !stack.IsValid())
            return null;
        return stack.Element.Aspects.AspectValue("lesson") > 0 ? stack : null;
    }

    private static List<string> PrinciplesOf(Element element)
    {
        var list = new List<string>();
        foreach (string p in Principles)
        {
            if (element.Aspects.AspectValue(p) > 0)
                list.Add(p);
        }
        return list;
    }

    /// <summary>Overlay on skills the lesson can't upgrade; none when no lesson is in focus.</summary>
    private static void Apply(ElementStack lesson)
    {
        if (!Watchman.Exists<HornedAxe>())
            return;
        Sphere tray = Watchman.Get<HornedAxe>().GetSphereByAbsolutePath(SkillsTray);
        if (tray == null || !tray.IsValid())
            return;
        List<string> principles = lesson != null ? PrinciplesOf(lesson.Element) : null;
        foreach (Token token in tray.Tokens)
        {
            if (token == null || !(token.Payload is ElementStack skill))
                continue;
            bool dim = principles != null && !Fits(skill, principles);
            Transform overlay = token.transform.Find(OverlayName);
            if (dim && overlay == null)
                overlay = AddOverlay(token.transform);
            LessonDimFade fade = overlay != null ? overlay.GetComponent<LessonDimFade>() : null;
            if (fade != null)
                fade.Target = dim ? Shade : 0f;
            else if (overlay != null)
                Destroy(overlay.gameObject); // left by an older version of this plugin
        }
    }

    private static bool Fits(ElementStack skill, List<string> principles)
    {
        AspectsDictionary aspects = skill.GetAspects(true);
        if (aspects.AspectValue("skill") >= MaxSkillLevel)
            return false;
        foreach (string p in principles)
        {
            if (aspects.AspectValue(p) > 0)
                return true;
        }
        return false;
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
        go.AddComponent<LessonDimFade>().Image = image;
        return rect;
    }
}

/// <summary>Moves the overlay's alpha towards Target over about a third of a second; gone at zero.</summary>
public class LessonDimFade : MonoBehaviour
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
