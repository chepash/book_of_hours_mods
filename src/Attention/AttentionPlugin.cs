using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BohShared;
using HarmonyLib;
using SecretHistories.Assets.Scripts.Application.UI;
using SecretHistories.Constants;
using SecretHistories.Infrastructure;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.Spheres;
using SecretHistories.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Attention;

/// <summary>
/// Places waiting for a card mid-recipe. While a book is read for mastery without enough of its
/// principle, the reading turns into "a Semblance?" and the router offers a few Opportunities
/// (1_consider_books: study.mastering.opportunity.*), each a recipe slot - a Soul, a Comfort, a
/// Memory... - open only for its 10-second warmup. Miss them and the reading fails ("Not Today",
/// which also fatigues whatever lay in the desk). An ongoing recipe's slots are Threshold spheres
/// active in the Ongoing state (OngoingState.IsActiveInThisState); an empty one is a place asking
/// for something. A button right of the game's "Go to the next completed" shows how many there are
/// and cycles through them like that one; when a new one appears the game pauses (config).
/// Salons work the same way: each conversation step (DLC_HOL_3b_salons_routing.json:
/// slnch.needs.ability.&lt;soul&gt;) is a 10-second recipe with one "Soul" slot; an empty one means the
/// talk is waiting for a Chor, Wist... card, and without it the conversation fizzles.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class AttentionPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.attention";
    public const string Name = "Attention";
    public const string Version = "0.1.0";

    private static readonly FieldInfo GoToField = AccessTools.Field(typeof(GlobalSituationCompletedWatcher), "buttonGoTo");

    private ConfigEntry<bool> _pause;
    private ConfigEntry<bool> _desksOnly;
    private ConfigEntry<bool> _salons;
    private GameObject _button;
    private TextMeshProUGUI _count;
    private readonly List<Situation> _waiting = new List<Situation>();
    private int _next;

    private void Awake()
    {
        _pause = Config.Bind("General", "PauseWhenAsked", true,
            "Pause the game when a place starts asking for a card (an opportunity lasts ~10 s).");
        _desksOnly = Config.Bind("General", "DesksOnly", true,
            "Only reading desks and Consider (where mastery opportunities appear).");
        _salons = Config.Bind("General", "Salons", true,
            "Also salons: a conversation waiting for a soul card (each ask lasts ~10 s).");
        // A light walk over the registered situations once a second; opportunities last 10 s.
        InvokeRepeating(nameof(Check), 1f, 1f);
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void OnDestroy()
    {
        if (_button != null)
            Destroy(_button);
    }

    private void Check()
    {
        if (!Watchman.Exists<HornedAxe>())
            return;
        var now = Watchman.Get<HornedAxe>().GetRegisteredSituations()
            .Where(s => s != null && s.StateIdentifier == StateEnum.Ongoing
                        && (_salons.Value && IsSalon(s)
                            || s.IsCyclableWorldSituation() && (!_desksOnly.Value || IsDesk(s)))
                        && AsksForCard(s))
            .ToList();
        bool appeared = now.Any(s => !_waiting.Contains(s));
        if (appeared || now.Count != _waiting.Count)
            Logger.LogInfo("Asking for a card: " + (now.Count == 0 ? "none" : string.Join(", ", now.Select(s => s.Id))));
        _waiting.Clear();
        _waiting.AddRange(now);
        if (appeared && _pause.Value)
            Pause();
        UpdateButton();
    }

    private static bool IsDesk(Situation s) =>
        s.VerbId != null && (s.VerbId == "consider" || s.VerbId.EndsWith(".consider", System.StringComparison.Ordinal));

    private static bool IsSalon(Situation s) =>
        s.VerbId != null && s.VerbId.StartsWith("salon.", System.StringComparison.Ordinal);

    private static bool AsksForCard(Situation s) =>
        s.GetSpheresByCategory(SphereCategory.Threshold)
            .Any(sp => sp != null && !sp.Defunct && sp.GoverningSphereSpec.IsActiveInState(StateEnum.Ongoing)
                       && sp.GetElementTokens().Count == 0);

    private void Pause()
    {
        UIController ui = Object.FindObjectOfType<UIController>(); // rare: only when one appears
        ui?.SpeedControlEvent.Invoke(new SpeedControlEventArgs
        {
            ControlPriorityLevel = SpeedControlEventArgs.DefaultControlPriorityLevel,
            GameSpeed = GameSpeed.Paused,
            WithSFX = true,
        });
    }

    private void UpdateButton()
    {
        if (_button == null)
        {
            if (_waiting.Count == 0)
                return;
            CreateButton();
            if (_button == null)
                return;
        }
        _button.SetActive(_waiting.Count > 0);
        _count.text = _waiting.Count.ToString();
    }

    /// <summary>A copy of the game's go-to button, tinted, with its own click and count.</summary>
    private void CreateButton()
    {
        GlobalSituationCompletedWatcher watcher = Object.FindObjectOfType<GlobalSituationCompletedWatcher>();
        if (watcher == null || !(GoToField?.GetValue(watcher) is Button original) || original == null)
            return;
        _button = Instantiate(original.gameObject, original.transform.parent);
        _button.name = "AttentionButton";
        foreach (Transform child in _button.transform)
            Destroy(child.gameObject); // the count badge belongs to the game's watcher
        foreach (MonoBehaviour hint in _button.GetComponents<MonoBehaviour>().Where(c => c.GetType().Name == "HasHintPanel"))
            Destroy(hint);
        var rect = (RectTransform)_button.transform;
        rect.anchoredPosition = ((RectTransform)original.transform).anchoredPosition + new Vector2(rect.rect.width + 6f, 0f);
        _button.GetComponent<Image>().color = new Color(0.85f, 0.55f, 1f, 1f); // violet: not the completed one
        var button = _button.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent(); // drop the game's go-to-completed listener
        button.onClick.AddListener(GoToNext);
        button.interactable = true;

        Image badge = GameStyle.Panel(_button.transform, "Badge", "box-rnd-16px", new Color(0.35f, 0.1f, 0.45f, 0.95f));
        RectTransform badgeRect = badge.rectTransform;
        badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(1f, 1f);
        badgeRect.sizeDelta = new Vector2(26f, 26f);
        badgeRect.anchoredPosition = new Vector2(-10f, -10f);
        badge.raycastTarget = false;
        _count = GameStyle.Text(badge.transform, "Count", "0", 16f, GameStyle.Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        GameStyle.Stretch(_count.rectTransform);
    }

    private void GoToNext()
    {
        _waiting.RemoveAll(s => s == null || s.Token == null || !s.Token.IsValid());
        if (_waiting.Count == 0)
            return;
        Situation s = _waiting[_next++ % _waiting.Count];
        Watchman.Get<CamOperator>().PointAtTableLevelAtQuiteCloseHeight(s.Token.transform.position, 0.6f, null);
        s.OpenAt(s.Token.Location);
    }
}
