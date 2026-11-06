using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BohShared;
using HarmonyLib;
using SecretHistories.Entities;
using SecretHistories.UI;
using SecretHistories.UI.Aspects;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AspectPin;

/// <summary>
/// Middle-click an aspect icon to keep highlighting every item with that aspect (one at a time). The game does
/// it only while the pointer stays on the icon (AureateElementFrame.PulseGlow repeats
/// Meniscate.AttentionPlsOnElementsWIthAspects until OnPointerExit). Pinned aspects sit in a
/// small panel under the clock; the cross or another middle click unpins, clicking one shows
/// its details.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class AspectPinPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.aspectpin";
    public const string Name = "Aspect Pin";
    public const string Version = "0.1.0";

    /// <summary>
    /// One pin at a time: several aspects at once lit up unrelated items all over the house and
    /// the highlight lost its point. A new pin replaces the old one.
    /// </summary>
    private const int MaxPins = 1;
    /// <summary>About the game's own hover rhythm (a pulse, then half a second of fade).</summary>
    private const float PulseInterval = 1.2f;
    private const string ModIconPrefix = "Aspect_";

    private static readonly FieldInfo FrameAspectField = AccessTools.Field(typeof(ElementFrame), "_aspect");
    private static readonly FieldInfo MasterDetailWindowField = AccessTools.Field(typeof(AureateNotifier), "masterDetailWindow");

    private readonly List<Element> _pins = new List<Element>();
    private readonly List<RaycastResult> _hits = new List<RaycastResult>();
    private RectTransform _panel;
    private float _nextPulse;
    /// <summary>Where the player last dragged the panel (canvas units from the top left corner).</summary>
    private ConfigEntry<float> _panelX;
    private ConfigEntry<float> _panelY;

    private void Awake()
    {
        _panelX = Config.Bind("Panel", "X", 420f, "Panel position from the screen's left edge (canvas units). Saved when the panel is dragged.");
        _panelY = Config.Bind("Panel", "Y", -195f, "Panel position from the screen's top edge (canvas units, negative is down). Saved when the panel is dragged.");
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void OnDestroy()
    {
        // ScriptEngine destroys the plugin on hot reload.
        if (_panel != null)
            Destroy(_panel.gameObject);
    }

    private void Update()
    {
        try
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.middleButton.wasPressedThisFrame)
            {
                Element aspect = AspectUnderPointer(mouse.position.ReadValue());
                if (aspect != null)
                    Toggle(aspect);
            }
            if (_pins.Count > 0 && Time.unscaledTime >= _nextPulse)
            {
                _nextPulse = Time.unscaledTime + PulseInterval;
                Pulse();
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e);
        }
    }

    private Element AspectUnderPointer(Vector2 position)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return null;
        _hits.Clear();
        eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = position }, _hits);
        if (_hits.Count == 0 || _hits[0].gameObject == null)
            return null;
        GameObject hit = _hits[0].gameObject;
        ElementFrame frame = hit.GetComponentInParent<ElementFrame>();
        if (frame != null)
            return FrameAspectField.GetValue(frame) as Element;
        // Other mods' icons (the Finder's result rows) are named "Aspect_<id>".
        if (hit.name.StartsWith(ModIconPrefix, StringComparison.Ordinal))
        {
            string id = hit.name.Substring(ModIconPrefix.Length);
            Element element = Watchman.Get<Compendium>().GetEntityById<Element>(id);
            // Unknown ids come back as a NullElement, not null.
            return element != null && element.Id == id ? element : null;
        }
        return null;
    }

    private void Toggle(Element aspect)
    {
        int index = _pins.FindIndex(p => p.Id == aspect.Id);
        if (index >= 0)
        {
            _pins.RemoveAt(index);
        }
        else
        {
            if (_pins.Count >= MaxPins)
                _pins.RemoveAt(0);
            _pins.Add(aspect);
            _nextPulse = 0f; // highlight right away
        }
        RebuildPanel();
    }

    /// <summary>
    /// The same highlight the game gives on hover, for every pinned aspect. Calls AttentionPls on
    /// the tokens directly: Meniscate's own method is throttled globally and would drop pins.
    /// </summary>
    private void Pulse()
    {
        if (!Watchman.Exists<HornedAxe>())
            return;
        var seen = new HashSet<Token>();
        foreach (Element aspect in _pins)
        {
            var requirement = new Dictionary<string, string> { { aspect.Id, "1" } };
            foreach (Token token in Watchman.Get<HornedAxe>().FindTokensWithAspectsInWorld(requirement))
            {
                if (token != null && seen.Add(token))
                    token.AttentionPls();
            }
        }
    }

    // ------------------------------------------------------------ panel

    private void RebuildPanel()
    {
        if (_panel == null && !TryCreatePanel())
            return;
        for (int i = _panel.childCount - 1; i >= 0; i--)
        {
            Transform child = _panel.GetChild(i);
            if (child.name.StartsWith("Pin_", StringComparison.Ordinal))
                Destroy(child.gameObject);
        }
        foreach (Element aspect in _pins)
            AddPin(aspect);
        _panel.gameObject.SetActive(_pins.Count > 0);
    }

    private bool TryCreatePanel()
    {
        // Same overlay canvas as the details window; it only exists once a game is loaded.
        if (!Watchman.Exists<AureateNotifier>())
            return false;
        if (!(MasterDetailWindowField.GetValue(Watchman.Get<AureateNotifier>()) is Component master) || master == null)
            return false;
        Canvas canvas = master.GetComponentInParent<Canvas>();
        if (canvas == null)
            return false;

        _panel = GameStyle.Rect("AspectPinPanel", canvas.rootCanvas.transform);
        // Under the clock and speed buttons, top left; draggable.
        _panel.anchorMin = _panel.anchorMax = new Vector2(0f, 1f);
        _panel.pivot = new Vector2(0f, 1f);
        _panel.anchoredPosition = new Vector2(_panelX.Value, _panelY.Value);
        GameStyle.Layout<VerticalLayoutGroup>(_panel, 4f, new RectOffset(10, 10, 6, 8), TextAnchor.UpperLeft);
        var fitter = _panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        GameStyle.Background(_panel, "slots_bg", new Color(1f, 1f, 1f, 0.9f)).raycastTarget = true;
        var drag = _panel.gameObject.AddComponent<DragHandle>();
        drag.Target = _panel;
        drag.Dropped = position =>
        {
            _panelX.Value = position.x;
            _panelY.Value = position.y;
        };

        TextMeshProUGUI caption = GameStyle.Text(_panel, "Caption", "Highlighting", 13f, GameStyle.Gold, FontStyles.SmallCaps);
        GameStyle.Size(caption, 150f, 16f);
        return true;
    }

    private void AddPin(Element aspect)
    {
        RectTransform row = GameStyle.Rect("Pin_" + aspect.Id, _panel);
        GameStyle.Layout<HorizontalLayoutGroup>(row, 8f, null, TextAnchor.MiddleLeft);
        GameStyle.Size(row, height: 30f);

        // Icon: click shows the aspect's details, like hovering it does.
        Image icon = GameStyle.Panel(row, "Icon", "", Color.white);
        icon.sprite = ResourcesManager.GetAppropriateSpriteForElement(aspect);
        icon.preserveAspect = true;
        GameStyle.Size(icon, 28f, 28f);
        TextMeshProUGUI label = GameStyle.Text(row, "Label", aspect.Label, 16f, GameStyle.Cream);
        GameStyle.Size(label, 120f, 28f);
        Element captured = aspect;
        icon.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            Watchman.Get<AureateNotifier>().ShowAspectDetails(captured));

        Image close = GameStyle.Panel(row, "Close", "window_x", Color.white);
        close.preserveAspect = true;
        GameStyle.Size(close, 13f, 13f);
        close.gameObject.AddComponent<Button>().onClick.AddListener(() =>
        {
            _pins.Remove(captured);
            RebuildPanel();
        });
    }
}

/// <summary>Drags its target by the pointer delta (canvas units); reports where it was dropped.</summary>
public class DragHandle : MonoBehaviour, IDragHandler, IEndDragHandler
{
    public RectTransform Target;
    public Action<Vector2> Dropped;

    public void OnEndDrag(PointerEventData eventData) => Dropped?.Invoke(Target.anchoredPosition);

    public void OnDrag(PointerEventData eventData)
    {
        Canvas canvas = Target.GetComponentInParent<Canvas>();
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        Target.anchoredPosition += eventData.delta / scale;
    }
}
