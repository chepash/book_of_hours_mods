using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using SecretHistories.Entities;
using SecretHistories.Infrastructure;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace QuietMenu;

/// <summary>
/// Hides the out-of-fiction parts of the main menu: the merch/promo banners (MenuContentBlock
/// in BHMenuScreenController.MenuBlocksHolder; in practice the banner is "ProemHolder", a button
/// calling ShowNews), the News button (ShowNews) and link buttons such as "Bugs and Issues?" (OpenUrl).
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class QuietMenuPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.quietmenu";
    public const string Name = "Quiet Menu";
    public const string Version = "0.1.0";

    private static readonly FieldInfo MenuBlocksHolderField = AccessTools.Field(typeof(BHMenuScreenController), "MenuBlocksHolder");
    private static readonly FieldInfo NewsFlashField = AccessTools.Field(typeof(BHMenuScreenController), "newsFlash");
    // ShowNews: News button and the promo banner (ProemHolder); the rest open external links
    // ("Bugs and Issues?", promo slots).
    private static readonly HashSet<string> HiddenButtonMethods = new HashSet<string>
    {
        "ShowNews", "OpenUrl", "NavigateToDestinationUrl", "External_Url1",
    };

    // The version number also opens the news on click, but it's part of the menu, not an ad.
    private static readonly HashSet<string> KeptButtons = new HashSet<string> { "VersionText" };

    // How long to look for the menu after a scene loads; FindObjectOfType is too slow to keep
    // calling during play.
    private const float SearchWindow = 10f;

    private readonly List<GameObject> _hidden = new List<GameObject>();
    private BHMenuScreenController _menu;
    private float _searchUntil;
    private float _nextCheck;
    private bool _reported;

    private void Awake()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        _searchUntil = Time.unscaledTime + SearchWindow;
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void OnDestroy()
    {
        // ScriptEngine destroys the plugin on hot reload: put the menu back as it was.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        foreach (GameObject go in _hidden)
        {
            if (go != null)
                go.SetActive(true);
        }
        _hidden.Clear();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _menu = null;
        _searchUntil = Time.unscaledTime + SearchWindow;
        _nextCheck = 0f;
        _reported = false;
    }

    private void Update()
    {
        // The menu may start before ScriptEngine adds this plugin, and banners can be filled in
        // later, so check periodically instead of patching BHMenuScreenController.Start.
        // Outside the menu this costs nothing: the search stops after SearchWindow.
        if (_menu == null && Time.unscaledTime > _searchUntil)
            return;
        if (Time.unscaledTime < _nextCheck)
            return;
        _nextCheck = Time.unscaledTime + (_menu == null ? 1f : 2f);

        if (_menu == null)
            _menu = FindObjectOfType<BHMenuScreenController>();
        BHMenuScreenController menu = _menu;
        if (menu == null)
            return;

        Scene scene = menu.gameObject.scene;
        if (MenuBlocksHolderField.GetValue(menu) is GameObject holder)
            Hide(holder, "promo banners");
        if (NewsFlashField.GetValue(menu) is Component newsFlash && newsFlash != null)
            Hide(newsFlash.gameObject, "news flash");
        foreach (MenuContentBlock block in Resources.FindObjectsOfTypeAll<MenuContentBlock>())
        {
            if (block.gameObject.scene == scene)
                Hide(block.gameObject, "promo " + block.URL);
        }

        // Buttons live all over the menu scene, not under the controller.
        var report = _reported ? null : new List<string>();
        foreach (Button button in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (button.gameObject.scene != scene)
                continue;
            var methods = new List<string>();
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                methods.Add(button.onClick.GetPersistentMethodName(i));
            report?.Add($"{button.name}[{(button.gameObject.activeInHierarchy ? "on" : "off")}]: {string.Join(",", methods)}");
            if (!KeptButtons.Contains(button.name) && methods.Exists(HiddenButtonMethods.Contains))
                Hide(button.gameObject, string.Join(",", methods));
        }
        if (report != null)
        {
            _reported = true;
            Logger.LogInfo($"Menu scene '{scene.name}', holder={(MenuBlocksHolderField.GetValue(menu) as GameObject)?.name ?? "null"}, buttons: {string.Join(" | ", report)}");
        }
    }

    private void Hide(GameObject go, string reason)
    {
        if (go == null || !go.activeSelf)
            return;
        go.SetActive(false);
        _hidden.Add(go);
        Logger.LogInfo($"Hid '{go.name}' ({reason})");
    }
}
