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
/// F (or Ctrl+F) opens a window that lists every book in explored rooms, filtered by mystery,
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
    /// <summary>Items tab: a double click may also send the card into a free slot of the carried things.</summary>
    internal static BepInEx.Configuration.ConfigEntry<bool> ItemsToHand;
    /// <summary>Books tab: the same for books.</summary>
    internal static BepInEx.Configuration.ConfigEntry<bool> BooksToHand;
    private Harmony _harmony;
    private float _nextVisitorScan;

    private void Awake()
    {
        ItemsToHand = Config.Bind("Items", "DoubleClickToHand", false,
            "Items tab: when no open window takes a double-clicked card, put it into a free slot of your carried things.");
        BooksToHand = Config.Bind("Books", "DoubleClickToHand", false,
            "Books tab: when no open window takes a double-clicked book, put it into a free slot of your carried things.");
        _harmony = new Harmony(Guid);
        _harmony.PatchAll(typeof(BlockZoomOverWindow));
        _harmony.PatchAll(typeof(RecordCooking));
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

        // F alone (free in the game's default bindings) or Ctrl+F; never while typing in a text field,
        // so an 'f' typed into the Finder's own search (or any game field) stays a letter.
        if (keyboard.fKey.wasPressedThisFrame && !keyboard.altKey.isPressed && !keyboard.shiftKey.isPressed
            && (keyboard.ctrlKey.isPressed || !IsEditingText()))
        {
            Toggle();
            return;
        }
        // Esc only while typing: otherwise the game also handles it (and may open its menu).
        if (Window != null && Window.IsOpen && Window.IsSearchFocused && keyboard.escapeKey.wasPressedThisFrame)
            Window.Close();
    }

    /// <summary>The same test the game uses (UIController.IsEditingText): a text field has the focus.</summary>
    private static bool IsEditingText()
    {
        GameObject selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        return selected != null && (selected.GetComponent<TMPro.TMP_InputField>() != null
            || selected.GetComponent<UnityEngine.UI.InputField>() != null);
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

    /// <summary>
    /// Kitchen recipes as they run, for the Cookbook tab (the game keeps no history). A prefix:
    /// the alt recipe has already replaced the started one, and the situation may be gone after.
    /// </summary>
    [HarmonyPatch(typeof(Situation), nameof(Situation.ExecuteCurrentRecipe))]
    private static class RecordCooking
    {
        private static void Prefix(Situation __instance)
        {
            try
            {
                CookBook.Record(__instance.GetCurrentRecipe()?.Id);
                CookBook.RecordBrew(__instance);
            }
            catch (Exception)
            {
                // never break the game's recipe execution over a log of dishes
            }
        }
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
