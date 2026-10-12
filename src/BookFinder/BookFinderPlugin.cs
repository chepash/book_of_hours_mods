using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using SecretHistories.Constants;
using SecretHistories.Entities;
using SecretHistories.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BookFinder;

/// <summary>
/// Ctrl+F opens a window that lists every book in explored rooms, filtered by mystery,
/// difficulty, read state, language and title, plus tabs for items and people (visitors, assistants) already met.
/// Clicking a result flies the camera to it.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class BookFinderPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.bookfinder";
    public const string Name = "Book Finder";
    public const string Version = "0.1.0";

    private static readonly FieldInfo MasterDetailWindowField = AccessTools.Field(typeof(AureateNotifier), "masterDetailWindow");

    internal static BookFinderWindow Window;
    private Harmony _harmony;
    private float _nextVisitorScan;

    private void Awake()
    {
        _harmony = new Harmony(Guid);
        _harmony.PatchAll(typeof(BlockZoomOverWindow));
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void OnDestroy()
    {
        // ScriptEngine destroys the plugin on hot reload.
        _harmony?.UnpatchSelf();
        if (Window != null)
            Destroy(Window.gameObject);
        Window = null;
    }

    private void Update()
    {
        TrackVisitors();
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.ctrlKey.isPressed && keyboard.fKey.wasPressedThisFrame)
        {
            Toggle();
            return;
        }
        // Esc only while typing: otherwise the game also handles it (and may open its menu).
        if (Window != null && Window.IsOpen && Window.IsSearchFocused && keyboard.escapeKey.wasPressedThisFrame)
            Window.Close();
    }

    /// <summary>
    /// Marks visitors as met while they're on stage, even with the window closed:
    /// a visit can be over before the player ever opens the Finder.
    /// </summary>
    private void TrackVisitors()
    {
        if (Time.unscaledTime < _nextVisitorScan)
            return;
        _nextVisitorScan = Time.unscaledTime + 5f;
        if (!Watchman.Exists<HornedAxe>())
            return;
        try
        {
            PeopleIndex.Scan();
        }
        catch (Exception e)
        {
            Logger.LogDebug("Visitor scan failed: " + e.Message);
        }
    }

    private void Toggle()
    {
        if (Window != null && Window.IsOpen)
        {
            Window.Close();
            return;
        }
        if (Window == null && !TryCreateWindow())
            return;
        try
        {
            Window.Open();
        }
        catch (Exception e)
        {
            Logger.LogError(e);
        }
    }

    private bool TryCreateWindow()
    {
        // Same overlay canvas as the details window; it only exists once a game is loaded.
        if (!Watchman.Exists<AureateNotifier>() || !Watchman.Exists<HornedAxe>())
            return false;
        if (!(MasterDetailWindowField.GetValue(Watchman.Get<AureateNotifier>()) is Component master) || master == null)
            return false;
        Canvas canvas = master.GetComponentInParent<Canvas>();
        if (canvas == null)
            return false;
        Window = BookFinderWindow.Create(canvas.rootCanvas.transform, Logger);
        return true;
    }

    /// <summary>Scrolling the result list shouldn't also zoom the camera.</summary>
    [HarmonyPatch(typeof(CamOperator), nameof(CamOperator.OnZoomEvent))]
    private static class BlockZoomOverWindow
    {
        private static bool Prefix(ZoomLevelEventArgs args)
        {
            if (Window == null || Mouse.current == null)
                return true;
            bool scrollZoom = args.AbsoluteTargetZoomLevel == ZoomLevel.Unspecified && args.CurrentZoomInput != 0f;
            return !(scrollZoom && Window.IsPointerOver(Mouse.current.position.ReadValue()));
        }
    }
}
