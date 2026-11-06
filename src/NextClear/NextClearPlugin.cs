using System.Reflection;
using BepInEx;
using HarmonyLib;
using SecretHistories.Assets.Scripts.Application.UI;
using SecretHistories.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NextClear;

/// <summary>
/// Keeps windows off the "Go to the next completed" button, so it can be clicked again and again
/// (go to, C to collect, go to...). Windows remember where the player dragged them, and one left
/// over the button blocks the next click. Every window places itself through
/// WindowPositioner.GetBoundCorrectedWorldPos (once for the target, again at the end of the show
/// animation); a postfix there moves the window straight down until its top is below the button.
/// The remembered position is untouched: only where the window shows this time changes. The window
/// never goes lower than BottomLimit, so a very tall one may still touch the button.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class NextClearPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.nextclear";
    public const string Name = "Next Clear";
    public const string Version = "0.1.0";

    private const float Gap = 8f;          // screen pixels between the button and the window top
    private const float BottomLimit = 10f; // lowest the window bottom may go, screen pixels

    private static readonly FieldInfo ButtonField = AccessTools.Field(typeof(GlobalSituationCompletedWatcher), "buttonGoTo");
    private static readonly MethodInfo CornersMethod = AccessTools.Method(typeof(WindowPositioner), "GetCornerPos");
    private static readonly MethodInfo ToScreenMethod = AccessTools.Method(typeof(WindowPositioner), "GetScreenPosFromWorld");
    private static readonly MethodInfo ToWorldMethod = AccessTools.Method(typeof(WindowPositioner), "GetWorldPosFromScreen");

    private static Button _button;
    private Harmony _harmony;

    private void Awake()
    {
        _harmony = new Harmony(Guid);
        _harmony.PatchAll(typeof(PositionerPatch));
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        _button = null;
    }

    /// <summary>The button, found once (the watcher lives for the whole game scene).</summary>
    private static Button GoToButton()
    {
        if (_button == null && ButtonField != null)
        {
            GlobalSituationCompletedWatcher watcher = Object.FindObjectOfType<GlobalSituationCompletedWatcher>();
            if (watcher != null)
                _button = ButtonField.GetValue(watcher) as Button;
        }
        return _button;
    }

    /// <summary>The button's rectangle in screen pixels.</summary>
    private static Rect ScreenRect(RectTransform rect)
    {
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Camera cam = canvas == null || canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 max = min;
        for (int i = 1; i < 4; i++)
        {
            Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    [HarmonyPatch(typeof(WindowPositioner), nameof(WindowPositioner.GetBoundCorrectedWorldPos))]
    private static class PositionerPatch
    {
        private static void Postfix(WindowPositioner __instance, ref Vector3 __result)
        {
            if (CornersMethod == null || ToScreenMethod == null || ToWorldMethod == null)
                return;
            Button button = GoToButton();
            if (button == null || !button.isActiveAndEnabled)
                return;
            Rect target = ScreenRect((RectTransform)button.transform);

            var corners = (Vector3[])CornersMethod.Invoke(__instance, new object[] { __result });
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector3 corner in corners)
            {
                Vector2 p = (Vector2)ToScreenMethod.Invoke(__instance, new object[] { corner });
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            bool overlaps = max.x > target.xMin && min.x < target.xMax && max.y > target.yMin - Gap && min.y < target.yMax;
            if (!overlaps)
                return;
            float down = Mathf.Min(max.y - (target.yMin - Gap), min.y - BottomLimit);
            if (down <= 0f)
                return;
            Vector2 screen = (Vector2)ToScreenMethod.Invoke(__instance, new object[] { __result });
            __result = (Vector3)ToWorldMethod.Invoke(__instance, new object[] { screen - new Vector2(0f, down) });
        }
    }
}
