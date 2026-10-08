using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using SecretHistories.Entities;
using SecretHistories.Entities.NullEntities;
using SecretHistories.UI;
using SecretHistories.UI.Aspects;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AltPeek;

/// <summary>
/// Hold Alt to show a second details window next to the cursor for whatever is under it
/// (card, slot, aspect icon, ...). The regular top-right details window is left untouched,
/// so it keeps showing whatever was clicked last.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class AltPeekPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.altpeek";
    public const string Name = "Alt Peek";
    public const string Version = "0.1.0";

    private static readonly FieldInfo MasterDetailWindowField = AccessTools.Field(typeof(AureateNotifier), "masterDetailWindow");
    private static readonly FieldInfo EditableIlluminationField = AccessTools.Field(typeof(AureateMasterDetailWindow), "EditableIllumination");
    private static readonly FieldInfo FrameAspectField = AccessTools.Field(typeof(ElementFrame), "_aspect");

    private ConfigEntry<float> _cursorOffset;

    private RectTransform _layer;
    private AureateMasterDetailWindow _peek;
    private RectTransform _peekRect;
    private object _shown;
    private readonly List<RaycastResult> _hits = new List<RaycastResult>();

    private void Awake()
    {
        _cursorOffset = Config.Bind("General", "CursorOffset", 24f, "Distance in pixels between the cursor and the peek window.");
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void OnDestroy()
    {
        // ScriptEngine destroys the plugin on hot reload; take the clone with it.
        if (_layer != null)
            Destroy(_layer.gameObject);
    }

    private void Update()
    {
        // Created ahead of time so the clone's Start() (which hides it) has run before first use.
        if (!EnsurePeek())
            return;

        object target = IsAltHeld() ? FindTargetUnderCursor() : null;
        if (target == null)
        {
            HidePeek();
            return;
        }

        if (!ReferenceEquals(target, _shown))
            ShowPeek(target);
        PositionAtCursor();
    }

    private static bool IsAltHeld()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
    }

    private bool EnsurePeek()
    {
        if (_peek != null)
            return true;
        if (!Watchman.Exists<AureateNotifier>())
            return false;

        AureateNotifier notifier = Watchman.Get<AureateNotifier>();
        if (notifier == null)
            return false;
        AureateMasterDetailWindow source = MasterDetailWindowField.GetValue(notifier) as AureateMasterDetailWindow;
        if (source == null)
            return false;
        Canvas canvas = source.GetComponentInParent<Canvas>();
        if (canvas == null)
            return false;

        // Full-screen layer on top of everything that never catches the mouse.
        var layerObject = new GameObject("AltPeekLayer", typeof(RectTransform), typeof(CanvasGroup));
        _layer = (RectTransform)layerObject.transform;
        _layer.SetParent(canvas.rootCanvas.transform, false);
        _layer.anchorMin = Vector2.zero;
        _layer.anchorMax = Vector2.one;
        _layer.offsetMin = Vector2.zero;
        _layer.offsetMax = Vector2.zero;
        _layer.SetAsLastSibling();
        var group = layerObject.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        GameObject clone = Instantiate(source.gameObject, _layer, true);
        clone.name = "AltPeekWindow";
        _peek = clone.GetComponent<AureateMasterDetailWindow>();
        _peekRect = (RectTransform)clone.transform;

        LayoutElement layoutElement = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;
        Vector2 size = _peekRect.rect.size;
        _peekRect.anchorMin = _peekRect.anchorMax = new Vector2(0.5f, 0.5f);
        _peekRect.sizeDelta = size;

        layerObject.SetActive(false);
        _shown = null;
        return true;
    }

    private object FindTargetUnderCursor()
    {
        EventSystem eventSystem = EventSystem.current;
        Mouse mouse = Mouse.current;
        if (eventSystem == null || mouse == null)
            return null;

        var pointer = new PointerEventData(eventSystem) { position = mouse.position.ReadValue() };
        _hits.Clear();
        eventSystem.RaycastAll(pointer, _hits);

        // Only the topmost hit: the same thing a click would land on.
        foreach (RaycastResult hit in _hits)
        {
            if (hit.gameObject == null || hit.gameObject.transform.IsChildOf(_layer))
                continue;
            return ResolveTarget(hit.gameObject.transform);
        }
        return null;
    }

    private static object ResolveTarget(Transform transform)
    {
        for (Transform t = transform; t != null; t = t.parent)
        {
            if (t.TryGetComponent(out ElementFrame frame) && FrameAspectField.GetValue(frame) is Element aspect)
                return aspect;
            if (t.TryGetComponent(out Token token))
                return token.Payload is IDetailable payload && !(payload is NullPayload) ? payload : null;
            foreach (MonoBehaviour behaviour in t.GetComponents<MonoBehaviour>())
            {
                if (behaviour is IDetailable detailable)
                    return detailable;
            }
        }
        return null;
    }

    private void ShowPeek(object target)
    {
        _layer.gameObject.SetActive(true);
        _layer.SetAsLastSibling();
        if (target is Element aspect)
        {
            _peek.HideAll();
            _peek.PreviewAspect(aspect);
        }
        else
        {
            _peek.ShowDetailable((IDetailable)target);
        }
        // Renaming books is an input field; meaningless in a window you can't click.
        (EditableIlluminationField.GetValue(_peek) as GameObject)?.SetActive(false);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_peekRect);
        _shown = target;
    }

    private void HidePeek()
    {
        if (_shown == null)
            return;
        // Deactivate first so the faders snap to hidden instead of starting a fade coroutine.
        _layer.gameObject.SetActive(false);
        _peek.HideAll();
        _shown = null;
    }

    private void PositionAtCursor()
    {
        Canvas canvas = _layer.GetComponentInParent<Canvas>();
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 mouse = Mouse.current.position.ReadValue();
        float offset = _cursorOffset.Value;

        // Open towards the larger part of the screen.
        bool toRight = mouse.x < Screen.width * 0.5f;
        bool below = mouse.y > Screen.height * 0.5f;
        _peekRect.pivot = new Vector2(toRight ? 0f : 1f, below ? 1f : 0f);
        Vector2 screenPoint = mouse + new Vector2(toRight ? offset : -offset, below ? -offset : offset);

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_layer, screenPoint, camera, out Vector2 local))
            return;

        // Keep the whole window inside the layer.
        Rect bounds = _layer.rect;
        Rect rect = _peekRect.rect;
        Vector2 scale = _peekRect.localScale;
        Vector2 min = local + Vector2.Scale(rect.min, scale);
        Vector2 max = local + Vector2.Scale(rect.max, scale);
        if (max.x > bounds.xMax) local.x -= max.x - bounds.xMax;
        if (min.x < bounds.xMin) local.x += bounds.xMin - min.x;
        if (max.y > bounds.yMax) local.y -= max.y - bounds.yMax;
        if (min.y < bounds.yMin) local.y += bounds.yMin - min.y;

        _peekRect.localPosition = new Vector3(local.x, local.y, 0f);
    }
}
