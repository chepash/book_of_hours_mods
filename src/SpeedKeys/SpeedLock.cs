using System;
using System.Collections.Generic;
using HarmonyLib;
using SecretHistories.Commands;
using SecretHistories.Constants;
using SecretHistories.Enums;
using SecretHistories.Infrastructure;
using SecretHistories.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpeedKeys;

/// <summary>
/// Speed lock: a middle click on the speed button (the middle one, "x2".."x6") locks the speed,
/// and the buttons turn purple. Daybreak no longer drops the game back to normal speed.
/// How the game does it: the recipe day.daybreak has the fx "meta.setspeed": 1; LocalNexus.BroadcastFx
/// turns that into SpeedControlEvent(priority 2, Normal), which Heart and the speed UI obey.
/// While locked, the same event goes out with the speed the player had instead (the args object is
/// shared by all listeners, so Heart and the buttons agree). The player's own speed changes still
/// work; the lock keeps whatever speed is running. A second middle click unlocks.
/// </summary>
internal static class SpeedLock
{
    public static bool Locked;

    /// <summary>True only while BroadcastFx is sending the "meta.setspeed" fx.</summary>
    private static bool _inSetSpeedFx;

    private static readonly string[] ButtonFields = { "pausedButton", "playingButton", "fastButton", "veryFastButton", "veryVeryFastButton" };
    private static readonly Color LockedColor = new Color(0.72f, 0.45f, 1f, 1f);

    private static SimplerSpeedControlUI _ui;
    private static readonly List<(Graphic graphic, Color color)> Originals = new List<(Graphic, Color)>();
    private static readonly List<SpeedLockClick> Clicks = new List<SpeedLockClick>();

    /// <summary>Finds the speed buttons once per scene (called rarely from the plugin's Update).</summary>
    public static void Attach()
    {
        if (_ui != null)
            return;
        Clicks.Clear();
        Originals.Clear();
        _ui = UnityEngine.Object.FindObjectOfType<SimplerSpeedControlUI>();
        if (_ui == null)
            return;
        foreach (string field in ButtonFields)
        {
            if (AccessTools.Field(typeof(SimplerSpeedControlUI), field)?.GetValue(_ui) is Button button && button != null)
            {
                SpeedLockClick click = button.gameObject.AddComponent<SpeedLockClick>();
                Clicks.Add(click);
                if (button.targetGraphic != null)
                    Originals.Add((button.targetGraphic, button.targetGraphic.color));
            }
        }
        Paint();
    }

    public static void Toggle()
    {
        Locked = !Locked;
        Paint();
        SpeedKeysPlugin.Log.LogInfo(Locked ? "Speed locked" : "Speed unlocked");
    }

    private static void Paint()
    {
        foreach ((Graphic graphic, Color color) in Originals)
            if (graphic != null)
                graphic.color = Locked ? LockedColor : color;
    }

    /// <summary>Removes the click handlers and restores the colours (plugin reload).</summary>
    public static void Detach()
    {
        Locked = false;
        Paint();
        foreach (SpeedLockClick click in Clicks)
            if (click != null)
                UnityEngine.Object.Destroy(click);
        Clicks.Clear();
        Originals.Clear();
        _ui = null;
    }

    [HarmonyPatch(typeof(LocalNexus), nameof(LocalNexus.BroadcastFx), typeof(EnviroFxCommand), typeof(bool))]
    internal static class SetSpeedFx
    {
        private static void Prefix(EnviroFxCommand enviroFxCommand) =>
            _inSetSpeedFx = enviroFxCommand != null && enviroFxCommand.MatchConcern("meta") && enviroFxCommand.MatchEffect("setspeed");

        private static void Finalizer() => _inSetSpeedFx = false;
    }

    /// <summary>
    /// The daybreak event reaches Heart and the speed buttons with one shared args object; whichever
    /// listener runs first swaps the speed, so both agree whatever the order.
    /// </summary>
    private static void Keep(SpeedControlEventArgs args)
    {
        if (!Locked || !_inSetSpeedFx || args == null)
            return;
        Heart heart = Watchman.Get<Heart>();
        if (heart == null)
            return;
        GameSpeed current = heart.GetEffectiveGameSpeed();
        if (current == GameSpeed.Paused)
            current = heart.GetGameSpeedBeforeMostRecentPause();
        if (current == GameSpeed.Paused || current == GameSpeed.DeferToNextLowestCommand || args.GameSpeed == current)
            return;
        SpeedKeysPlugin.Log.LogInfo($"Speed lock: kept {current} instead of {args.GameSpeed} at daybreak");
        args.GameSpeed = current;
    }

    [HarmonyPatch(typeof(Heart), nameof(Heart.RespondToSpeedControlCommand))]
    internal static class KeepSpeed
    {
        private static void Prefix(SpeedControlEventArgs args) => Keep(args);
    }

    [HarmonyPatch(typeof(SimplerSpeedControlUI), nameof(SimplerSpeedControlUI.RespondToSpeedControlCommand))]
    internal static class KeepSpeedShown
    {
        private static void Prefix(SpeedControlEventArgs args) => Keep(args);
    }
}

/// <summary>Middle click on a speed button toggles the lock; other clicks are left to the Button.</summary>
internal class SpeedLockClick : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Middle)
            SpeedLock.Toggle();
    }
}
