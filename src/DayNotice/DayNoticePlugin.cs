using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using SecretHistories.Commands;
using SecretHistories.Entities;
using SecretHistories.UI;
using UnityEngine;

namespace DayNotice;

/// <summary>
/// The Daybreak and Dusk banners in the middle of the screen close by themselves after a short
/// time. They are "notable" recipes (celestial_recipes_time.json, "notable":true):
/// RecipeCompletionEffectCommand.RespectNotables shows them through AureateNotifier, which builds an
/// AureateNotificationWindow with a timer of 10 s + 2 × the NotificationTime setting. In practice
/// they stayed until clicked, so the plugin hides them itself: the window built while a notable
/// recipe is being announced is remembered, and Hide() is called on it once the delay has passed
/// (real time, so it works while the game is paused). Hide() does nothing while the window is
/// still animating in, so it is retried until the window is gone.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class DayNoticePlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.daynotice";
    public const string Name = "Day Notice";
    public const string Version = "0.1.0";

    private const float RetryEvery = 0.25f;

    private static ConfigEntry<float> _seconds;
    private static ConfigEntry<bool> _onlyDayChange;
    private static bool _announcing;
    private static readonly List<Tracked> Windows = new List<Tracked>();

    private Harmony _harmony;

    private class Tracked
    {
        public AureateNotificationWindow Window;
        public float HideAt;
    }

    private void Awake()
    {
        _seconds = Config.Bind("General", "Seconds", 1f,
            "How long the banner stays before closing by itself, in real seconds.");
        _onlyDayChange = Config.Bind("General", "OnlyDaybreakAndDusk", true,
            "true: only Daybreak and Dusk close by themselves; false: every notable-recipe banner (also Numa).");
        _harmony = new Harmony(Guid);
        _harmony.PatchAll(typeof(Patches));
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        Windows.Clear();
        _announcing = false;
    }

    private void Update()
    {
        if (Windows.Count == 0)
            return;
        float now = Time.unscaledTime;
        for (int i = Windows.Count - 1; i >= 0; i--)
        {
            Tracked t = Windows[i];
            if (t.Window == null || !t.Window.gameObject.activeInHierarchy && now > t.HideAt)
            {
                Windows.RemoveAt(i);
                continue;
            }
            if (now < t.HideAt)
                continue;
            t.Window.Hide(); // no-op while it is still animating; retried below
            t.HideAt = now + RetryEvery;
        }
    }

    private static class Patches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(RecipeCompletionEffectCommand), "RespectNotables")]
        private static void Before(Recipe notableRecipe)
        {
            _announcing = notableRecipe != null && notableRecipe.Notable
                          && (!_onlyDayChange.Value || (notableRecipe.Id ?? "").StartsWith("day."));
        }

        [HarmonyPostfix, HarmonyPatch(typeof(RecipeCompletionEffectCommand), "RespectNotables")]
        private static void After() => _announcing = false;

        [HarmonyPostfix, HarmonyPatch(typeof(AureateNotifier), "BuildNotificationWindow")]
        private static void Built(AureateNotificationWindow __result)
        {
            if (!_announcing || __result == null)
                return;
            Windows.Add(new Tracked { Window = __result, HideAt = Time.unscaledTime + Mathf.Max(0f, _seconds.Value) });
        }
    }
}
