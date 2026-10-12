using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using SecretHistories.Constants;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SmoothZoom;

/// <summary>
/// Mouse-wheel zoom in proportional steps. The game moves the camera height by a fixed
/// distance per notch: scroll/y (120 per notch on Windows) is clamped to 5 by the mousezoom
/// action and multiplied by ZoomStepDistance (80), i.e. 400 units over a -120..-2400 range.
/// Close up that quadruples the view in one notch. Here each notch scales the height by a
/// constant percentage instead, so every step feels the same at any distance.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class SmoothZoomPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.smoothzoom";
    public const string Name = "Smooth Zoom";
    public const string Version = "0.1.0";

    private static readonly FieldInfo SmoothTargetField = AccessTools.Field(typeof(CamOperator), "smoothTargetPosition");
    private static readonly FieldInfo LastInteractionField = AccessTools.Field(typeof(CamOperator), "_lastCameraInteractionInteractionType");
    private static readonly FieldInfo LastTruckField = AccessTools.Field(typeof(CamOperator), "lastTruckInput");
    private static readonly FieldInfo LastPedestalField = AccessTools.Field(typeof(CamOperator), "lastPedestalInput");
    private static readonly MethodInfo CursorWorldPositionMethod = AccessTools.Method(typeof(CamOperator), "GetCursorWorldPosition");
    private static readonly MethodInfo ResetKeyboardMovementMethod = AccessTools.Method(typeof(CamOperator), "ResetKeyboardMovement");

    internal static ConfigEntry<float> StepPercent;
    private Harmony _harmony;

    private void Awake()
    {
        StepPercent = Config.Bind("General", "StepPercent", 40f,
            new ConfigDescription("How much one mouse-wheel notch changes the camera height, in percent.",
                new AcceptableValueRange<float>(3f, 200f)));
        _harmony = new Harmony(Guid);
        _harmony.PatchAll(typeof(WheelZoomPatch));
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(CamOperator), nameof(CamOperator.OnZoomEvent))]
    private static class WheelZoomPatch
    {
        // Low priority: let other mods (BookFinder blocks zoom over its window) decide first.
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(CamOperator __instance, ZoomLevelEventArgs args, bool __runOriginal)
        {
            if (!__runOriginal)
                return false;
            // Only the mouse wheel; keys (Repeat), absolute levels and touch (ToPosition) stay vanilla.
            bool wheel = args.AbsoluteTargetZoomLevel == ZoomLevel.Unspecified && !args.Repeat && !args.CancelRepeat
                && !args.ToPosition.HasValue && args.CurrentZoomInput != 0f;
            if (!wheel)
                return true;
            // Over a scrollable list (a desk's Craftable recipes, our Finder...) the wheel scrolls it.
            ScrollRect list = ScrollAreaUnderPointer();
            if (list != null)
            {
                // Some of the game's lists have wheel scrolling switched off (scrollSensitivity 0,
                // e.g. the Craftable recipes), leaving only the scrollbar: scroll those ourselves.
                // Lists with their own wheel handler besides ScrollRect (Finder's WheelStepper) are
                // left alone, or they'd scroll twice.
                if (list.scrollSensitivity <= 0f && list.GetComponents<IScrollHandler>().Length <= 1)
                    ScrollBy(list, Mathf.Sign(args.CurrentZoomInput) * ListStep);
                return false;
            }

            // Base on the current target, not the camera, so quick notches accumulate.
            float current = ((Vector3)SmoothTargetField.GetValue(__instance)).z;
            // A notch arrives clamped to ±5; smaller values (precision touchpads) give a partial step.
            float steps = Mathf.Clamp(args.CurrentZoomInput, -1f, 1f);
            float factor = Mathf.Pow(1f + StepPercent.Value / 100f, steps);
            // Heights are negative: positive input zooms out (further from 0).
            float target = Mathf.Clamp(current * factor, __instance.ZOOM_Z_FARTHEST, __instance.ZOOM_Z_CLOSE);
            if (Mathf.Abs(target - current) < 0.1f)
                return false;

            if ((float)LastTruckField.GetValue(__instance) == 0f && (float)LastPedestalField.GetValue(__instance) == 0f)
                ResetKeyboardMovementMethod.Invoke(__instance, null);
            SmoothTargetField.SetValue(__instance, CursorWorldPositionMethod.Invoke(__instance, new object[] { target }));
            LastInteractionField.SetValue(__instance, target > current ? CameraInteractionType.StepIn : CameraInteractionType.StepOut);
            return false;
        }

        /// <summary>Pixels per wheel notch for lists whose own wheel scrolling is off.</summary>
        private const float ListStep = 60f;

        private static readonly List<RaycastResult> Hits = new List<RaycastResult>();

        /// <summary>
        /// The ScrollRect holding the topmost UI element under the mouse, if its content doesn't fit
        /// (so there is something to scroll). The game's own guard, LocalNexus.DisablePlayerZoom,
        /// is wired per prefab and misses some lists.
        /// </summary>
        private static ScrollRect ScrollAreaUnderPointer()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || Mouse.current == null)
                return null;
            var pointer = new PointerEventData(eventSystem) { position = Mouse.current.position.ReadValue() };
            Hits.Clear();
            eventSystem.RaycastAll(pointer, Hits);
            if (Hits.Count == 0 || Hits[0].gameObject == null)
                return null;
            ScrollRect scroll = Hits[0].gameObject.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null)
                return null;
            RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            bool overflows = scroll.content.rect.height > viewport.rect.height + 1f
                || scroll.horizontal && scroll.content.rect.width > viewport.rect.width + 1f;
            return overflows ? scroll : null;
        }

        /// <summary>Positive moves towards the end of the list (wheel down: zoom input is positive).</summary>
        private static void ScrollBy(ScrollRect scroll, float pixels)
        {
            RectTransform content = scroll.content;
            RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            float max = Mathf.Max(0f, content.rect.height - viewport.rect.height);
            Vector2 position = content.anchoredPosition;
            position.y = Mathf.Clamp(position.y + pixels, 0f, max);
            scroll.StopMovement();
            content.anchoredPosition = position;
        }
    }
}
