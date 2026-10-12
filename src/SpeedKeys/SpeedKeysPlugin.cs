using BepInEx;
using HarmonyLib;
using SecretHistories.Constants;
using SecretHistories.Enums;
using SecretHistories.Infrastructure;
using UnityEngine.InputSystem;

namespace SpeedKeys;

/// <summary>
/// Keys 1-4 set the game speed instead of jumping between zoom levels (the mouse wheel zooms
/// well enough). The game's kbzoom1..4 actions are kept and only their handlers in
/// UIController are replaced, so rebinding them in the game's settings moves the speed keys too.
/// Speed changes go through SpeedControlEvent like the N/M keys; it also lifts a pause.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class SpeedKeysPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.speedkeys";
    public const string Name = "Speed Keys";
    public const string Version = "0.1.0";

    private Harmony _harmony;

    private void Awake()
    {
        _harmony = new Harmony(Guid);
        _harmony.PatchAll(typeof(ZoomKeysToSpeed));
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(UIController))]
    private static class ZoomKeysToSpeed
    {
        [HarmonyPrefix, HarmonyPatch(nameof(UIController.Input_ZoomClose))]
        private static bool Key1(UIController __instance, InputAction.CallbackContext context) => SetSpeed(__instance, context, GameSpeed.Normal);

        [HarmonyPrefix, HarmonyPatch(nameof(UIController.Input_ZoomMid))]
        private static bool Key2(UIController __instance, InputAction.CallbackContext context) => SetSpeed(__instance, context, GameSpeed.Fast);

        [HarmonyPrefix, HarmonyPatch(nameof(UIController.Input_ZoomQuiteFar))]
        private static bool Key3(UIController __instance, InputAction.CallbackContext context) => SetSpeed(__instance, context, GameSpeed.VeryFast);

        [HarmonyPrefix, HarmonyPatch(nameof(UIController.Input_ZoomFarthest))]
        private static bool Key4(UIController __instance, InputAction.CallbackContext context) => SetSpeed(__instance, context, GameSpeed.VeryVeryFast);

        private static bool SetSpeed(UIController ui, InputAction.CallbackContext context, GameSpeed speed)
        {
            // Same guards as the game's own speed keys: not while typing or force-paused.
            if (context.started && !ui.HotkeysDisabled())
            {
                ui.SpeedControlEvent.Invoke(new SpeedControlEventArgs
                {
                    ControlPriorityLevel = SpeedControlEventArgs.DefaultControlPriorityLevel,
                    GameSpeed = speed,
                    WithSFX = true,
                });
            }
            return false; // never zoom
        }
    }
}
